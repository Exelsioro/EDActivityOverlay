using EDActivityOverlay.Services;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace EDActivityOverlay.Windows;

public partial class TradeRouteWindow
{
    private bool tradeSalvageInitialized;

    private sealed class TradeSearchSessionState
    {
        public bool HasValues { get; set; }
        public string NearSystem { get; set; } = string.Empty;
        public string Cargo { get; set; } = string.Empty;
        public int MaxRouteDistance { get; set; } = 7;
        public int MaxPriceAge { get; set; } = 4;
        public bool IncludeRoundTrips { get; set; } = true;
        public int MinLandingPad { get; set; }
        public int MaxStationDistance { get; set; }
        public int UseSurfaceStations { get; set; }
        public int MinSupply { get; set; }
        public int MinDemand { get; set; }
        public int OrderBy { get; set; } = 4;
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

        tradeSalvageInitialized = true;

        if (TradeSearchSession.HasValues)
        {
            ApplyTradeSearchSession();
        }

        foreach (ComboBox comboBox in new[]
                 {
                     MinLandingPadComboBox,
                     MaxStationDistanceComboBox,
                     UseSurfaceStationsComboBox,
                     MinSupplyComboBox,
                     MinDemandComboBox,
                     OrderByComboBox
                 })
        {
            comboBox.SelectionChanged += TradeAdvancedFilter_SelectionChanged;
        }

        ShowFiltersButton.Click += TradeShowFiltersButton_PostClick;
        UseJournalValuesButton.Click += TradeUseJournalValuesButton_PostClick;
        Closing += TradeRouteWindow_SalvageClosing;

        UpdateTradeAdvancedFiltersButton();
    }

    private void ApplyTradeSearchSession()
    {
        applyingJournalValues = true;

        try
        {
            NearStarSystemTextBox.Text = TradeSearchSession.NearSystem;
            CargoCapacityTextBox.Text = TradeSearchSession.Cargo;
            MaxRouteDistanceComboBox.SelectedIndex = TradeSearchSession.MaxRouteDistance;
            MaxPriceAgeComboBox.SelectedIndex = TradeSearchSession.MaxPriceAge;
            IncludeRoundTripsCheckBox.IsChecked = TradeSearchSession.IncludeRoundTrips;
            MinLandingPadComboBox.SelectedIndex = TradeSearchSession.MinLandingPad;
            MaxStationDistanceComboBox.SelectedIndex = TradeSearchSession.MaxStationDistance;
            UseSurfaceStationsComboBox.SelectedIndex = TradeSearchSession.UseSurfaceStations;
            MinSupplyComboBox.SelectedIndex = TradeSearchSession.MinSupply;
            MinDemandComboBox.SelectedIndex = TradeSearchSession.MinDemand;
            OrderByComboBox.SelectedIndex = TradeSearchSession.OrderBy;
            AdditionalFiltersGroupBox.Visibility =
                TradeSearchSession.AdvancedFiltersVisible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
        finally
        {
            applyingJournalValues = false;
        }

        systemOverridden = true;
        cargoOverridden = true;
    }

    private void CaptureTradeSearchSession()
    {
        TradeSearchSession.HasValues = true;
        TradeSearchSession.NearSystem = NearStarSystemTextBox.Text.Trim();
        TradeSearchSession.Cargo = CargoCapacityTextBox.Text.Trim();
        TradeSearchSession.MaxRouteDistance = SafeSelectedIndex(MaxRouteDistanceComboBox, 7);
        TradeSearchSession.MaxPriceAge = SafeSelectedIndex(MaxPriceAgeComboBox, 4);
        TradeSearchSession.IncludeRoundTrips = IncludeRoundTripsCheckBox.IsChecked == true;
        TradeSearchSession.MinLandingPad = SafeSelectedIndex(MinLandingPadComboBox);
        TradeSearchSession.MaxStationDistance = SafeSelectedIndex(MaxStationDistanceComboBox);
        TradeSearchSession.UseSurfaceStations = SafeSelectedIndex(UseSurfaceStationsComboBox);
        TradeSearchSession.MinSupply = SafeSelectedIndex(MinSupplyComboBox);
        TradeSearchSession.MinDemand = SafeSelectedIndex(MinDemandComboBox);
        TradeSearchSession.OrderBy = SafeSelectedIndex(OrderByComboBox, 4);
        TradeSearchSession.AdvancedFiltersVisible =
            AdditionalFiltersGroupBox.Visibility == Visibility.Visible;
    }

    private static int SafeSelectedIndex(
        ComboBox comboBox,
        int fallback = 0) =>
        comboBox.SelectedIndex >= 0
            ? comboBox.SelectedIndex
            : fallback;

    private int CountActiveTradeAdvancedFilters()
    {
        int count = 0;

        if (MinLandingPadComboBox.SelectedIndex > 0) count++;
        if (MaxStationDistanceComboBox.SelectedIndex > 0) count++;
        if (UseSurfaceStationsComboBox.SelectedIndex > 0) count++;
        if (MinSupplyComboBox.SelectedIndex > 0) count++;
        if (MinDemandComboBox.SelectedIndex > 0) count++;

        if (OrderByComboBox.SelectedIndex >= 0
            && OrderByComboBox.SelectedIndex != 4)
        {
            count++;
        }

        return count;
    }

    private void UpdateTradeAdvancedFiltersButton()
    {
        int count = CountActiveTradeAdvancedFilters();

        string label =
            Loc.Get(
                AdditionalFiltersGroupBox.Visibility == Visibility.Visible
                    ? "Loc_ADVANCED_FILTERS_OPEN"
                    : "Loc_ADVANCED_FILTERS");

        ShowFiltersButton.Content =
            count > 0
                ? $"{label} · {count}"
                : label;
    }

    private void TradeAdvancedFilter_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        CaptureTradeSearchSession();
        UpdateTradeAdvancedFiltersButton();
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
    }
}
