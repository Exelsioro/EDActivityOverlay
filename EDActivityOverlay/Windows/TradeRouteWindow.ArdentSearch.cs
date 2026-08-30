using EDActivityOverlay.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EDActivityOverlay.Models;
using EDActivityOverlay.Models.Trading;
using EDActivityOverlay.Services.Ardent;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Trading;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows;

public partial class TradeRouteWindow
{
    private static readonly int[] TradeRadiusValues =
    [
        10,
        20,
        30,
        40,
        50,
        60,
        80,
        100,
        150,
        200
    ];

    private static readonly double?[] StationDistanceValues =
    [
        null,
        100,
        500,
        1_000,
        2_000,
        5_000,
        10_000,
        15_000,
        20_000,
        25_000,
        50_000,
        100_000
    ];

    private static readonly long[] VolumeValues =
    [
        1,
        100,
        500,
        1_000,
        2_500,
        5_000,
        10_000,
        50_000
    ];

    private readonly TradeSearchService ardentTradeSearchService =
        new();

    private ComboBox? targetRouteDistanceComboBox;
    private CheckBox? includeFleetCarriersCheckBox;
    private CancellationTokenSource? ardentSearchCancellation;

    private List<TradeRoute> lastArdentRoutes =
        new();

    private int lastArdentCompleted;
    private int lastArdentTotal;
    private int lastArdentFailed;

    internal ComboBox TargetRouteDistanceComboBox =>
        targetRouteDistanceComboBox
        ?? throw new InvalidOperationException(
            "Target radius control has not been initialized.");

    internal CheckBox IncludeFleetCarriersCheckBox =>
        includeFleetCarriersCheckBox
        ?? throw new InvalidOperationException(
            "Fleet Carrier control has not been initialized.");

    private void InitializeArdentTradeUi()
    {
        if (targetRouteDistanceComboBox is not null)
        {
            return;
        }

        SearchButton.Click -=
            SearchButton_Click;

        SearchButton.Click +=
            ArdentSearchButton_Click;

        ConfigureRadiusCombo(
            MaxRouteDistanceComboBox,
            defaultRadius:
                30);

        if (MaxRouteDistanceComboBox.Parent
            is not Grid basicGrid)
        {
            throw new InvalidOperationException(
                "Trade route parameter grid was not found.");
        }

        TextBlock? sourceRadiusLabel =
            FindGridTextBlock(
                basicGrid,
                row: 1,
                column: 0);

        sourceRadiusLabel?.SetResourceReference(
            TextBlock.TextProperty,
            "Loc_TRADE_SOURCE_RADIUS");

        targetRouteDistanceComboBox =
            CreateRadiusCombo(
                defaultRadius:
                    80);

        Grid.SetRow(
            targetRouteDistanceComboBox,
            2);

        Grid.SetColumn(
            targetRouteDistanceComboBox,
            1);

        basicGrid.Children.Add(
            targetRouteDistanceComboBox);

        var targetRadiusLabel =
            new TextBlock
            {
                VerticalAlignment =
                    VerticalAlignment.Center,
                Margin =
                    new Thickness(
                        0,
                        5,
                        10,
                        5)
            };

        if (TryFindResource(
                "BodyTextStyle")
            is Style bodyTextStyle)
        {
            targetRadiusLabel.Style =
                bodyTextStyle;
        }

        targetRadiusLabel.SetResourceReference(
            TextBlock.TextProperty,
            "Loc_TRADE_TARGET_RADIUS");

        Grid.SetRow(
            targetRadiusLabel,
            2);

        Grid.SetColumn(
            targetRadiusLabel,
            0);

        basicGrid.Children.Add(
            targetRadiusLabel);

        includeFleetCarriersCheckBox =
            new CheckBox
            {
                VerticalAlignment =
                    VerticalAlignment.Center,
                Margin =
                    new Thickness(
                        0,
                        5,
                        0,
                        5)
            };

        includeFleetCarriersCheckBox.SetResourceReference(
            ContentControl.ContentProperty,
            "Loc_TRADE_INCLUDE_FLEET_CARRIERS");

        includeFleetCarriersCheckBox.SetResourceReference(
            Control.ForegroundProperty,
            "PrimaryTextColorBrush");

        Grid.SetRow(
            includeFleetCarriersCheckBox,
            2);

        Grid.SetColumn(
            includeFleetCarriersCheckBox,
            2);

        Grid.SetColumnSpan(
            includeFleetCarriersCheckBox,
            2);

        basicGrid.Children.Add(
            includeFleetCarriersCheckBox);

        IncludeRoundTripsCheckBox.Visibility =
            Visibility.Collapsed;

        DisplayPowerplayBonusesCheckBox.Visibility =
            Visibility.Collapsed;

        HideUnsupportedLegacyFilters();

        SearchButton.SetResourceReference(
            ContentControl.ContentProperty,
            "Loc_SEARCH_ROUTES");
    }

