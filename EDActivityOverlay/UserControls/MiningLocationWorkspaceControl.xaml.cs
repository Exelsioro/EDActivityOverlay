using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Mining;

namespace EDActivityOverlay.UserControls;

public partial class MiningLocationWorkspaceControl : UserControl, IDisposable
{
    private sealed record FilterOption<T>(T Value, string LabelKey)
    {
        public string Label => Loc.Get(LabelKey);
    }

    private sealed record RadiusOption(int Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record LocationRow(
        MiningLocationCandidate Candidate,
        string Score,
        string System,
        string Ring,
        string ClassReserve,
        string Targets,
        string Special,
        string Distance,
        string Arrival);

    private static readonly FilterOption<string>[] RingOptions =
    [
        new("Any", "Loc_MINING_LOCATION_ANY"),
        new("Metallic", "Loc_MINING_RING_METALLIC"),
        new("Metal Rich", "Loc_MINING_RING_METAL_RICH"),
        new("Rocky", "Loc_MINING_RING_ROCKY"),
        new("Icy", "Loc_MINING_RING_ICY")
    ];

    private static readonly FilterOption<int>[] ReserveOptions =
    [
        new(0, "Loc_MINING_LOCATION_ANY"),
        new(2, "Loc_MINING_LOCATION_RESERVE_COMMON_PLUS"),
        new(3, "Loc_MINING_LOCATION_RESERVE_MAJOR_PLUS"),
        new(4, "Loc_MINING_RESERVE_PRISTINE")
    ];

    private static readonly int[] Radii = [30, 50, 80, 120, 200, 300];

    private readonly MiningLocationFinderService finder = new();
    private CancellationTokenSource? searchCancellation;
    private GameStateSnapshot currentJournal = GameStateSnapshot.Empty;
    private IReadOnlyList<MiningLocationCandidate> currentCandidates =
        Array.Empty<MiningLocationCandidate>();
    private MiningLocationCandidate? selectedCandidate;
    private bool disposed;

    public MiningLocationWorkspaceControl()
    {
        InitializeComponent();
        currentJournal = JournalMonitorService.Instance.Current;
        LoadFilterOptions();
        LoadTargets();
        UpdateJournalState(currentJournal);
        ApplySelection(null);
        StatusText.Text = Loc.Get("Loc_MINING_LOCATION_READY");
        SourceFooterText.Text = Loc.Get("Loc_MINING_LOCATION_SOURCE_FOOTER");
    }

    public event Action? BackRequested;
    public event Action? CloseRequested;
    public event Action<string>? NavigateSystemRequested;

    public void UpdateJournalState(GameStateSnapshot state)
    {
        currentJournal = state ?? GameStateSnapshot.Empty;
        if (string.IsNullOrWhiteSpace(OriginSystemTextBox.Text)
            && !string.IsNullOrWhiteSpace(currentJournal.StarSystem))
        {
            OriginSystemTextBox.Text = currentJournal.StarSystem;
        }

        ContextText.Text = string.IsNullOrWhiteSpace(currentJournal.StarSystem)
            ? Loc.Get("Loc_MINING_LOCATION_CONTEXT")
            : Loc.Format("Loc_MINING_LOCATION_CONTEXT_SYSTEM", currentJournal.StarSystem);
    }

    public void RefreshLocalization()
    {
        string ringValue = (RingClassComboBox.SelectedItem as FilterOption<string>)?.Value ?? "Any";
        int reserveValue = (ReserveComboBox.SelectedItem as FilterOption<int>)?.Value ?? 0;
        int radius = (RadiusComboBox.SelectedItem as RadiusOption)?.Value ?? 80;
        string[] selectedIds = CommodityListBox.SelectedItems
            .Cast<MiningTargetOption>()
            .Select(option => option.CommodityId)
            .ToArray();

        LoadFilterOptions(ringValue, reserveValue, radius);
        LoadTargets(selectedIds);
        ApplyRows(currentCandidates);
        UpdateJournalState(currentJournal);
        SourceFooterText.Text = Loc.Get("Loc_MINING_LOCATION_SOURCE_FOOTER");
    }

    private void LoadFilterOptions(
        string ringValue = "Any",
        int reserveValue = 0,
        int radius = 80)
    {
        RingClassComboBox.ItemsSource = null;
        RingClassComboBox.ItemsSource = RingOptions;
        RingClassComboBox.SelectedItem =
            RingOptions.First(option => option.Value.Equals(ringValue, StringComparison.OrdinalIgnoreCase));

        ReserveComboBox.ItemsSource = null;
        ReserveComboBox.ItemsSource = ReserveOptions;
        ReserveComboBox.SelectedItem =
            ReserveOptions.First(option => option.Value == reserveValue);

        RadiusOption[] radiusOptions = Radii
            .Select(value => new RadiusOption(
                value,
                Loc.Format("Loc_MINING_LOCATION_RADIUS_VALUE", value)))
            .ToArray();
        RadiusComboBox.ItemsSource = radiusOptions;
        RadiusComboBox.SelectedItem =
            radiusOptions.FirstOrDefault(option => option.Value == radius)
            ?? radiusOptions.First(option => option.Value == 80);
    }

    private void LoadTargets(IReadOnlyList<string>? preferred = null)
    {
        MiningTargetOption[] options = MiningTargetCatalog.Targets
            .OrderBy(option => MiningTargetCatalog.GetDisplayName(option))
            .ToArray();

        string[] selected = preferred?.Count > 0
            ? preferred
                .Select(id => MiningTargetCatalog.Find(id)?.CommodityId ?? string.Empty)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MiningTargetSelector.MaxTargets)
                .ToArray()
            : DefaultTargetIds();

        CommodityListBox.ItemsSource = options;
        CommodityListBox.SelectedItems.Clear();

        foreach (MiningTargetOption option in options)
        {
            if (selected.Contains(option.CommodityId, StringComparer.OrdinalIgnoreCase))
            {
                CommodityListBox.SelectedItems.Add(option);
            }
        }
    }

