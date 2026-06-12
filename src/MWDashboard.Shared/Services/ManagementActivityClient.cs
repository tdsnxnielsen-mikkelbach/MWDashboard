using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MWDashboard.Shared.Services;

/// <summary>
/// A single Audit.General content blob descriptor returned by the
/// Office 365 Management Activity API <c>/subscriptions/content</c> endpoint.
/// </summary>
public sealed record AuditContentBlob(
    string ContentType,
    string ContentId,
    string ContentUri,
    DateTime ContentCreated,
    DateTime ContentExpiration);

public interface IManagementActivityClient
{
    /// <summary>Ensures the <c>Audit.General</c> subscription is started for the tenant (idempotent).</summary>
    Task EnsureSubscriptionAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Lists available content blobs for <c>Audit.General</c> in the [startUtc, endUtc] window
    /// (the API requires the window to be ≤ 24 h and within the last 7 days). Follows NextPageUri.
    /// </summary>
    Task<List<AuditContentBlob>> ListContentAsync(string tenantId, DateTime startUtc, DateTime endUtc, CancellationToken ct = default);

    /// <summary>Retrieves and deserializes the audit records contained in a single content blob.</summary>
    Task<List<JsonElement>> GetBlobRecordsAsync(string tenantId, string contentUri, CancellationToken ct = default);
}

/// <summary>
/// Thin client over the Office 365 Management Activity API (<c>manage.office.com</c>) used to pull
/// raw <c>Audit.General</c> events. Unlike Microsoft Graph this is a stateful, subscription-based
/// feed: a subscription must be started per tenant, then content blobs are polled incrementally.
/// Auth uses the same app registration (client credentials) but a different audience.
/// </summary>
public class ManagementActivityClient : IManagementActivityClient
{
    private const string Audience = "https://manage.office.com/.default";
    private const string ContentType = "Audit.General";

    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<ManagementActivityClient> _logger;

    // Cache one credential + token per tenant for the lifetime of a collection run.
    private readonly ConcurrentDictionary<string, ClientSecretCredential> _credentials = new();
    private readonly ConcurrentDictionary<string, AccessToken> _tokens = new();

