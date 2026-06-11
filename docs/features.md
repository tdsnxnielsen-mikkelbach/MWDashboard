# Features

All charts include axis labels (Y-axis: metric name, X-axis: time/category) and bottom-positioned legends identifying each data series. Chart text (legends, axis labels, titles) automatically adapts to dark/light mode via MudBlazor CSS variables. All pages show a full-page loading state with spinner while loading data (on initial load and when the tenant filter changes).

## Authentication & Access Control

- **Azure AD OpenID Connect** — all dashboard pages require authentication (authorization code flow)
- **Multi-tenant sign-in** — users from the home tenant (where the app registration lives) and any active customer tenant can sign in
- **Automatic tenant validation** — on sign-in, the user's tenant ID is checked against the database; only active (consented) tenants are allowed
- **Immediate access control** — deactivating or removing a tenant immediately blocks login for users from that tenant; adding/activating allows login
- **Access denied page** — rejected tenants see a styled "Access Denied" page (`/access-denied`) with user-friendly message ("Your organization is not authorized...") and "Sign Out & Try Another Account" link; internal OIDC errors are never exposed to users
- **Data isolation** — customer-tenant users can only view data from their own tenant; queries always filter by scoped tenant ID (never fetches all data)
- **Page-level restrictions** — Tenants management (`/tenants`) and Settings (`/settings`) pages are only accessible to home-tenant users; nav links are hidden for customer-tenant users, and direct URL access silently redirects them to the dashboard (the restricted pages are never revealed)
- **AppBar user controls** — user icon (tooltip shows name) + sign-out button visible when authenticated
- **Login redirect** — unauthenticated users are automatically redirected to Azure AD sign-in (HTTP-level enforcement before Blazor renders)
- **Container Apps TLS** — forwarded headers middleware trusts proxy `X-Forwarded-Proto` to ensure `https://` redirect URIs

## Global Tenant Selector

- **Persistent tenant filter** in the AppBar — visible only for home-tenant users (admins/MSPs)
- **Customer-tenant users** see their tenant name displayed in the AppBar (no selector — data is pre-scoped)
- Select individual tenants, all, or none via dropdown menu with checkboxes
- **Disabled during data load** — shows a spinner and prevents interaction while pages are fetching data
- **Multi-tenant attribution** — when multiple tenants are selected, all charts show series labeled `"Service (TenantName)"` and all tables include a Tenant column so data origin is always clear
- **Single-tenant mode** — when only one tenant is selected, labels stay clean without redundant tenant names
- Selector immediately reflects tenant activation/deactivation/deletion — deactivated tenants are removed from both the list and current selection in real-time
- Newly reactivated tenants are auto-selected

## Dashboard (`/`)

- **KPI cards** showing total active users, Teams/Exchange/SharePoint/OneDrive counts — each card displays active users vs total licensed seats with a color-coded progress bar (green ≥70%, yellow ≥40%, red <40% adoption)
- **Active Users by Service** — shows per-service active users vs **service-relevant** license seats (only counts SKUs that include that service, not all tenant licenses). Uses dynamic `IncludedServices` field auto-detected from Graph API service plans, with static `ServiceSkuMap` fallback for older data
- 12-month MAU trend line chart (per-tenant series in multi-tenant view)
- Per-service bar chart comparison (grouped by tenant when multi-tenant)
- License utilization table (with Tenant column in multi-tenant view) — Usage column shows a progress bar alongside the utilization percentage

## Services (`/services`)

- Per-service sparkline cards with aggregated user counts across selected tenants
- Combined multi-service comparison line chart (per-tenant series in multi-tenant view)

## Licenses & Adoption (`/licenses`)

- **Date range picker** — filter all charts and data by custom time period, with **Reset** button to restore full available range
- **Data availability indicator** — shows the actual historical data range from Graph reports (uses `ReportDate`, not collection timestamp); data accumulates beyond the 180-day Graph API limit
- **Month-over-month adoption chart** — per-service active users as % of total licenses, adapts to selected date range, Y-axis 0–100% with 2 decimal places. Chart automatically re-renders when date range changes via `@key` forcing component recreation
- **Automated recommendations** — scale up/down/monitor based on utilization thresholds and trend analysis
- **License detail table** — searchable with utilization bars and recommendation chips
- **M365 Message Center** — displays service announcements and advisories with:
  - **Multi-select filter chips** for Category and Severity (toggle on/off to combine filters)
  - **Collapsible month groups** (yyyy-MM) via expansion panels — individually expand/collapse for easy scanning of large post volumes
  - Tenant column visible in multi-tenant view
  - Inline severity color coding and "no matches" feedback

