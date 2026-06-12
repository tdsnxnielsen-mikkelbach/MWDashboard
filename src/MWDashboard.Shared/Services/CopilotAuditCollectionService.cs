using System.Text.Json;
using Microsoft.Extensions.Logging;
using MWDashboard.Shared.Models;

namespace MWDashboard.Shared.Services;

public interface ICopilotAuditCollectionService
{
    /// <summary>
    /// Pulls new Copilot-Chat audit activity for a single tenant from the Office 365 Management
    /// Activity API, aggregates it per surface per day, and persists <see cref="CopilotChatUsageSnapshot"/>s.
    /// Advances the per-tenant cursor so only new content blobs are pulled on the next run.
    /// </summary>
    Task CollectForTenantAsync(string tenantId, string tenantName, CancellationToken ct = default);
}

public class CopilotAuditCollectionService : ICopilotAuditCollectionService
{
    // AppHost surfaces that map to the free "Microsoft 365 Copilot Chat" experience.
    private static readonly HashSet<string> CopilotChatAppHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "BizChat", "Bing", "Edge", "Office", "M365App", "OfficeCopilotSearchAnswer",
    };

    // The Management Activity API only retains content blobs for 7 days and limits each
    // content query to a ≤ 24 h window.
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan MaxQueryWindow = TimeSpan.FromHours(24);

    private readonly IManagementActivityClient _activity;
    private readonly IGraphReportService _graph;
    private readonly IMauDataService _data;
    private readonly ILogger<CopilotAuditCollectionService> _logger;

    public CopilotAuditCollectionService(
        IManagementActivityClient activity,
        IGraphReportService graph,
        IMauDataService data,
        ILogger<CopilotAuditCollectionService> logger)
    {
        _activity = activity;
        _graph = graph;
        _data = data;
        _logger = logger;
    }

    public async Task CollectForTenantAsync(string tenantId, string tenantName, CancellationToken ct = default)
    {
        _logger.LogInformation("Copilot Chat audit collection started for tenant {TenantName} ({TenantId})", tenantName, tenantId);

        await _activity.EnsureSubscriptionAsync(tenantId, ct);

        var now = DateTime.UtcNow;
        var earliest = now - RetentionWindow + TimeSpan.FromMinutes(5); // small buffer inside the retention edge
        var cursor = await _data.GetCopilotAuditCursorAsync(tenantId);

        // Self-heal a stale/poisoned cursor. A previous version advanced the cursor to "now" even when
        // no audit content had been ingested yet (e.g. the tenant was polled before its Copilot audit
        // content had propagated — that can take >12h after enabling). That left the cursor pointing at
        // the leading edge of the retention window, so every later run queried an empty [cursor, now]
        // window and never found anything. If the cursor is set but we have never stored a single
        // snapshot for this tenant, ignore it and re-scan the full retention window.
        if (cursor.HasValue)
        {
            var existing = await _data.GetCopilotChatUsageAsync([tenantId], days: (int)RetentionWindow.TotalDays + 1);
            if (existing.Count == 0)
            {
                _logger.LogInformation(
                    "Ignoring stale Copilot audit cursor for tenant {TenantId}: no snapshots ingested yet, re-scanning full retention window", tenantId);
                cursor = null;
            }
        }

        var start = cursor.HasValue && cursor.Value > earliest ? cursor.Value : earliest;

        if (start >= now)
        {
            _logger.LogInformation("Copilot Chat audit cursor for tenant {TenantId} is current; nothing to pull", tenantId);
            return;
        }

        // Aggregation keyed by (AppHost, day). Track distinct users + interaction counts.
        var aggregates = new Dictionary<(string AppHost, DateTime Day), Aggregate>();
        var maxContentCreated = cursor ?? DateTime.MinValue;

        for (var windowStart = start; windowStart < now; windowStart += MaxQueryWindow)
        {
            var windowEnd = windowStart + MaxQueryWindow;
            if (windowEnd > now) windowEnd = now;

            var blobs = await _activity.ListContentAsync(tenantId, windowStart, windowEnd, ct);
            foreach (var blob in blobs)
            {
                // Skip blobs already processed in a previous run.
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
            _logger.LogInformation("No Copilot Chat audit activity for tenant {TenantName} in window", tenantName);
        }
        else
        {
            var licensedUpns = await _graph.GetCopilotLicensedUpnsAsync(tenantId);

            var snapshots = aggregates.Select(kvp => new CopilotChatUsageSnapshot
            {
                TenantId = tenantId,
                TenantName = tenantName,
                ReportDate = kvp.Key.Day,
                AppHost = kvp.Key.AppHost,
                ActiveUsers = kvp.Value.Users.Count,
                InteractionCount = kvp.Value.InteractionCount,
                UnlicensedUsers = kvp.Value.Users.Count(u => !licensedUpns.Contains(u)),
                CollectedAt = DateTime.UtcNow,
            }).ToList();

            await _data.SaveCopilotChatUsageAsync(snapshots);
            _logger.LogInformation("Saved {Count} Copilot Chat usage snapshots for tenant {TenantName}", snapshots.Count, tenantName);
        }

        // Advance the cursor only to the newest content we actually ingested. Never push it to "now"
        // on an empty run — doing so would skip past audit content that has not propagated yet and is
        // the cause of tenants that perpetually report "no activity". Leaving the cursor where it is
        // means the next run re-scans the retention window and picks up late-arriving data.
        if (maxContentCreated > (cursor ?? DateTime.MinValue))
            await _data.UpdateCopilotAuditCursorAsync(tenantId, maxContentCreated);
    }

    private void AccumulateRecord(JsonElement record, Dictionary<(string, DateTime), Aggregate> aggregates)
    {
        var workload = GetString(record, "Workload");
        if (!string.Equals(workload, "Copilot", StringComparison.OrdinalIgnoreCase))
            return;

        var appHost = GetString(record, "AppHost");
        if (string.IsNullOrEmpty(appHost) || !CopilotChatAppHosts.Contains(appHost))
            return;

        var userId = GetString(record, "UserId");
        if (string.IsNullOrEmpty(userId))
            return;

        if (!TryGetDateTime(record, "CreationTime", out var creationTime))
            return;

        var key = (appHost, creationTime.Date);
        if (!aggregates.TryGetValue(key, out var agg))
        {
            agg = new Aggregate();
            aggregates[key] = agg;
        }
        agg.Users.Add(userId);
        agg.InteractionCount++;
    }

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
        public HashSet<string> Users { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int InteractionCount { get; set; }
    }
}