    private void HideUnsupportedLegacyFilters()
    {
        if (UseSurfaceStationsComboBox.Parent
            is not Grid filterGrid)
        {
            return;
        }

        HideFilterRow(
            filterGrid,
            row: 2,
            UseSurfaceStationsComboBox);

        HideFilterRow(
            filterGrid,
            row: 3,
            SourceStationPowerComboBox);

        HideFilterRow(
            filterGrid,
            row: 4,
            TargetStationPowerComboBox);

        HideFilterRow(
            filterGrid,
            row: 7,
            OrderByComboBox);
    }

    private static void HideFilterRow(
        Grid grid,
        int row,
        Control control)
    {
        control.Visibility =
            Visibility.Collapsed;

        foreach (UIElement child
                 in grid.Children)
        {
            if (Grid.GetRow(
                    child)
                == row
                && Grid.GetColumn(
                       child)
                   == 0
                && child
                   is TextBlock label)
            {
                label.Visibility =
                    Visibility.Collapsed;
            }
        }
    }

    private ComboBox CreateRadiusCombo(
        int defaultRadius)
    {
        var combo =
            new ComboBox
            {
                Margin =
                    new Thickness(
                        0,
                        5,
                        10,
                        5)
            };

        if (TryFindResource(
                "ComboBoxStyle")
            is Style comboStyle)
        {
            combo.Style =
                comboStyle;
        }

        ConfigureRadiusCombo(
            combo,
            defaultRadius);

        return
            combo;
    }

    private void ConfigureRadiusCombo(
        ComboBox comboBox,
        int defaultRadius)
    {
        comboBox.Items.Clear();

        Style? itemStyle =
            TryFindResource(
                "ComboBoxItemStyle")
            as Style;

        foreach (int radius
                 in TradeRadiusValues)
        {
            var item =
                new ComboBoxItem
                {
                    Content =
                        $"{radius} LY",
                    Tag =
                        radius
                };

            if (itemStyle is not null)
            {
                item.Style =
                    itemStyle;
            }

            comboBox.Items.Add(
                item);
        }

        SelectRadius(
            comboBox,
            defaultRadius);
    }

    internal static void SelectRadius(
        ComboBox comboBox,
        int radius)
    {
        for (int index = 0;
             index < comboBox.Items.Count;
             index++)
        {
            if (comboBox.Items[index]
                is ComboBoxItem item
                && item.Tag
                   is int value
                && value == radius)
            {
                comboBox.SelectedIndex =
                    index;

                return;
            }
        }

        comboBox.SelectedIndex =
            0;
    }

    internal static int GetSelectedRadius(
        ComboBox comboBox)
    {
        if (comboBox.SelectedItem
            is ComboBoxItem item
            && item.Tag
               is int value)
        {
            return
                value;
        }

        return
            10;
    }

    private static TextBlock? FindGridTextBlock(
        Grid grid,
        int row,
        int column) =>
        grid.Children
            .OfType<TextBlock>()
            .FirstOrDefault(
                text =>
                    Grid.GetRow(
                        text)
                    == row
                    && Grid.GetColumn(
                           text)
                       == column);

