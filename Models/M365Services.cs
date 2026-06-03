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
