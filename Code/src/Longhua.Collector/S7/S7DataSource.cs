using System.Threading.Channels;
using Longhua.Collector.Abstractions;
using Longhua.Collector.Configuration;
using Microsoft.Extensions.Logging;

namespace Longhua.Collector.S7;

/// <summary>
/// 采集模式下轮询 S7 DB，按 [[longhua]] 文本块写入独立 PLC 文件。
/// 连不上 PLC 时会持续重试并记日志，不阻断 MQTT。
/// </summary>
public sealed class S7DataSource : IDataSource
{
    private readonly SourceOptions _source;
    private readonly CollectorMetrics _metrics;
    private readonly ILogger<S7DataSource> _logger;
    private readonly string _dataRoot;
    private readonly string _plcFileStem;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private StreamWriter? _dumpWriter;
    private string? _dumpPath;
    private readonly object _fileGate = new();

    public S7DataSource(
        SourceOptions source,
        CollectorMetrics metrics,
        ILogger<S7DataSource> logger,
        string dataRoot,
        string plcFileStem,
        ChannelWriter<RawFrame>? writer = null)
    {
        _source = source;
        _metrics = metrics;
        _logger = logger;
        _dataRoot = dataRoot;
        _plcFileStem = string.IsNullOrWhiteSpace(plcFileStem) ? "plc" : plcFileStem;
        _ = writer;
    }

    public string Id => _source.Id;

    public ProtocolKind Protocol => ProtocolKind.S7;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var s7 = _source.S7 ?? throw new InvalidOperationException($"采集源 {Id} 缺少 S7 配置。");
        if (s7.Polls.Count == 0)
        {
            _logger.LogWarning("S7 采集源 {SourceId} 没有 Polls，跳过。", Id);
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(() => RunAsync(s7, _cts.Token), CancellationToken.None);
        _logger.LogInformation(
            "S7 采集已启动 Source={SourceId} Ip={Ip} Polls={Polls} DumpStem={Stem}",
            Id, s7.Ip, s7.Polls.Count, _plcFileStem);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "等待 S7 循环结束失败");
            }
        }

        lock (_fileGate)
        {
            _dumpWriter?.Flush();
            _dumpWriter?.Dispose();
            _dumpWriter = null;
            _dumpPath = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _cts?.Dispose();
    }

    private async Task RunAsync(S7SourceOptions s7, CancellationToken cancellationToken)
    {
        _metrics.SetConnected(Id, false);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(s7, cancellationToken);
                _metrics.SetConnected(Id, true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _metrics.SetConnected(Id, false);
                _logger.LogWarning(ex, "S7 轮询失败 Ip={Ip}，将重试", s7.Ip);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _metrics.SetConnected(Id, false);
    }

    private async Task PollOnceAsync(S7SourceOptions s7, CancellationToken cancellationToken)
    {
        using var reader = new PlcDbReader(s7.Ip, s7.Port, s7.Rack, s7.Slot, _logger);
        await reader.EnsureConnectedAsync(cancellationToken);

        foreach (var poll in s7.Polls)
        {
            var length = poll.Length > 0 ? poll.Length : 494;
            var interval = Math.Max(50, poll.IntervalMs);
            var buffer = new byte[length];
            await reader.ReadDbAsync(poll.Db, poll.Start, buffer, cancellationToken);

            var now = DateTimeOffset.Now;
            var frameId = now.Ticks.ToString();
            var address = $"DB{poll.Db}.{poll.Start}";
            WriteDump(now, frameId, address, buffer);
            _metrics.IncrementReceived(Id);

            await Task.Delay(interval, cancellationToken);
        }
    }

    private void WriteDump(DateTimeOffset timestamp, string frameId, string address, byte[] buffer)
    {
        var text = PlcDumpParser.FormatDumpBlock(timestamp, frameId, address, buffer);
        var day = timestamp.ToString("yyyy-MM-dd");
        var dir = Path.Combine(_dataRoot, day);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{_plcFileStem}.txt");

        lock (_fileGate)
        {
            if (_dumpWriter is null || !string.Equals(_dumpPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _dumpWriter?.Flush();
                _dumpWriter?.Dispose();
                _dumpPath = path;
                _dumpWriter = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    AutoFlush = true
                };
            }

            _dumpWriter.Write(text);
        }
    }
}
