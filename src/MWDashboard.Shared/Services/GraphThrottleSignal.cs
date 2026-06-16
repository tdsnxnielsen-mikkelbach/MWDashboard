namespace MWDashboard.Shared.Services;

/// <summary>
/// Live, in-process throttle signal for a single tenant's Microsoft Graph traffic. Written by
/// <see cref="ThrottleObservingHandler"/> when Graph returns 429/503, read by the adaptive
/// concurrency gate in the collection pipeline. Thread-safe via <see cref="Interlocked"/>.
///
/// This is the real-time alternative to querying Application Insights: the 429s flow through the
/// collector's own HTTP pipeline, so back-pressure is observed at the source with no ingestion lag
/// and no external dependency. App Insights still receives the same telemetry for offline review.
/// </summary>
public sealed class GraphThrottleSignal
{
    /// <summary>Upper bound on a single back-off, guarding against a pathological Retry-After value.</summary>
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(120);

    private long _lastThrottleTicks; // DateTime.UtcNow.Ticks of the most recent throttle
    private long _resumeAtTicks;     // earliest UTC time we should resume issuing requests
    private long _throttleCount;     // total throttles observed this run (diagnostics)

    /// <summary>Total number of 429/503 responses observed for this tenant during the run.</summary>
    public long ThrottleCount => Interlocked.Read(ref _throttleCount);

    /// <summary>Records a throttle response, honoring the server's Retry-After (capped).</summary>
    public void RecordThrottle(TimeSpan retryAfter)
    {
        if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.Zero;
        if (retryAfter > MaxBackoff) retryAfter = MaxBackoff;

        Interlocked.Increment(ref _throttleCount);
        Interlocked.Exchange(ref _lastThrottleTicks, DateTime.UtcNow.Ticks);

        // Keep the furthest-out resume time if several throttles land concurrently.
        var resumeAt = DateTime.UtcNow.Add(retryAfter).Ticks;
        long current;
        do
        {
            current = Interlocked.Read(ref _resumeAtTicks);
            if (resumeAt <= current) break;
        }
        while (Interlocked.CompareExchange(ref _resumeAtTicks, resumeAt, current) != current);
    }

    /// <summary>How long the caller should pause before the next request (<see cref="TimeSpan.Zero"/> if clear).</summary>
    public TimeSpan RetryAfterDelay()
    {
        var resumeAt = new DateTime(Interlocked.Read(ref _resumeAtTicks), DateTimeKind.Utc);
        var delay = resumeAt - DateTime.UtcNow;
        return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
    }

    /// <summary>True if a throttle was observed within the supplied recent window.</summary>
    public bool ThrottledWithin(TimeSpan window)
    {
        var lastTicks = Interlocked.Read(ref _lastThrottleTicks);
        if (lastTicks == 0) return false;
        var last = new DateTime(lastTicks, DateTimeKind.Utc);
        return DateTime.UtcNow - last < window;
    }
}
