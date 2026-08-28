using System.Windows;
using System.Windows.Controls;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Engineering;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Navigation;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows;

public partial class EngineeringWindow
{
    private readonly MaterialTraderFinderService materialTraderFinder =
        new();

    private CancellationTokenSource? materialTraderSearchCancellation;
    private CancellationTokenSource? materialTraderRouteCancellation;

    private void MaterialTraderUseCurrentSystem_Click(
        object sender,
        RoutedEventArgs e)
    {
        string currentSystem =
            JournalMonitorService.Instance.Current.StarSystem;

        if (string.IsNullOrWhiteSpace(
                currentSystem))
        {
            MaterialTraderStatusText.Text =
                Loc.Get(
                    "Loc_MATERIAL_TRADER_CURRENT_SYSTEM_UNAVAILABLE");

            return;
        }

        MaterialTraderOriginBox.Text =
            currentSystem;
    }

    private async void MaterialTraderSearch_Click(
        object sender,
        RoutedEventArgs e)
    {
        string origin =
            MaterialTraderOriginBox.Text
                ?.Trim()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(
                origin))
        {
            origin =
                JournalMonitorService.Instance.Current.StarSystem;

            MaterialTraderOriginBox.Text =
                origin;
        }

        if (string.IsNullOrWhiteSpace(
                origin))
        {
            MaterialTraderStatusText.Text =
                Loc.Get(
                    "Loc_MATERIAL_TRADER_CURRENT_SYSTEM_UNAVAILABLE");

            return;
        }

        materialTraderSearchCancellation?.Cancel();
        materialTraderSearchCancellation?.Dispose();

        materialTraderSearchCancellation =
            new CancellationTokenSource();

        MaterialTraderSearchButton.IsEnabled =
            false;

        MaterialTraderStatusText.Text =
            Loc.Get(
                "Loc_MATERIAL_TRADER_SEARCHING");

        try
        {
            EngineeringMaterialCategory[]? desired =
                MaterialTraderNeededOnlyCheckBox.IsChecked
                    == true
                    ? snapshot.Requirements
                        .Where(
                            requirement =>
                                !requirement.IsComplete)
                        .Select(
                            requirement =>
                                requirement.Category)
                        .Where(
                            category =>
                                category
                                is EngineeringMaterialCategory.Raw
                                or EngineeringMaterialCategory.Manufactured
                                or EngineeringMaterialCategory.Encoded)
                        .Distinct()
                        .ToArray()
                    : null;

            IReadOnlyList<MaterialTraderStation> traders =
                await materialTraderFinder.FindNearestAsync(
                    origin,
                    desired,
                    materialTraderSearchCancellation.Token);

            MaterialTraderGrid.ItemsSource =
                traders
                    .Select(
                        station =>
                            MaterialTraderRow.From(
                                station))
                    .ToArray();

            MaterialTraderStatusText.Text =
                traders.Count == 0
                    ? Loc.Get(
                        "Loc_MATERIAL_TRADER_NONE_FOUND")
                    : Loc.Format(
                        "Loc_MATERIAL_TRADER_FOUND_FORMAT",
                        traders.Count,
                        origin);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"Material trader search failed: {ex.Message}");

            MaterialTraderStatusText.Text =
                Loc.Get(
                    "Loc_MATERIAL_TRADER_SEARCH_FAILED");
        }
        finally
        {
            if (IsLoaded)
            {
                MaterialTraderSearchButton.IsEnabled =
                    true;
            }
        }
    }

    private async void MaterialTraderRoute_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender
            is not Button
            {
                CommandParameter:
                    MaterialTraderRow row
            }
            || string.IsNullOrWhiteSpace(
                row.SystemName)
            || targetWindow
               == IntPtr.Zero)
        {
            return;
        }

        materialTraderRouteCancellation?.Cancel();
        materialTraderRouteCancellation?.Dispose();

        materialTraderRouteCancellation =
            new CancellationTokenSource();

        try
        {
            Clipboard.SetText(
                row.SystemName);
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"Unable to copy material trader system before navigation: {ex.Message}");
        }

        bool automatic =
            SettingsService.Instance.Settings.EnableExperimentalRouteAutomation;

        MaterialTraderStatusText.Text =
            Loc.Format(
                "Loc_NAVIGATION_PREPARING",
                row.SystemName);

        if (overlayMode)
        {
            WindowsAPI.SetClickThrough(
                this,
                true);
        }

        try
        {
            await Task.Yield();

            EliteNavigationResult result =
                await EliteRouteNavigationService.Instance.PrepareAsync(
                    row.SystemName,
                    targetWindow,
                    automatic,
                    materialTraderRouteCancellation.Token);

            MaterialTraderStatusText.Text =
                string.IsNullOrWhiteSpace(
                    result.Detail)
                    ? Loc.Format(
                        result.MessageKey,
                        result.TargetSystem)
                    : Loc.Format(
                        result.MessageKey,
                        result.TargetSystem,
                        result.Detail);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (overlayMode
                && IsLoaded)
            {
                ApplyInteractionMode(
                    canInteract: true,
                    showCursor: true);
            }
        }
    }

    private sealed record MaterialTraderRow(
        string Type,
        string SystemName,
        string StationName,
        string Economy,
        string DistanceLy,
        string DistanceLs,
        string UpdatedText)
    {
        public static MaterialTraderRow From(
            MaterialTraderStation station) =>
            new(
                station.Type switch
                {
                    MaterialTraderType.Raw =>
                        Loc.Get(
                            "Loc_MATERIAL_TRADER_RAW"),
                    MaterialTraderType.Manufactured =>
                        Loc.Get(
                            "Loc_MATERIAL_TRADER_MANUFACTURED"),
                    MaterialTraderType.Encoded =>
                        Loc.Get(
                            "Loc_MATERIAL_TRADER_ENCODED"),
                    _ =>
                        station.Type.ToString()
                },
                station.SystemName,
                station.StationName,
                string.IsNullOrWhiteSpace(
                    station.PrimaryEconomy)
                    ? station.SecondaryEconomy
                    : station.PrimaryEconomy,
                Loc.Format(
                    "Loc_MATERIAL_TRADER_DISTANCE_LY_FORMAT",
                    station.DistanceLy),
                station.DistanceToArrivalLs is double arrival
                    ? Loc.Format(
                        "Loc_MATERIAL_TRADER_DISTANCE_LS_FORMAT",
                        arrival)
                    : "—",
                station.UpdatedUtc is DateTimeOffset updated
                    ? updated.ToLocalTime()
                        .ToString(
                            "g")
                    : "—");
    }
}
