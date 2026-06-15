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

## Unlicensed Copilot Chat Usage via Office 365 Management Activity API ✅ COMPLETED

**Priority**: Medium
**Status**: ✅ **Completed** — collection pipeline + dedicated `MWDashboard.CopilotAudit` container app + `/copilot` UI section (tenant-aware), shipped and in production
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
- `AppHost` identifies the surface — the **free Copilot Chat** values are `BizChat`, `Bing`, `Edge`, `Office`, `M365App`, `OfficeCopilotSearchAnswer` (all map to "Microsoft 365 Copilot Chat"). **`AppHost` is nested inside the `CopilotEventData` object**, not at the top level of the record (older/edge payloads may also expose it at the top level — read nested first, fall back to top-level)
- `AppIdentity` e.g. `Copilot.MicrosoftCopilot.BizChat`
- `UserId`, `CreationTime` — aggregate into active-user / interaction counts
- The audit record does **not** state whether the user is licensed; cross-reference `UserId` against assigned Copilot SKUs (already collected in `LicenseSnapshot`) to split licensed vs. unlicensed.
- **Record matching**: identify Copilot records resiliently via `Workload == "Copilot"` **or** `Operation == "CopilotInteraction"` **or** `RecordType == 261` — relying on `Workload`/`AppHost` alone silently drops every record on tenants whose payloads differ.

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
- [x] M365 App per-user detail (`getM365AppUserDetail`) app × platform adoption matrix + Office desktop activations (`getOffice365ActivationCounts` / `getOffice365ActivationsUserDetail`) by product & device — models, Graph endpoints, collection, UI on `/m365apps`, CSV exports. Per-user identities anonymized (irreversible per-tenant HMAC pseudonyms via `PiiProtector`; UPNs/names never stored)
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

---

## Untapped Data Sources — Next Wave (security, governance & adoption depth)

**Context**: The Tier 1–3 Graph reporting/security backlog above is essentially complete. The remaining high-value data falls into two buckets the app has barely touched: (1) the **Office 365 Management Activity API's *other* content types** — today only the `Audit.General` Copilot feed is consumed, but the same `ManagementActivityClient` subscription model exposes SharePoint/Exchange/DLP audit events; and (2) **directory & app-governance endpoints** in Graph. Each item follows the existing scaffolding pattern: new `*Snapshot` model + DbSet + migration → a `GraphReportService` (or `ManagementActivityClient`) method → a cached `MauDataService` save/query pair → a page or tab. Reuse the existing P1/P2 premium gating and tenant-isolation (`GetFilteredTenantIds`) conventions. Ordered by ROI (value vs. effort).