    private async void ArdentSearchButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ardentSearchCancellation is not null)
        {
            ardentSearchCancellation.Cancel();

            StatusText.Text =
                Loc.Get(
                    "Loc_TRADE_SEARCH_CANCELLING");

            return;
        }

        if (!TryBuildArdentSearchConstraints(
                out TradeSearchConstraints constraints,
                out string validationError))
        {
            StatusText.Text =
                validationError;

            return;
        }

        CaptureTradeSearchSession();

        var cancellation =
            new CancellationTokenSource();

        ardentSearchCancellation =
            cancellation;

        isSearchInProgress =
            true;

        lastArdentRoutes =
            new List<TradeRoute>();

        lastArdentCompleted =
            0;

        lastArdentTotal =
            0;

        lastArdentFailed =
            0;

        SetArdentSearchRunning(
            true);

        if (Owner
            is MainWindow mainWindow)
        {
            mainWindow.UnpinRouteOverlay();
        }

        try
        {
            await foreach (TradeSearchProgress progress
                           in ardentTradeSearchService.SearchProgressAsync(
                               constraints,
                               cancellation.Token))
            {
                ApplyArdentSearchProgress(
                    progress);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text =
                Loc.Format(
                    "Loc_TRADE_SEARCH_CANCELLED",
                    lastArdentRoutes.Count);

            PresentLastArdentRoutes(
                searching:
                    false);
        }
        catch (ArdentApiException ex)
        {
            StatusText.Text =
                Loc.Format(
                    "Loc_TRADE_SEARCH_ERROR",
                    $"{(int)ex.StatusCode} {ex.StatusCode}");

            Logger.Logger.Error(
                $"Ardent trade search failed: {ex}");
        }
        catch (Exception ex)
        {
            StatusText.Text =
                Loc.Format(
                    "Loc_TRADE_SEARCH_ERROR",
                    ex.Message);

            Logger.Logger.Error(
                $"Trade search failed: {ex}");
        }
        finally
        {
            if (ReferenceEquals(
                    ardentSearchCancellation,
                    cancellation))
            {
                ardentSearchCancellation =
                    null;
            }

            cancellation.Dispose();

            isSearchInProgress =
                false;

            SetArdentSearchRunning(
                false);
        }
    }

    private void ApplyArdentSearchProgress(
        TradeSearchProgress progress)
    {
        lastArdentCompleted =
            progress.CompletedCommodities;

        lastArdentTotal =
            progress.TotalCommodities;

        lastArdentFailed =
            progress.FailedCommodities;

        switch (progress.Stage)
        {
            case TradeSearchStage.ResolvingOrigin:
                StatusText.Text =
                    Loc.Get(
                        "Loc_TRADE_SEARCH_RESOLVING");
                return;

            case TradeSearchStage.LoadingCommodityReports:
                StatusText.Text =
                    Loc.Get(
                        "Loc_TRADE_SEARCH_LOADING_MARKET");
                return;

            case TradeSearchStage.Searching:
                StatusText.Text =
                    progress.FailedCommodities > 0
                        ? Loc.Format(
                            "Loc_TRADE_SEARCH_PROGRESS_FAILED",
                            progress.CompletedCommodities,
                            progress.TotalCommodities,
                            progress.BestCandidates.Count,
                            progress.FailedCommodities)
                        : Loc.Format(
                            "Loc_TRADE_SEARCH_PROGRESS",
                            progress.CompletedCommodities,
                            progress.TotalCommodities,
                            progress.BestCandidates.Count);

                if (progress.BestCandidates.Count > 0)
                {
                    lastArdentRoutes =
                        TradeRoutePresentationAdapter.ToPresentation(
                            progress.BestCandidates);

                    PresentLastArdentRoutes(
                        searching:
                            true);
                }

                return;

            case TradeSearchStage.Completed:
                if (progress.BestCandidates.Count > 0)
                {
                    lastArdentRoutes =
                        TradeRoutePresentationAdapter.ToPresentation(
                            progress.BestCandidates);
                }

                StatusText.Text =
                    Loc.Format(
                        "Loc_TRADE_SEARCH_DONE",
                        lastArdentRoutes.Count,
                        progress.Elapsed.TotalSeconds);

                PresentLastArdentRoutes(
                    searching:
                        false);

                return;
        }
    }

    private void PresentLastArdentRoutes(
        bool searching)
    {
        if (lastArdentRoutes.Count == 0
            || Owner
               is not MainWindow mainWindow)
        {
            return;
        }

        mainWindow.ShowProgressiveTradeResults(
            lastArdentRoutes,
            searching,
            lastArdentCompleted,
            lastArdentTotal,
            lastArdentFailed);
    }

    private bool TryBuildArdentSearchConstraints(
        out TradeSearchConstraints constraints,
        out string error)
    {
        constraints =
            null!;

        error =
            string.Empty;

        string systemName =
            NearStarSystemTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                systemName))
        {
            error =
                Loc.Get(
                    "Loc_TRADE_VALIDATION_SYSTEM");

            return
                false;
        }

        if (!int.TryParse(
                CargoCapacityTextBox.Text.Trim(),
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out int cargo)
            || cargo < 1)
        {
            error =
                Loc.Get(
                    "Loc_TRADE_VALIDATION_CARGO");

            return
                false;
        }

        GameStateSnapshot journal =
            JournalMonitorService.Instance.Current;

        long systemAddress =
            journal.SystemAddress != 0
            && journal.StarSystem.Equals(
                systemName,
                StringComparison.OrdinalIgnoreCase)
                ? journal.SystemAddress
                : 0;

        int priceAgeIndex =
            Math.Clamp(
                MaxPriceAgeComboBox.SelectedIndex,
                0,
                4);

        TimeSpan maxAge =
            priceAgeIndex switch
            {
                0 =>
                    TimeSpan.FromHours(
                        8),
                1 =>
                    TimeSpan.FromHours(
                        16),
                2 =>
                    TimeSpan.FromDays(
                        1),
                3 =>
                    TimeSpan.FromDays(
                        2),
                _ =>
                    TimeSpan.FromDays(
                        3)
            };

        int stationDistanceIndex =
            Math.Clamp(
                MaxStationDistanceComboBox.SelectedIndex,
                0,
                StationDistanceValues.Length - 1);

        int supplyIndex =
            Math.Clamp(
                MinSupplyComboBox.SelectedIndex,
                0,
                VolumeValues.Length - 1);

        int demandIndex =
            Math.Clamp(
                MinDemandComboBox.SelectedIndex,
                0,
                VolumeValues.Length - 1);

        constraints =
            new TradeSearchConstraints
            {
                OriginSystemName =
                    systemName,
                OriginSystemAddress =
                    systemAddress,
                CargoCapacity =
                    cargo,
                SourceSearchRadiusLy =
                    GetSelectedRadius(
                        MaxRouteDistanceComboBox),
                TargetSearchRadiusLy =
                    GetSelectedRadius(
                        TargetRouteDistanceComboBox),
                MaxDataAge =
                    maxAge,
                MinLandingPadSize =
                    Math.Clamp(
                        MinLandingPadComboBox.SelectedIndex + 1,
                        1,
                        3),
                MaxStationDistanceLs =
                    StationDistanceValues[
                        stationDistanceIndex],
                IncludeFleetCarriers =
                    IncludeFleetCarriersCheckBox.IsChecked
                    == true,
                MinSupply =
                    VolumeValues[
                        supplyIndex],
                MinDemand =
                    VolumeValues[
                        demandIndex],
                MaxCommodityCandidates =
                    50,
                MaxResults =
                    50,
                MaxConcurrentCommoditySearches =
                    6
            };

        try
        {
            constraints.Validate();
        }
        catch (Exception ex)
        {
            error =
                ex.Message;

            constraints =
                null;

            return
                false;
        }

        return
            true;
    }

    private void SetArdentSearchRunning(
        bool running)
    {
        NearStarSystemTextBox.IsEnabled =
            !running;

        CargoCapacityTextBox.IsEnabled =
            !running;

        MaxRouteDistanceComboBox.IsEnabled =
            !running;

        TargetRouteDistanceComboBox.IsEnabled =
            !running;

        MaxPriceAgeComboBox.IsEnabled =
            !running;

        IncludeFleetCarriersCheckBox.IsEnabled =
            !running;

        ShowFiltersButton.IsEnabled =
            !running;

        AdditionalFiltersGroupBox.IsEnabled =
            !running;

        UseJournalValuesButton.IsEnabled =
            !running;

        TestDataButton.IsEnabled =
            !running;

        SearchButton.IsEnabled =
            true;

        SearchButton.SetResourceReference(
            ContentControl.ContentProperty,
            running
                ? "Loc_TRADE_CANCEL"
                : "Loc_SEARCH_ROUTES");
    }

    private void CancelArdentTradeSearch()
    {
        ardentSearchCancellation?.Cancel();
    }
}