## Feature-Level Activity (`/activity`)

- **Per-workload feature breakdown** — Teams (meetings, chat messages, channel messages, calls), Exchange (emails sent/received/read), SharePoint (files viewed, files shared, page views, files synced), OneDrive (files viewed, files shared, files synced)
- **30-day activity trend charts** — one chart per workload with line series per activity type; in multi-tenant view, series are labeled `"ActivityType (TenantName)"` for per-tenant granularity
- **Activity summary table** — searchable, sortable by total count and daily average across all workloads
- Multi-tenant support — Tenant column visible when multiple tenants are selected; charts show tenant-level breakdown
- Data source: Graph Reports API (`getTeamsUserActivityCounts`, `getSharePointActivityUserCounts`, `getOneDriveActivityUserCounts`, `getEmailActivityCounts`)

## M365 App Usage (`/m365apps`)

- **Users by Application** — bar chart of active user counts per app (Outlook, Word, Excel, PowerPoint, OneNote, Teams)
- **Users by Platform** — donut chart of active users by platform (Windows, Mac, Mobile, Web)
- **App usage details table** — searchable/sortable per-app user counts with report date; Tenant column in multi-tenant view. In multi-tenant view the table is grouped into expandable sections by tenant (initially expanded) for easier scanning across tenants
- **All apps always shown** — every app column present in the report is recorded even when its count is 0 or blank, so apps with no recent activity (e.g. PowerPoint) still appear with a 0 value rather than silently disappearing
- Data source: Graph Reports API (`getM365AppUserCounts` with D30 period); usage data lags ~2–3 days and depends on tenant telemetry/diagnostic-data settings

## Copilot Adoption (`/copilot`)

- **KPI cards** — Total Copilot active users, total assigned licenses, overall adoption rate with color-coded thresholds
- **Per-app bar chart** — Active users across Word, Excel, PowerPoint, Outlook, Teams, OneNote, Loop, and Copilot Chat
- **Adoption detail table** — per-app breakdown with utilization progress bars and status chips (Strong/Growing/Low/Critical)
- **Automated recommendations** — actionable alerts based on adoption patterns (low app usage, overall low adoption, Chat-only usage patterns)
- Multi-tenant view — per-tenant grouped bar chart series
- Data source: Graph Beta API (`getMicrosoft365CopilotUsageUserDetail`); requires Copilot licenses

## User Segmentation (`/segmentation`)

- **Segment KPIs** — Total users, Heavy (3+ active workloads), Light (1–2 workloads), Inactive (0 workloads) with percentages
- **Distribution chart** — Donut chart in single-tenant view; switches to grouped bar chart (one series per tenant) in multi-tenant view showing Heavy/Light/Inactive side by side
- **Segment trend** — Stacked area chart tracking segment sizes over time (up to 6 months); in multi-tenant view, series are labeled per-tenant (e.g., "Heavy (Contoso)", "Inactive (Fabrikam)") for independent tracking
- **Per-tenant breakdown table** — shows waste % (inactive/total) per tenant in multi-tenant view
- **Recommendations** — license waste alerts for high inactive %, adoption maturity recognition, light-user campaign suggestions
- Data source: Graph Reports API (`getOffice365ActiveUserDetail` with D30 period); user detail is aggregated into counts, no PII stored

## Department Adoption (`/departments`)

- **KPI cards** — Departments tracked, average adoption rate, lowest-adoption department
- **Horizontal stacked bar chart** — Active vs. inactive users per department (top 20 by size)
- **Department detail table** — searchable, with adoption progress bars and status chips (High/Medium/Low)
- Multi-tenant support — Tenant column in table when multiple tenants selected
- **Minimum threshold** — departments with fewer than 5 users excluded from "lowest adoption" KPI to avoid noise
- Data source: Graph API `/users` (department field) + `getOffice365ActiveUserDetail`; requires `User.Read.All` permission

## Consumption Proxy (`/consumption`)

- **Composite consumption score** (0–100) — weighted from 4 dimensions:
  - Active User Adoption (30%) — MAU / licensed seats
  - Activity Intensity (30%) — total actions (emails, meetings, files) / active users
  - Storage Utilization (20%) — used storage / estimated allocation
  - Workload Breadth (20%) — avg services used per user (from segmentation)
