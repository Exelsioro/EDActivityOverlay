using System.Windows;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Mining;

namespace EDActivityOverlay.UserControls;

public partial class MiningWorkspaceControl
{
    private MiningRingContextSnapshot CurrentRingContext()
    {
        string ringName = currentSession.IsActive
            ? currentSession.RingName
            : string.Empty;
        string bodyName = currentSession.IsActive && !string.IsNullOrWhiteSpace(currentSession.BodyName)
            ? currentSession.BodyName
            : currentJournal.CurrentBody;
        long systemAddress = currentSession.IsActive && currentSession.SystemAddress != 0
            ? currentSession.SystemAddress
            : currentJournal.SystemAddress;
        string systemName = currentSession.IsActive && !string.IsNullOrWhiteSpace(currentSession.SystemName)
            ? currentSession.SystemName
            : currentJournal.StarSystem;

        MiningRingContextSnapshot ring = MiningRingContextService.Instance.Resolve(
            ringName,
            bodyName,
            systemAddress,
            systemName);

        return MiningRingContextService.EnrichFromDestination(
            ring,
            MiningDestinationService.Instance.Current);
    }

    private MiningTargetSelection CurrentTargetSelection(
        AppSettings settings,
        MiningRingContextSnapshot? ring = null)
    {
        return MiningTargetSelector.Select(
            settings,
            ring ?? CurrentRingContext(),
            MiningMarketPriceService.Instance.Current);
    }

    private void RequestMarketRefresh()
    {
        AppSettings settings = SettingsService.Instance.Settings;
        MiningRingContextSnapshot ring = CurrentRingContext();
        MiningProspectSnapshot? prospect = currentSession.Prospects.LastOrDefault();
        IReadOnlyList<string> candidates = MiningTargetSelector.GetMarketCandidates(
            settings,
            ring,
            prospect,
            currentJournal.CargoByCommodityId.Values);

        if (candidates.Count == 0)
        {
            return;
        }

        MiningMarketPriceService.Instance.RequestRefresh(currentJournal, candidates);
    }