    private static string[] DefaultTargetIds()
    {
        AppSettings settings = SettingsService.Instance.Settings;
        IReadOnlyList<string> saved = MiningTargetSelector.NormalizeManualTargets(settings);
        if (saved.Count > 0)
        {
            return saved.Take(MiningTargetSelector.MaxTargets).ToArray();
        }

        string[] priced = MiningMarketPriceService.Instance.Current.Quotes.Values
            .Where(quote => quote.Available)
            .OrderByDescending(quote => quote.ReferenceSellPrice)
            .Take(MiningTargetSelector.MaxTargets)
            .Select(quote => quote.CommodityId)
            .ToArray();

        return priced.Length > 0 ? priced : ["Platinum"];
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (searchCancellation is not null)
        {
            searchCancellation.Cancel();
            return;
        }

        if (!TryBuildQuery(out MiningLocationQuery query, out string error))
        {
            StatusText.Text = error;
            return;
        }

        var cancellation = new CancellationTokenSource();
        searchCancellation = cancellation;
        SetSearchRunning(true);
        StatusText.Text = Loc.Get("Loc_MINING_LOCATION_SEARCHING");

        try
        {
            MiningLocationSearchResult result = await finder.SearchAsync(
                query,
                MiningMarketPriceService.Instance.Current,
                cancellation.Token);

            currentCandidates = result.Candidates;
            ApplyRows(currentCandidates);
            StatusText.Text = Loc.Format(
                "Loc_MINING_LOCATION_FOUND",
                currentCandidates.Count);

            SourceFooterText.Text = result.Warnings.Count == 0
                ? Loc.Get("Loc_MINING_LOCATION_SOURCE_FOOTER")
                : Loc.Get("Loc_MINING_LOCATION_SOURCE_FOOTER")
                  + Environment.NewLine
                  + string.Join(" · ", result.Warnings);

            if (ResultsGrid.Items.Count > 0)
            {
                ResultsGrid.SelectedIndex = 0;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = Loc.Get("Loc_MINING_LOCATION_CANCELLED");
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.Format("Loc_MINING_LOCATION_ERROR", ex.Message);
            Logger.Logger.Warning($"Mining location search failed: {ex}");
        }
        finally
        {
            if (ReferenceEquals(searchCancellation, cancellation))
            {
                searchCancellation = null;
            }

            cancellation.Dispose();
            SetSearchRunning(false);
        }
    }

    private bool TryBuildQuery(
        out MiningLocationQuery query,
        out string error)
    {
        query = null!;
        error = string.Empty;

        string origin = OriginSystemTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(origin))
        {
            error = Loc.Get("Loc_MINING_LOCATION_ORIGIN_REQUIRED");
            return false;
        }

        string[] targets = CommodityListBox.SelectedItems
            .Cast<MiningTargetOption>()
            .Select(option => option.CommodityId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MiningTargetSelector.MaxTargets)
            .ToArray();

        if (targets.Length == 0)
        {
            error = Loc.Get("Loc_MINING_LOCATION_TARGET_REQUIRED");
            return false;
        }

        query = new MiningLocationQuery
        {
            ReferenceSystem = origin,
            RadiusLy = (RadiusComboBox.SelectedItem as RadiusOption)?.Value ?? 80,
            CommodityIds = targets,
            RingClass = (RingClassComboBox.SelectedItem as FilterOption<string>)?.Value ?? "Any",
            MinimumReserveRank = (ReserveComboBox.SelectedItem as FilterOption<int>)?.Value ?? 0,
            SpecialOnly = SpecialOnlyCheckBox.IsChecked == true,
            MaxResults = 100
        };

        try
        {
            query.Validate();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            query = null!;
            return false;
        }
    }

