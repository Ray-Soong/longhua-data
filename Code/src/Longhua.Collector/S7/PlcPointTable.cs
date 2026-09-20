using System.Text.Json;
using System.Text.Json.Serialization;

namespace Longhua.Collector.S7;

public sealed class PlcPointTable
{
    public string Name { get; set; } = "";
    public int Version { get; set; }
    public PlcConnectionConfig Plc { get; set; } = new();
    public Dictionary<string, PlcTypeDef> Types { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<PlcStationDef> Stations { get; set; } = new();

    public static PlcPointTable Load(string path)
    {
        var json = File.ReadAllText(path);
        var table = JsonSerializer.Deserialize<PlcPointTable>(json, JsonOptions)
            ?? throw new InvalidOperationException($"无法解析点表: {path}");
        table.Validate();
        return table;
    }

    public IEnumerable<PlcStationDef> EnabledStations() =>
        Stations.Where(s => s.Enabled);

    public void Validate()
    {
        foreach (var station in Stations)
        {
            if (!Types.ContainsKey(station.Type))
            {
                throw new InvalidOperationException($"工位 {station.Id} 类型 {station.Type} 未在 types 中定义。");
            }

            var typeLen = Types[station.Type].SizeBytes;
            if (station.Length <= 0)
            {
                station.Length = typeLen;
            }

            if (station.Length != typeLen)
            {
                throw new InvalidOperationException(
                    $"工位 {station.Id} length={station.Length} 与类型 {station.Type} sizeBytes={typeLen} 不一致。");
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

public sealed class PlcConnectionConfig
{
    public string Ip { get; set; } = "";
    public int Port { get; set; } = 102;
    public int Rack { get; set; }
    public int Slot { get; set; } = 1;
    public int Db { get; set; } = 3;
    public string DbName { get; set; } = "ConveyorInfor";
    public int Start { get; set; }
    /// <summary>模型关注长度（如 494）。</summary>
    public int Length { get; set; } = 494;
    /// <summary>现场整块读取长度（如 514）；缺省用 Length。</summary>
    public int ReadLength { get; set; }
    public string ByteOrder { get; set; } = "BigEndian";
    public int IntervalMs { get; set; } = 200;
    public bool WriteRawEveryPoll { get; set; } = true;

    public int EffectiveReadLength => ReadLength > 0 ? ReadLength : Length;
}

public sealed class PlcTypeDef
{
    public string Meaning { get; set; } = "";
    public int SizeBytes { get; set; }
    public List<PlcFieldDef> Fields { get; set; } = new();
}

public sealed class PlcFieldDef
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Offset { get; set; } = "0.0";
    public string Comment { get; set; } = "";
}

public sealed class PlcStationDef
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public int Offset { get; set; }
    public int Length { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class StationDecodeResult
{
    public int StationId { get; init; }
    public string Type { get; init; } = "";
    public int Offset { get; init; }
    public int Length { get; init; }
    public string RawHex { get; init; } = "";
    public Dictionary<string, object?> Fields { get; init; } = new();
}

public static class StationDecoder
{
    public static StationDecodeResult Decode(PlcPointTable table, PlcStationDef station, ReadOnlySpan<byte> frame)
    {
        if (station.Offset + station.Length > frame.Length)
        {
            throw new InvalidOperationException(
                $"工位 {station.Id} 需要字节 [{station.Offset}..{station.Offset + station.Length})，帧长仅 {frame.Length}。");
        }

        var slice = frame.Slice(station.Offset, station.Length);
        var typeDef = table.Types[station.Type];
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        var bigEndian = !string.Equals(table.Plc.ByteOrder, "LittleEndian", StringComparison.OrdinalIgnoreCase);

        foreach (var field in typeDef.Fields)
        {
            fields[field.Name] = ReadField(slice, field, bigEndian);
        }

        return new StationDecodeResult
        {
            StationId = station.Id,
            Type = station.Type,
            Offset = station.Offset,
            Length = station.Length,
            RawHex = Convert.ToHexString(slice),
            Fields = fields
        };
    }

    private static object? ReadField(ReadOnlySpan<byte> block, PlcFieldDef field, bool bigEndian)
    {
        ParseOffset(field.Offset, out var byteOffset, out var bitOffset);
        var kind = field.Type.Trim();

        return kind.ToUpperInvariant() switch
        {
            "BOOL" => (block[byteOffset] & (1 << bitOffset)) != 0,
            "SINT" => (sbyte)block[byteOffset],
            "USINT" or "BYTE" => block[byteOffset],
            "INT" => ReadInt16(block, byteOffset, bigEndian),
            "UINT" or "WORD" => (ushort)ReadInt16(block, byteOffset, bigEndian),
            "DINT" => ReadInt32(block, byteOffset, bigEndian),
            "UDINT" or "DWORD" => (uint)ReadInt32(block, byteOffset, bigEndian),
            _ => throw new InvalidOperationException($"不支持的字段类型 {field.Type} ({field.Name})")
        };
    }

    private static short ReadInt16(ReadOnlySpan<byte> block, int offset, bool bigEndian)
    {
        if (bigEndian)
        {
            return (short)((block[offset] << 8) | block[offset + 1]);
        }

        return (short)(block[offset] | (block[offset + 1] << 8));
    }

    private static int ReadInt32(ReadOnlySpan<byte> block, int offset, bool bigEndian)
    {
        if (bigEndian)
        {
            return (block[offset] << 24) | (block[offset + 1] << 16) | (block[offset + 2] << 8) | block[offset + 3];
        }

        return block[offset] | (block[offset + 1] << 8) | (block[offset + 2] << 16) | (block[offset + 3] << 24);
    }

    public static void ParseOffset(string text, out int byteOffset, out int bitOffset)
    {
        bitOffset = 0;
        var parts = text.Split('.', 2);
        byteOffset = int.Parse(parts[0]);
        if (parts.Length > 1)
        {
            bitOffset = int.Parse(parts[1]);
        }
    }
}
