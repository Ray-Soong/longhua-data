using System.Text.Json;
using System.Text.Json.Serialization;

namespace Longhua.Collector.Abstractions;

public enum ProtocolKind
{
    Mqtt,
    S7
}

public enum EventKind
{
    Command,
    Callback,
    TaskState,
    DeviceState,
    Scan,
    Health
}

public enum DataQuality
{
    Good,
    Stale,
    ParseError
}

public sealed class RawFrame
{
    public required string SourceId { get; init; }
    public required ProtocolKind Protocol { get; init; }
    public required string Identity { get; init; }
    public required DateTimeOffset CapturedAtUtc { get; init; }
    public byte[] PayloadBytes { get; init; } = [];
    public string? PayloadText { get; init; }
    public int? MqttQoS { get; init; }
    public bool IsRetained { get; init; }
    public string? ClientId { get; init; }
}

public sealed class TelemetryRecord
{
    public required string RecordId { get; init; }
    public required DateTimeOffset ReceivedAtUtc { get; init; }
    public DateTimeOffset? SourceTimestamp { get; init; }
    public required string Protocol { get; init; }
    public required string SourceId { get; init; }
    public required string DeviceType { get; init; }
    public required string DeviceId { get; init; }
    public required string Identity { get; init; }
    public required string EventKind { get; init; }
    public bool IsRetained { get; init; }
    public required string Quality { get; init; }
    public JsonElement? Payload { get; init; }

    [JsonIgnore]
    public string FileStem { get; init; } = "unknown";
}

public sealed class DeadLetterRecord
{
    public required DateTimeOffset ReceivedAtUtc { get; init; }
    public required string SourceId { get; init; }
    public required string Protocol { get; init; }
    public required string Identity { get; init; }
    public required string Reason { get; init; }
    public bool IsRetained { get; init; }
    public string? PayloadText { get; init; }
}

public interface IDataSource : IAsyncDisposable
{
    string Id { get; }
    ProtocolKind Protocol { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface ITelemetrySink
{
    ValueTask WriteRawAsync(RawFrame frame, CancellationToken cancellationToken);
    ValueTask WriteEventAsync(TelemetryRecord record, CancellationToken cancellationToken);
    ValueTask WriteDeadLetterAsync(DeadLetterRecord record, CancellationToken cancellationToken);
    ValueTask FlushAsync(CancellationToken cancellationToken);
}