    private void OnMiningRingContextChanged(
        object? sender,
        MiningRingContextChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            RequestMarketRefresh();
            RefreshPresentation();
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            RequestMarketRefresh();
            RefreshPresentation();
        }));
    }

    private void OnMiningMarketPriceChanged(
        object? sender,
        MiningMarketPriceChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            RefreshPresentation();
            return;
        }

        Dispatcher.BeginInvoke(new Action(RefreshPresentation));
    }

    private static string BuildTargetLabel(IReadOnlyList<string> targets, double threshold)
    {
        if (targets.Count == 0)
        {
            return Loc.Get("Loc_MINING_TARGET_HINT");
        }

        string names = string.Join(
            ", ",
            targets.Select(MiningTargetCatalog.GetDisplayName));
        return Loc.Format("Loc_MINING_TARGETS_FORMAT", names, threshold);
    }

    private static string BuildMaterialsLine(
        MiningProspectSnapshot prospect,
        MiningMarketPriceSnapshot prices,
        IReadOnlyList<string> targets)
    {
        var selected = new HashSet<string>(
            targets.Select(item =>
                MiningTargetCatalog.Find(item)?.CommodityId ?? item),
            StringComparer.OrdinalIgnoreCase);

        string materials = string.Join(
            Environment.NewLine,
            prospect.Materials
                .Take(5)
                .Select(item =>
                {
                    string commodityId = MiningTargetCatalog.Find(item.CommodityId)?.CommodityId
                        ?? MiningTargetCatalog.Find(item.DisplayName)?.CommodityId
                        ?? item.CommodityId;
                    string price = FormatMarketPriceStatus(commodityId, prices);
                    bool target = selected.Contains(commodityId);
                    string name = target
                        ? item.DisplayName.ToUpperInvariant()
                        : item.DisplayName;
                    string marker = target ? "► " : "  ";
                    return $"{marker}{name} {item.Proportion:0.#}% · {price}";
                }));

        if (!prospect.HasMotherlode)
        {
            return string.IsNullOrWhiteSpace(materials)
                ? Loc.Get("Loc_No_prospect_data")
                : materials;
        }

        string coreCommodity = string.IsNullOrWhiteSpace(prospect.MotherlodeCommodityId)
            ? prospect.MotherlodeDisplayName
            : prospect.MotherlodeCommodityId;
        string stableCoreId = MiningTargetCatalog.Find(coreCommodity)?.CommodityId
            ?? coreCommodity;
        string corePrice = FormatMarketPriceStatus(stableCoreId, prices);
        string coreName = string.IsNullOrWhiteSpace(prospect.MotherlodeDisplayName)
            ? MiningTargetCatalog.GetDisplayName(stableCoreId)
            : prospect.MotherlodeDisplayName;
        if (selected.Contains(stableCoreId))
        {
            coreName = coreName.ToUpperInvariant();
        }

        string core = $"◆ {coreName} · {corePrice}";
        return string.IsNullOrWhiteSpace(materials)
            ? core
            : core + Environment.NewLine + materials;
    }

    private static string BuildRingContextText(MiningRingContextSnapshot ring)
    {
        if (!ring.Available)
        {
            return string.Empty;
        }

        string ringClass = Loc.Get(RingClassKey(ring.RingClass));
        string reserve = Loc.Get(ReserveKey(ring.ReserveLevel));
        return Loc.Format("Loc_MINING_RING_CONTEXT_FORMAT", ringClass, reserve);
    }

    private static string BuildMarketContextText(
        MiningRingContextSnapshot ring,
        MiningTargetSelection selection,
        MiningMarketPriceSnapshot prices)
    {
        if (prices.IsLoading && prices.Quotes.Count == 0)
        {
            return Loc.Get("Loc_MINING_MARKET_LOADING");
        }

        if (ring.HasHotspots)
        {
            string hotspots = FormatCommodityPriceList(
                ring.HotspotCommodityIds,
                prices,
                3);
            if (!string.IsNullOrWhiteSpace(hotspots))
            {
                return Loc.Format("Loc_MINING_DSS_CONTEXT_FORMAT", hotspots);
            }
        }

        if (MiningTargetSelector.HasResolvedRingClass(ring.RingClass))
        {
            string bestHere = FormatCommodityPriceList(
                MiningTargetSelector.GetCompatibleCommodityIds(ring.RingClass),
                prices,
                3);
            if (!string.IsNullOrWhiteSpace(bestHere))
            {
                return Loc.Format("Loc_MINING_BEST_HERE_FORMAT", bestHere);
            }
        }

        if (selection.CommodityIds.Count > 0)
        {
            string targets = FormatCommodityPriceList(
                selection.CommodityIds,
                prices,
                3);
            if (!string.IsNullOrWhiteSpace(targets))
            {
                return Loc.Format(
                    selection.Automatic
                        ? "Loc_MINING_AUTO_CONTEXT_FORMAT"
                        : "Loc_MINING_MANUAL_CONTEXT_FORMAT",
                    targets);
            }
        }

        return prices.IsLoading
            ? Loc.Get("Loc_MINING_MARKET_LOADING")
            : Loc.Get("Loc_MINING_MARKET_UNAVAILABLE");
    }

    private static string FormatCommodityPriceList(
        IEnumerable<string> commodityIds,
        MiningMarketPriceSnapshot prices,
        int limit)
    {
        return string.Join(
            " · ",
            commodityIds
                .Select(id =>
                {
                    string stableId = MiningTargetCatalog.Find(id)?.CommodityId ?? id;
                    bool available = prices.TryGet(stableId, out MiningMarketPriceQuote? quote);
                    return new
                    {
                        Id = stableId,
                        Price = available ? quote!.ReferenceSellPrice : 0
                    };
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .DistinctBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(item => item.Price)
                .ThenBy(item => MiningTargetCatalog.GetDisplayName(item.Id))
                .Take(Math.Max(1, limit))
                .Select(item =>
                    $"{MiningTargetCatalog.GetDisplayName(item.Id)} "
                    + FormatMarketPriceStatus(item.Id, prices)));
    }

    private static bool HasSellableMiningCargo(GameStateSnapshot state) =>
        state.CargoByCommodityId.Values.Any(item =>
            item.Count > 0
            && (MiningTargetCatalog.Find(item.CommodityId)
                ?? MiningTargetCatalog.Find(item.DisplayName)) is not null);

    private static string BuildEconomicsText(MiningEconomicsSnapshot economics)
    {
        var parts = new List<string>();
        if (economics.HasCargoEstimate)
        {
            parts.Add(Loc.Format(
                "Loc_MINING_ECONOMICS_CARGO_FORMAT",
                economics.EstimatedCargoValue,
                economics.PricedCargoTons));
        }

        if (economics.HasSessionEstimate)
        {
            parts.Add(Loc.Format(
                "Loc_MINING_ECONOMICS_SESSION_FORMAT",
                economics.EstimatedSessionValue,
                economics.PricedRefinedTons));
        }

        if (economics.EstimatedCreditsPerHour > 0)
        {
            parts.Add(Loc.Format(
                "Loc_MINING_ECONOMICS_RATE_FORMAT",
                economics.EstimatedCreditsPerHour));
        }

        return string.Join("  ·  ", parts);
    }

    private static string FormatMarketPrice(int price)
    {
        if (price <= 0)
        {
            return Loc.Get("Loc_MINING_PRICE_UNAVAILABLE");
        }

        string compact = price >= 1_000_000
            ? $"{price / 1_000_000d:0.##}M"
            : price >= 1_000
                ? $"{price / 1_000d:0.#}k"
                : price.ToString("N0");
        return Loc.Format("Loc_MINING_PRICE_FORMAT", compact);
    }

    private static string FormatMarketPriceStatus(
        string commodityId,
        MiningMarketPriceSnapshot prices)
    {
        if (prices.TryGet(commodityId, out MiningMarketPriceQuote? quote))
        {
            return FormatMarketPrice(quote!.ReferenceSellPrice);
        }

        if (prices.IsLoading)
        {
            return Loc.Get("Loc_MINING_PRICE_LOADING");
        }

        if (!string.IsNullOrWhiteSpace(prices.Error))
        {
            return Loc.Get("Loc_MINING_PRICE_PROVIDER_ERROR");
        }

        string stableId = MiningTargetCatalog.Find(commodityId)?.CommodityId
            ?? commodityId;
        return prices.Quotes.ContainsKey(stableId)
            ? Loc.Get("Loc_MINING_PRICE_NO_RECENT_DATA")
            : Loc.Get("Loc_MINING_PRICE_UNAVAILABLE");
    }

    private static string RingClassKey(string? ringClass)
    {
        string value = ringClass ?? string.Empty;
        if (value.Contains("MetalRich", StringComparison.OrdinalIgnoreCase))
        {
            return "Loc_MINING_RING_METAL_RICH";
        }

        if (value.Contains("Metalic", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Metallic", StringComparison.OrdinalIgnoreCase))
        {
            return "Loc_MINING_RING_METALLIC";
        }

        if (value.Contains("Rocky", StringComparison.OrdinalIgnoreCase))
        {
            return "Loc_MINING_RING_ROCKY";
        }

        if (value.Contains("Icy", StringComparison.OrdinalIgnoreCase))
        {
            return "Loc_MINING_RING_ICY";
        }

        return "Loc_MINING_RING_UNKNOWN";
    }

    private static string ReserveKey(string? reserveLevel)
    {
        string value = reserveLevel ?? string.Empty;
        if (value.Contains("Pristine", StringComparison.OrdinalIgnoreCase))
        {
            return "Loc_MINING_RESERVE_PRISTINE";
        }

        if (value.Contains("Major", StringComparison.OrdinalIgnoreCase))
        {
            return "Loc_MINING_RESERVE_MAJOR";
        }

        if (value.Contains("Common", StringComparison.OrdinalIgnoreCase))
        {
            return "Loc_MINING_RESERVE_COMMON";
        }

        if (value.Contains("Low", StringComparison.OrdinalIgnoreCase))
        {
            return "Loc_MINING_RESERVE_LOW";
        }

        if (value.Contains("Depleted", StringComparison.OrdinalIgnoreCase))
        {
            return "Loc_MINING_RESERVE_DEPLETED";
        }

        return "Loc_MINING_RESERVE_UNKNOWN";
    }

    private void RefreshTargetInputEnabledState()
    {
        bool automatic = AutoTargetsCheckBox.IsChecked == true;
        TargetCommodityListBox.IsEnabled = !automatic;
    }

    private void AutoTargetsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        RefreshTargetInputEnabledState();
    }
}
