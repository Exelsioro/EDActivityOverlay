using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using EDActivityOverlay.Services;

namespace EDActivityOverlay.Windows;

public partial class TradeRouteWindow
{
    private bool tradeSalvageInitialized;

    private sealed class TradeSearchSessionState
    {
        public bool HasValues { get; set; }
        public string NearSystem { get; set; } = string.Empty;
        public string Cargo { get; set; } = string.Empty;
        public int SourceRadiusLy { get; set; } = 30;
        public int TargetRadiusLy { get; set; } = 80;
        public int MaxPriceAge { get; set; } = 4;
        public bool IncludeFleetCarriers { get; set; }
        public int MinLandingPad { get; set; }
        public int MaxStationDistance { get; set; }
        public int MinSupply { get; set; }
        public int MinDemand { get; set; }
        public bool AdvancedFiltersVisible { get; set; }
    }

    private static readonly TradeSearchSessionState TradeSearchSession =
        new();

    private void TradeRouteWindow_SalvageLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (tradeSalvageInitialized)
        {
            return;
        }

        tradeSalvageInitialized =
            true;

        InitializeArdentTradeUi();

        if (TradeSearchSession.HasValues)
        {
            ApplyTradeSearchSession();
        }

        foreach (ComboBox comboBox in new[]
                 {
                     MaxRouteDistanceComboBox,
                     TargetRouteDistanceComboBox,
                     MaxPriceAgeComboBox,
                     MinLandingPadComboBox,
                     MaxStationDistanceComboBox,
                     MinSupplyComboBox,
                     MinDemandComboBox
                 })
        {
            comboBox.SelectionChanged +=
                TradeFilter_SelectionChanged;
        }

        IncludeFleetCarriersCheckBox.Checked +=
            TradeFilter_CheckChanged;

        IncludeFleetCarriersCheckBox.Unchecked +=
            TradeFilter_CheckChanged;

        ShowFiltersButton.Click +=
            TradeShowFiltersButton_PostClick;

        UseJournalValuesButton.Click +=
            TradeUseJournalValuesButton_PostClick;

        Closing +=
            TradeRouteWindow_SalvageClosing;

        UpdateTradeAdvancedFiltersButton();
    }

    private void ApplyTradeSearchSession()
    {
        applyingJournalValues =
            true;

        try
        {
            NearStarSystemTextBox.Text =
                TradeSearchSession.NearSystem;

            CargoCapacityTextBox.Text =
                TradeSearchSession.Cargo;

            SelectRadius(
                MaxRouteDistanceComboBox,
                TradeSearchSession.SourceRadiusLy);

            SelectRadius(
                TargetRouteDistanceComboBox,
                TradeSearchSession.TargetRadiusLy);

            MaxPriceAgeComboBox.SelectedIndex =
                TradeSearchSession.MaxPriceAge;

            IncludeFleetCarriersCheckBox.IsChecked =
                TradeSearchSession.IncludeFleetCarriers;

            MinLandingPadComboBox.SelectedIndex =
                TradeSearchSession.MinLandingPad;

            MaxStationDistanceComboBox.SelectedIndex =
                TradeSearchSession.MaxStationDistance;

            MinSupplyComboBox.SelectedIndex =
                TradeSearchSession.MinSupply;

            MinDemandComboBox.SelectedIndex =
                TradeSearchSession.MinDemand;

            AdditionalFiltersGroupBox.Visibility =
                TradeSearchSession.AdvancedFiltersVisible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
        finally
        {
            applyingJournalValues =
                false;
        }

        systemOverridden =
            true;

        cargoOverridden =
            true;
    }

    private void CaptureTradeSearchSession()
    {
        if (!tradeSalvageInitialized)
        {
            return;
        }

        TradeSearchSession.HasValues =
            true;

        TradeSearchSession.NearSystem =
            NearStarSystemTextBox.Text.Trim();

        TradeSearchSession.Cargo =
            CargoCapacityTextBox.Text.Trim();

        TradeSearchSession.SourceRadiusLy =
            GetSelectedRadius(
                MaxRouteDistanceComboBox);

        TradeSearchSession.TargetRadiusLy =
            GetSelectedRadius(
                TargetRouteDistanceComboBox);

        TradeSearchSession.MaxPriceAge =
            SafeSelectedIndex(
                MaxPriceAgeComboBox,
                fallback: 4);

        TradeSearchSession.IncludeFleetCarriers =
            IncludeFleetCarriersCheckBox.IsChecked
            == true;

        TradeSearchSession.MinLandingPad =
            SafeSelectedIndex(
                MinLandingPadComboBox);

        TradeSearchSession.MaxStationDistance =
            SafeSelectedIndex(
                MaxStationDistanceComboBox);

        TradeSearchSession.MinSupply =
            SafeSelectedIndex(
                MinSupplyComboBox);

        TradeSearchSession.MinDemand =
            SafeSelectedIndex(
                MinDemandComboBox);

        TradeSearchSession.AdvancedFiltersVisible =
            AdditionalFiltersGroupBox.Visibility
            == Visibility.Visible;
    }

    private static int SafeSelectedIndex(
        ComboBox comboBox,
        int fallback = 0) =>
        comboBox.SelectedIndex >= 0
            ? comboBox.SelectedIndex
            : fallback;

    private int CountActiveTradeAdvancedFilters()
    {
        int count =
            0;

        if (MinLandingPadComboBox.SelectedIndex > 0)
        {
            count++;
        }

        if (MaxStationDistanceComboBox.SelectedIndex > 0)
        {
            count++;
        }

        if (MinSupplyComboBox.SelectedIndex > 0)
        {
            count++;
        }

        if (MinDemandComboBox.SelectedIndex > 0)
        {
            count++;
        }

        return
            count;
    }

    private void UpdateTradeAdvancedFiltersButton()
    {
        int count =
            CountActiveTradeAdvancedFilters();

        string label =
            Loc.Get(
                AdditionalFiltersGroupBox.Visibility
                == Visibility.Visible
                    ? "Loc_ADVANCED_FILTERS_OPEN"
                    : "Loc_ADVANCED_FILTERS");

        ShowFiltersButton.Content =
            count > 0
                ? $"{label} · {count}"
                : label;
    }

    private void TradeFilter_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        CaptureTradeSearchSession();
        UpdateTradeAdvancedFiltersButton();
    }

    private void TradeFilter_CheckChanged(
        object sender,
        RoutedEventArgs e)
    {
        CaptureTradeSearchSession();
    }

    private void TradeShowFiltersButton_PostClick(
        object sender,
        RoutedEventArgs e)
    {
        CaptureTradeSearchSession();
        UpdateTradeAdvancedFiltersButton();
    }

    private void TradeUseJournalValuesButton_PostClick(
        object sender,
        RoutedEventArgs e)
    {
        CaptureTradeSearchSession();
    }

    private void TradeRouteWindow_SalvageClosing(
        object? sender,
        CancelEventArgs e)
    {
        CaptureTradeSearchSession();
        CancelArdentTradeSearch();
    }
}
