using System.Collections.Concurrent;

namespace Longhua.Collector;

public sealed class CollectorMetrics
{
    private readonly ConcurrentDictionary<string, SourceCounters> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly CounterValue _events = new();
    private readonly CounterValue _deadLetters = new();
    private readonly CounterValue _duplicates = new();

    public void IncrementReceived(string sourceId) => Get(sourceId).Received.Increment();

    public void IncrementDropped(string sourceId) => Get(sourceId).Dropped.Increment();

    public void IncrementEvents() => _events.Increment();

    public void IncrementDeadLetters() => _deadLetters.Increment();

    public void IncrementDuplicates() => _duplicates.Increment();

    public void MarkMqttConnected(string sourceId) => Get(sourceId).Connected = true;

    public void MarkMqttDisconnected(string sourceId) => Get(sourceId).Connected = false;

    public long EventCount => _events.Value;

    public long DeadLetterCount => _deadLetters.Value;

    public long DuplicateCount => _duplicates.Value;

    public IReadOnlyDictionary<string, SourceSnapshot> Snapshot()
    {
        return _sources.ToDictionary(
            pair => pair.Key,
            pair => new SourceSnapshot(pair.Value.Received.Value, pair.Value.Dropped.Value, pair.Value.Connected),
            StringComparer.OrdinalIgnoreCase);
    }

    private SourceCounters Get(string sourceId) => _sources.GetOrAdd(sourceId, _ => new SourceCounters());

    public sealed record SourceSnapshot(long Received, long Dropped, bool Connected);

    private sealed class SourceCounters
    {
        public CounterValue Received { get; } = new();
        public CounterValue Dropped { get; } = new();
        public volatile bool Connected;
    }
}

internal sealed class CounterValue
{
    private long _value;

    public long Value => Interlocked.Read(ref _value);

    public void Increment() => Interlocked.Increment(ref _value);
}
