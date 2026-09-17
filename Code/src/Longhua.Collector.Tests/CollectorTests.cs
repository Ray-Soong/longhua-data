using System.Text.Json;
using Longhua.Collector.Abstractions;
using Longhua.Collector.Configuration;
using Longhua.Collector.Mqtt;
using Longhua.Collector.Persistence;
using Longhua.Collector.Pipeline;
using Xunit;

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
            Models = new List<CatalogModelOptions>
            {
                new CatalogModelOptions { ModelName = "SLS600", DeviceType = "warehouse" },
                new CatalogModelOptions { ModelName = "SLS600-RGV", DeviceType = "rgv" }
            },
            EventKinds = new List<CatalogEventOptions>
            {
                new CatalogEventOptions { Suffix = "TASK-ASSIGN", EventKind = "Command", File = "task" },
                new CatalogEventOptions { Suffix = "TASK/STATE", EventKind = "TaskState", File = "task" },
                new CatalogEventOptions { Suffix = "STATE", EventKind = "DeviceState" },
                new CatalogEventOptions { Suffix = "HEALTH", EventKind = "Health" }
            }
        });
        return new FrameNormalizer(catalog);
    }

    private static RawFrame Frame(
        string topic,
        string payload,
        bool retained = false,
        string? linkId = null,
        string? dataType = null,
        string? name = null) => new()
    {
        SourceId = "mqtt-sls600",
        Protocol = ProtocolKind.Mqtt,
        Identity = topic,
        LinkId = linkId ?? "",
        DataType = dataType ?? "",
        Name = name ?? "",
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
            "{\"warehouseNo\":\"1\",\"offOnStatus\":true,\"timestamp\":\"2017-04-15T11:40:03.12Z\"}"));

        Assert.Null(result.DeadLetter);
        Assert.Equal("warehouse", result.Record!.DeviceType);
        Assert.Equal("W1", result.Record.DeviceId);
        Assert.Equal("DeviceState", result.Record.EventKind);
        Assert.Equal("warehouse", result.Record.FileStem);
        Assert.Equal("Good", result.Record.Quality);
        Assert.Equal("", result.Record.LinkId);
        Assert.Equal("W1", result.Record.Name);
    }

    [Fact]
    public void Normalize_CopiesSessionLinkIdTypeAndName()
    {
        var result = CreateNormalizer().Normalize(Frame(
            "SLS600-RGV/V1/HUAZH/RGVW1SN1/STATE",
            "{\"warehouseNo\":\"1\",\"carNo\":1,\"status\":\"1\",\"timestamp\":\"2017-04-15T11:40:03.12Z\"}",
            linkId: "a1b2c3d4e5f6",
            dataType: "Rgv",
            name: "Rgv1"));

        Assert.Null(result.DeadLetter);
        Assert.Equal("a1b2c3d4e5f6", result.Record!.LinkId);
        Assert.Equal("Rgv", result.Record.DataType);
        Assert.Equal("Rgv1", result.Record.Name);
        Assert.Equal("RGVW1SN1", result.Record.DeviceId);
    }

    [Fact]
    public void Normalize_TaskAssign_GoesToTaskFile()
    {
        var result = CreateNormalizer().Normalize(Frame(
            "SLS600/V1/HUAZH/W1/TASK-ASSIGN",
            "{\"taskId\":89801,\"taskType\":1,\"timestamp\":\"2017-04-15T11:40:03.12Z\"}"));

        Assert.Equal("Command", result.Record!.EventKind);
        Assert.Equal("task", result.Record.FileStem);
    }

    [Fact]
    public void Normalize_UnknownCallbackSuffix_StillMaps()
    {
        var result = CreateNormalizer().Normalize(Frame(
            "SLS600/V1/HUAZH/W1/TASK-ASSIGN-CALLBACK",
            "{\"taskId\":1,\"success\":true,\"timestamp\":\"2017-04-15T11:40:03.12Z\"}"));

        Assert.Null(result.DeadLetter);
        Assert.Equal("Callback", result.Record!.EventKind);
        Assert.Equal("task", result.Record.FileStem);
    }

    [Fact]
    public void Normalize_TimestampWithSpace_Accepted()
    {
        var result = CreateNormalizer().Normalize(Frame(
            "SLS600/V1/HUAZH/W1/HEALTH",
            "{\"warehouseNo\":\"2\",\"status\":1,\"timestamp\":\"2017-04-15T11:40: 03.12Z\"}"));

        Assert.Null(result.DeadLetter);
        Assert.Equal("Health", result.Record!.EventKind);
        Assert.NotNull(result.Record.SourceTimestamp);
    }

    [Fact]
    public void Normalize_MissingTimestamp_DeadLetter()
    {
        var result = CreateNormalizer().Normalize(Frame(
            "SLS600/V1/HUAZH/W1/STATE",
            "{\"warehouseNo\":\"1\"}"));

        Assert.Equal("missing-timestamp", result.DeadLetter!.Reason);
    }

    [Fact]
    public void Normalize_UnknownModel_DeadLetter()
    {
        var result = CreateNormalizer().Normalize(Frame(
            "OTHER/V1/HUAZH/X1/STATE",
            "{\"timestamp\":\"2017-04-15T11:40:03.12Z\"}"));

        Assert.Equal("unknown-model", result.DeadLetter!.Reason);
    }

    [Fact]
    public void Normalize_InvalidJson_DeadLetter()
    {
        var result = CreateNormalizer().Normalize(Frame("SLS600/V1/HUAZH/W1/STATE", "not-json"));
        Assert.Equal("invalid-json", result.DeadLetter!.Reason);
    }
}

