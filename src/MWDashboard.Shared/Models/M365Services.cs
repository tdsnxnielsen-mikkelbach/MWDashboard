namespace MWDashboard.Shared.Models;

public static class M365Services
{
    public const string Office365 = "Office365";
    public const string Teams = "Teams";
    public const string Exchange = "Exchange";
    public const string SharePoint = "SharePoint";
    public const string OneDrive = "OneDrive";

    public static readonly string[] All =
    [
        Office365,
        Teams,
        Exchange,
        SharePoint,
        OneDrive
    ];

    /// <summary>
    /// Maps M365 service names to SKU part numbers that include that service.
    /// Used as fallback for license records that don't have IncludedServices populated.
    /// </summary>
    public static readonly Dictionary<string, HashSet<string>> ServiceSkuMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [Office365] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Full M365/O365 suites (include Office apps + all workloads)
            "O365_BUSINESS_ESSENTIALS", "O365_BUSINESS_PREMIUM", "O365_BUSINESS",
            "SMB_BUSINESS_ESSENTIALS", "SMB_BUSINESS_PREMIUM", "SMB_BUSINESS",
            "MICROSOFT_365_BASIC", "M365_BUSINESS_BASIC", "M365_BUSINESS_STANDARD", "M365_BUSINESS_PREMIUM",
            "SPE_E3", "SPE_E5", "SPE_F1",
            "MICROSOFT_365_E3", "MICROSOFT_365_E5", "MICROSOFT_365_F1", "MICROSOFT_365_F3",
            "M365_E3", "M365_E5", "M365_F1", "M365_F3",
            "ENTERPRISEPACK", "ENTERPRISEPREMIUM", "ENTERPRISEPREMIUM_NOPSTNCONF",
            "DESKLESSPACK", "MIDSIZEPACK",
            "OFFICESUBSCRIPTION",
            "M365_G3_GOV", "M365_G5_GOV",
            // Exchange plans that include Office apps
            "EXCHANGESTANDARD", "EXCHANGESTANDARD_STUDENT", "EXCHANGEENTERPRISE",
            "MICROSOFT_365_COPILOT",
        },
        [Teams] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Suites that include Teams
            "O365_BUSINESS_ESSENTIALS", "O365_BUSINESS_PREMIUM",
            "SMB_BUSINESS_ESSENTIALS", "SMB_BUSINESS_PREMIUM",
            "MICROSOFT_365_BASIC", "M365_BUSINESS_BASIC", "M365_BUSINESS_STANDARD", "M365_BUSINESS_PREMIUM",
            "SPE_E3", "SPE_E5", "SPE_F1",
            "MICROSOFT_365_E3", "MICROSOFT_365_E5", "MICROSOFT_365_F1", "MICROSOFT_365_F3",
            "M365_E3", "M365_E5", "M365_F1", "M365_F3",
            "ENTERPRISEPACK", "ENTERPRISEPREMIUM", "ENTERPRISEPREMIUM_NOPSTNCONF",
            "DESKLESSPACK", "MIDSIZEPACK",
            "M365_G3_GOV", "M365_G5_GOV",
            // Standalone Teams
            "TEAMS1", "TEAMS_EXPLORATORY", "TEAMS_FREE",
            "MICROSOFT_365_COPILOT",
        },
        [Exchange] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Suites that include Exchange
            "O365_BUSINESS_ESSENTIALS", "O365_BUSINESS_PREMIUM",
            "SMB_BUSINESS_ESSENTIALS", "SMB_BUSINESS_PREMIUM",
            "MICROSOFT_365_BASIC", "M365_BUSINESS_BASIC", "M365_BUSINESS_STANDARD", "M365_BUSINESS_PREMIUM",
            "SPE_E3", "SPE_E5", "SPE_F1",
            "MICROSOFT_365_E3", "MICROSOFT_365_E5", "MICROSOFT_365_F1", "MICROSOFT_365_F3",
            "M365_E3", "M365_E5", "M365_F1", "M365_F3",
            "ENTERPRISEPACK", "ENTERPRISEPREMIUM", "ENTERPRISEPREMIUM_NOPSTNCONF",
            "DESKLESSPACK", "MIDSIZEPACK",
            "M365_G3_GOV", "M365_G5_GOV",
            // Standalone Exchange
            "EXCHANGESTANDARD", "EXCHANGESTANDARD_STUDENT", "EXCHANGEENTERPRISE",
            "EXCHANGE_S_ESSENTIALS", "EXCHANGEDESKLESS",
            "MICROSOFT_365_COPILOT",
        },
        [SharePoint] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Suites that include SharePoint
            "O365_BUSINESS_ESSENTIALS", "O365_BUSINESS_PREMIUM",
            "SMB_BUSINESS_ESSENTIALS", "SMB_BUSINESS_PREMIUM",
            "MICROSOFT_365_BASIC", "M365_BUSINESS_BASIC", "M365_BUSINESS_STANDARD", "M365_BUSINESS_PREMIUM",
            "SPE_E3", "SPE_E5", "SPE_F1",
            "MICROSOFT_365_E3", "MICROSOFT_365_E5", "MICROSOFT_365_F1", "MICROSOFT_365_F3",
            "M365_E3", "M365_E5", "M365_F1", "M365_F3",
            "ENTERPRISEPACK", "ENTERPRISEPREMIUM", "ENTERPRISEPREMIUM_NOPSTNCONF",
            "DESKLESSPACK", "MIDSIZEPACK",
            "M365_G3_GOV", "M365_G5_GOV",
            // Standalone SharePoint
            "SHAREPOINTSTANDARD", "SHAREPOINTENTERPRISE",
            "MICROSOFT_365_COPILOT",
        },
        [OneDrive] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Suites that include OneDrive
            "O365_BUSINESS_ESSENTIALS", "O365_BUSINESS_PREMIUM", "O365_BUSINESS",
            "SMB_BUSINESS_ESSENTIALS", "SMB_BUSINESS_PREMIUM", "SMB_BUSINESS",
            "MICROSOFT_365_BASIC", "M365_BUSINESS_BASIC", "M365_BUSINESS_STANDARD", "M365_BUSINESS_PREMIUM",
            "SPE_E3", "SPE_E5", "SPE_F1",
            "MICROSOFT_365_E3", "MICROSOFT_365_E5", "MICROSOFT_365_F1", "MICROSOFT_365_F3",
            "M365_E3", "M365_E5", "M365_F1", "M365_F3",
            "ENTERPRISEPACK", "ENTERPRISEPREMIUM", "ENTERPRISEPREMIUM_NOPSTNCONF",
            "DESKLESSPACK", "MIDSIZEPACK",
            "M365_G3_GOV", "M365_G5_GOV",
            // Standalone OneDrive
            "ONEDRIVE_BASIC", "WACONEDRIVESTANDARD",
            "MICROSOFT_365_COPILOT",
        },
    };

    /// <summary>
    /// Gets the total relevant license seats for a specific service from a set of license snapshots.
    /// Uses the dynamic IncludedServices field when available, falls back to static ServiceSkuMap.
    /// </summary>
    public static int GetServiceLicenseCount(string serviceName, IEnumerable<LicenseSnapshot> licenses)
    {
        var total = 0;
        ServiceSkuMap.TryGetValue(serviceName, out var fallbackSkus);

        foreach (var l in licenses)
        {
            if (!string.IsNullOrEmpty(l.IncludedServices))
            {
                // Dynamic: use auto-detected services from Graph API service plans
                if (l.IncludedServices.Contains(serviceName, StringComparison.OrdinalIgnoreCase))
                    total += l.TotalLicenses;
            }
            else if (fallbackSkus != null && fallbackSkus.Contains(l.SkuPartNumber))
            {
                // Fallback: use static SKU map for older records without IncludedServices
                total += l.TotalLicenses;
            }
        }

        return total;
    }

    /// <summary>
    /// Service plan names that indicate actual user-facing service access.
    /// Foundation/infrastructure plans (e.g. EXCHANGE_S_FOUNDATION) are excluded
    /// because Microsoft includes them in nearly every SKU as backend dependencies.
    /// </summary>
    private static readonly Dictionary<string, string[]> ServicePlanKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        [Teams] = ["TEAMS1", "TEAMS_AR_DOD", "TEAMS_AR_GCCHIGH", "TEAMS_GOV", "MCO_TEAMS_IW",
                   "MCOSTANDARD", "MCOIMP", "MCOMEETADV", "MCOEV", "MCOPSTN"],
        [Exchange] = ["EXCHANGE_S_STANDARD", "EXCHANGE_S_ENTERPRISE", "EXCHANGE_S_ESSENTIALS",
                      "EXCHANGE_S_DESKLESS", "EXCHANGE_S_ARCHIVE", "EXCHANGE_B_STANDARD",
                      "BPOS_S_TODO_1", "EXCHANGE_ANALYTICS"],
        [SharePoint] = ["SHAREPOINTSTANDARD", "SHAREPOINTENTERPRISE", "SHAREPOINTONLINE_MIDMARKET",
                        "SHAREPOINTDESKLESS", "SHAREPOINT_S_DEVELOPER"],
        [OneDrive] = ["ONEDRIVESTANDARD", "ONEDRIVE_BASIC", "SHAREPOINTWAC"],
        [Office365] = ["OFFICESUBSCRIPTION", "OFFICE_BUSINESS", "OFFICEMOBILE_SUBSCRIPTION",
                       "OFFICE_SHARED_COMPUTER_ACTIVATION", "OFFICE_PRO_PLUS_SUBSCRIPTION_SMBIZ"],
    };

    /// <summary>
    /// Detects which M365 services are included in a SKU based on its service plan names.
    /// Returns a comma-separated string (e.g. "Teams,Exchange,SharePoint,OneDrive,Office365").
    /// </summary>
    public static string DetectServicesFromPlans(IEnumerable<string> servicePlanNames)
    {
        var detected = new HashSet<string>();
        var plans = servicePlanNames.ToList();

        foreach (var (service, keywords) in ServicePlanKeywords)
        {
            if (plans.Any(plan => keywords.Any(kw =>
                plan.Equals(kw, StringComparison.OrdinalIgnoreCase) ||
                plan.StartsWith(kw, StringComparison.OrdinalIgnoreCase))))
            {
                detected.Add(service);
            }
        }

        // If any core workload is detected, also mark as Office365
        if (detected.Count > 0 && !detected.Contains(Office365))
        {
            if (detected.Contains(Teams) || detected.Contains(Exchange) ||
                detected.Contains(SharePoint) || detected.Contains(OneDrive))
            {
                detected.Add(Office365);
            }
        }

        return string.Join(",", detected.OrderBy(s => s));
    }
}

