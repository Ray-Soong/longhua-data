using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Longhua.Collector.Abstractions;
using Longhua.Collector.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Longhua.Collector.Persistence;

public sealed class FileTelemetrySink : ITelemetrySink, IDisposable
{
    private readonly CollectorOptions _options;
    private readonly TimeZoneInfo _timeZone;
    private readonly ILogger<FileTelemetrySink> _logger;
    private readonly ConcurrentDictionary<string, JsonlWriter> _writers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _rotateGate = new();

    public FileTelemetrySink(IOptions<CollectorOptions> options, TimeZoneInfo timeZone, ILogger<FileTelemetrySink> logger)
    {
        _options = options.Value;
        _timeZone = timeZone;
        _logger = logger;
        Directory.CreateDirectory(_options.DataRoot);
    }

    public ValueTask WriteRawAsync(RawFrame frame, CancellationToken cancellationToken)
    {
        var stem = Sanitize(frame.SourceId);
        var relative = Path.Combine(DateFolder(frame.CapturedAtUtc), "raw", stem + ".jsonl");
        var payloadJson = TryParseJson(frame.PayloadText);
        var line = new RawFileRecord
        {
            CapturedAtUtc = frame.CapturedAtUtc,
            SourceId = frame.SourceId,
            Protocol = frame.Protocol.ToString(),
            Identity = frame.Identity,
            Qos = frame.MqttQoS,
            IsRetained = frame.IsRetained,
            ClientId = frame.ClientId,
            Payload = payloadJson,
            PayloadText = payloadJson is null ? frame.PayloadText : null,
            PayloadHex = frame.Protocol == ProtocolKind.S7 ? Convert.ToHexString(frame.PayloadBytes) : null
        };
        WriteLine(relative, JsonSerializer.Serialize(line, JsonDefaults.File));
        return ValueTask.CompletedTask;
    }

    public ValueTask WriteEventAsync(TelemetryRecord record, CancellationToken cancellationToken)
    {
        var stem = Sanitize(string.IsNullOrWhiteSpace(record.FileStem) ? record.DeviceType : record.FileStem);
        var relative = Path.Combine(DateFolder(record.ReceivedAtUtc), "events", stem + ".jsonl");
        WriteLine(relative, JsonSerializer.Serialize(record, JsonDefaults.File));
        return ValueTask.CompletedTask;
    }

    public ValueTask WriteDeadLetterAsync(DeadLetterRecord record, CancellationToken cancellationToken)
    {
        var relative = Path.Combine(DateFolder(record.ReceivedAtUtc), "dead-letter", "parse-error.jsonl");
        WriteLine(relative, JsonSerializer.Serialize(record, JsonDefaults.File));
        return ValueTask.CompletedTask;
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        foreach (var writer in _writers.Values)
        {
            writer.Flush();
        }

        return ValueTask.CompletedTask;
    }

    public void DeleteExpiredDays()
    {
        var keepDays = _options.FileSink.KeepDays;
        if (keepDays <= 0)
        {
            return;
        }

        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _timeZone).Date;
        if (!Directory.Exists(_options.DataRoot))
        {
            return;
        }

        foreach (var dir in Directory.GetDirectories(_options.DataRoot))
        {
            var name = Path.GetFileName(dir);
            if (!DateTime.TryParseExact(name, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var day))
            {
                continue;
            }

            if (today - day.Date > TimeSpan.FromDays(keepDays))
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                    _logger.LogInformation("已删除过期数据目录 {Dir}", dir);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "删除过期目录失败 {Dir}", dir);
                }
            }
        }
    }

    public void Dispose()
    {
        foreach (var writer in _writers.Values)
        {
            writer.Dispose();
        }

        _writers.Clear();
    }

    private string DateFolder(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, _timeZone).ToString("yyyy-MM-dd");

    private void WriteLine(string relativePath, string json)
    {
        var path = Path.GetFullPath(Path.Combine(_options.DataRoot, relativePath));
        var writer = _writers.GetOrAdd(path, CreateWriter);
        writer = RotateIfNeeded(path, writer);
        writer.WriteLine(json);
    }

    private JsonlWriter RotateIfNeeded(string path, JsonlWriter writer)
    {
        var max = _options.FileSink.MaxFileBytes;
        if (max <= 0 || writer.Length < max)
        {
            return writer;
        }

        lock (_rotateGate)
        {
            if (!_writers.TryGetValue(path, out var current))
            {
                current = CreateWriter(path);
                _writers[path] = current;
                return current;
            }

            if (current.Length < max)
            {
                return current;
            }

            current.Dispose();
            var rotated = NextRotatedPath(path);
            File.Move(path, rotated, overwrite: false);
            var created = CreateWriter(path);
            _writers[path] = created;
            _logger.LogInformation("JSONL 按体积滚动 {From} -> {To}", path, rotated);
            return created;
        }
    }

    private static string NextRotatedPath(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var index = 2;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, $"{name}.{index}{ext}");
            index++;
        } while (File.Exists(candidate));

        return candidate;
    }

    private JsonlWriter CreateWriter(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 16 * 1024, FileOptions.SequentialScan);
        var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = false
        };
        return new JsonlWriter(writer, stream);
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var name = new string(chars);
        return string.IsNullOrWhiteSpace(name) ? "unknown" : name.ToLowerInvariant();
    }

    private static JsonElement? TryParseJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class JsonlWriter : IDisposable
    {
        private readonly StreamWriter _writer;
        private readonly FileStream _stream;
        private readonly object _gate = new();

        public JsonlWriter(StreamWriter writer, FileStream stream)
        {
            _writer = writer;
            _stream = stream;
        }

        public long Length
        {
            get
            {
                lock (_gate)
                {
                    _writer.Flush();
                    return _stream.Length;
                }
            }
        }

        public void WriteLine(string json)
        {
            lock (_gate)
            {
                _writer.WriteLine(json);
            }
        }

        public void Flush()
        {
            lock (_gate)
            {
                _writer.Flush();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _writer.Flush();
                _writer.Dispose();
            }
        }
    }

    private sealed class RawFileRecord
    {
        public DateTimeOffset CapturedAtUtc { get; init; }
        public string SourceId { get; init; } = "";
        public string Protocol { get; init; } = "";
        public string Identity { get; init; } = "";
        public int? Qos { get; init; }
        public bool IsRetained { get; init; }
        public string? ClientId { get; init; }
        public JsonElement? Payload { get; init; }
        public string? PayloadText { get; init; }
        public string? PayloadHex { get; init; }
    }
}