public class FileTelemetrySinkTests
{
    [Fact]
    public async Task WriteEvent_WritesPlaybackEnvelope()
    {
        var root = Path.Combine(Path.GetTempPath(), "longhua-collect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = Microsoft.Extensions.Options.Options.Create(new CollectorOptions
            {
                DataRoot = root,
                FileSink = new FileSinkOptions { FileName = "collect", MaxFileBytes = 0 }
            });
            using var sink = new FileTelemetrySink(
                options,
                TimeZoneInfo.Utc,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<FileTelemetrySink>.Instance);

            using var document = JsonDocument.Parse("{\"warehouseNo\":\"1\",\"carNo\":1,\"status\":\"1\"}");
            var record = new TelemetryRecord
            {
                LinkId = "a1b2c3d4e5f6",
                DataType = "Rgv",
                Name = "Rgv1",
                ReceivedAtUtc = new DateTimeOffset(2017, 4, 15, 12, 0, 0, TimeSpan.Zero),
                SourceTimestamp = DateTimeOffset.Parse("2017-04-15T11:40:03.12Z"),
                Payload = document.RootElement.Clone()
            };

            await sink.WriteEventAsync(record, CancellationToken.None);
            await sink.FlushAsync(CancellationToken.None);

            var path = Path.Combine(root, "2017-04-15", "collect.jsonl");
            var line = File.ReadAllText(path).Trim();
            using var written = JsonDocument.Parse(line);
            Assert.Equal("a1b2c3d4e5f6", written.RootElement.GetProperty("linkId").GetString());
            Assert.Equal("Rgv", written.RootElement.GetProperty("dataType").GetString());
            Assert.Equal("Rgv1", written.RootElement.GetProperty("name").GetString());
            Assert.Equal(
                DateTimeOffset.Parse("2017-04-15T11:40:03.12Z"),
                written.RootElement.GetProperty("timestamp").GetDateTimeOffset());
            Assert.Equal(1, written.RootElement.GetProperty("data").GetProperty("carNo").GetInt32());
            Assert.False(written.RootElement.TryGetProperty("deviceId", out _));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
