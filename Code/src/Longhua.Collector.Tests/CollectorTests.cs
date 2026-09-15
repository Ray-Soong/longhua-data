using Longhua.Collector.Abstractions;
using Xunit;
using Longhua.Collector.Configuration;
using Longhua.Collector.Mqtt;
using Longhua.Collector.Pipeline;

namespace Longhua.Collector.Tests;

public class MqttTopicParserTests
{
    [Fact]
    public void Parse_WarehouseState()
    {
        Assert.True(MqttTopicParser.TryParse("SLS600/V1/HUAZH/W1/STATE", out var topic));
        Assert.Equal("SLS600", topic.ModelName);
        Assert.Equal("V1", topic.MajorVersion);
        Assert.Equal("HUAZH", topic.Manufacturer);
        Assert.Equal("W1", topic.SerialNumber);
        Assert.Equal("STATE", topic.Suffix);
    }

    [Fact]
    public void Parse_TaskState_KeepsSlashInSuffix()
    {
        Assert.True(MqttTopicParser.TryParse("SLS600/V1/HUAZH/W1/TASK/STATE", out var topic));
        Assert.Equal("TASK/STATE", topic.Suffix);
        Assert.Equal("W1", topic.SerialNumber);
    }

    [Fact]
    public void Parse_RgvState()
    {
        Assert.True(MqttTopicParser.TryParse("SLS600-RGV/V1/HUAZH/RGVW1SN1/STATE", out var topic));
        Assert.Equal("SLS600-RGV", topic.ModelName);
        Assert.Equal("RGVW1SN1", topic.SerialNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SLS600/V1/HUAZH/W1")]
    [InlineData("SLS600/V1/HUAZH//STATE")]
    public void Parse_RejectsInvalid(string? topic)
    {
        Assert.False(MqttTopicParser.TryParse(topic, out _));
    }
}

public class FrameNormalizerTests
{
    private static FrameNormalizer CreateNormalizer()
    {
        var catalog = new DeviceCatalog(new CatalogOptions
        {
            Manufacturer = "HUAZH",
            Models =
            [
                new CatalogModelOptions { ModelName = "SLS600", DeviceType = "warehouse" },
                new CatalogModelOptions { ModelName = "SLS600-RGV", DeviceType = "rgv" }
            ],
            EventKinds =
            [
                new CatalogEventOptions { Suffix = "TASK-ASSIGN", EventKind = "Command", File = "task" },
                new CatalogEventOptions { Suffix = "TASK/STATE", EventKind = "TaskState", File = "task" },
                new CatalogEventOptions { Suffix = "STATE", EventKind = "DeviceState" },
                new CatalogEventOptions { Suffix = "HEALTH", EventKind = "Health" }
            ]
        });
        return new FrameNormalizer(catalog);
    }

    private static RawFrame Frame(string topic, string payload, bool retained = false) => new()
    {
        SourceId = "mqtt-sls600",
        Protocol = ProtocolKind.Mqtt,
        Identity = topic,
        CapturedAtUtc = DateTimeOffset.UtcNow,
        PayloadText = payload,
        IsRetained = retained,
        MqttQoS = 0,
        ClientId = "test"
    };

    [Fact]
    public void Normalize_WarehouseState()
    {
        var result = CreateNormalizer().Normalize(Frame(
            "SLS600/V1/HUAZH/W1/STATE",
            """{"warehouseNo":"1","offOnStatus":true,"timestamp":"2017-04-15T11:40:03.12Z"}"""));

        Assert.Null(result.DeadLetter);
        Assert.Equal("warehouse", result.Record!.DeviceType);
        Assert.Equal("W1", result.Record.DeviceId);
        Assert.Equal("DeviceState", result.Record.EventKind);
        Assert.Equal("warehouse", result.Record.FileStem);
        Assert.Equal("Good", result.Record.Quality);
    }

    [Fact]
    public void Normalize_TaskAssign_GoesToTaskFile()
    {
        var result = CreateNormalizer().Normalize(Frame(
            "SLS600/V1/HUAZH/W1/TASK-ASSIGN",
            """{"taskId":89801,"taskType":1,"timestamp":"2017-04-15T11:40:03.12Z"}"""));

        Assert.Equal("Command", result.Record!.EventKind);
        Assert.Equal("task", result.Record.FileStem);
    }

    [Fact]
    public void Normalize_UnknownCallbackSuffix_StillMaps()
    {
        var result = CreateNormalizer().Normalize(Frame(
            "SLS600/V1/HUAZH/W1/TASK-ASSIGN-CALLBACK",
            """{"taskId":1,"success":true,"timestamp":"2017-04-15T11:40:03.12Z"}"""));

        Assert.Null(result.DeadLetter);
        Assert.Equal("Callback", result.Record!.EventKind);
        Assert.Equal("task", result.Record.FileStem);
    }

    [Fact]
    public void Normalize_TimestampWithSpace_Accepted()
    {
        var result = CreateNormalizer().Normalize(Frame(
            "SLS600/V1/HUAZH/W1/HEALTH",
            """{"warehouseNo":"2","status":1,"timestamp":"2017-04-15T11:40: 03.12Z"}"""));

        Assert.Null(result.DeadLetter);
        Assert.Equal("Health", result.Record!.EventKind);
        Assert.NotNull(result.Record.SourceTimestamp);
    }

    [Fact]
    public void Normalize_MissingTimestamp_DeadLetter()
    {
        var result = CreateNormalizer().Normalize(Frame(
            "SLS600/V1/HUAZH/W1/STATE",
            """{"warehouseNo":"1"}"""));

        Assert.Equal("missing-timestamp", result.DeadLetter!.Reason);
    }

    [Fact]
    public void Normalize_UnknownModel_DeadLetter()
    {
        var result = CreateNormalizer().Normalize(Frame(
            "OTHER/V1/HUAZH/X1/STATE",
            """{"timestamp":"2017-04-15T11:40:03.12Z"}"""));

        Assert.Equal("unknown-model", result.DeadLetter!.Reason);
    }

    [Fact]
    public void Normalize_InvalidJson_DeadLetter()
    {
        var result = CreateNormalizer().Normalize(Frame("SLS600/V1/HUAZH/W1/STATE", "not-json"));
        Assert.Equal("invalid-json", result.DeadLetter!.Reason);
    }
}
