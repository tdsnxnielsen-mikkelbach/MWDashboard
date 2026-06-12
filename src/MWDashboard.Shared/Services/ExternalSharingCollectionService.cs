using System.Text.Json;
using Microsoft.Extensions.Logging;
using MWDashboard.Shared.Models;

namespace MWDashboard.Shared.Services;

public interface IExternalSharingCollectionService
{
    /// <summary>
    /// Pulls new SharePoint/OneDrive external-sharing audit activity for a single tenant from the
    /// Office 365 Management Activity API (<c>Audit.SharePoint</c>), aggregates it by share type per
    /// day, and persists <see cref="ExternalSharingSnapshot"/>s. Advances the per-tenant cursor so
    /// only new content blobs are pulled on the next run.
    /// </summary>
    Task CollectForTenantAsync(string tenantId, string tenantName, CancellationToken ct = default);
}

public class ExternalSharingCollectionService : IExternalSharingCollectionService
{
    private const string ContentType = "Audit.SharePoint";

    // The Management Activity API only retains content blobs for 7 days and limits each
    // content query to a ≤ 24 h window.
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan MaxQueryWindow = TimeSpan.FromHours(24);

    private readonly IManagementActivityClient _activity;
    private readonly IMauDataService _data;
    private readonly ILogger<ExternalSharingCollectionService> _logger;

    public ExternalSharingCollectionService(
        IManagementActivityClient activity,
        IMauDataService data,
        ILogger<ExternalSharingCollectionService> logger)
    {
        _activity = activity;
        _data = data;
        _logger = logger;
    }

    public async Task CollectForTenantAsync(string tenantId, string tenantName, CancellationToken ct = default)
    {
        _logger.LogInformation("External sharing audit collection started for tenant {TenantName} ({TenantId})", tenantName, tenantId);

        await _activity.EnsureSubscriptionAsync(tenantId, ContentType, ct);

        var now = DateTime.UtcNow;
        var earliest = now - RetentionWindow + TimeSpan.FromMinutes(5);
        var cursor = await _data.GetSharePointAuditCursorAsync(tenantId);

        // Self-heal a stale cursor: if it is set but no snapshot was ever stored, re-scan the window.
        if (cursor.HasValue)
        {
            var existing = await _data.GetExternalSharingAsync([tenantId], days: (int)RetentionWindow.TotalDays + 1);
            if (existing.Count == 0)
            {
                _logger.LogInformation(
                    "Ignoring stale external-sharing cursor for tenant {TenantId}: no snapshots ingested yet, re-scanning full retention window", tenantId);
                cursor = null;
            }
        }

        var start = cursor.HasValue && cursor.Value > earliest ? cursor.Value : earliest;
        if (start >= now)
        {
            _logger.LogInformation("External sharing audit cursor for tenant {TenantId} is current; nothing to pull", tenantId);
            return;
        }

        // Aggregation keyed by (ShareType, day). Track distinct actors + event counts.
        var aggregates = new Dictionary<(string ShareType, DateTime Day), Aggregate>();
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
            _logger.LogInformation("No external sharing audit activity for tenant {TenantName} in window", tenantName);
        }
        else
        {
            var snapshots = aggregates.Select(kvp => new ExternalSharingSnapshot
            {
                TenantId = tenantId,
                TenantName = tenantName,
                ReportDate = kvp.Key.Day,
                ShareType = kvp.Key.ShareType,
                EventCount = kvp.Value.EventCount,
                DistinctUsers = kvp.Value.Users.Count,
                CollectedAt = DateTime.UtcNow,
            }).ToList();

            await _data.SaveExternalSharingAsync(snapshots);
            _logger.LogInformation("Saved {Count} external sharing snapshots for tenant {TenantName}", snapshots.Count, tenantName);
        }

        // Advance the cursor only to the newest content actually ingested (never to "now" on an empty run).
        if (maxContentCreated > (cursor ?? DateTime.MinValue))
            await _data.UpdateSharePointAuditCursorAsync(tenantId, maxContentCreated);
    }

    private static void AccumulateRecord(JsonElement record, Dictionary<(string, DateTime), Aggregate> aggregates)
    {
        var shareType = ClassifyShare(record);
        if (shareType == null)
            return;

        if (!TryGetDateTime(record, "CreationTime", out var creationTime))
            return;

        var key = (shareType, creationTime.Date);
        if (!aggregates.TryGetValue(key, out var agg))
        {
            agg = new Aggregate();
            aggregates[key] = agg;
        }
        agg.EventCount++;
        var userId = GetString(record, "UserId");
        if (!string.IsNullOrEmpty(userId))
            agg.Users.Add(userId);
    }

    /// <summary>
    /// Classifies a SharePoint/OneDrive audit record into an external-exposure bucket, or returns
    /// <c>null</c> when the event does not represent external/broad sharing.
    /// </summary>
    private static string? ClassifyShare(JsonElement record)
    {
        var op = GetString(record, "Operation");
        if (string.IsNullOrEmpty(op))
            return null;

        // "Anyone"/anonymous links — the broadest exposure.
        if (op.Contains("AnonymousLink", StringComparison.OrdinalIgnoreCase))
            return "Anonymous";

        // "People in your organization" links.
        if (op.Contains("CompanyLink", StringComparison.OrdinalIgnoreCase))
            return "Organization";

        // Sharing with specific external/guest people.
        if (op.Equals("SharingInvitationCreated", StringComparison.OrdinalIgnoreCase) ||
            op.Equals("AddedToSecureLink", StringComparison.OrdinalIgnoreCase) ||
            op.Equals("SecureLinkCreated", StringComparison.OrdinalIgnoreCase) ||
            op.Equals("SharingSet", StringComparison.OrdinalIgnoreCase))
        {
            var target = GetString(record, "TargetUserOrGroupType");
            if (target.Equals("Guest", StringComparison.OrdinalIgnoreCase))
                return "External";
        }

        return null;
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
        public int EventCount { get; set; }
    }
}
