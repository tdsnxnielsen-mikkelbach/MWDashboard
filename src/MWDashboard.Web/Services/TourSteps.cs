namespace MWDashboard.Web.Services;

/// <summary>
/// Central catalogue of guided-tour steps, one list per dashboard page keyed by a short
/// page key. Each page's <c>&lt;PageTour PageKey="..." /&gt;</c> looks up its steps here so
/// page markup stays clean. Every page's tour ends with the shared navigation steps
/// (<see cref="Common"/>) that explain the app chrome (tenant filter, nav menu, theme, replay).
/// Steps whose target element isn't rendered are dropped client-side (see tour.js).
/// </summary>
public static class TourSteps
{
    // Shared chrome steps appended to every page tour. The anchors live in MainLayout /
    // NavMenu / TenantSelector and the PageTour button itself.
    private static readonly TourStep[] Common =
    [
        new("[data-tour=\"tenant-filter\"]",
            "Filter by tenant",
            "Scope every chart and table to one or more customer tenants. Leave it on \"All Tenants\" for an aggregate view.",
            Side: "bottom", Align: "end"),
        new("[data-tour=\"nav-menu\"]",
            "Navigate the dashboards",
            "Jump between the MAU dashboard, adoption, security, identity, governance and threat-protection pages here.",
            Side: "right", Align: "start"),
        new("[data-tour=\"theme-toggle\"]",
            "Light or dark",
            "Toggle between light and dark themes at any time.",
            Side: "bottom", Align: "end"),
        new("[data-tour=\"page-tour-btn\"]",
            "Replay this tour",
            "Click \"Tour\" on any page to replay its walkthrough whenever you need a refresher.",
            Side: "bottom", Align: "end"),
    ];

