using System.Text.Json;
using System.Text.Json.Serialization;

namespace Longhua.Collector;

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions File = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static readonly JsonSerializerOptions Config = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