    private void SetSearchRunning(bool running)
    {
        OriginSystemTextBox.IsEnabled = !running;
        CommodityListBox.IsEnabled = !running;
        RadiusComboBox.IsEnabled = !running;
        RingClassComboBox.IsEnabled = !running;
        ReserveComboBox.IsEnabled = !running;
        SpecialOnlyCheckBox.IsEnabled = !running;
        SearchButton.Content = Loc.Get(
            running
                ? "Loc_TRADE_CANCEL"
                : "Loc_MINING_LOCATION_SEARCH");
    }

    private void ApplyRows(IReadOnlyList<MiningLocationCandidate> candidates)
    {
        LocationRow[] rows = candidates
            .Select(ToRow)
            .ToArray();

        ResultsGrid.ItemsSource = rows;
        if (rows.Length == 0)
        {
            ApplySelection(null);
        }
    }

    private static LocationRow ToRow(MiningLocationCandidate candidate)
    {
        string targets = string.Join(
            " · ",
            candidate.HotspotCounts
                .OrderByDescending(pair => pair.Value)
                .Take(4)
                .Select(pair =>
                    $"{MiningTargetCatalog.GetDisplayName(pair.Key)} ×{pair.Value}"));

        return new LocationRow(
            candidate,
            candidate.Score.ToString(CultureInfo.InvariantCulture),
            candidate.SystemName,
            ShortRingName(candidate.SystemName, candidate.RingName),
            string.Join(
                " · ",
                new[] { NormalizeRingClass(candidate.RingClass), ReserveLabel(candidate.ReserveLevel) }
                    .Where(value => !string.IsNullOrWhiteSpace(value))),
            targets,
            SpecialSummary(candidate),
            Loc.Format("Loc_MINING_LOCATION_LY_VALUE", candidate.DistanceLy),
            candidate.DistanceToArrivalLs > 0
                ? Loc.Format("Loc_MINING_LOCATION_LS_VALUE", candidate.DistanceToArrivalLs)
                : "—");
    }

