using System.Windows;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Mining;

namespace EDActivityOverlay.UserControls;

public partial class MiningWorkspaceControl
{
    private void RefreshLocationPresentation(
        string currentSystem,
        string currentRingName,
        MiningRingContextSnapshot ringContext)
    {
        MiningDestinationSnapshot destination =
            MiningDestinationService.Instance.Current;

        CompactJournalContextText.Text = BuildCurrentLocationLine(
            currentSystem,
            destination.Available ? string.Empty : currentRingName);

        if (!destination.Available)
        {
            DestinationPanel.Visibility = Visibility.Collapsed;
            DestinationLabelText.Text = string.Empty;
            DestinationSystemText.Text = string.Empty;
            DestinationBodyText.Text = string.Empty;
            DestinationMetaText.Text = string.Empty;
            return;
        }

        bool sameSystem = string.Equals(
            currentSystem,
            destination.SystemName,
            StringComparison.OrdinalIgnoreCase);

        bool sameRing = sameSystem
                        && ringContext.Available
                        && RingNamesMatch(
                            currentSystem,
                            ringContext.RingName,
                            destination);

        DestinationLabelText.Text = Loc.Get(
            sameRing
                ? "Loc_MINING_CURRENT_RING"
                : sameSystem
                    ? "Loc_MINING_DESTINATION_IN_SYSTEM"
                    : "Loc_MINING_DESTINATION");

        DestinationSystemText.Text = destination.SystemName;
        DestinationBodyText.Text = string.IsNullOrWhiteSpace(destination.BodyName)
            ? MiningDestinationSnapshot.ShortRingName(
                destination.SystemName,
                destination.RingName)
            : destination.BodyName;
        DestinationMetaText.Text = BuildDestinationMeta(destination);
        DestinationPanel.Visibility = Visibility.Visible;
    }

    private static string BuildCurrentLocationLine(
        string currentSystem,
        string currentRingName)
    {
        if (string.IsNullOrWhiteSpace(currentSystem))
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(currentRingName)
            ? Loc.Format(
                "Loc_MINING_CURRENT_LOCATION_FORMAT",
                currentSystem)
            : Loc.Format(
                "Loc_MINING_CURRENT_LOCATION_RING_FORMAT",
                currentSystem,
                MiningDestinationSnapshot.ShortRingName(
                    currentSystem,
                    currentRingName));
    }

    private static string BuildDestinationMeta(
        MiningDestinationSnapshot destination)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(destination.RingDisplayName))
        {
            parts.Add(destination.RingDisplayName.ToUpperInvariant());
        }

        string ringClass = Loc.Get(RingClassKey(destination.RingClass));
        if (!ringClass.Equals(
                Loc.Get("Loc_MINING_RING_UNKNOWN"),
                StringComparison.Ordinal))
        {
            parts.Add(ringClass);
        }

        string reserve = Loc.Get(ReserveKey(destination.ReserveLevel));
        if (!reserve.Equals(
                Loc.Get("Loc_MINING_RESERVE_UNKNOWN"),
                StringComparison.Ordinal))
        {
            parts.Add(reserve);
        }

        if (destination.ResType != MiningResSiteType.None)
        {
            parts.Add(Loc.Get(destination.ResType switch
            {
                MiningResSiteType.Hazardous => "Loc_MINING_LOCATION_RES_HAZ",
                MiningResSiteType.High => "Loc_MINING_LOCATION_RES_HIGH",
                MiningResSiteType.Regular => "Loc_MINING_LOCATION_RES_REGULAR",
                MiningResSiteType.Low => "Loc_MINING_LOCATION_RES_LOW",
                _ => "Loc_MINING_LOCATION_NO_SPECIAL"
            }));
        }

        if (destination.OverlapMultiplier >= 2)
        {
            parts.Add($"{destination.OverlapMultiplier}x");
        }

        if (destination.DistanceLy > 0)
        {
            parts.Add(Loc.Format(
                "Loc_MINING_LOCATION_LY_VALUE",
                destination.DistanceLy));
        }

        if (destination.DistanceToArrivalLs > 0)
        {
            parts.Add(Loc.Format(
                "Loc_MINING_LOCATION_LS_VALUE",
                destination.DistanceToArrivalLs));
        }

        return string.Join(" · ", parts);
    }

    private static bool RingNamesMatch(
        string currentSystem,
        string currentRing,
        MiningDestinationSnapshot destination)
    {
        static string Normalize(string value) =>
            string.Join(
                " ",
                (value ?? string.Empty)
                    .Split(
                        [' ', '\t', '\r', '\n'],
                        StringSplitOptions.RemoveEmptyEntries))
            .Trim();

        string current = Normalize(
            MiningDestinationSnapshot.ShortRingName(
                currentSystem,
                currentRing));
        string expected = Normalize(
            MiningDestinationSnapshot.ShortRingName(
                destination.SystemName,
                destination.RingName));
        string composed = Normalize(
            $"{destination.BodyName} {destination.RingDisplayName}");

        return current.Equals(
                   expected,
                   StringComparison.OrdinalIgnoreCase)
               || (!string.IsNullOrWhiteSpace(composed)
                   && current.Equals(
                       composed,
                       StringComparison.OrdinalIgnoreCase));
    }

    private void ClearMiningDestinationButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        MiningDestinationService.Instance.Clear();
        RefreshPresentation();
    }
}
