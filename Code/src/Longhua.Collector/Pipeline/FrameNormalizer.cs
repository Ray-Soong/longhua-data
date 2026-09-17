using System.Globalization;
using System.Text.Json;
using Longhua.Collector.Abstractions;
using Longhua.Collector.Mqtt;

namespace Longhua.Collector.Pipeline;

public sealed class FrameNormalizer
{
    private readonly DeviceCatalog _catalog;

    public FrameNormalizer(DeviceCatalog catalog)
    {
        _catalog = catalog;
    }

    public NormalizeResult Normalize(RawFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Protocol == ProtocolKind.S7)
        {
            return Dead(frame, "s7-not-implemented");
        }

        if (!MqttTopicParser.TryParse(frame.Identity, out var topic))
        {
            return Dead(frame, "invalid-topic");
        }

        if (!_catalog.TryResolve(topic, out var deviceType, out var eventKind, out var fileStem, out var error))
        {
            return Dead(frame, error ?? "unknown-topic");
        }

        if (string.IsNullOrWhiteSpace(frame.PayloadText))
        {
            return Dead(frame, "empty-payload");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(frame.PayloadText);
        }
        catch (JsonException)
        {
            return Dead(frame, "invalid-json");
        }

        using (document)
        {
            if (!TryReadTimestamp(document.RootElement, out var sourceTimestamp))
            {
                return Dead(frame, "missing-timestamp");
            }

            var record = new TelemetryRecord
            {
                RecordId = Guid.NewGuid().ToString("N"),
                ReceivedAtUtc = frame.CapturedAtUtc,
                SourceTimestamp = sourceTimestamp,
                Protocol = frame.Protocol.ToString(),
                SourceId = frame.SourceId,
                DeviceType = deviceType,
                DeviceId = topic.SerialNumber,
                Identity = frame.Identity,
                LinkId = frame.LinkId,
                DataType = frame.DataType,
                Name = string.IsNullOrWhiteSpace(frame.Name) ? topic.SerialNumber : frame.Name,
                EventKind = eventKind,
                IsRetained = frame.IsRetained,
                Quality = nameof(DataQuality.Good),
                Payload = document.RootElement.Clone(),
                FileStem = fileStem
            };

            return new NormalizeResult(record, null);
        }
    }

    public static bool TryReadTimestamp(JsonElement root, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!TryGetPropertyIgnoreCase(root, "timestamp", out var node)
            || node.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var raw = node.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp))
        {
            return true;
        }

        var compact = raw.Replace(" ", "", StringComparison.Ordinal);
        return DateTimeOffset.TryParse(compact, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp);
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals(name) || property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static NormalizeResult Dead(RawFrame frame, string reason) =>
        new(null, new DeadLetterRecord
        {
            ReceivedAtUtc = frame.CapturedAtUtc,
            SourceId = frame.SourceId,
            Protocol = frame.Protocol.ToString(),
            Identity = frame.Identity,
            Reason = reason,
            IsRetained = frame.IsRetained,
            PayloadText = frame.PayloadText
        });
}

public sealed record NormalizeResult(TelemetryRecord? Record, DeadLetterRecord? DeadLetter)
{
    public bool IsDeadLetter => DeadLetter is not null;
}
