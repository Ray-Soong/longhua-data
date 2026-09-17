using Longhua.Collector.Abstractions;
using Longhua.Collector.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Longhua.Collector.Pipeline;

public sealed class HealthWatch
{
    private readonly ITelemetrySink _sink;
    private readonly CollectorOptions _options;
    private readonly ILogger<HealthWatch> _logger;
    private readonly Dictionary<string, DeviceHealth> _devices = new(StringComparer.OrdinalIgnoreCase);

    public HealthWatch(ITelemetrySink sink, IOptions<CollectorOptions> options, ILogger<HealthWatch> logger)
    {
        _sink = sink;
        _options = options.Value;
        _logger = logger;
    }

    public void Observe(TelemetryRecord record)
    {
        if (!string.Equals(record.EventKind, nameof(EventKind.Health), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(record.Quality, nameof(DataQuality.Good), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_devices)
        {
            _devices[record.DeviceId] = new DeviceHealth(
                record.SourceId,
                record.DeviceType,
                record.DeviceId,
                record.Identity,
                record.LinkId,
                record.DataType,
                record.Name,
                record.Protocol,
                DateTimeOffset.UtcNow,
                StaleEmitted: false);
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var staleAfter = TimeSpan.FromSeconds(Math.Max(5, _options.HealthStaleSeconds));
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Min(10, staleAfter.TotalSeconds)));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await EmitStaleAsync(staleAfter, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task EmitStaleAsync(TimeSpan staleAfter, CancellationToken cancellationToken)
    {
        List<DeviceHealth> stale;
        lock (_devices)
        {
            var now = DateTimeOffset.UtcNow;
            stale = new List<DeviceHealth>();
            foreach (var pair in _devices.ToList())
            {
                var item = pair.Value;
                if (item.StaleEmitted)
                {
                    continue;
                }

                if (now - item.LastSeenUtc < staleAfter)
                {
                    continue;
                }

                var updated = item with { StaleEmitted = true };
                _devices[pair.Key] = updated;
                stale.Add(updated);
            }
        }

        foreach (var device in stale)
        {
            var record = new TelemetryRecord
            {
                RecordId = Guid.NewGuid().ToString("N"),
                ReceivedAtUtc = DateTimeOffset.UtcNow,
                SourceTimestamp = DateTimeOffset.UtcNow,
                Protocol = device.Protocol,
                SourceId = device.SourceId,
                DeviceType = device.DeviceType,
                DeviceId = device.DeviceId,
                Identity = device.Identity,
                LinkId = device.LinkId,
                DataType = device.DataType,
                Name = device.Name,
                EventKind = nameof(EventKind.Health),
                IsRetained = false,
                Quality = nameof(DataQuality.Stale),
                Payload = null,
                FileStem = device.DeviceType
            };

            _logger.LogWarning("设备心跳超时 DeviceId={DeviceId} Identity={Identity}", device.DeviceId, device.Identity);
            await _sink.WriteEventAsync(record, cancellationToken);
        }
    }

    private sealed record DeviceHealth(
        string SourceId,
        string DeviceType,
        string DeviceId,
        string Identity,
        string LinkId,
        string DataType,
        string Name,
        string Protocol,
        DateTimeOffset LastSeenUtc,
        bool StaleEmitted);
}
