using System.Net;
using Microsoft.Extensions.Logging;

namespace MWDashboard.Shared.Services;

/// <summary>
/// Graph SDK middleware that observes throttle responses (HTTP 429 / 503) and feeds the per-tenant
/// <see cref="GraphThrottleSignal"/>. It is added as the innermost delegating handler so it sees
/// every raw response — including each retry attempt — before the SDK's built-in RetryHandler acts.
/// It never alters the response; the SDK still performs its own per-call retry/back-off. This handler
/// only surfaces the live signal the collection pipeline uses to throttle the <em>next</em> wave.
/// </summary>
public sealed class ThrottleObservingHandler : DelegatingHandler
{
    private readonly GraphThrottleSignal _signal;
    private readonly ILogger? _logger;
    private readonly string _tenantId;
    private static readonly TimeSpan DefaultBackoff = TimeSpan.FromSeconds(10);

    public ThrottleObservingHandler(GraphThrottleSignal signal, string tenantId, ILogger? logger = null)
    {
        _signal = signal;
        _tenantId = tenantId;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests ||   // 429 — throttled
            response.StatusCode == HttpStatusCode.ServiceUnavailable)  // 503 — transient overload
        {
            // Graph sends Retry-After (delta seconds or an HTTP date) on a genuine throttle.
            var retryAfter = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null)
                ?? DefaultBackoff;

            _signal.RecordThrottle(retryAfter);
            _logger?.LogWarning(
                "Graph throttled tenant {TenantId} ({Status}); backing off {Seconds:F1}s (throttle #{Count})",
                _tenantId, (int)response.StatusCode, retryAfter.TotalSeconds, _signal.ThrottleCount);
        }

        return response;
    }
}