- **KPI cards with month-over-month deltas** — Overall score, adoption %, total storage, avg workloads; each shows a color-coded chip indicating change vs previous month (+green / -red)
- **Export CSV** — "Export CSV" button downloads all consumption data as `/api/export/consumption` (TenantId, Score, Adoption, Storage, Workloads, Activity)
- **Per-tenant score comparison** — horizontal progress bars ranked by score
- **Radar chart** — 4-axis breakdown (Adoption, Activity, Storage, Breadth) per tenant
- **Score trend** — line chart tracking consumption score over time (up to 6 months); per-tenant series in multi-tenant view
- **Storage by service** — breakdown of SharePoint, OneDrive, Exchange storage with bars
- **Recommendations** — actionable alerts for low scores, license waste, narrow workload usage
- Data source: Computed from `getSharePointSiteUsageStorage`, `getOneDriveUsageStorage`, `getMailboxUsageStorage` + existing MAU/activity/segmentation data

## Security Services (`/security`)

- **Tenant Entra ID License Levels table** — auto-detects P1/P2/Free tier from stored license SKUs per tenant, shows which tenants can provide sign-in and MFA data
- **Tenant-scoped** — Entra ID tier table and sign-in data filtered to selected/scoped tenants (customer-tenant users see only their own tenant)
- **Sign-in activity chart** (30 days) — success vs failure area chart with axis labels and legend
- **Per-service breakdown** — Defender, Entra ID, Intune, Sentinel cards with failure rates
- **MFA adoption rate** — gauge with percentage and actionable recommendations
- **True MFA data** — uses Microsoft Graph Beta API `AuthenticationDetails` (counts non-password auth methods that succeeded). Only available from tenants with Entra ID P1/P2
- **MFA & Authentication Method Registration** — tenant-wide registration gauges for member accounts (guests excluded): MFA registered, MFA capable, passwordless capable, SSPR registered (with raw counts). Available on **all tiers** via `AuditLog.Read.All` — does not require P1/P2. Data source: Microsoft Graph `GET /reports/authenticationMethods/userRegistrationDetails`
- **Inactive / Stale Licensed Accounts** — KPI cards for enabled, licensed member accounts: total licensed, inactive 30+ days, inactive 90+ days, never signed in (each with % of licensed). Surfaces license-reclamation opportunities. **Requires Microsoft Entra ID P1/P2** — the underlying `signInActivity` property is premium-gated and returns 403 on the free tier even when `AuditLog.Read.All` + `User.Read.All` are consented. The dashboard detects the tenant tier from its SKUs and **skips this collection entirely on free-tier tenants** (logged at Information, not as a permission error). Data source: Microsoft Graph `GET /users` with `signInActivity`
- **Clear data availability notes** — explains exactly what's available per license tier and that Free-tier tenants cannot expose sign-in/audit-derived data via Graph

## Secure Score (`/securescore`)

- **KPI cards** — current secure score % (points achieved / max), delta vs. global average benchmark, count of actions to improve, licensed/active users. The licensed-users count falls back to the tenant's consumed license seats when Microsoft Graph reports a stale/zero `licensedUserCount`, and is never shown lower than the active-user count
- **Score trend chart** — secure score % over the last 90 days; per-tenant series in multi-tenant view
- **Score by category** — donut chart of achieved points per control category (Identity, Data, Device, Apps, Infrastructure)
- **Recommended remediation actions** — searchable/sortable table of controls not fully implemented, ordered by lowest progress first, with progress bar, category, and implementation status (Complete/Partial/Not started); Tenant column in multi-tenant view
- **Collapsible grouping** — "Group by" selector groups remediation actions into expandable/collapsible sections by Category, Tenant, or Tenant → Category (nested). Defaults to Tenant → Category in multi-tenant view and Category in single-tenant view; tenant-based modes auto-fall back to Category when a single tenant is selected. Each group header shows an action count for quick scanning across many tenants
- **Tenant-scoped** — customer-tenant users see only their own tenant's score
- Data source: Microsoft Graph `GET /security/secureScores` (90-day trend + embedded control scores); requires `SecurityEvents.Read.All`

## Service Health (`/servicehealth`)

