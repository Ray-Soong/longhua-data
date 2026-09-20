using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Longhua.Collector.S7;

/// <summary>
/// 解析 [[longhua]] / 时间 / id / DB3.0 / 空格字节 的 PLC dump（如 test2.txt）。
/// </summary>
public sealed class PlcDumpParser
{
    private readonly ILogger? _logger;

    public PlcDumpParser(ILogger? logger = null)
    {
        _logger = logger;
    }

    public IEnumerable<PlcDumpFrame> ParseFile(string path, int? expectedBlockLength = null)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!line.Trim().Equals("[[longhua]]", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var timeLine = reader.ReadLine();
            var idLine = reader.ReadLine();
            var addrLine = reader.ReadLine();
            var dataLine = reader.ReadLine();
            if (timeLine is null || idLine is null || addrLine is null || dataLine is null)
            {
                yield break;
            }

            var bytes = ParseBytes(dataLine);
            if (expectedBlockLength is int need && bytes.Length != need)
            {
                _logger?.LogWarning(
                    "块长度不符 Expect={Expect} Actual={Actual} Time={Time}",
                    need, bytes.Length, timeLine.Trim());
            }

            DateTimeOffset.TryParse(timeLine.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var ts);
            yield return new PlcDumpFrame
            {
                Timestamp = ts == default ? DateTimeOffset.Now : ts,
                FrameId = idLine.Trim(),
                Address = addrLine.Trim(),
                Bytes = bytes
            };
        }
    }

    public static byte[] ParseBytes(string dataLine)
    {
        var parts = dataLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!byte.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out bytes[i]))
            {
                throw new FormatException($"无法解析字节 '{parts[i]}'");
            }
        }

        return bytes;
    }

    public static string FormatDumpBlock(DateTimeOffset timestamp, string frameId, string address, ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[[longhua]]");
        sb.AppendLine(timestamp.ToLocalTime().ToString("yyyy/M/d HH:mm:ss", CultureInfo.InvariantCulture));
        sb.AppendLine(frameId);
        sb.AppendLine(address);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(bytes[i].ToString(CultureInfo.InvariantCulture));
        }

        sb.AppendLine();
        return sb.ToString();
    }
}

public sealed class PlcDumpFrame
{
    public DateTimeOffset Timestamp { get; init; }
    public string FrameId { get; init; } = "";
    public string Address { get; init; } = "";
    public byte[] Bytes { get; init; } = Array.Empty<byte>();
}

public sealed class PhaseRunner
{
    private readonly ILogger<PhaseRunner> _logger;

    public PhaseRunner(ILogger<PhaseRunner> logger)
    {
        _logger = logger;
    }

    public async Task<int> RunAsync(PhaseCliOptions cli, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cli.InputPath) || !File.Exists(cli.InputPath))
        {
            _logger.LogError("phase 需要有效 --input 文件。当前: {Input}", cli.InputPath);
            return 2;
        }

        if (string.IsNullOrWhiteSpace(cli.PointTablePath) || !File.Exists(cli.PointTablePath))
        {
            _logger.LogError("phase 需要有效 --point-table。当前: {Path}", cli.PointTablePath);
            return 2;
        }

        var table = PlcPointTable.Load(cli.PointTablePath);
        var stations = table.EnabledStations().ToList();
        if (stations.Count == 0)
        {
            _logger.LogError("点表中没有 enabled 工位。");
            return 2;
        }

        var blockLength = cli.BlockLength > 0 ? cli.BlockLength : table.Plc.EffectiveReadLength;
        var output = string.IsNullOrWhiteSpace(cli.OutputPath)
            ? Path.Combine(Path.GetDirectoryName(cli.InputPath) ?? ".", Path.GetFileNameWithoutExtension(cli.InputPath) + ".phased.jsonl")
            : cli.OutputPath;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var parser = new PlcDumpParser(_logger);
        var frames = 0;
        var rows = 0;

        await using var writer = new StreamWriter(output, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        foreach (var frame in parser.ParseFile(cli.InputPath, blockLength))
        {
            cancellationToken.ThrowIfCancellationRequested();
            frames++;
            foreach (var station in stations)
            {
                if (station.Offset + station.Length > frame.Bytes.Length)
                {
                    _logger.LogWarning(
                        "跳过工位 {Station}：帧长 {Len} 不足 Offset+Length={Need}",
                        station.Id, frame.Bytes.Length, station.Offset + station.Length);
                    continue;
                }

                var decoded = StationDecoder.Decode(table, station, frame.Bytes);
                var line = new
                {
                    frameTimestamp = frame.Timestamp,
                    frameId = frame.FrameId,
                    address = frame.Address,
                    blockLength = frame.Bytes.Length,
                    stationId = decoded.StationId,
                    dataType = decoded.Type,
                    offset = decoded.Offset,
                    length = decoded.Length,
                    rawHex = decoded.RawHex,
                    data = decoded.Fields
                };
                await writer.WriteLineAsync(JsonSerializer.Serialize(line, PhaseJson.Options));
                rows++;
            }
        }

        await writer.FlushAsync();
        _logger.LogInformation(
            "phase 完成 Input={Input} Frames={Frames} Rows={Rows} Stations={Stations} BlockLength={Block} Output={Output}",
            cli.InputPath, frames, rows, stations.Count, blockLength, output);
        return 0;
    }
}

public sealed class PhaseCliOptions
{
    public string InputPath { get; set; } = "";
    public string PointTablePath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public int BlockLength { get; set; }
}

internal static class PhaseJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}
