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

## Other Future Items

- [x] Redis caching for consumption/storage queries (TTL 15 min) — `CachedMauDataService` decorator wraps `MauDataService`
- [x] M365 App Platform usage (`getM365AppUserCounts`) for cross-app engagement metrics — model, Graph endpoint, collection pipeline
- [x] Export consumption report to CSV — `/api/export/consumption` endpoint with download button on page
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

### Tier 3 — Deeper usage/adoption detail (no new consent — uses existing `Reports.Read.All`)

- [ ] **Mailbox usage detail** — mailbox sizes, over-quota and inactive mailboxes
  - Endpoints: `getMailboxUsageDetail`, `getMailboxUsageQuotaStatusMailboxCounts`
- [ ] **Teams device usage** — desktop vs. mobile vs. web Teams split
  - Endpoint: `getTeamsDeviceUsageUserCounts`
- [ ] **SharePoint / OneDrive site detail** — per-site/per-account storage and activity drill-down (totals already collected)
  - Endpoints: `getSharePointSiteUsageDetail`, `getOneDriveUsageAccountDetail`
- [ ] **Viva Engage / Yammer activity** — completes workload coverage alongside Teams/Exchange/SharePoint
  - Endpoint: `getYammerActivityUserCounts`
- [ ] **Groups & Teams sprawl** — count of M365 groups/Teams + ownerless groups (governance hygiene)
  - Endpoints: `GET /groups`, `GET /groups/{id}/owners`
  - Permission: `Group.Read.All` (new)
