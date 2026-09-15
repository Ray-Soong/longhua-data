using System.Threading.Channels;
using Longhua.Collector.Abstractions;
using Longhua.Collector.Configuration;
using Longhua.Collector.Persistence;
using Longhua.Collector.Pipeline;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Longhua.Collector;

public sealed class CollectorWorker : BackgroundService
{
    private readonly IOptions<CollectorOptions> _options;
    private readonly DataSourceFactory _factory;
    private readonly Channel<RawFrame> _raw;
    private readonly ITelemetrySink _sink;
    private readonly FrameNormalizer _normalizer;
    private readonly HealthWatch _healthWatch;
    private readonly CollectorMetrics _metrics;
    private readonly ILogger<CollectorWorker> _logger;
    private readonly Deduplicator? _deduplicator;

    public CollectorWorker(
        IOptions<CollectorOptions> options,
        DataSourceFactory factory,
        Channel<RawFrame> raw,
        ITelemetrySink sink,
        FrameNormalizer normalizer,
        HealthWatch healthWatch,
        CollectorMetrics metrics,
        ILogger<CollectorWorker> logger)
    {
        _options = options;
        _factory = factory;
        _raw = raw;
        _sink = sink;
        _normalizer = normalizer;
        _healthWatch = healthWatch;
        _metrics = metrics;
        _logger = logger;
        if (options.Value.Deduplicate)
        {
            _deduplicator = new Deduplicator(options.Value.DedupCacheSize);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.Value;
        var enabled = options.Sources.Where(source => source.Enabled).ToList();
        if (enabled.Count == 0)
        {
            _logger.LogError("没有启用的采集源。请编辑 config/appsettings.json 中 Collector:Sources。");
            return;
        }

        var sources = enabled.Select(_factory.Create).ToList();
        _logger.LogInformation("启动 {Count} 个采集源，数据目录 {DataRoot}", sources.Count, options.DataRoot);

        foreach (var source in sources)
        {
            await source.StartAsync(stoppingToken);
        }

        var consume = ConsumeRawAsync(CancellationToken.None);
        var flush = FlushLoopAsync(stoppingToken);
        var health = _healthWatch.RunAsync(stoppingToken);
        var metrics = MetricsLoopAsync(stoppingToken);
        var cleanup = CleanupLoopAsync(stoppingToken);

        try
        {
            await WaitForStopAsync(stoppingToken);
        }
        finally
        {
            foreach (var source in sources)
            {
                try
                {
                    await source.StopAsync(CancellationToken.None);
                    await source.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "停止采集源失败 {SourceId}", source.Id);
                }
            }

            _raw.Writer.TryComplete();
            try
            {
                await consume;
            }
            catch (OperationCanceledException)
            {
            }

            await _sink.FlushAsync(CancellationToken.None);
        }

        try
        {
            await Task.WhenAll(flush, health, metrics, cleanup);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ConsumeRawAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in _raw.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await ProcessFrameAsync(frame, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理报文失败 Source={SourceId} Identity={Identity}", frame.SourceId, frame.Identity);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ProcessFrameAsync(RawFrame frame, CancellationToken cancellationToken)
    {
        await _sink.WriteRawAsync(frame, cancellationToken);
        var result = _normalizer.Normalize(frame);
        if (result.DeadLetter is { } dead)
        {
            _metrics.IncrementDeadLetters();
            await _sink.WriteDeadLetterAsync(dead, cancellationToken);
            return;
        }

        var record = result.Record!;
        if (_deduplicator?.IsDuplicate(record) == true)
        {
            _metrics.IncrementDuplicates();
            return;
        }

        _metrics.IncrementEvents();
        await _sink.WriteEventAsync(record, cancellationToken);
        _healthWatch.Observe(record);
    }

    private async Task FlushLoopAsync(CancellationToken cancellationToken)
    {
        var interval = Math.Max(200, _options.Value.FileSink.FlushIntervalMs);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await _sink.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await _sink.FlushAsync(CancellationToken.None);
        }
    }

    private async Task MetricsLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var snapshot = _metrics.Snapshot();
                foreach (var (id, item) in snapshot)
                {
                    _logger.LogInformation(
                        "采集统计 Source={SourceId} Connected={Connected} Received={Received} Dropped={Dropped} Events={Events} DeadLetter={Dead} Dup={Dup}",
                        id, item.Connected, item.Received, item.Dropped, _metrics.EventCount, _metrics.DeadLetterCount, _metrics.DuplicateCount);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CleanupLoopAsync(CancellationToken cancellationToken)
    {
        if (_sink is not FileTelemetrySink files)
        {
            return;
        }

        files.DeleteExpiredDays();
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                files.DeleteExpiredDays();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task WaitForStopAsync(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = stoppingToken.Register(() => tcs.TrySetResult());
        await tcs.Task;
    }
}