- **KPI cards** — services healthy (of total monitored), services affected (degraded/interrupted), active incidents, active advisories
- **Active service issues** — searchable/sortable table of unresolved incidents and advisories with service, type (Incident/Advisory), status, feature, and start time; Tenant column in multi-tenant view. **Collapsible grouping** via a "Group by" selector: None, Service, Type (incident/advisory), Tenant, or Tenant → Service (nested). Defaults to Tenant in multi-tenant view, Service in single-tenant view
- **Service status overview** — per-service operational status with color-coded icon (operational/degraded/interrupted), affected services sorted to the top. **Collapsible grouping** via a "Group by" selector: None, Status, Tenant, or Tenant → Status (nested). Defaults to Tenant in multi-tenant view. Group headers show a service count
- **Tenant-scoped** — customer-tenant users see only their own tenant's service health
- Data source: Microsoft Graph `GET /admin/serviceAnnouncement/healthOverviews` (per-service status) + `GET /admin/serviceAnnouncement/issues` (active issues); requires `ServiceHealth.Read.All`

## Identity & Devices (`/identity`)

A single page with four tabs covering endpoint and identity governance. Each tab degrades gracefully with an info alert when its data/permission is unavailable, shows KPI cards plus charts, and adds a per-tenant comparison table in multi-tenant view.

- **Device Compliance** — Intune-managed device posture: managed device count, compliant %, non-compliant count, in-grace/error count; compliance-status donut and devices-by-platform bar chart. Data source: `GET /deviceManagement/managedDevices`; requires `DeviceManagementManagedDevices.Read.All` (available on all tiers)
- **Conditional Access** — policy inventory and coverage gaps: total/enabled/report-only policy counts, MFA-enforced indicator, legacy-auth-blocked indicator (aggregated across tenants); policy-state donut and per-tenant coverage table with MFA/legacy-block icons (icon legend + per-icon tooltips explain enforced vs. coverage-gap states). Data source: `GET /identity/conditionalAccess/policies`; requires `Policy.Read.All` **and Entra ID P1/P2** — Conditional Access is a premium feature, so Free-tier tenants have no policies to report
- **Guest Users** — external-collaboration governance: total guests, accepted vs. pending-acceptance, recently-added (last 30 days); invitation-status donut. Data source: `GET /users?$filter=userType eq 'Guest'`; reuses the already-granted `User.Read.All` (all tiers)
- **Risky Users** — Identity Protection risk: at-risk users by High/Medium/Low risk level; risk-level donut. Data source: `GET /identityProtection/riskyUsers`; requires `IdentityRiskyUser.Read.All` **and Entra ID P2** — tenants below P2 are skipped during collection (logged at Information)
- **Tenant-scoped** — customer-tenant users see only their own tenant's data
- Data models: `DeviceComplianceSnapshot`, `ConditionalAccessSnapshot`, `GuestUserSnapshot`, `RiskyUserSnapshot` (one row per tenant per day)

## Usage & Governance (`/usage`)

A single page with five tabs covering workload adoption and Microsoft 365 governance hygiene. Each tab degrades gracefully with an info alert when its data/permission is unavailable, shows KPI cards plus charts, and adds top-N drill-down tables (with a Tenant column in multi-tenant view).

- **Mailbox Usage** — Exchange Online mailbox posture: total/active mailboxes, total storage used, at/over-quota count (warning + send-prohibited); quota-status and active-vs-inactive donuts; top-20 largest mailboxes table (storage, item count, last activity). Data sources: `getMailboxUsageDetail`, `getMailboxUsageQuotaStatusMailboxCounts`; requires `Reports.Read.All` (all tiers)
- **Teams Devices** — Teams client adoption: desktop / mobile / web user counts, total active users; users-by-device-type bar and platform-mix donut. Data source: `getTeamsDeviceUsageUserCounts`; requires `Reports.Read.All` (all tiers)
- **SharePoint & OneDrive** — collaboration storage: SharePoint site count + storage, OneDrive account count + storage, combined storage and file totals; storage-by-workload donut and site-count bar; top-20 largest sites/accounts table. Data sources: `getSharePointSiteUsageDetail`, `getOneDriveUsageAccountDetail`; requires `Reports.Read.All` (all tiers)
- **Viva Engage** — Yammer/Viva Engage engagement: users who posted / read / liked; engagement-mix bar. Data source: `getYammerActivityUserCounts`; requires `Reports.Read.All` and an active Viva Engage deployment (all tiers)
- **Groups & Teams** — group governance: total groups, Microsoft 365 groups, Teams-connected groups, ownerless M365 groups (governance-risk warning when > 0); groups-by-type donut and per-tenant table. Data source: `GET /groups` with `owners` expand; requires `Group.Read.All` (**new permission** — must be re-consented by each tenant)
- **Tenant-scoped** — customer-tenant users see only their own tenant's data
- Data models: `MailboxUsageSnapshot`, `TopMailboxSnapshot`, `TeamsDeviceUsageSnapshot`, `SiteUsageSnapshot`, `SiteUsageDetailSnapshot`, `YammerActivitySnapshot`, `GroupSnapshot` (aggregates one row per tenant per day; top-N rows ranked per tenant per day)

