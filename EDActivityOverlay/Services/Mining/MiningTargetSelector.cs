using EDActivityOverlay.Models;
using EDActivityOverlay.Services;

namespace EDActivityOverlay.Services.Mining;

public sealed record MiningTargetSelection(
    bool Automatic,
    IReadOnlyList<string> CommodityIds);

public static class MiningTargetSelector
{
    public const int MaxTargets = 5;

    private static readonly string[] Metallic =
    [
        "Monazite", "Serendibite", "Rhodplumsite", "Platinum", "Painite",
        "Osmium", "Palladium", "Gold", "Silver", "Samarium", "Bertrandite",
        "Gallite", "Indite", "Praseodymium", "Thorium"
    ];

    private static readonly string[] MetalRich =
    [
        "Monazite", "Alexandrite", "Grandidierite", "Serendibite", "Rhodplumsite",
        "Benitoite", "Platinum", "Painite", "Osmium", "Silver", "Samarium",
        "Bertrandite", "Gallite", "Indite", "Coltan", "Praseodymium", "Uraninite",
        "Lepidolite", "Thorium"
    ];

    private static readonly string[] Rocky =
    [
        "Monazite", "Musgravite", "Alexandrite", "Serendibite", "Benitoite",
        "Samarium", "Gallite", "Indite", "Coltan", "Cobalt", "Rutile",
        "Uraninite", "Bauxite", "Lepidolite"
    ];

    private static readonly string[] Icy =
    [
        "Opal", "LowTemperatureDiamond", "Alexandrite", "Grandidierite", "Tritium",
        "Bromellite", "LithiumHydroxide", "MethanolMonohydrateCrystals",
        "MethaneClathrate", "HydrogenPeroxide", "LiquidOxygen", "Water"
    ];

    public static MiningTargetSelection Select(
        AppSettings settings,
        MiningRingContextSnapshot ring,
        MiningMarketPriceSnapshot prices)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ring);
        ArgumentNullException.ThrowIfNull(prices);

        if (!settings.MiningAutoSelectTargets)
        {
            return new MiningTargetSelection(
                false,
                NormalizeManualTargets(settings));
        }

        string[] candidates = GetAutoCandidates(ring).ToArray();
        bool sameSystem = ring.SystemAddress != 0 && prices.SystemAddress != 0
            ? ring.SystemAddress == prices.SystemAddress
            : !string.IsNullOrWhiteSpace(ring.SystemName)
              && ring.SystemName.Equals(prices.SystemName, StringComparison.OrdinalIgnoreCase);
        string[] priced = candidates
            .Select(id => new
            {
                Id = id,
                Price = sameSystem && prices.TryGet(id, out MiningMarketPriceQuote? quote)
                    ? quote!.ReferenceSellPrice
                    : 0
            })
            .Where(item => item.Price > 0)
            .OrderByDescending(item => item.Price)
            .ThenBy(item => MiningTargetCatalog.GetDisplayName(item.Id), StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxTargets)
            .Select(item => item.Id)
            .ToArray();

        return new MiningTargetSelection(
            true,
            priced.Length > 0
                ? priced
                : candidates.Take(MaxTargets).ToArray());
    }

    public static IReadOnlyList<string> GetAutoCandidates(MiningRingContextSnapshot ring)
    {
        ArgumentNullException.ThrowIfNull(ring);

        string[] hotspotIds = ring.HotspotCommodityIds
            .Select(item => MiningTargetCatalog.Find(item)?.CommodityId ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!HasResolvedRingClass(ring.RingClass))
        {
            // Signal data is useful even before the ring class is known, but an
            // unknown class must never expand AUTO to the whole mining catalog.
            return hotspotIds
                .Take(MaxTargets)
                .ToArray();
        }

        string[] compatible = GetCompatibleCommodityIds(ring.RingClass).ToArray();
        if (hotspotIds.Length == 0)
        {
            return compatible;
        }

        var hotspotSet = hotspotIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] hotspotCompatible = compatible
            .Where(hotspotSet.Contains)
            .ToArray();

        return hotspotCompatible.Length > 0
            ? hotspotCompatible
            : compatible;
    }

    public static bool HasResolvedRingClass(string? ringClass)
    {
        string value = ringClass?.Trim() ?? string.Empty;
        return value.Contains("MetalRich", StringComparison.OrdinalIgnoreCase)
               || value.Contains("Metalic", StringComparison.OrdinalIgnoreCase)
               || value.Contains("Metallic", StringComparison.OrdinalIgnoreCase)
               || value.Contains("Rocky", StringComparison.OrdinalIgnoreCase)
               || value.Contains("Icy", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> GetCompatibleCommodityIds(string? ringClass)
    {
        string value = ringClass?.Trim() ?? string.Empty;
        IEnumerable<string> candidates = value.Contains("MetalRich", StringComparison.OrdinalIgnoreCase)
            ? MetalRich
            : value.Contains("Metalic", StringComparison.OrdinalIgnoreCase)
              || value.Contains("Metallic", StringComparison.OrdinalIgnoreCase)
                ? Metallic
                : value.Contains("Rocky", StringComparison.OrdinalIgnoreCase)
                    ? Rocky
                    : value.Contains("Icy", StringComparison.OrdinalIgnoreCase)
                        ? Icy
                        : MiningTargetCatalog.Targets.Select(item => item.CommodityId);

        return candidates
            .Select(item => MiningTargetCatalog.Find(item)?.CommodityId ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static IReadOnlyList<string> GetMarketCandidates(
        AppSettings settings,
        MiningRingContextSnapshot ring,
        MiningProspectSnapshot? prospect,
        IEnumerable<CargoCommoditySnapshot> cargo)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ring);
        ArgumentNullException.ThrowIfNull(cargo);

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // DSS signals are useful independently of whether the body Scan event
        // that carries ring class and reserve level was observed this session.
        candidates.UnionWith(ring.HotspotCommodityIds);

        if (HasResolvedRingClass(ring.RingClass))
        {
            candidates.UnionWith(GetCompatibleCommodityIds(ring.RingClass));
        }
        else if (!settings.MiningAutoSelectTargets)
        {
            candidates.UnionWith(NormalizeManualTargets(settings));
        }

        if (prospect is not null)
        {
            foreach (MiningProspectMaterialSnapshot material in prospect.Materials)
            {
                AddKnownCommodity(candidates, material.CommodityId, material.DisplayName);
            }

            AddKnownCommodity(
                candidates,
                prospect.MotherlodeCommodityId,
                prospect.MotherlodeDisplayName);
        }

        foreach (CargoCommoditySnapshot item in cargo.Where(item => item.Count > 0))
        {
            AddKnownCommodity(candidates, item.CommodityId, item.DisplayName);
        }

        return candidates
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddKnownCommodity(
        ISet<string> candidates,
        string? commodityId,
        string? displayName)
    {
        MiningTargetOption? option = MiningTargetCatalog.Find(commodityId)
            ?? MiningTargetCatalog.Find(displayName);
        if (option is not null && !string.IsNullOrWhiteSpace(option.CommodityId))
        {
            candidates.Add(option.CommodityId);
        }
    }

    public static IReadOnlyList<string> NormalizeManualTargets(AppSettings settings)
    {
        IEnumerable<string> source = settings.MiningTargetCommodities.Count > 0
            ? settings.MiningTargetCommodities
            : string.IsNullOrWhiteSpace(settings.MiningTargetCommodity)
                ? Array.Empty<string>()
                : new[] { settings.MiningTargetCommodity };

        return source
            .Select(item => MiningTargetCatalog.Find(item)?.CommodityId ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTargets)
            .ToArray();
    }
}
