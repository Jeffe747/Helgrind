using System.Collections.Concurrent;

namespace Helgrind.Services;

public sealed class TelemetryRateTracker
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<long>> _buckets = new(StringComparer.OrdinalIgnoreCase);

    public bool RegisterAndCheckBurst(string remoteAddress, DateTimeOffset occurredUtc, TimeSpan window, int threshold)
    {
        if (string.IsNullOrWhiteSpace(remoteAddress) || threshold <= 1)
        {
            return false;
        }

        var queue = _buckets.GetOrAdd(remoteAddress, static _ => new ConcurrentQueue<long>());
        var now = occurredUtc.ToUnixTimeMilliseconds();
        var cutoff = occurredUtc.Subtract(window).ToUnixTimeMilliseconds();

        queue.Enqueue(now);
        while (queue.TryPeek(out var tick) && tick < cutoff)
        {
            queue.TryDequeue(out _);
        }

        return queue.Count >= threshold;
    }
}