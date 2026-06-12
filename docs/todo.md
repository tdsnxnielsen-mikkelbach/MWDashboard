# TODO — Future Enhancements

## Partner Center API Integration (Cost/Billing Data)

**Priority**: High  
**Status**: Not started  
**Context**: The current consumption score is a proxy built from usage metrics (MAU, activity, storage, workload breadth). Microsoft 365 is per-user licensed, so there's no usage-based billing via Graph API. However, for CSP partners, the **Partner Center API** exposes actual billing/cost data per customer.

### What it enables
- True cost per tenant (monthly invoice amounts)
- License cost vs. consumption value comparison
- Cost-per-active-user metric
- ROI calculation per workload
- "Over-licensed" vs. "under-licensed" detection with real spend data

### API Details
- **Endpoint**: Partner Center REST API (`https://api.partnercenter.microsoft.com`)
- **Auth**: Separate app registration with Partner Center API permissions (different from Graph)
- **Key endpoints**:
  - `GET /v1/customers/{customer-id}/subscriptions` — subscriptions per customer
  - `GET /v1/invoices/{invoice-id}/lineitems` — billing line items
  - `GET /v1/customers/{customer-id}/subscriptions/usagerecords` — usage-based records
  - `GET /v1/customers/{customer-id}/orders` — order history

### Implementation Plan
1. Add Partner Center API authentication (separate `ClientSecretCredential` or Partner Center token flow)
2. Add `BillingSnapshot` model (TenantId, InvoiceMonth, TotalCharge, Currency, LicenseCount)
3. Map Partner Center `customer-id` to our `TenantId` (may need a mapping table or use tenant domain matching)
4. Add cost metrics to the Consumption page (cost/user, cost trend, cost vs. score correlation)
5. New page? `/cost` or extend `/consumption` with a "Cost" tab

### Prerequisites
- Partner Center account with admin agent or sales agent role
- App registration with Partner Center API access
- Mapping between Partner Center customers and registered tenants

---

## Unlicensed Copilot Chat Usage via Office 365 Management Activity API

**Priority**: Medium
**Status**: Implemented (collection pipeline + dedicated container app + `/copilot` UI)
**Context**: The Microsoft Graph Copilot usage reports API (`getMicrosoft365CopilotUsageUserDetail`) **only returns data for users holding a Microsoft 365 Copilot license**. Free, unlicensed **Copilot Chat** usage (e.g. on Business Standard tenants without the Copilot add-on) is *not* exposed by Graph. The only programmatic source is the **Office 365 Management Activity API** (raw `CopilotInteraction` audit events) or Microsoft Purview audit logs (`Search-UnifiedAuditLog`). This is the only data source in the app that isn't a stateless Graph read, so it warrants its own topic.

### Why it's a separate, larger project
Unlike every existing feature, this is **not** a new Graph endpoint slotted into `GraphReportService`. It's a different API surface, a different auth audience, a new consent scope, a **stateful subscription lifecycle**, and a per-event aggregation pipeline with a hard data-retention window.

| | Today (Graph reports) | Management Activity API |
|---|---|---|
| Endpoint | `graph.microsoft.com` | `manage.office.com/api/v1.0/{tenantId}/activity/feed` |
| Data shape | Pre-aggregated CSV (full dataset each pull) | Raw, per-event audit blobs you aggregate yourself |
| Model | Stateless GET | **Stateful subscription** + incremental content-blob retrieval |
| Permission | `Reports.Read.All` (Graph) | `ActivityFeed.Read` (**Office 365 Management APIs** resource) |
| Latency | Current as of report-refresh date | Up to **12 h** before first blobs appear; events out of order |
| Retention | N/A | Content blobs expire after **7 days** — miss the window = data lost |

### What the data looks like
Copilot Chat interactions land in `Audit.General` content blobs as records with:
- `Operation: "CopilotInteraction"`, `Workload: "Copilot"`, `RecordType: "CopilotInteraction"`
- `AppHost` identifies the surface — the **free Copilot Chat** values are `BizChat`, `Bing`, `Edge`, `Office`, `M365App`, `OfficeCopilotSearchAnswer` (all map to "Microsoft 365 Copilot Chat")
- `AppIdentity` e.g. `Copilot.MicrosoftCopilot.BizChat`
- `UserId`, `CreationTime` — aggregate into active-user / interaction counts
- The audit record does **not** state whether the user is licensed; cross-reference `UserId` against assigned Copilot SKUs (already collected in `LicenseSnapshot`) to split licensed vs. unlicensed.