    private void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplySelection((ResultsGrid.SelectedItem as LocationRow)?.Candidate);
    }

    private void ApplySelection(MiningLocationCandidate? candidate)
    {
        selectedCandidate = candidate;
        bool canUse = candidate is not null
                      && !string.IsNullOrWhiteSpace(candidate.SystemName);
        SelectLocationButton.IsEnabled = canUse;
        PlotButton.IsEnabled = canUse;

        if (candidate is null)
        {
            SelectedTitleText.Text = Loc.Get("Loc_MINING_LOCATION_SELECT");
            SelectedMetaText.Text = string.Empty;
            SelectedTargetsText.Text = string.Empty;
            SelectedSpecialText.Text = string.Empty;
            SelectedScoreText.Text = string.Empty;
            SelectedMarketText.Text = string.Empty;
            SelectedSourceText.Text = string.Empty;
            PlotButton.Tag = null;
            return;
        }

        SelectedTitleText.Text =
            $"{candidate.SystemName}{Environment.NewLine}{ShortRingName(candidate.SystemName, candidate.RingName)}";

        SelectedMetaText.Text = Loc.Format(
            "Loc_MINING_LOCATION_DETAIL_META",
            NormalizeRingClass(candidate.RingClass),
            ReserveLabel(candidate.ReserveLevel),
            candidate.DistanceLy,
            candidate.DistanceToArrivalLs);

        SelectedTargetsText.Text = Loc.Format(
            "Loc_MINING_LOCATION_DETAIL_TARGETS",
            string.Join(
                " · ",
                candidate.HotspotCounts
                    .OrderByDescending(pair => pair.Value)
                    .Select(pair =>
                        $"{MiningTargetCatalog.GetDisplayName(pair.Key)} ×{pair.Value}")));

        SelectedSpecialText.Text = BuildSpecialDetail(candidate);

        SelectedScoreText.Text = Loc.Format(
            "Loc_MINING_LOCATION_SCORE_BREAKDOWN",
            candidate.Score,
            candidate.TargetScore,
            candidate.ReserveScore,
            candidate.SpecialScore,
            candidate.TravelScore,
            candidate.MarketScore);

        SelectedMarketText.Text = candidate.MarketReferencePrice > 0
            ? Loc.Format(
                "Loc_MINING_LOCATION_MARKET_CONTEXT",
                MiningTargetCatalog.GetDisplayName(candidate.PrimaryCommodityId),
                candidate.MarketReferencePrice)
            : Loc.Get("Loc_MINING_LOCATION_MARKET_UNAVAILABLE");

        SelectedSourceText.Text = candidate.SpecialSites.Count > 0
            ? Loc.Get("Loc_MINING_LOCATION_SOURCE_WITH_COMMUNITY")
            : Loc.Get("Loc_MINING_LOCATION_SOURCE_SPANSH");

        PlotButton.Tag = candidate.SystemName;
    }

    private static string BuildSpecialDetail(MiningLocationCandidate candidate)
    {
        var parts = new List<string>();
        foreach (MiningLocationSpecialSite site in candidate.SpecialSites
                     .OrderByDescending(item => item.ResType)
                     .ThenByDescending(item => item.OverlapMultiplier))
        {
            string commodity = MiningTargetCatalog.GetDisplayName(site.CommodityId);
            if (site.ResType != MiningResSiteType.None)
            {
                parts.Add($"{commodity}: {ResLabel(site.ResType)}");
            }

            if (site.OverlapMultiplier >= 2)
            {
                parts.Add(Loc.Format(
                    "Loc_MINING_LOCATION_KNOWN_OVERLAP",
                    commodity,
                    site.OverlapMultiplier));
            }
        }

        int signalCount = candidate.HighestHotspotCount;
        bool hasKnownOverlap = candidate.SpecialSites.Any(site => site.HasKnownOverlap);
        if (!hasKnownOverlap && signalCount >= 2)
        {
            parts.Add(Loc.Format(
                "Loc_MINING_LOCATION_UNCONFIRMED_OVERLAP",
                signalCount));
        }

        return parts.Count == 0
            ? Loc.Get("Loc_MINING_LOCATION_NO_SPECIAL")
            : string.Join(Environment.NewLine, parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string SpecialSummary(MiningLocationCandidate candidate)
    {
        MiningResSiteType res = candidate.SpecialSites
            .Select(site => site.ResType)
            .DefaultIfEmpty(MiningResSiteType.None)
            .Max();

        int overlap = candidate.SpecialSites
            .Select(site => site.OverlapMultiplier)
            .DefaultIfEmpty(0)
            .Max();

        var parts = new List<string>();
        if (res != MiningResSiteType.None)
            parts.Add(ResLabel(res));
        if (overlap >= 2)
            parts.Add($"{overlap}x");

        if (parts.Count == 0 && candidate.HighestHotspotCount >= 2)
        {
            parts.Add(Loc.Format(
                "Loc_MINING_LOCATION_MULTI_HOTSPOT_SHORT",
                candidate.HighestHotspotCount));
        }

        return parts.Count == 0 ? "—" : string.Join(" + ", parts);
    }

    private static string ResLabel(MiningResSiteType type) =>
        Loc.Get(type switch
        {
            MiningResSiteType.Hazardous => "Loc_MINING_LOCATION_RES_HAZ",
            MiningResSiteType.High => "Loc_MINING_LOCATION_RES_HIGH",
            MiningResSiteType.Regular => "Loc_MINING_LOCATION_RES_REGULAR",
            MiningResSiteType.Low => "Loc_MINING_LOCATION_RES_LOW",
            _ => "Loc_MINING_LOCATION_NO_SPECIAL"
        });

    private static string ReserveLabel(string reserve)
    {
        int rank = MiningLocationRanker.ReserveRank(reserve);
        return Loc.Get(rank switch
        {
            4 => "Loc_MINING_RESERVE_PRISTINE",
            3 => "Loc_MINING_RESERVE_MAJOR",
            2 => "Loc_MINING_RESERVE_COMMON",
            1 => "Loc_MINING_RESERVE_LOW",
            _ => "Loc_MINING_RESERVE_UNKNOWN"
        });
    }

    private static string NormalizeRingClass(string? ringClass)
    {
        string value = ringClass?.Trim() ?? string.Empty;
        if (value.Contains("MetalRich", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Metal Rich", StringComparison.OrdinalIgnoreCase))
            return Loc.Get("Loc_MINING_RING_METAL_RICH");
        if (value.Contains("Metalic", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Metallic", StringComparison.OrdinalIgnoreCase))
            return Loc.Get("Loc_MINING_RING_METALLIC");
        if (value.Contains("Rocky", StringComparison.OrdinalIgnoreCase))
            return Loc.Get("Loc_MINING_RING_ROCKY");
        if (value.Contains("Icy", StringComparison.OrdinalIgnoreCase))
            return Loc.Get("Loc_MINING_RING_ICY");
        return Loc.Get("Loc_MINING_RING_UNKNOWN");
    }

    private static string ShortRingName(string system, string ring)
    {
        if (!string.IsNullOrWhiteSpace(system)
            && ring.StartsWith(system, StringComparison.OrdinalIgnoreCase))
        {
            return ring[system.Length..].Trim();
        }

        return ring;
    }

    private void SelectLocationButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedCandidate is null)
        {
            return;
        }

        MiningDestinationService.Instance.Select(selectedCandidate);
        StatusText.Text = Loc.Format(
            "Loc_MINING_LOCATION_SELECTED_FORMAT",
            selectedCandidate.SystemName,
            ShortRingName(selectedCandidate.SystemName, selectedCandidate.RingName));
    }

    private void PlotButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedCandidate is not null)
        {
            MiningDestinationService.Instance.Select(selectedCandidate);
        }

        if (PlotButton.Tag is string system && !string.IsNullOrWhiteSpace(system))
        {
            NavigateSystemRequested?.Invoke(system);
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) =>
        BackRequested?.Invoke();

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke();

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = null;
    }
}