> **Note on `graphify`** (https://github.com/safishamsi/graphify): evaluated and **not adopted**. It is a *code-knowledge-graph* tool for AI coding assistants ("Graph" as in graph theory) — unrelated to Microsoft Graph / the Management Activity API. It cannot read M365 tenants and adds no dashboard data. At most a developer code-navigation aid, already covered by VS Code semantic search + `copilot-instructions.md`. Skipped.

### Tier A — Highest ROI, low effort

- [x] **App registration & secret/certificate expiry monitoring** — flag client secrets/certs expiring in 30/60/90 days (expired credentials are a top cause of sudden integration outages) + inventory over-privileged/third-party OAuth apps (illicit-consent attack surface). Strong proactive MSP/QBR deliverable. ✅ **Shipped** — `AppCredentialSnapshot` + **App Credential Expiry** tab on `/identity` + `app-credential-expiry` export.
  - Endpoints: `GET /applications` (`passwordCredentials`, `keyCredentials` end dates), `GET /servicePrincipals` (enterprise app permissions/grants)
  - Permission: `Application.Read.All` (new — requires re-consent)
  - Model: `AppCredentialSnapshot` (`TenantId`, `TenantName`, `AppId`, `AppDisplayName`, `CredentialType` [Secret/Certificate], `KeyId`, `EndDateTime`, `DaysToExpiry`, `CollectedAt`); composite unique index `(TenantId, AppId, KeyId)`
  - UI: tab on the **Identity & Devices** page (`/identity`) or a new `/governance` page — expiry table with red/amber/green chips + "expiring soon" KPI

- [x] **External sharing & anonymous-link activity** — who shared what externally, anonymous-link creation, external-user file access. A data-exposure security narrative the Graph reports API cannot provide. **Reuses the existing `ManagementActivityClient` subscription plumbing** (different content type + record parser). ✅ **Shipped** — `ExternalSharingSnapshot` via the generalized `ManagementActivityClient` (`Audit.SharePoint`) + `ExternalSharingCollectionService` in the CopilotAudit container + **External Sharing** section on `/security` + `external-sharing` export.
  - Source: **Office 365 Management Activity API**, content type `Audit.SharePoint` (`SharingSet`, `AnonymousLinkCreated`, `AddedToSecureLink` operations)
  - Permission: `ActivityFeed.Read` (**already granted** for the Copilot audit feed — no new consent)
  - Model: `ExternalSharingSnapshot` (`TenantId`, `TenantName`, `ReportDate`, `ShareType` [External/Anonymous/Guest], `EventCount`, `DistinctUsers`, `CollectedAt`); composite unique index `(TenantId, ShareType, ReportDate)`
  - Implementation note: add an `Audit.SharePoint` subscription + a sibling collection service alongside `CopilotAuditCollectionService` in the **CopilotAudit container** (rename concept to a generic "audit collector"), reusing the cursor/retention/`Retry-After` logic

### Tier B — Strong security story, moderate effort

- [x] **Privileged role / Global Admin inventory** — count of standing Global Admins (best practice ≤ 4), stale/eligible admin assignments, PIM activation usage. Pairs with Secure Score + Identity pages. ✅ **Shipped** — `PrivilegedRoleSnapshot` (`/directoryRoles` standing members) + **Privileged Roles** tab on `/identity` + `privileged-roles` export. (PIM eligible-assignment depth deferred.)
  - Endpoints: `GET /directoryRoles` + `GET /directoryRoles/{id}/members`; PIM `GET /roleManagement/directory/roleAssignmentScheduleInstances` / `roleEligibilityScheduleInstances`
  - Permission: `RoleManagement.Read.Directory` (new — requires re-consent)
  - Model: `PrivilegedRoleSnapshot` (`TenantId`, `TenantName`, `ReportDate`, `RoleName`, `StandingMembers`, `EligibleMembers`, `CollectedAt`); composite unique index `(TenantId, RoleName, ReportDate)`

- [x] **Defender / Identity Protection security alerts** — active alert counts by severity per tenant; complements Risky Users. ✅ **Shipped** — `DefenderAlertSnapshot` (`/security/alerts_v2`) + **Defender Alerts** section on `/security` + `defender-alerts` export.
  - Endpoints: `GET /security/alerts_v2`, `GET /security/incidents`
  - Permission: `SecurityAlert.Read.All` (new — requires re-consent)
  - Model: `SecurityAlertSnapshot` (`TenantId`, `TenantName`, `ReportDate`, `Severity`, `Status`, `AlertCount`, `CollectedAt`); composite unique index `(TenantId, Severity, Status, ReportDate)`
  - UI: surface on the **Security** page alongside existing posture metrics

- [x] **Suspicious inbox / auto-forwarding rules** — mailbox forwarding-rule creation is a classic Business Email Compromise (BEC) indicator. High MSP security value. ✅ **Shipped** — `MailRuleEventSnapshot` (`Audit.Exchange` via `MailRuleAuditCollectionService`, new `TenantInfo.ExchangeAuditCursorUtc` cursor) + **Suspicious Mailbox Rules** section on `/security` + `mail-rules` export.
  - Source: **Office 365 Management Activity API**, content type `Audit.Exchange` (`New-InboxRule`, `Set-InboxRule`, `Set-Mailbox ForwardingSmtpAddress`)
  - Permission: `ActivityFeed.Read` (**already granted**)
  - Model: `MailRuleEventSnapshot` (`TenantId`, `TenantName`, `ReportDate`, `RuleType` [Forwarding/Redirect/Delete], `EventCount`, `DistinctMailboxes`, `CollectedAt`)

- [x] **DLP policy matches** — Purview DLP sensitive-data exposure events (only for tenants running DLP). ✅ **Shipped** — `DlpEventSnapshot` (`DLP.All` via `DlpAuditCollectionService`, new `TenantInfo.DlpAuditCursorUtc` cursor) + **DLP Policy Matches** section on `/security` (shows a "not configured" notice when a tenant isn't running DLP) + `dlp-events` export.
  - Source: **Office 365 Management Activity API**, content type `DLP.All`
  - Permission: `ActivityFeed.Read` (**already granted**)
  - Model: `DlpEventSnapshot` (`TenantId`, `TenantName`, `ReportDate`, `PolicyName`, `MatchCount`, `Severity`, `CollectedAt`)

### Tier C — Adoption / quality depth (mostly existing permissions)

- [x] **License renewal / expiry dates** — track subscription renewal dates (counts already collected); complements the planned Partner Center billing work. ✅ **Shipped** — `SubscriptionSnapshot` (`GET /directory/subscriptions`) + **Subscription Renewals & Expiry** section on `/licenses` (expiring-≤30-days KPI + per-subscription days-remaining) + `subscriptions` export. Delivered without Partner Center billing work.
  - Endpoint: `GET /directory/subscriptions` (`nextLifecycleDateTime`, `status`)
  - Permission: `Directory.Read.All` (**already granted**)
  - Model: `SubscriptionSnapshot` (`TenantId`, `TenantName`, `ReportDate`, `SkuId`, `SkuPartNumber`, `Status`, `IsTrial`, `TotalLicenses`, `NextLifecycleDateTime`, `CollectedAt`); composite unique index `(TenantId, SkuId, ReportDate)`

- [x] **Teams team & channel activity** — per-team activity detail (per-user/device already collected). ✅ **Shipped** — `TeamsTeamActivitySnapshot` (`getTeamsTeamActivityDetail`, top-20 teams by active users) + **Top Teams by Activity** section on `/activity` + `teams-activity` export.
  - Endpoint: `getTeamsTeamActivityDetail`
  - Permission: `Reports.Read.All` (**already granted**)
  - UI: tab/drill-down on the **Activity** page

### Suggested implementation order
1. ✅ **App secret/cert expiry** (Tier A) — cheapest, prevents real outages, instant MSP value. **Done.**
2. ✅ **External sharing audit** (Tier A) — reuses the Management Activity collector, top security story, no new consent. **Done.**
3. ✅ **Privileged role inventory** (Tier B) + **Defender alerts** (Tier B) — complete the security posture. **Done.**
4. ✅ **Suspicious inbox/forwarding rules** + **DLP matches** (Tier B, Management Activity API) + **License renewal dates** + **Teams team activity** (Tier C, existing permissions). **Done.**

---

## Wave 3 — CSP Reseller Value (customer-facing depth, no Partner Center billing)

**Context**: Viewed from a **CSP reseller's** seat, the dashboard already covers posture, adoption and audit-feed security. The remaining high-value data that a reseller can present *to its customers* — without touching Partner Center billing — falls into three themes: (1) **change & accountability** ("what changed in my tenant and who did it"), (2) **license hygiene** ("am I paying for licenses that are broken or wasted"), and (3) **attack-surface & device health** ("where am I exposed"). Every item below is reachable from Microsoft Graph, the Office 365 Management Activity API, or Intune — all **excluding Partner Center / billing APIs**.

Each item follows the existing scaffolding pattern (the `new-model` skill): new `*Snapshot` model + DbSet + EF migration → a `GraphReportService` (or `ManagementActivityClient`) method → a cached `MauDataService` save/query pair (+ Redis key + invalidation) → a page or tab → a `ResolveScope`-based export entry in `ExportEndpoints.cs`. Reuse the existing P1/P2 premium gating (`TenantEntraTier.FromLicenses`), PII pseudonymization (`PiiProtector`) for any per-user rows, and tenant-isolation (`GetFilteredTenantIds`) conventions. Ordered by ROI (value vs. effort).

> **Note**: Items marked **(already granted)** need **no re-consent** — the permission is already on the app registration. Items marked **(new — requires re-consent)** must be added to the app registration and re-consented by each customer.

### Tier 1 — Highest ROI, no new consent (uses already-granted permissions)

- [x] **Directory audit / change tracking** — "what changed in my tenant, when, and who did it": admin role grants, user/group create-delete, app consents, password resets, policy edits, license changes. The single strongest **QBR accountability** deliverable and an early-warning signal for compromise. High event volume → aggregate to per-day counts by `category` + `activityDisplayName`, and keep a rolling top-N of the most recent/sensitive events. ✅ **Shipped** — `DirectoryAuditSnapshot` + `TenantInfo.DirectoryAuditCursorUtc` (cursor + **ADD-on-upsert** so split-day events accumulate and history is preserved beyond the tenant's ~7-day free / ~30-day P1-P2 retention) + **Change Log — Directory Audit** section on `/security` (retention-caveat empty-state) + `directory-audit` export.
  - Endpoint: `GET /auditLogs/directoryAudits` (`activityDisplayName`, `category`, `initiatedBy`, `targetResources`, `result`, `activityDateTime`); page with `$top` + `@odata.nextLink`, filter `activityDateTime ge {cursor}`
  - Permission: `AuditLog.Read.All` **(already granted)**
  - Model: `DirectoryAuditSnapshot` (`TenantId`, `TenantName`, `ReportDate`, `Category`, `Activity`, `EventCount`, `FailureCount`, `CollectedAt`); composite unique index `(TenantId, Category, Activity, ReportDate)`. Pseudonymize any actor UPNs via `PiiProtector`
  - Implementation steps:
    1. Add `DirectoryAuditSnapshot` to `MauSnapshot.cs` + DbSet + `OnModelCreating` config; `dotnet ef migrations add AddDirectoryAudit --project ../MWDashboard.Shared`
    2. `GraphReportService.GetDirectoryAuditsAsync(tenantId, sinceUtc)` — paginate, aggregate by (category, activity, day), count failures
    3. Persist a per-tenant cursor (mirror the Management-Activity cursor pattern; reuse a `TenantInfo.DirectoryAuditCursorUtc` field) so only new events are pulled (directoryAudits retain ~30 days on P1/P2, 7 days on free)
    4. `MauDataService` save (upsert by composite key) + query (cutoff-days filter) + `CachedMauDataService` wrapper (`directory-audit` key, 60-min TTL) + invalidation
    5. Wire into `OnDemandDataCollectionService` (+ Job inherits); add a **Change Log** tab on `/security` (or a new `/audit` page) with a per-day category bar chart + recent-sensitive-events table
    6. Add a `directory-audit` entry to `ExportEndpoints.cs`
  - Gating note: free-tier tenants retain only ~7 days of directory audit; surface the retention caveat in the empty-state (same pattern as the Copilot audit section)

- [x] **License assignment errors & seat waste detail** — surface users whose license assignment **failed** (`licenseAssignmentStates.state == Error`, e.g. dependency/conflict) and disabled-but-licensed accounts. A reseller-specific deliverable: "you are paying for N seats that aren't actually applied." Complements the already-shipped inactive-account and subscription-renewal sections. ✅ **Shipped** — `LicenseAssignmentIssueSnapshot` + **License Assignment Issues** section on `/licenses` (seats-in-error / disabled-but-licensed KPIs + per-SKU table) + `license-issues` export.
  - Endpoint: `GET /users?$select=id,accountEnabled,assignedLicenses,licenseAssignmentStates&$top=999` (+ paging)
  - Permission: `User.Read.All` + `Organization.Read.All` **(already granted)**
  - Model: `LicenseAssignmentIssueSnapshot` (`TenantId`, `TenantName`, `ReportDate`, `SkuPartNumber`, `ErrorUsers`, `DisabledLicensedUsers`, `CollectedAt`); composite unique index `(TenantId, SkuPartNumber, ReportDate)`
  - Implementation steps:
    1. Model + DbSet + migration
    2. `GraphReportService.GetLicenseAssignmentIssuesAsync(tenantId)` — enumerate users, bucket per SKU: assignment errors + accounts where `accountEnabled == false` but a license is assigned. Cross-reference SKU GUID → part number via the already-collected `LicenseSnapshot`
    3. Data-service save (delete-then-insert per `(TenantId, ReportDate)`) + query + cache wrapper (`license-issues` key, 60-min TTL)
    4. Wire into `OnDemandDataCollectionService`; add a **License Issues** section on `/licenses` (KPI: seats in error / disabled-but-licensed + a per-SKU table)
    5. `license-issues` export entry

- [x] **OAuth app consent grants & over-privileged enterprise apps** — inventory third-party/enterprise apps with delegated + application permission grants; flag high-risk scopes (`Mail.Read`, `Files.ReadWrite.All`, `Directory.ReadWrite.All`, full-access app roles) and apps consented by non-admins. This is the **illicit-consent / OAuth-phishing attack surface** explicitly deferred when the App Credential Expiry tab shipped (Tier A above) — a top MSP security narrative. ✅ **Shipped** — `OAuthGrantSnapshot` + **OAuth Apps** tab on `/identity` (high-risk-app / admin-consented KPIs + table with risk chips) + `oauth-grants` export. (Delegated grants via `/oauth2PermissionGrants`; application app-role grants left as a future enhancement.)
  - Endpoints: `GET /servicePrincipals` (`appDisplayName`, `appRoleAssignedTo`), `GET /oauth2PermissionGrants` (delegated grants + scopes)
  - Permission: `Application.Read.All` **(already granted)** + `Directory.Read.All` **(already granted)** (delegated-grant detail may also use `DelegatedPermissionGrant.Read.All` — add only if the `oauth2PermissionGrants` read 403s)
  - Model: `OAuthGrantSnapshot` (`TenantId`, `TenantName`, `ReportDate`, `AppDisplayName`, `AppId`, `GrantType` [Delegated/Application], `HighRiskScopes`, `ScopeCount`, `IsAdminConsented`, `CollectedAt`); composite unique index `(TenantId, AppId, GrantType, ReportDate)`
  - Implementation steps:
    1. Model + DbSet + migration
    2. `GraphReportService.GetOAuthGrantsAsync(tenantId)` — join service principals to their delegated (`oauth2PermissionGrants`) + application (`appRoleAssignedTo`) grants; classify a curated high-risk-scope set; flag admin- vs user-consent
    3. Data-service save (delete-then-insert per `(TenantId, ReportDate)`) + query + cache wrapper (`oauth-grants` key, 60-min TTL)
    4. Wire into `OnDemandDataCollectionService`; add an **OAuth Apps** tab to `/identity` (high-risk-app KPI + table with risk chips)
    5. `oauth-grants` export entry

- [ ] **Legacy authentication & risky sign-in detail** — extend the existing sign-in summary with **legacy-auth protocol usage** (POP/IMAP/SMTP/older clients that bypass MFA — a concrete remediation the customer can act on) plus failed-sign-in / risky-sign-in and sign-in-by-country breakdowns. Builds directly on the data already pulled for `SecuritySignInSummary`.
  - Endpoint: `GET /auditLogs/signIns` (`clientAppUsed`, `status`, `location.countryOrRegion`, `riskLevelAggregated`); filter `createdDateTime ge {cursor}`
  - Permission: `AuditLog.Read.All` **(already granted)** — **Entra ID P1/P2 gated** (skip on free tenants via `TenantEntraTier.FromLicenses`, same as the inactive-account pipeline)
  - Model: `SignInDetailSnapshot` (`TenantId`, `TenantName`, `ReportDate`, `ClientApp`, `IsLegacyAuth`, `SuccessCount`, `FailureCount`, `RiskyCount`, `CollectedAt`); composite unique index `(TenantId, ClientApp, ReportDate)`
  - Implementation steps:
    1. Model + DbSet + migration
    2. `GraphReportService.GetSignInDetailAsync(tenantId, sinceUtc)` — aggregate by (clientApp, day); mark legacy-auth client strings; count failures + risky aggregated levels. **Skip entirely on free-tier tenants** and reuse `IsPremiumLicenseError`
    3. Cursor (`TenantInfo.SignInDetailCursorUtc`) so only new sign-ins are pulled (sign-in logs retain ~30 days on P1, longer on P2)
    4. Data-service save (upsert) + query + cache wrapper (`signin-detail` key, 15-min TTL) + invalidation
    5. Wire into `OnDemandDataCollectionService`; add a **Legacy Auth & Risky Sign-ins** section to `/security` (legacy-auth-user KPI + per-client table + country breakdown). Surface the P1/P2 caveat on free tenants
    6. `signin-detail` export entry

- [ ] **Windows patch / OS-version compliance** — turn the **already-collected** `managedDevices` data into a patch-hygiene story: OS-version distribution, out-of-date / unsupported builds, last-check-in age. No new Graph call if the device list is already pulled for the Device Compliance tab — just an added aggregation + projection.
  - Endpoint: `GET /deviceManagement/managedDevices` (`operatingSystem`, `osVersion`, `complianceState`, `lastSyncDateTime`) — **already consumed** for Device Compliance
  - Permission: `DeviceManagementManagedDevices.Read.All` **(already granted)**
  - Model: `DevicePatchSnapshot` (`TenantId`, `TenantName`, `ReportDate`, `OsPlatform`, `OsVersion`, `DeviceCount`, `StaleCount`, `CollectedAt`); composite unique index `(TenantId, OsPlatform, OsVersion, ReportDate)`
  - Implementation steps:
    1. Model + DbSet + migration
    2. In the existing device-compliance collection path, additionally aggregate devices by (osPlatform, osVersion, day); count devices whose `lastSyncDateTime` is older than N days as stale
    3. Data-service save (delete-then-insert per `(TenantId, ReportDate)`) + query + cache wrapper (`device-patch` key, 60-min TTL)
    4. Add a **Patch / OS Versions** tab to `/identity` (top-versions bar + stale-device KPI). Optionally a curated "supported vs. unsupported build" flag for common Windows builds
    5. `device-patch` export entry

- [x] **Mailbox non-owner / delegate access** — surface `MailItemsAccessed` and non-owner mailbox access from the **already-subscribed** `Audit.Exchange` feed — an insider-threat / compromised-delegate indicator that pairs with the shipped Suspicious Mailbox Rules section. Pure parser addition to the existing collector; **no new subscription or consent**. ✅ **Shipped** — `MailboxAccessSnapshot` + a second classifier in `MailRuleAuditCollectionService` (same `Audit.Exchange` blob loop / `ExchangeAuditCursorUtc` cursor, no extra API calls) + **Mailbox Access** card on `/security` + `mailbox-access` export.
  - Source: **Office 365 Management Activity API**, content type `Audit.Exchange` (`MailItemsAccessed`, `Add-MailboxPermission`, non-owner `LogonType`)
  - Permission: `ActivityFeed.Read` **(already granted)** — reuses `ManagementActivityClient` + the `ExchangeAuditCursorUtc` cursor already advanced by `MailRuleAuditCollectionService`
  - Model: `MailboxAccessSnapshot` (`TenantId`, `TenantName`, `ReportDate`, `AccessType` [NonOwnerAccess/DelegateGrant], `EventCount`, `DistinctMailboxes`, `CollectedAt`); composite unique index `(TenantId, AccessType, ReportDate)`
  - Implementation steps:
    1. Model + DbSet + migration
    2. Extend `MailRuleAuditCollectionService` (or add a sibling parser) to also accumulate non-owner mailbox-access records from the same `Audit.Exchange` blobs it already pulls — **no extra API calls**, just an added classifier
    3. Data-service save (upsert by `(TenantId, AccessType, ReportDate)`) + query + cache wrapper (`mailbox-access` key, 60-min TTL)
    4. Add a **Mailbox Access** card to the `/security` Suspicious Mailbox Rules area
    5. `mailbox-access` export entry

### Tier 2 — Strong value, new consent or add-on license required

- [ ] **Stale Entra-registered devices** — registered/joined devices that haven't signed in for 90+ days (device-hygiene / cleanup story distinct from Intune compliance, since it covers all registered devices, not just managed ones).
  - Endpoint: `GET /devices` (`displayName`, `operatingSystem`, `approximateLastSignInDateTime`, `isManaged`, `isCompliant`, `accountEnabled`)
  - Permission: `Device.Read.All` **(new — requires re-consent)** (or reuse `Directory.Read.All` if it suffices for `/devices` in the target tenant)
  - Model: `StaleDeviceSnapshot` (`TenantId`, `TenantName`, `ReportDate`, `OsPlatform`, `TotalDevices`, `Stale90Plus`, `DisabledDevices`, `CollectedAt`); composite unique index `(TenantId, OsPlatform, ReportDate)`
  - Implementation steps: model + migration → `GraphReportService.GetStaleDevicesAsync(tenantId)` (bucket by platform, count `approximateLastSignInDateTime` older than 90 days) → data-service save (delete-then-insert) + cache (`stale-devices`, 60-min) → tab on `/identity` → export. Add `Device.Read.All` to `GraphPermissions` + consent probe + `docs/permissions.md`

- [ ] **Defender for Office 365 email threat protection** — phishing / malware / spam messages **blocked** by EOP/MDO (the "we stopped X threats this month" headline metric customers love in a QBR). Requires the customer to hold a Defender for Office 365 / EOP plan.
  - Endpoints: `getMailDetailMalwareReport` / `getMailDetailPhishReport` / threat reports under `/security` (validate exact Graph availability per tenant; some live only in the Defender portal reporting API)
  - Permission: likely `SecurityEvents.Read.All` **(already granted)** or `ThreatHunting.Read.All` / `SecurityAnalyzedMessage.Read.All` **(new)** — confirm during spike; **requires a Defender for Office 365 subscription** on the tenant
  - Model: `EmailThreatSnapshot` (`TenantId`, `TenantName`, `ReportDate`, `ThreatType` [Malware/Phish/Spam], `BlockedCount`, `DeliveredCount`, `CollectedAt`); composite unique index `(TenantId, ThreatType, ReportDate)`
  - Implementation steps: **spike first** to confirm the exact Graph endpoint + permission + license requirement (this is the least-certain item) → model + migration → `GraphReportService` method (gracefully no-op when the tenant lacks the Defender plan, like the existing Defender-alerts handling) → data-service save + cache (`email-threats`, 60-min) → section on `/security` with a "Defender for Office 365 not licensed" empty-state → export

- [ ] **Attack Simulation Training results** — phishing-simulation campaigns and click/report rates (security-awareness posture). Requires Defender for Office 365 Plan 2.
  - Endpoint: `GET /security/attackSimulation/simulations` (+ per-simulation report)
  - Permission: `AttackSimulation.Read.All` **(new — requires re-consent)**; **Defender for Office 365 Plan 2** on the tenant
  - Model: `AttackSimSnapshot` (`TenantId`, `TenantName`, `ReportDate`, `CampaignName`, `TargetedUsers`, `ClickedCount`, `ReportedCount`, `CompromisedRate`, `CollectedAt`); composite unique index `(TenantId, CampaignName, ReportDate)`
  - Implementation steps: model + migration → `GraphReportService.GetAttackSimulationsAsync(tenantId)` (no-op when unlicensed) → data-service save + cache (`attack-sim`, 60-min) → tab on `/security` → export → add `AttackSimulation.Read.All` to `GraphPermissions` + consent probe + `docs/permissions.md`

### Tier 3 — Optional / overlap

- [ ] **`Audit.AzureActiveDirectory` Management-Activity content type** — an *alternative* source for the same admin/sign-in/consent change events as Tier 1's Directory Audit (Graph `directoryAudits`). Only pursue if the Graph `directoryAudits` retention window proves too short for a given customer — the Management Activity feed (7-day blobs, already plumbed via `ManagementActivityClient`) can backfill. Otherwise **skip to avoid double-counting** the same events the Directory Audit item already aggregates.
  - Source: **Office 365 Management Activity API**, content type `Audit.AzureActiveDirectory`
  - Permission: `ActivityFeed.Read` **(already granted)**
  - Decision: implement **only** Directory Audit (Tier 1) first; revisit this if retention gaps appear

### Suggested implementation order (Wave 3)
1. ~~**Directory audit / change tracking** (Tier 1)~~ — ✅ **Shipped** (Change Log section on Security page; cursor + ADD-on-upsert accumulates history beyond the ~7-day free retention).
2. ~~**License assignment errors** (Tier 1)~~ — ✅ **Shipped** (License Assignment Issues section on Licenses page; seats-in-error + disabled-but-licensed seat waste).
3. ~~**OAuth app consent grants** (Tier 1)~~ — ✅ **Shipped** (OAuth Apps tab on Identity & Devices page; high-risk scope + admin-consent flags).
4. ~~**Mailbox non-owner access** (Tier 1)~~ — ✅ **Shipped** (Mailbox Access section on Security page; parser add-on to the existing `Audit.Exchange` collector, no extra API calls).
5. **Windows patch / OS-version compliance** (Tier 1) — aggregation over already-collected device data.
6. **Legacy auth & risky sign-in detail** (Tier 1, P1/P2-gated) — extends the existing sign-in pipeline.
7. **Stale registered devices** (Tier 2) + **Defender for O365 email threats** (Tier 2, spike first) + **Attack Simulation Training** (Tier 2) — once the no-consent Tier 1 items ship.
