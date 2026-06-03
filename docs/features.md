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

- KPI cards showing total active users, Teams/Exchange/SharePoint/OneDrive counts
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

## Security Services (`/security`)

- **Tenant Entra ID License Levels table** — auto-detects P1/P2/Free tier from stored license SKUs per tenant, shows which tenants can provide sign-in and MFA data
- **Sign-in activity chart** (30 days) — success vs failure area chart with axis labels and legend
- **Per-service breakdown** — Defender, Entra ID, Intune, Sentinel cards with failure rates
- **MFA adoption rate** — gauge with percentage and actionable recommendations
- **True MFA data** — uses Microsoft Graph Beta API `AuthenticationDetails` (counts non-password auth methods that succeeded). Only available from tenants with Entra ID P1/P2
- **Clear data availability notes** — explains exactly what's available per license tier and that Free-tier tenants cannot expose audit log data via Graph

## Tenant Management (`/tenants`)

- Register/deregister tenants — form auto-resets after successful registration with auto-dismissing success message
- Admin consent URL generator with clipboard copy
- **Collect Now button** — triggers immediate data collection for a specific tenant
- Toggle tenant active/inactive — global tenant selector updates immediately when toggling or deleting tenants
