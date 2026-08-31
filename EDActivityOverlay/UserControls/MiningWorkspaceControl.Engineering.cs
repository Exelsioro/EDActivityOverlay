using EDActivityOverlay.Models;
using EDActivityOverlay.Services;

namespace EDActivityOverlay.UserControls;

public partial class MiningWorkspaceControl
{
    private static string BuildEngineeringMaterialsText(
        MiningEngineeringMaterialsSnapshot materials)
    {
        if (!materials.HasGains)
        {
            return string.Empty;
        }

        string body = string.Join(
            "  •  ",
            materials.Materials
                .Take(3)
                .Select(item =>
                    item.IsEngineeringTarget
                        ? Loc.Format(
                            "Loc_MINING_ENG_TARGET_FORMAT",
                            item.DisplayName,
                            item.GainedThisSession,
                            item.Available,
                            item.Required,
                            item.Missing)
                        : Loc.Format(
                            "Loc_MINING_ENG_GAIN_FORMAT",
                            item.DisplayName,
                            item.GainedThisSession)));

        return Loc.Format(
            "Loc_MINING_ENG_SESSION_FORMAT",
            materials.TotalGained,
            body);
    }
}
