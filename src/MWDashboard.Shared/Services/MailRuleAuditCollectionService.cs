using System.Text.Json;
using Microsoft.Extensions.Logging;
using MWDashboard.Shared.Models;

namespace MWDashboard.Shared.Services;

public interface IMailRuleAuditCollectionService
{
    /// <summary>
    /// Pulls new Exchange admin audit activity for a single tenant from the Office 365 Management
    /// Activity API (<c>Audit.Exchange</c>), detects suspicious inbox-rule / auto-forwarding changes
    /// (a common business-email-compromise indicator), aggregates them by rule type per day, and
    /// persists <see cref="MailRuleEventSnapshot"/>s. Advances the per-tenant cursor so only new
    /// content blobs are pulled on the next run.
    /// </summary>
    Task CollectForTenantAsync(string tenantId, string tenantName, CancellationToken ct = default);
}

public class MailRuleAuditCollectionService : IMailRuleAuditCollectionService
{
    private const string ContentType = "Audit.Exchange";

    // The Management Activity API only retains content blobs for 7 days and limits each
    // content query to a ≤ 24 h window.
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan MaxQueryWindow = TimeSpan.FromHours(24);

    private readonly IManagementActivityClient _activity;
    private readonly IMauDataService _data;
    private readonly ILogger<MailRuleAuditCollectionService> _logger;

    public MailRuleAuditCollectionService(
        IManagementActivityClient activity,
        IMauDataService data,
        ILogger<MailRuleAuditCollectionService> logger)
    {
        _activity = activity;
        _data = data;
        _logger = logger;
    }

    public async Task CollectForTenantAsync(string tenantId, string tenantName, CancellationToken ct = default)
    {
        _logger.LogInformation("Mail-rule audit collection started for tenant {TenantName} ({TenantId})", tenantName, tenantId);

        await _activity.EnsureSubscriptionAsync(tenantId, ContentType, ct);

        var now = DateTime.UtcNow;
        var earliest = now - RetentionWindow + TimeSpan.FromMinutes(5);
        var cursor = await _data.GetExchangeAuditCursorAsync(tenantId);

        // Self-heal a stale cursor: if it is set but no snapshot was ever stored, re-scan the window.
        if (cursor.HasValue)
        {
            var existing = await _data.GetMailRuleEventsAsync([tenantId], days: (int)RetentionWindow.TotalDays + 1);
            if (existing.Count == 0)
            {
                _logger.LogInformation(
                    "Ignoring stale Exchange audit cursor for tenant {TenantId}: no snapshots ingested yet, re-scanning full retention window", tenantId);
                cursor = null;
            }
        }

        var start = cursor.HasValue && cursor.Value > earliest ? cursor.Value : earliest;
        if (start >= now)
        {
            _logger.LogInformation("Exchange audit cursor for tenant {TenantId} is current; nothing to pull", tenantId);
            return;
        }

        // Aggregation keyed by (RuleType, day). Track distinct mailboxes + event counts.
        var aggregates = new Dictionary<(string RuleType, DateTime Day), Aggregate>();
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
            _logger.LogInformation("No suspicious mail-rule activity for tenant {TenantName} in window", tenantName);
        }
        else
        {
            var snapshots = aggregates.Select(kvp => new MailRuleEventSnapshot
            {
                TenantId = tenantId,
                TenantName = tenantName,
                ReportDate = kvp.Key.Day,
                RuleType = kvp.Key.RuleType,
                EventCount = kvp.Value.EventCount,
                DistinctMailboxes = kvp.Value.Mailboxes.Count,
                CollectedAt = DateTime.UtcNow,
            }).ToList();

            await _data.SaveMailRuleEventsAsync(snapshots);
            _logger.LogInformation("Saved {Count} mail-rule snapshots for tenant {TenantName}", snapshots.Count, tenantName);
        }

        // Advance the cursor only to the newest content actually ingested (never to "now" on an empty run).
        if (maxContentCreated > (cursor ?? DateTime.MinValue))
            await _data.UpdateExchangeAuditCursorAsync(tenantId, maxContentCreated);
    }

    private static void AccumulateRecord(JsonElement record, Dictionary<(string, DateTime), Aggregate> aggregates)
    {
        var ruleType = ClassifyRule(record);
        if (ruleType == null)
            return;

        if (!TryGetDateTime(record, "CreationTime", out var creationTime))
            return;

        var key = (ruleType, creationTime.Date);
        if (!aggregates.TryGetValue(key, out var agg))
        {
            agg = new Aggregate();
            aggregates[key] = agg;
        }
        agg.EventCount++;

        // Identify the affected mailbox (use the object cmdlet target, falling back to the actor).
        var mailbox = GetString(record, "MailboxOwnerUPN");
        if (string.IsNullOrEmpty(mailbox))
            mailbox = GetString(record, "UserId");
        if (!string.IsNullOrEmpty(mailbox))
            agg.Mailboxes.Add(mailbox);
    }

    /// <summary>
    /// Classifies an Exchange admin-audit record into a suspicious mail-rule bucket
    /// (Forwarding / Redirect / Delete), or returns <c>null</c> when it is not a rule change of interest.
    /// </summary>
    private static string? ClassifyRule(JsonElement record)
    {
        var op = GetString(record, "Operation");
        if (string.IsNullOrEmpty(op))
            return null;

        var isInboxRule = op.Equals("New-InboxRule", StringComparison.OrdinalIgnoreCase) ||
                          op.Equals("Set-InboxRule", StringComparison.OrdinalIgnoreCase) ||
                          op.Equals("Enable-InboxRule", StringComparison.OrdinalIgnoreCase);
        var isMailboxFwd = op.Equals("Set-Mailbox", StringComparison.OrdinalIgnoreCase);

        if (!isInboxRule && !isMailboxFwd)
            return null;

        var parameters = GetParameterNames(record);

        if (isMailboxFwd)
        {
            // Tenant-wide / mailbox auto-forwarding configured by an admin or attacker.
            if (parameters.Contains("ForwardingSmtpAddress") || parameters.Contains("ForwardingAddress"))
                return "Forwarding";
            return null;
        }

        // Inbox rules — order matters: forwarding/redirect are the higher-signal BEC indicators.
        if (parameters.Contains("ForwardTo") || parameters.Contains("ForwardAsAttachmentTo"))
            return "Forwarding";
        if (parameters.Contains("RedirectTo"))
            return "Redirect";
        if (parameters.Contains("DeleteMessage"))
            return "Delete";

        return null;
    }

    /// <summary>
    /// Extracts the set of parameter names from an Exchange cmdlet audit record. Parameters are stored
    /// as an array of <c>{ Name, Value }</c> objects under the <c>Parameters</c> property.
    /// </summary>
    private static HashSet<string> GetParameterNames(JsonElement record)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (record.TryGetProperty("Parameters", out var parameters) && parameters.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in parameters.EnumerateArray())
            {
                if (p.TryGetProperty("Name", out var name) && name.ValueKind == JsonValueKind.String)
                {
                    var n = name.GetString();
                    if (!string.IsNullOrEmpty(n))
                        names.Add(n);
                }
            }
        }
        return names;
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
        public HashSet<string> Mailboxes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int EventCount { get; set; }
    }
}