public static class ActivityTypes
{
    // Teams
    public const string TeamsMeetings = "Meetings Organized";
    public const string TeamsChatMessages = "Chat Messages";
    public const string TeamsChannelMessages = "Channel Messages";
    public const string TeamsCalls = "Calls";

    // SharePoint
    public const string SharePointFilesViewed = "Files Viewed";
    public const string SharePointFilesShared = "Files Shared";
    public const string SharePointPageViews = "Page Views";
    public const string SharePointFileSynced = "Files Synced";

    // OneDrive
    public const string OneDriveFilesViewed = "Files Viewed";
    public const string OneDriveFilesShared = "Files Shared";
    public const string OneDriveFilesSynced = "Files Synced";

    // Exchange
    public const string ExchangeEmailsSent = "Emails Sent";
    public const string ExchangeEmailsReceived = "Emails Received";
    public const string ExchangeEmailsRead = "Emails Read";
}

public static class CopilotApps
{
    public const string Word = "Word";
    public const string Excel = "Excel";
    public const string PowerPoint = "PowerPoint";
    public const string Outlook = "Outlook";
    public const string Teams = "Teams";
    public const string OneNote = "OneNote";
    public const string Loop = "Loop";
    public const string CopilotChat = "Copilot Chat";

    public static readonly string[] All =
    [
        Word, Excel, PowerPoint, Outlook, Teams, OneNote, Loop, CopilotChat
    ];
}

public static class SecurityServices
{
    public const string Defender = "Defender";
    public const string EntraId = "Entra ID";
    public const string Intune = "Intune";
    public const string Sentinel = "Sentinel";
    public const string Other = "Other";

    public static readonly string[] All =
    [
        Defender,
        EntraId,
        Intune,
        Sentinel,
        Other
    ];
}