### Prerequisites (customer-side, per tenant)
- **Unified audit logging must be ON** (default-on for new tenants, but not guaranteed). *(verified / enabled)*
- **New admin-consented permission**: `ActivityFeed.Read` on the *Office 365 Management APIs* resource — added to the app registration and re-consented.
- Audit (Standard) covers Microsoft Copilot for free; only *non-Microsoft* AI apps are pay-as-you-go, so Copilot Chat itself adds no billing.

### Implementation Plan
> **Delivered** in `MWDashboard.CopilotAudit` — a dedicated container app (HTTP `POST /collect/{tenantId}` + internal 24 h cron scheduler), wired into infra the same way as the on-demand collector (placeholder image → real image on first deploy). The `/copilot` page surfaces the data with KPIs + a trend chart and an on-demand "Poll Copilot Chat" button.

1. **Auth / consent**
   - [x] Add `ActivityFeed.Read` (Office 365 Management APIs) to the app registration. *(manual, customer-side — done)*
   - [x] Add the scope to `GraphPermissions`. Consent-URL builder needs no change (the `/adminconsent` `.default` URL grants every configured app permission); `CheckMissingPermissionsAsync` intentionally **not** changed (it only exercises Graph endpoints).
   - [x] Document in `docs/permissions.md`.
2. **New client + subscription lifecycle** (the genuinely new part)
   - [x] `ManagementActivityClient` — raw `HttpClient` + `ClientSecretCredential` for the `manage.office.com` audience (per-tenant token cache).
   - [x] On first run per tenant: `POST /subscriptions/start?contentType=Audit.General`; handles "already enabled" (AF20024) and lazily starts on AF20022.
   - [x] Polling: `GET /subscriptions/content` over rolling ≤24 h windows, follows `NextPageUri` pagination, then `GET`s each `contentUri` blob.
   - [x] Persists a **cursor** (`TenantInfo.CopilotAuditCursorUtc`, last processed `contentCreated`) so only new blobs are pulled and the cursor never moves backwards.
3. **Parsing + aggregation**
   - [x] Deserializes blobs; filters to `Workload == "Copilot"` + the BizChat `AppHost` set; dedupes distinct `UserId`/day; splits licensed vs. unlicensed against assigned Copilot SKUs (`GraphReportService.GetCopilotLicensedUpnsAsync`).
