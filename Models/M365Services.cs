namespace MWDashboard.Models;

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
