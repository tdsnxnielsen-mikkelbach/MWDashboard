using System.Text.Json;
using Microsoft.Extensions.Logging;
using MWDashboard.Shared.Models;

namespace MWDashboard.Shared.Services;

public interface IDlpAuditCollectionService
{
    /// <summary>
    /// Pulls new Purview DLP audit activity for a single tenant from the Office 365 Management
    /// Activity API (<c>DLP.All</c>), aggregates sensitive-data policy matches by policy per day, and
    /// persists <see cref="DlpEventSnapshot"/>s. Advances the per-tenant cursor so only new content
    /// blobs are pulled on the next run. Tenants not running DLP simply yield no snapshots (the UI
    /// surfaces a "not configured" message in that case).
    /// </summary>
    Task CollectForTenantAsync(string tenantId, string tenantName, CancellationToken ct = default);
}

public class DlpAuditCollectionService : IDlpAuditCollectionService
{
    private const string ContentType = "DLP.All";

    // The Management Activity API only retains content blobs for 7 days and limits each
    // content query to a ≤ 24 h window.
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan MaxQueryWindow = TimeSpan.FromHours(24);

    private readonly IManagementActivityClient _activity;
    private readonly IMauDataService _data;
    private readonly ILogger<DlpAuditCollectionService> _logger;

    public DlpAuditCollectionService(
        IManagementActivityClient activity,
        IMauDataService data,
        ILogger<DlpAuditCollectionService> logger)
    {
        _activity = activity;
        _data = data;
        _logger = logger;
    }

    public async Task CollectForTenantAsync(string tenantId, string tenantName, CancellationToken ct = default)
    {
        _logger.LogInformation("DLP audit collection started for tenant {TenantName} ({TenantId})", tenantName, tenantId);

        await _activity.EnsureSubscriptionAsync(tenantId, ContentType, ct);

        var now = DateTime.UtcNow;
        var earliest = now - RetentionWindow + TimeSpan.FromMinutes(5);
        var cursor = await _data.GetDlpAuditCursorAsync(tenantId);

        // Self-heal a stale cursor: if it is set but no snapshot was ever stored, re-scan the window.
        if (cursor.HasValue)
        {
            var existing = await _data.GetDlpEventsAsync([tenantId], days: (int)RetentionWindow.TotalDays + 1);
            if (existing.Count == 0)
            {
                _logger.LogInformation(
                    "Ignoring stale DLP audit cursor for tenant {TenantId}: no snapshots ingested yet, re-scanning full retention window", tenantId);
                cursor = null;
            }
        }

        var start = cursor.HasValue && cursor.Value > earliest ? cursor.Value : earliest;
        if (start >= now)
        {
            _logger.LogInformation("DLP audit cursor for tenant {TenantId} is current; nothing to pull", tenantId);
            return;
        }

        // Aggregation keyed by (PolicyName, day). Track match counts + highest severity seen.
        var aggregates = new Dictionary<(string PolicyName, DateTime Day), Aggregate>();
        var maxContentCreated = cursor ?? DateTime.MinValue;

        for (var windowStart = start; windowStart < now; windowStart += MaxQueryWindow)
        {
            var windowEnd = windowStart + MaxQueryWindow;
            if (windowEnd > now) windowEnd = now;

            var blobs = await _activity.ListContentAsync(tenantId, ContentType, windowStart, windowEnd, ct);
            foreach (var blob in blobs)
            {
                if (cursor.HasValue && blob.ContentCreated <= cursor.Value)
                    continue;
                if (blob.ContentCreated > maxContentCreated)
                    maxContentCreated = blob.ContentCreated;

                var records = await _activity.GetBlobRecordsAsync(tenantId, blob.ContentUri, ct);
                foreach (var record in records)
                    AccumulateRecord(record, aggregates);
            }
        }

        if (aggregates.Count == 0)
        {
            _logger.LogInformation("No DLP policy matches for tenant {TenantName} in window", tenantName);
        }
        else
        {
            var snapshots = aggregates.Select(kvp => new DlpEventSnapshot
            {
                TenantId = tenantId,
                TenantName = tenantName,
                ReportDate = kvp.Key.Day,
                PolicyName = kvp.Key.PolicyName,
                Severity = kvp.Value.Severity,
                MatchCount = kvp.Value.MatchCount,
                CollectedAt = DateTime.UtcNow,
            }).ToList();

            await _data.SaveDlpEventsAsync(snapshots);
            _logger.LogInformation("Saved {Count} DLP snapshots for tenant {TenantName}", snapshots.Count, tenantName);
        }

        // Advance the cursor only to the newest content actually ingested (never to "now" on an empty run).
        if (maxContentCreated > (cursor ?? DateTime.MinValue))
            await _data.UpdateDlpAuditCursorAsync(tenantId, maxContentCreated);
    }

    private static void AccumulateRecord(JsonElement record, Dictionary<(string, DateTime), Aggregate> aggregates)
    {
        if (!TryGetDateTime(record, "CreationTime", out var creationTime))
            return;

        // A single DLP audit record can carry multiple matched policies, each with its own rules.
        if (!record.TryGetProperty("PolicyDetails", out var policyDetails) || policyDetails.ValueKind != JsonValueKind.Array)
            return;

        foreach (var policy in policyDetails.EnumerateArray())
        {
            var policyName = GetString(policy, "PolicyName");
            if (string.IsNullOrEmpty(policyName))
                policyName = "(unnamed policy)";

            var severity = "Low";
            var matchCount = 0;

            if (policy.TryGetProperty("Rules", out var rules) && rules.ValueKind == JsonValueKind.Array)
            {
                foreach (var rule in rules.EnumerateArray())
                {
                    matchCount++;
                    var ruleSeverity = GetString(rule, "Severity");
                    if (!string.IsNullOrEmpty(ruleSeverity) && SeverityRank(ruleSeverity) > SeverityRank(severity))
                        severity = ruleSeverity;
                }
            }
            else
            {
                matchCount = 1;
            }

            var key = (policyName, creationTime.Date);
            if (!aggregates.TryGetValue(key, out var agg))
            {
                agg = new Aggregate { Severity = severity };
                aggregates[key] = agg;
            }
            agg.MatchCount += matchCount;
            if (SeverityRank(severity) > SeverityRank(agg.Severity))
                agg.Severity = severity;
        }
    }

    private static int SeverityRank(string severity) => severity?.ToLowerInvariant() switch
    {
        "high" => 3,
        "medium" => 2,
        "low" => 1,
        _ => 0,
    };

    private static string GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool TryGetDateTime(JsonElement element, string property, out DateTime value)
    {
        value = default;
        return element.TryGetProperty(property, out var prop)
            && prop.ValueKind == JsonValueKind.String
            && DateTime.TryParse(prop.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out value);
    }

    private sealed class Aggregate
    {
        public int MatchCount { get; set; }
        public string Severity { get; set; } = "Low";
    }
}