4. **New model + migration** (followed the `new-model` skill)
   - [x] `CopilotChatUsageSnapshot` (`TenantId`, `TenantName`, `ReportDate`, `AppHost`, `ActiveUsers`, `InteractionCount`, `UnlicensedUsers`, `CollectedAt`) with composite unique index `(TenantId, AppHost, ReportDate)`.
   - [x] Added DbSet (#31), save/upsert + query methods, cache integration + invalidation (`copilot-chat` key, 60 min TTL).
5. **Collection wiring**
   - [x] Runs in the dedicated `MWDashboard.CopilotAudit` container app: on-demand HTTP endpoint **and** an internal `PeriodicTimer` cron that loops active tenants (keeps each cursor advancing within the 7-day window).
6. **UI** — Copilot page (`/copilot`)
   - [x] Added a **"Copilot Chat (unlicensed)"** section: KPI cards (unlicensed/active chat users for the latest day, 30-day interactions, active surfaces), a daily activity trend chart (interactions + active + unlicensed users), and a per-surface breakdown table. Softened the licensed empty-state caveat to point at the new section.
   - [x] Guarded the trend ApexChart series against empty data; the section shows a clear info message when no audit activity has been collected yet (covers the 12 h latency / audit-logging-off / 7-day-retention cases).
   - [x] Added a **"Poll Copilot Chat"** button that triggers on-demand collection for the selected tenant(s) via the `MWDashboard.CopilotAudit` container (`HttpCopilotAuditClient`, local fallback), then reloads.
7. **Resilience**
   - [x] Handles AF20022 (no subscription → start + retry), AF429 throttling with `Retry-After` backoff, and out-of-order events (cursor keyed on blob `contentCreated`; late events arrive in newer blobs).

### Risks
- **7-day retention**: a stalled collector loses that data permanently. *Mitigated*: the `MWDashboard.CopilotAudit` container runs `minReplicas: 1` with an internal 24 h cron so each tenant's cursor keeps advancing inside the window even without traffic; the cursor is seeded at `now - 7 days + 5 min` on first run.
- **12h+ initial latency** after subscription start. *Surfaced in UI*: the empty-state and per-surface caption both warn that audit events can lag usage by up to 12 hours.
- **Throttling at scale** across many tenants (2,000 req/min baseline per tenant). *Mitigated*: `ManagementActivityClient` honors `Retry-After` with bounded backoff.
- **Audit logging may be disabled** on a tenant. *Surfaced in UI*: the empty-state explicitly lists "unified audit logging off" as a cause and links the required `ActivityFeed.Read` permission.

---

## Other Future Items

- [x] Redis caching for consumption/storage queries (TTL 15 min) — `CachedMauDataService` decorator wraps `MauDataService`
- [x] M365 App Platform usage (`getM365AppUserCounts`) for cross-app engagement metrics — model, Graph endpoint, collection pipeline
- [x] Export consumption report to CSV — `/api/export/consumption` endpoint with download button on page
- [x] CSV export for all datasets — per-page "Export CSV" buttons (`ExportButton`), per-dataset dropdowns on tabbed pages (`ExportMenu`), and a "Export All Data" ZIP (`/api/export-all`) on the Dashboard. All endpoints (`ExportEndpoints.cs`) enforce tenant-data isolation from the user's claims
- [x] Separate on-demand collector container app (scales 0→3 independently, internal HTTP, with Web fallback)
- [x] Cache warm-up hosted service — pre-populates common queries on startup to avoid thundering herd
- [x] Sliding + absolute expiration — active dashboards stay warm (5/15 min or 20/60 min)
- [x] Cache multi-tenant combos — short 4-min TTL instead of bypassing cache entirely
- [x] Redis pub/sub cross-replica cache invalidation — `RedisCacheInvalidationService`
- [x] Automated consent flow with tenant auto-registration — Static Web App + Consent Callback container (HMAC-validated, Graph `/organization` verification, initial data collection trigger)
- [x] Azure AD authentication — OpenID Connect multi-tenant login with home tenant (full access) + customer tenant (scoped to own data) isolation
- [x] Branding / whitelabeling settings page — logo, favicon, light/dark theme colors, app title (home-tenant only)
- [x] Per-tenant consent health indicator — Tenants page surfaces a "Re-consent" warning banner + per-tenant status column (probed each collection run + on-demand "Check permissions" button); persisted on `TenantInfo` (`MissingPermissions`, `PermissionsCheckedAt`)
- [x] Premium-tier gating — sign-in-based collection (sign-in summary, inactive accounts) auto-skipped on free-tier tenants; per-tenant Plan (Free/P1/P2) chip on the Tenants page; premium-license 403s no longer mislabeled as permission/consent errors
- [x] Collapsible grouping on data tables — Secure Score remediation actions (by Category/Tenant) and Service Health active issues + status overview (by Service/Type/Status/Tenant), with smart per-view defaults
- [ ] Consumption score threshold alerts (email/Teams notification when score drops)
- [x] Historical comparison: month-over-month score change indicators — delta chips on all KPI cards
- [ ] Per-department consumption scoring (combine department usage + segmentation)
- [x] **Unlicensed Copilot Chat usage** — implemented: collection pipeline + `MWDashboard.CopilotAudit` container app + model/migration + `/copilot` UI section with on-demand polling. See the dedicated [Unlicensed Copilot Chat Usage via Office 365 Management Activity API](#unlicensed-copilot-chat-usage-via-office-365-management-activity-api) topic above.

---

## Additional Graph Data Sources

**Context**: Beyond the Partner Center billing integration above, Microsoft Graph (and sister APIs) expose more data that increases the dashboard's value for CSPs to present to their customers. Ordered by ROI (value vs. effort). Each item follows the existing scaffolding pattern: new `*Snapshot` model + DbSet + migration, a `GraphReportService` method, a cached `MauDataService` save/query pair, and a page (or tab on an existing page). Gate premium-SKU data behind the existing P1/P2 detection used on the Security page.

### Tier 1 — High value (security & governance story)

- [x] **Microsoft Secure Score** — single 0–100 security posture score per tenant + prioritized remediation actions and score trend (strongest QBR metric)
  - Endpoints: `GET /security/secureScores`, `GET /security/secureScoreControlProfiles`
  - Permission: `SecurityEvents.Read.All` (new)
  - Implemented: `/securescore` page (KPIs, 90-day trend, category breakdown, remediation table), `SecureScoreSnapshot` + `SecureScoreControlSnapshot` models, collection pipeline (Job + on-demand), Redis caching
- [x] **MFA / auth method registration** — % registered for MFA, passwordless/FIDO2 adoption, SSPR-capable users (member accounts only; surfaced on Security page; uses `AuditLog.Read.All` — no new permission)
  - Endpoint: `GET /reports/authenticationMethods/userRegistrationDetails`
  - Permission: `AuditLog.Read.All` (already granted) or `Reports.Read.All` — **no new consent needed**
- [x] **Service health & incidents** — proactive per-tenant M365 incident/advisory awareness — implemented as `/servicehealth` page (KPIs, active-issues table, per-service status overview), `ServiceHealthSnapshot` + `ServiceHealthIssueSnapshot` models, collection pipeline (Job + on-demand), Redis caching
  - Endpoints: `GET /admin/serviceAnnouncement/healthOverviews`, `GET /admin/serviceAnnouncement/issues`
  - Permission: `ServiceHealth.Read.All` (new)
- [x] **Inactive / stale accounts** — licensed accounts with no sign-in in 30/60/90 days (license-waste $ story; pairs with Partner Center cost data) — surfaced on Security page; uses `AuditLog.Read.All` + `User.Read.All`. **Requires Entra ID P1/P2** on the target tenant (the `signInActivity` property is premium-gated); collection is auto-skipped on free-tier tenants
  - Endpoint: `GET /users?$select=signInActivity,assignedLicenses`
  - Permission: `AuditLog.Read.All` (already granted) + `User.Read.All` + Entra ID P1/P2 on target tenant

### Tier 2 — Endpoint & identity governance

- [x] **Intune device compliance** — compliant vs. non-compliant devices, OS breakdown, grace/error states
  - Endpoints: `GET /deviceManagement/managedDevices`
  - Permission: `DeviceManagementManagedDevices.Read.All` (new — requires re-consent)
- [x] **Conditional Access coverage** — count of CA policies + gaps (legacy-auth block, MFA grant detection)
  - Endpoint: `GET /identity/conditionalAccess/policies`
  - Permission: `Policy.Read.All` (new — requires re-consent)
- [x] **Guest / external users** — guest sprawl, accepted vs. pending, recently-added (30 days)
  - Endpoint: `GET /users?$filter=userType eq 'Guest'`
  - Permission: `User.Read.All` (already granted)
- [x] **Risky users (Identity Protection)** — at-risk accounts by risk level; gated behind Entra ID P2
  - Endpoints: `GET /identityProtection/riskyUsers`
  - Permission: `IdentityRiskyUser.Read.All` (new, **Entra ID P2 only**)
  - Surfaced on the **Identity & Devices** page (`/identity`) with tabs for all four features

### Tier 3 — Deeper usage/adoption detail (adds `Group.Read.All`; rest use existing `Reports.Read.All`)

- [x] **Mailbox usage detail** — mailbox sizes, over-quota and inactive mailboxes
  - Endpoints: `getMailboxUsageDetail`, `getMailboxUsageQuotaStatusMailboxCounts`
- [x] **Teams device usage** — desktop vs. mobile vs. web Teams split
  - Endpoint: `getTeamsDeviceUsageUserCounts`
- [x] **SharePoint / OneDrive site detail** — per-site/per-account storage and activity drill-down (totals already collected)
  - Endpoints: `getSharePointSiteUsageDetail`, `getOneDriveUsageAccountDetail`
- [x] **Viva Engage / Yammer activity** — completes workload coverage alongside Teams/Exchange/SharePoint
  - Endpoint: `getYammerActivityUserCounts`
- [x] **Groups & Teams sprawl** — count of M365 groups/Teams + ownerless groups (governance hygiene)
  - Endpoints: `GET /groups`, `GET /groups/{id}/owners`
  - Permission: `Group.Read.All` (new)
  - Surfaced on the **Usage & Governance** page (`/usage`) with tabs for all five features