    public ManagementActivityClient(HttpClient http, IConfiguration config, ILogger<ManagementActivityClient> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    private string BaseUrl(string tenantId) => $"https://manage.office.com/api/v1.0/{tenantId}/activity/feed";

    private async Task<string> GetTokenAsync(string tenantId, CancellationToken ct)
    {
        if (_tokens.TryGetValue(tenantId, out var cached) && cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(2))
            return cached.Token;

        var credential = _credentials.GetOrAdd(tenantId, tid => new ClientSecretCredential(
            tid, _config["AzureAd:ClientId"], _config["AzureAd:ClientSecret"]));

        try
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext([Audience]), ct);
            _tokens[tenantId] = token;
            return token.Token;
        }
        catch (AuthenticationFailedException ex)
        {
            // Most common after moving the app registration: the customer tenant has not
            // admin-consented the (new) app, or the configured client secret is wrong/expired.
            throw new InvalidOperationException(
                $"Could not acquire an Office 365 Management Activity API token for tenant {tenantId}. " +
                "Verify the app registration is admin-consented in this tenant (ActivityFeed.Read on the " +
                "Office 365 Management APIs) and that the configured client secret is current. " +
                $"Underlying error: {ex.Message}", ex);
        }
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string url, string tenantId, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(tenantId, ct));
        return request;
    }

    /// <summary>Sends a request, retrying on HTTP 429 honouring the Retry-After header (bounded attempts).</summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpMethod method, string url, string tenantId, CancellationToken ct, int maxAttempts = 4)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = await CreateRequestAsync(method, url, tenantId, ct);
            var response = await _http.SendAsync(request, ct);

            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= maxAttempts)
                return response;

            var delay = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null)
                ?? TimeSpan.FromSeconds(Math.Min(60, 5 * attempt));
            if (delay < TimeSpan.Zero) delay = TimeSpan.FromSeconds(5);

            _logger.LogWarning("Management Activity API throttled tenant {TenantId} (attempt {Attempt}); retrying in {Delay}s",
                tenantId, attempt, delay.TotalSeconds);
            response.Dispose();
            await Task.Delay(delay, ct);
        }
    }

    public async Task EnsureSubscriptionAsync(string tenantId, CancellationToken ct = default)
    {
        var url = $"{BaseUrl(tenantId)}/subscriptions/start?contentType={ContentType}";
        using var response = await SendWithRetryAsync(HttpMethod.Post, url, tenantId, ct);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Started Audit.General subscription for tenant {TenantId}", tenantId);
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        // AF20024 = "The subscription is already enabled" — that's the desired state, not an error.
        if (body.Contains("AF20024", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("already enabled", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Audit.General subscription already enabled for tenant {TenantId}", tenantId);
            return;
        }

        _logger.LogError("Failed to start Audit.General subscription for tenant {TenantId}: {Status} {Body}",
            tenantId, (int)response.StatusCode, body);
        throw new InvalidOperationException(
            $"Office 365 Management Activity API rejected the Audit.General subscription start for tenant {tenantId} " +
            $"({(int)response.StatusCode} {response.ReasonPhrase}). {DescribeApiError(body)}");
    }

    public async Task<List<AuditContentBlob>> ListContentAsync(string tenantId, DateTime startUtc, DateTime endUtc, CancellationToken ct = default)
    {
        var blobs = new List<AuditContentBlob>();
        var start = startUtc.ToString("yyyy-MM-ddTHH:mm:ss");
        var end = endUtc.ToString("yyyy-MM-ddTHH:mm:ss");
        string? url = $"{BaseUrl(tenantId)}/subscriptions/content?contentType={ContentType}&startTime={start}&endTime={end}";

        var subscriptionRetried = false;
        while (!string.IsNullOrEmpty(url))
        {
            using var response = await SendWithRetryAsync(HttpMethod.Get, url, tenantId, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                // AF20022 = subscription not enabled. Start it once, then retry the same page.
                if (!subscriptionRetried &&
                    (body.Contains("AF20022", StringComparison.OrdinalIgnoreCase) ||
                     body.Contains("subscription", StringComparison.OrdinalIgnoreCase) && response.StatusCode == HttpStatusCode.BadRequest))
                {
                    _logger.LogInformation("No active subscription for tenant {TenantId}; starting and retrying content listing", tenantId);
                    await EnsureSubscriptionAsync(tenantId, ct);
                    subscriptionRetried = true;
                    continue;
                }

                _logger.LogError("Failed to list Audit.General content for tenant {TenantId}: {Status} {Body}",
                    tenantId, (int)response.StatusCode, body);
                throw new InvalidOperationException(
                    $"Office 365 Management Activity API rejected the content listing for tenant {tenantId} " +
                    $"({(int)response.StatusCode} {response.ReasonPhrase}). {DescribeApiError(body)}");
            }

            var items = await response.Content.ReadFromJsonAsync<List<ContentListItem>>(cancellationToken: ct) ?? [];
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.ContentUri)) continue;
                blobs.Add(new AuditContentBlob(
                    item.ContentType ?? ContentType,
                    item.ContentId ?? string.Empty,
                    item.ContentUri,
                    item.ContentCreated,
                    item.ContentExpiration));
            }

            // The API paginates via a NextPageUri response header (not a body property).
            url = response.Headers.TryGetValues("NextPageUri", out var next) ? next.FirstOrDefault() : null;
        }

        return blobs;
    }

    public async Task<List<JsonElement>> GetBlobRecordsAsync(string tenantId, string contentUri, CancellationToken ct = default)
    {
        using var response = await SendWithRetryAsync(HttpMethod.Get, contentUri, tenantId, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to fetch audit blob for tenant {TenantId}: {Status} {Uri}",
                tenantId, (int)response.StatusCode, contentUri);
            return [];
        }

        return await response.Content.ReadFromJsonAsync<List<JsonElement>>(cancellationToken: ct) ?? [];
    }

    private sealed class ContentListItem
    {
        public string? ContentType { get; set; }
        public string? ContentId { get; set; }
        public string? ContentUri { get; set; }
        public DateTime ContentCreated { get; set; }
        public DateTime ContentExpiration { get; set; }
    }

    /// <summary>
    /// Turns a Management Activity API error body (<c>{ "error": { "code", "message" } }</c>) into a
    /// concise, actionable sentence, adding hints for the well-known <c>AFxxxxx</c> failure codes.
    /// </summary>
    private static string DescribeApiError(string body)
    {
        string code = string.Empty, message = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                if (err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String)
                    code = c.GetString() ?? string.Empty;
                if (err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    message = m.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Non-JSON body (e.g. an HTML error page) — fall back to the raw text below.
        }

        var hint = code switch
        {
            _ when code.Contains("AF20022", StringComparison.OrdinalIgnoreCase)
                => "The Audit.General subscription is not enabled for this tenant.",
            _ when code.Contains("AF20023", StringComparison.OrdinalIgnoreCase)
                => "Unified audit logging is turned off for this tenant — enable it (Set-AdminAuditLogConfig -UnifiedAuditLogIngestionEnabled $true) before polling.",
            _ when code.Contains("AF20055", StringComparison.OrdinalIgnoreCase)
                => "The tenant context is invalid — confirm the app is admin-consented in this tenant with ActivityFeed.Read on the Office 365 Management APIs.",
            _ => string.Empty,
        };

        // "Tenant does not exist" is returned when the audit backend has never been provisioned for
        // the tenant — i.e. unified audit logging has never been enabled there. The first-time
        // enablement can take up to 24 h to provision before a subscription can be started.
        if (string.IsNullOrEmpty(hint) &&
            (code.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
             body.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
        {
            hint = "The tenant's audit backend is not provisioned — turn on unified audit logging in the " +
                   "Microsoft Purview portal (Solutions \u2192 Audit \u2192 Start recording user and admin activity). " +
                   "First-time enablement can take up to 24 hours before polling will succeed.";
        }

        // When we recognise the failure, the hint is self-explanatory — don't append the
        // verbose server-side stack trace the API returns in the message.
        if (!string.IsNullOrEmpty(hint))
            return hint;

        var detail = (code, message) switch
        {
            ("", "") => string.IsNullOrWhiteSpace(body) ? "No error detail returned." : body.Trim(),
            ("", _) => message,
            (_, "") => code,
            _ => $"{code}: {message}",
        };

        return Shorten(detail);
    }

    /// <summary>
    /// Strips the server-side stack trace and bounds the length of an API error detail so it is
    /// usable in a UI snackbar / log line.
    /// </summary>
    private static string Shorten(string detail, int maxLength = 300)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "No error detail returned.";

        // The audit API embeds a full .NET stack trace after the exception message; cut it off.
        var cut = detail.IndexOf(" at Microsoft.", StringComparison.OrdinalIgnoreCase);
        if (cut < 0) cut = detail.IndexOf(" at System.", StringComparison.OrdinalIgnoreCase);
        if (cut > 0) detail = detail[..cut];

        detail = detail.Trim();
        if (detail.Length > maxLength)
            detail = detail[..maxLength].TrimEnd() + "…";

        return detail;
    }
}
