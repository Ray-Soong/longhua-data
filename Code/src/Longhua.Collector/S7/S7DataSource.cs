using Longhua.Collector.Abstractions;
using Longhua.Collector.Configuration;
using Microsoft.Extensions.Logging;

namespace Longhua.Collector.S7;

/// <summary>
/// 二期占位。Enabled=true 时只记日志，不采集，避免把 S7 字节伪装成 MQTT。
/// </summary>
public sealed class S7DataSource : IDataSource
{
    private readonly SourceOptions _source;
    private readonly ILogger<S7DataSource> _logger;

    public S7DataSource(SourceOptions source, ILogger<S7DataSource> logger)
    {
        _source = source;
        _logger = logger;
    }

    public string Id => _source.Id;

    public ProtocolKind Protocol => ProtocolKind.S7;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var s7 = _source.S7;
        _logger.LogWarning(
            "S7 采集源 {SourceId} 为一期预留，尚未实现。Ip={Ip} Rack={Rack} Slot={Slot} Polls={Polls}",
            Id,
            s7?.Ip,
            s7?.Rack,
            s7?.Slot,
            s7?.Polls.Count ?? 0);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