## Branding & Appearance (`/settings`)

- **Home-tenant only** — page restricted to home-tenant users (same guard pattern as Tenants page)
- **Custom app title** — override "Modern Workplace Dashboard" text in the AppBar
- **Logo upload** — displayed in AppBar next to the title (max 512KB; PNG, JPG, SVG, WebP)
- **Favicon upload** — custom browser tab icon (max 128KB; PNG, ICO, SVG)
- **Light theme colors** — Primary, Secondary, and AppBar background color pickers
- **Dark theme colors** — Primary, Secondary, and AppBar background color pickers
- **Live preview** — branding applied on next page load (stored in DB as singleton row)
- **Reset to defaults** — one-click restore of default MudBlazor theme colors
- **Whitelabeling** — enables CSP partners to match their corporate branding across the dashboard for all users (including customer-tenant users)
- Data model: `BrandingSettings` (single row, stores Base64 images + hex colors + app title)

## Tenant Management (`/tenants`)

- **Home-tenant only** — page restricted to home-tenant users; nav link hidden and page guarded for customer-tenant users
- Register/deregister tenants — form auto-resets after successful registration with auto-dismissing success message
- **Inline display name editing** — pencil icon next to each tenant name opens an inline text field with save/cancel buttons; updates propagate to the global tenant selector immediately
- Admin consent URL generator with clipboard copy — redirect URI points to the Static Web App consent-complete page (configured via `ConsentCallback:RedirectUri`)
- **Collect Now button** — triggers immediate data collection for a specific tenant via the dedicated Collector container app (internal HTTP call); automatically falls back to local collection if the collector is unavailable. Collects MAU, licenses, Message Center, sign-ins, activity, Copilot, segmentation, departments, storage, M365 app usage, and consumption score
- Toggle tenant active/inactive — global tenant selector updates immediately when toggling or deleting tenants; deactivating blocks login for that tenant's users
- **Login access tied to tenant status** — adding a tenant allows users from that tenant to sign in; removing/deactivating blocks their access immediately
- **Consent health indicator** — a per-tenant "Consent" column shows a green check when all required Graph permissions are consented, or a "Re-consent" warning chip (with tooltip listing the missing permissions) when a tenant admin needs to re-approve. A warning banner at the top of the page lists all affected tenants with direct re-consent links. Status is refreshed automatically on every data collection and on-demand via a per-row "Check permissions" button. The probe distinguishes genuine consent gaps from premium-license limitations (e.g. `signInActivity` needing P1/P2) so consented permissions are never falsely flagged
- **Entra ID plan column** — a per-tenant "Plan" chip (Free / P1 / P2) derived from the tenant's license SKUs, making it clear at a glance which tenants support sign-in-based features (inactive accounts, sign-in summary) and which don't

## Automated Consent & Tenant Registration

- **Consent Complete Page** — Static Web App (`static/consent-complete/index.html`) hosted separately from the dashboard on Azure Static Web Apps (Free tier)
- **Isolation**: Customer admins are never redirected to the actual dashboard — they see only a branded static page with no access to customer-sensitive data
- **Auto-registration flow**:
  1. Tenant admin clicks the consent URL (generated from Tenants page or sent via email)
  2. Azure AD shows the permission consent prompt
  3. On approval, Azure AD redirects to the Static Web App with `?tenant={tenantId}&admin_consent=True`
  4. The static page computes an HMAC token and POSTs to the Consent Callback container (`/consent-callback`)
  5. The Consent container validates the HMAC, calls Graph API `GET /organization` to verify consent and retrieve tenant details
  6. Auto-fills: TenantId (from URL), TenantName (*.onmicrosoft.com domain), DisplayName (organization name)
  7. Upserts `TenantInfo` with `IsActive = true`
  8. Triggers initial data collection for the newly registered tenant
  9. Returns success — static page shows "Consent granted successfully" with org details and a **"Go to Dashboard"** button linking to the web app
- **Dashboard link**: On successful consent, partner tenant users see a button to navigate directly to the dashboard and sign in with their organizational account
- **Security**: HMAC shared secret validation prevents unauthorized callback abuse; Graph API call verifies consent is actually granted
- **Telemetry**: Application Insights JS SDK tracks consent page views and success/failure rates
