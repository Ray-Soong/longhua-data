namespace Longhua.Collector.Mqtt;

public sealed record MqttTopicIdentity(
    string ModelName,
    string MajorVersion,
    string Manufacturer,
    string SerialNumber,
    string Suffix);

/// <summary>
/// 协议 Topic：modelName/majorVersion/HUAZH/serialNumber/suffix。
/// suffix 可含斜杠，例如 TASK/STATE。
/// </summary>
public static class MqttTopicParser
{
    public static bool TryParse(string? topic, out MqttTopicIdentity identity)
    {
        identity = default!;
        if (string.IsNullOrWhiteSpace(topic))
        {
            return false;
        }

        var parts = topic.Split('/');
        if (parts.Length < 5)
        {
            return false;
        }

        for (var i = 0; i < 4; i++)
        {
            if (string.IsNullOrWhiteSpace(parts[i]))
            {
                return false;
            }
        }

        var suffix = string.Join('/', parts.Skip(4));
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return false;
        }

        identity = new MqttTopicIdentity(
            ModelName: parts[0],
            MajorVersion: parts[1],
            Manufacturer: parts[2],
            SerialNumber: parts[3],
            Suffix: suffix);
        return true;
    }
}
