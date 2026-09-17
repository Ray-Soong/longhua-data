namespace Longhua.Collector.Configuration;

public sealed class CollectorOptions
{
    public const string SectionName = "Collector";

    /// <summary>相对路径相对配置目录的上一级（Code/ 或发布目录）。</summary>
    public string DataRoot { get; set; } = "data";

    public string TimeZone { get; set; } = "Asia/Shanghai";

    public int RawChannelCapacity { get; set; } = 20_000;

    public int HealthStaleSeconds { get; set; } = 30;

    public bool Deduplicate { get; set; }

    public int DedupCacheSize { get; set; } = 10_000;

    public List<SourceOptions> Sources { get; set; } = new();

    public FileSinkOptions FileSink { get; set; } = new();
}

public sealed class FileSinkOptions
{
    public int FlushIntervalMs { get; set; } = 1000;

    /// <summary>单个 JSONL 超过该字节后滚动到 .2.jsonl、.3.jsonl。0 表示不按体积切分。</summary>
    public long MaxFileBytes { get; set; } = 256L * 1024 * 1024;

    public int KeepDays { get; set; } = 30;

    /// <summary>当天采集主文件名（不含扩展名）。所有规范化记录写入这一个文件，便于以后整文件导入数据库。</summary>
    public string FileName { get; set; } = "collect";

    /// <summary>是否额外写 raw/ 原包。默认关闭，先只保留一份统一记录。</summary>
    public bool WriteRaw { get; set; }

    /// <summary>是否把解析失败另写 dead-letter。默认关闭，避免拆成多文件。</summary>
    public bool WriteDeadLetter { get; set; }
}

public sealed class SourceOptions
{
    public string Id { get; set; } = "";

    public string Protocol { get; set; } = "";

    public bool Enabled { get; set; }

    public MqttSourceOptions? Mqtt { get; set; }

    public S7SourceOptions? S7 { get; set; }
}

public sealed class MqttSourceOptions
{
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 1883;

    public bool UseTls { get; set; }

    public string ClientId { get; set; } = "longhua-collector";

    public string? Username { get; set; }

    public string? Password { get; set; }

    public bool CleanSession { get; set; }

    public int KeepAliveSeconds { get; set; } = 30;

    public int ReconnectMinSeconds { get; set; } = 1;

    public int ReconnectMaxSeconds { get; set; } = 30;

    public List<MqttSubscriptionOptions> Subscriptions { get; set; } = new();
}

public sealed class MqttSubscriptionOptions
{
    /// <summary>数据类型，如 TaskAssign、TaskState、Rgv。多台同类设备共用一个 Type。</summary>
    public string Type { get; set; } = "";

    /// <summary>实例名，如 TaskAssign、Rgv1、Rgv2。</summary>
    public string Name { get; set; } = "";

    public string Topic { get; set; } = "+/+/HUAZH/#";

    public int QoS { get; set; } = 1;
}

public sealed class S7SourceOptions
{
    public string Ip { get; set; } = "";

    public int Port { get; set; } = 102;

    public int Rack { get; set; }

    public int Slot { get; set; } = 1;

    public List<S7PollOptions> Polls { get; set; } = new();
}

public sealed class S7PollOptions
{
    public string DeviceType { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public int Db { get; set; }

    public int Start { get; set; }

    public int Length { get; set; }

    public int IntervalMs { get; set; } = 1000;

    public string? PointTable { get; set; }

    public bool WriteRawEveryPoll { get; set; }
}

public sealed class CatalogOptions
{
    public const string SectionName = "Catalog";

    public string? Manufacturer { get; set; } = "HUAZH";

    public List<CatalogModelOptions> Models { get; set; } = new();

    public List<CatalogEventOptions> EventKinds { get; set; } = new();
}

public sealed class CatalogModelOptions
{
    public string ModelName { get; set; } = "";

    public string DeviceType { get; set; } = "";
}

public sealed class CatalogEventOptions
{
    public string Suffix { get; set; } = "";

    public string EventKind { get; set; } = "";

    /// <summary>events 目录下的文件名（不含扩展名）。空则用 DeviceType。</summary>
    public string? File { get; set; }
}
