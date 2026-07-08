namespace MWDashboard.Web.Services;

/// <summary>
/// A single step in a driver.js guided tour. Serialized to the <c>tour.js</c> interop
/// module (camelCase via the default Blazor JS-interop serializer).
/// </summary>
/// <param name="Element">
/// CSS selector of the element to highlight (usually <c>[data-tour="..."]</c>). When
/// <c>null</c>/empty the step renders as a centered modal popover (used for page intros).
/// </param>
/// <param name="Title">Popover heading.</param>
/// <param name="Description">Popover body text.</param>
/// <param name="Side">Popover placement relative to the element: top/bottom/left/right.</param>
/// <param name="Align">Popover alignment along the chosen side: start/center/end.</param>
public sealed record TourStep(
    string? Element,
    string Title,
    string Description,
    string? Side = "bottom",
    string? Align = "start");
