using Longhua.Collector.Configuration;
using Longhua.Collector.Mqtt;

namespace Longhua.Collector.Pipeline;

public sealed class DeviceCatalog
{
    private readonly string? _manufacturer;
    private readonly Dictionary<string, string> _models;
    private readonly Dictionary<string, CatalogEventOptions> _events;

    public DeviceCatalog(CatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _manufacturer = string.IsNullOrWhiteSpace(options.Manufacturer)
            ? null
            : options.Manufacturer.Trim();
        _models = options.Models
            .Where(m => !string.IsNullOrWhiteSpace(m.ModelName) && !string.IsNullOrWhiteSpace(m.DeviceType))
            .ToDictionary(m => m.ModelName.Trim(), m => m.DeviceType.Trim(), StringComparer.OrdinalIgnoreCase);
        _events = options.EventKinds
            .Where(e => !string.IsNullOrWhiteSpace(e.Suffix) && !string.IsNullOrWhiteSpace(e.EventKind))
            .ToDictionary(e => e.Suffix.Trim(), e => e, StringComparer.OrdinalIgnoreCase);
    }

    public static DeviceCatalog FromOptions(CatalogOptions options) => new(options);

    public bool TryResolve(MqttTopicIdentity topic, out string deviceType, out string eventKind, out string fileStem, out string? error)
    {
        deviceType = "";
        eventKind = "";
        fileStem = "";
        error = null;

        if (_manufacturer is not null
            && !string.Equals(topic.Manufacturer, _manufacturer, StringComparison.OrdinalIgnoreCase))
        {
            error = "unexpected-manufacturer";
            return false;
        }

        if (!_models.TryGetValue(topic.ModelName, out deviceType!))
        {
            error = "unknown-model";
            return false;
        }

        if (TryMapEvent(topic.Suffix, out eventKind, out var file))
        {
            fileStem = string.IsNullOrWhiteSpace(file) ? deviceType : file!;
            return true;
        }

        error = "unknown-suffix";
        return false;
    }

    private bool TryMapEvent(string suffix, out string eventKind, out string? file)
    {
        if (_events.TryGetValue(suffix, out var mapped))
        {
            eventKind = mapped.EventKind;
            file = mapped.File;
            return true;
        }

        if (suffix.EndsWith("-CALLBACK", StringComparison.OrdinalIgnoreCase))
        {
            eventKind = nameof(Abstractions.EventKind.Callback);
            file = "task";
            return true;
        }

        eventKind = "";
        file = null;
        return false;
    }
}
