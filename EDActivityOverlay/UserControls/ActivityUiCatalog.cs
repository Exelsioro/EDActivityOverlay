using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.UserControls;

internal sealed record ActivityUiOption(
    ActivityType Activity,
    string LabelKey)
{
    public string Label => Loc.Get(LabelKey);
}

internal static class ActivityUiCatalog
{
    public static readonly ActivityUiOption[] All =
    [
        new(ActivityType.Trade, "Loc_Trade"),
        new(ActivityType.Engineering, "Loc_Engineering"),
        new(ActivityType.Exploration, "Loc_Exploration"),
        new(ActivityType.Mining, "Loc_Mining")
    ];
}