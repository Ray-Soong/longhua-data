using System.Security.Cryptography;
using System.Text;
using Longhua.Collector.Abstractions;

namespace Longhua.Collector.Pipeline;

public sealed class Deduplicator
{
    private readonly int _capacity;
    private readonly LinkedList<string> _order = new();
    private readonly HashSet<string> _keys;

    public Deduplicator(int capacity)
    {
        _capacity = Math.Max(16, capacity);
        _keys = new HashSet<string>(StringComparer.Ordinal);
    }

    public bool IsDuplicate(TelemetryRecord record)
    {
        var key = BuildKey(record);
        lock (_keys)
        {
            if (!_keys.Add(key))
            {
                return true;
            }

            _order.AddLast(key);
            while (_keys.Count > _capacity && _order.First is { } first)
            {
                _keys.Remove(first.Value);
                _order.RemoveFirst();
            }
        }

        return false;
    }

    private static string BuildKey(TelemetryRecord record)
    {
        var ts = record.SourceTimestamp?.ToString("O") ?? "";
        var payload = record.Payload?.GetRawText() ?? "";
        var raw = $"{record.Identity}\n{ts}\n{payload}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}