    private static readonly IReadOnlyDictionary<string, TourStep[]> Pages =
        new Dictionary<string, TourStep[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["home"] =
            [
                new(null, "Welcome to the MAU Dashboard",
                    "This is your at-a-glance view of Monthly Active Users across Microsoft 365 workloads, licensed seats and adoption trends."),
                new("[data-tour=\"page-header\"]", "Dashboard overview",
                    "KPI cards summarise total and per-workload active users versus licensed seats.", Side: "bottom"),
                new("[data-tour=\"page-export\"]", "Export everything",
                    "Download a ZIP of every dataset across all pages for offline analysis or reporting.", Side: "left"),
            ],
            ["services"] =
            [
                new(null, "Service Adoption",
                    "See which Microsoft 365 services (Exchange, Teams, SharePoint, OneDrive and more) are included in each tenant's licenses and how they're being used."),
                new("[data-tour=\"page-header\"]", "Services breakdown",
                    "Compare service coverage and activity across the selected tenants.", Side: "bottom"),
            ],
            ["activity"] =
            [
                new(null, "Workload Activity",
                    "Track active-user counts per workload over time to understand engagement trends across Teams, Exchange, SharePoint and OneDrive."),
                new("[data-tour=\"page-header\"]", "Activity trends",
                    "Charts break down activity by workload and date range for the selected tenants.", Side: "bottom"),
            ],
            ["licenses"] =
            [
                new(null, "License Inventory",
                    "Review assigned versus available license seats per SKU, spot under- and over-provisioning, and see which services each SKU includes."),
                new("[data-tour=\"page-header\"]", "License snapshots",
                    "Seat counts, utilisation and included services are pulled from Graph for each tenant.", Side: "bottom"),
                new("[data-tour=\"page-export\"]", "Export the data",
                    "Download the current dataset as CSV for reporting.", Side: "left"),
            ],
            ["copilot"] =
            [
                new(null, "Microsoft 365 Copilot",
                    "Monitor Copilot adoption — both licensed usage (from Graph) and free, unlicensed Copilot Chat activity (from the Office 365 audit log)."),
                new("[data-tour=\"page-header\"]", "Copilot usage",
                    "KPIs and trends cover active users and interactions across Copilot surfaces.", Side: "bottom"),
                new("[data-tour=\"page-action\"]", "Poll Copilot Chat",
                    "Trigger an on-demand pull of the latest unlicensed Copilot Chat activity from the audit log.", Side: "left"),
            ],
            ["segmentation"] =
            [
                new(null, "User Segmentation",
                    "Break active users into segments to understand who is (and isn't) adopting Microsoft 365."),
                new("[data-tour=\"page-header\"]", "Segments",
                    "Charts split usage by segment for the selected tenants.", Side: "bottom"),
            ],
            ["departments"] =
            [
                new(null, "Department Usage",
                    "Compare adoption across departments to target enablement where it's needed most."),
                new("[data-tour=\"page-header\"]", "By department",
                    "Usage is aggregated per department from user profile data.", Side: "bottom"),
            ],
            ["consumption"] =
            [
                new(null, "Consumption Score",
                    "A composite adoption score built from MAU, workload activity, storage and service breadth — a proxy for how much value each tenant draws from its licenses."),
                new("[data-tour=\"page-header\"]", "Score breakdown",
                    "See the score and the metrics that drive it for the selected tenants.", Side: "bottom"),
            ],
            ["m365apps"] =
            [
                new(null, "Microsoft 365 Apps",
                    "Track desktop and web app usage and Office activation counts per product and platform."),
                new("[data-tour=\"page-header\"]", "App usage",
                    "Anonymised per-user app × platform data and activation counts appear here.", Side: "bottom"),
            ],
            ["security"] =
            [
                new(null, "Security Services",
                    "Sign-in and MFA metrics, Microsoft Defender alerts, suspicious mail rules, DLP matches and external-sharing activity in one place."),
                new("[data-tour=\"page-header\"]", "Security posture",
                    "Data is drawn from Entra audit logs, Defender and the Office 365 unified audit log.", Side: "bottom"),
                new("[data-tour=\"page-export\"]", "Export datasets",
                    "Each security dataset can be exported individually from this menu.", Side: "left"),
            ],
            ["securescore"] =
            [
                new(null, "Microsoft Secure Score",
                    "Track each tenant's Secure Score and the individual controls contributing to it, with trends over time."),
                new("[data-tour=\"page-header\"]", "Score & controls",
                    "See the overall percentage and the highest-impact improvement actions.", Side: "bottom"),
            ],
            ["servicehealth"] =
            [
                new(null, "Service Health",
                    "Current Microsoft 365 service health and any active incidents or advisories affecting your tenants."),
                new("[data-tour=\"page-header\"]", "Health & incidents",
                    "Service statuses and open issues are listed for the selected tenants.", Side: "bottom"),
            ],
            ["identity"] =
            [
                new(null, "Identity & Devices",
                    "Device compliance and patch hygiene, Conditional Access coverage, guest and risky users, MFA registration, inactive and stale accounts."),
                new("[data-tour=\"page-header\"]", "Identity posture",
                    "Tabs cover devices, Conditional Access, guests, risky users and stale devices.", Side: "bottom"),
            ],
            ["threat-protection"] =
            [
                new(null, "Threat Protection",
                    "Microsoft Defender for Office 365 signals — email threats and Attack Simulation Training results — gated by each tenant's Defender license tier."),
                new("[data-tour=\"page-header\"]", "Email threats & simulations",
                    "Switch between the Email Threats and Attack Simulation tabs.", Side: "bottom"),
            ],
            ["usage"] =
            [
                new(null, "Usage & Governance",
                    "Mailbox, Teams, SharePoint, OneDrive and Viva Engage usage alongside governance signals like app-credential expiry, privileged roles, OAuth grants and group sprawl."),
                new("[data-tour=\"page-header\"]", "Usage & governance",
                    "Explore per-workload usage and governance risk tabs for the selected tenants.", Side: "bottom"),
            ],
            ["tenants"] =
            [
                new(null, "Tenant Management",
                    "Register, activate and manage customer tenants, check consent health, collect data on demand and purge tenant history."),
                new("[data-tour=\"page-header\"]", "Manage tenants",
                    "Each row shows a tenant's consent status, license tiers and last collection.", Side: "bottom"),
                new("[data-tour=\"page-action\"]", "Collect all tenants",
                    "Kick off a parallel data collection across every active tenant with live progress.", Side: "left"),
            ],
            ["settings"] =
            [
                new(null, "Branding & Settings",
                    "Customise the dashboard's logo, favicon, theme colours and title to match your brand."),
                new("[data-tour=\"page-header\"]", "Branding",
                    "Upload assets and pick theme colours; changes apply on the next page load.", Side: "bottom"),
            ],
        };

    /// <summary>
    /// Returns the tour steps for a page (page-specific steps followed by the shared
    /// navigation steps). Unknown keys yield just the shared steps so a tour is always available.
    /// </summary>
    public static IReadOnlyList<TourStep> Get(string pageKey)
    {
        if (pageKey is not null && Pages.TryGetValue(pageKey, out var steps))
        {
            return [.. steps, .. Common];
        }
        return Common;
    }
}
