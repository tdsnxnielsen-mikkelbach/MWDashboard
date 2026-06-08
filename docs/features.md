# Features

All charts include axis labels (Y-axis: metric name, X-axis: time/category) and bottom-positioned legends identifying each data series. Chart text (legends, axis labels, titles) automatically adapts to dark/light mode via MudBlazor CSS variables. All pages show a full-page loading state with spinner while loading data (on initial load and when the tenant filter changes).

## Global Tenant Selector

- **Persistent tenant filter** in the AppBar — visible on every page, uses `MudMenu` with label + dropdown arrow
- Select individual tenants, all, or none via dropdown menu with checkboxes
- **Disabled during data load** — shows a spinner and prevents interaction while pages are fetching data
- **Multi-tenant attribution** — when multiple tenants are selected, all charts show series labeled `"Service (TenantName)"` and all tables include a Tenant column so data origin is always clear
- **Single-tenant mode** — when only one tenant is selected, labels stay clean without redundant tenant names
- Selector immediately reflects tenant activation/deactivation/deletion — deactivated tenants are removed from both the list and current selection in real-time
- Newly reactivated tenants are auto-selected

## Dashboard (`/`)

- **KPI cards** showing total active users, Teams/Exchange/SharePoint/OneDrive counts — each card displays active users vs total licensed seats with a color-coded progress bar (green ≥70%, yellow ≥40%, red <40% adoption)
- 12-month MAU trend line chart (per-tenant series in multi-tenant view)
- Per-service bar chart comparison (grouped by tenant when multi-tenant)
- License utilization table (with Tenant column in multi-tenant view)

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
- **KPI cards** — Overall score, adoption %, total storage used, avg workloads per user
- **Per-tenant score comparison** — horizontal progress bars ranked by score
- **Radar chart** — 4-axis breakdown (Adoption, Activity, Storage, Breadth) per tenant
- **Score trend** — line chart tracking consumption score over time (up to 6 months); per-tenant series in multi-tenant view
- **Storage by service** — breakdown of SharePoint, OneDrive, Exchange storage with bars
- **Recommendations** — actionable alerts for low scores, license waste, narrow workload usage
- Data source: Computed from `getSharePointSiteUsageStorage`, `getOneDriveUsageStorage`, `getMailboxUsageStorage` + existing MAU/activity/segmentation data

## Security Services (`/security`)

- **Tenant Entra ID License Levels table** — auto-detects P1/P2/Free tier from stored license SKUs per tenant, shows which tenants can provide sign-in and MFA data
- **Sign-in activity chart** (30 days) — success vs failure area chart with axis labels and legend
- **Per-service breakdown** — Defender, Entra ID, Intune, Sentinel cards with failure rates
- **MFA adoption rate** — gauge with percentage and actionable recommendations
- **True MFA data** — uses Microsoft Graph Beta API `AuthenticationDetails` (counts non-password auth methods that succeeded). Only available from tenants with Entra ID P1/P2
- **Clear data availability notes** — explains exactly what's available per license tier and that Free-tier tenants cannot expose audit log data via Graph

## Tenant Management (`/tenants`)

- Register/deregister tenants — form auto-resets after successful registration with auto-dismissing success message
- Admin consent URL generator with clipboard copy — redirect URI dynamically uses the app's base URL pointing to `/consent-complete`
- **Consent Complete page** (`/consent-complete`) — thank-you page shown after a tenant admin grants consent, with a link back to Tenant Management
- **Collect Now button** — triggers immediate data collection for a specific tenant (MAU, licenses, Message Center, sign-ins, activity, Copilot, segmentation, departments)
- Toggle tenant active/inactive — global tenant selector updates immediately when toggling or deleting tenants
