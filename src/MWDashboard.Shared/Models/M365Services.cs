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
