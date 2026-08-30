using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Trading;

public sealed class CargoSaleSearchService
{
    private sealed record Observation(
        TradeMarketOrder Order,
        CargoSaleLine Line,
        bool IsLocal);

    private readonly ITradeDataProvider provider;

    public CargoSaleSearchService()
        : this(new ArdentMarketDataProvider())
    {
    }

    public CargoSaleSearchService(
        ITradeDataProvider provider)
    {
        this.provider =
            provider
            ?? throw new ArgumentNullException(
                nameof(provider));
    }

    public async Task<IReadOnlyList<CargoSaleCandidate>> SearchAsync(
        GameStateSnapshot state,
        TradeSearchConstraints constraints,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(constraints);

        constraints.Validate();

        CargoCommoditySnapshot[] cargo =
            state.CargoByCommodityId
                .Values
                .Where(item =>
                    item.Count > 0
                    && !string.IsNullOrWhiteSpace(
                        item.CommodityId))
                .OrderBy(item =>
                    item.CommodityId,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (cargo.Length == 0)
        {
            return Array.Empty<CargoSaleCandidate>();
        }

        TradeSystemLocation origin =
            await provider.ResolveSystemAsync(
                    new TradeSystemReference(
                        state.StarSystem,
                        state.SystemAddress),
                    cancellationToken)
                .ConfigureAwait(false);

        using var gate =
            new SemaphoreSlim(
                Math.Max(
                    1,
                    constraints.MaxConcurrentCommoditySearches));

        Task<IReadOnlyList<Observation>>[] tasks =
            cargo
                .Select(item =>
                    SearchCommodityAsync(
                        origin,
                        item,
                        constraints,
                        gate,
                        cancellationToken))
                .ToArray();

        IReadOnlyList<Observation>[] remote =
            await Task.WhenAll(tasks)
                .ConfigureAwait(false);

        var observations =
            new List<Observation>(
                remote.Sum(items => items.Count)
                + cargo.Length);

        foreach (IReadOnlyList<Observation> items in remote)
        {
            observations.AddRange(items);
        }

        observations.AddRange(
            BuildCurrentMarketObservations(
                state,
                cargo));

        return BuildCandidates(
            observations,
            cargo.Sum(item => item.Count),
            constraints.MaxResults);
    }

    private async Task<IReadOnlyList<Observation>> SearchCommodityAsync(
        TradeSystemLocation origin,
        CargoCommoditySnapshot cargo,
        TradeSearchConstraints constraints,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            IReadOnlyList<TradeMarketOrder> orders =
                await provider.GetNearbyImportsAsync(
                        origin,
                        cargo.CommodityId,
                        constraints.TargetSearchRadiusLy,
                        constraints,
                        cancellationToken)
                    .ConfigureAwait(false);

            DateTimeOffset now =
                DateTimeOffset.UtcNow;

            return orders
                .Where(order =>
                    IsUsableBuyer(
                        order,
                        constraints,
                        now))
                .Select(order =>
                    BuildObservation(
                        order,
                        cargo,
                        isLocal: false))
                .Where(item =>
                    item.Line.SellAmount > 0)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsUsableBuyer(
        TradeMarketOrder order,
        TradeSearchConstraints constraints,
        DateTimeOffset now)
    {
        if (order.SellToStationPrice <= 0)
        {
            return false;
        }

        if (!constraints.IncludeFleetCarriers
            && order.IsFleetCarrier)
        {
            return false;
        }

        if (order.MaxLandingPadSize
            < constraints.MinLandingPadSize)
        {
            return false;
        }

        if (constraints.MaxStationDistanceLs is { } maxLs
            && (order.DistanceToArrivalLs is null
                || order.DistanceToArrivalLs > maxLs))
        {
            return false;
        }

        double distance =
            order.ReferenceDistanceLy
            ?? double.MaxValue;

        if (distance > constraints.TargetSearchRadiusLy)
        {
            return false;
        }

        if (order.UpdatedAt == DateTimeOffset.MinValue)
        {
            return false;
        }

        TimeSpan age =
            now > order.UpdatedAt
                ? now - order.UpdatedAt
                : TimeSpan.Zero;

        return age <= constraints.MaxDataAge;
    }

    private static Observation BuildObservation(
        TradeMarketOrder order,
        CargoCommoditySnapshot cargo,
        bool isLocal)
    {
        long usableDemand =
            order.HasInfiniteDemand
                ? cargo.Count
                : Math.Max(
                    0,
                    order.Demand);

        int sellAmount =
            checked(
                (int)Math.Min(
                    cargo.Count,
                    usableDemand));

        long revenue =
            checked(
                (long)sellAmount
                * order.SellToStationPrice);

        return new Observation(
            order,
            new CargoSaleLine
            {
                CommodityId =
                    cargo.CommodityId,
                DisplayName =
                    string.IsNullOrWhiteSpace(
                        cargo.DisplayName)
                        ? cargo.CommodityId
                        : cargo.DisplayName,
                CargoAmount =
                    cargo.Count,
                SellAmount =
                    sellAmount,
                SellPrice =
                    order.SellToStationPrice,
                Revenue =
                    revenue,
                Demand =
                    order.Demand,
                UpdatedAt =
                    order.UpdatedAt
            },
            isLocal);
    }

    private static IReadOnlyList<Observation> BuildCurrentMarketObservations(
        GameStateSnapshot state,
        IReadOnlyList<CargoCommoditySnapshot> cargo)
    {
        if (!state.Docked
            || state.MarketId is null
            || state.MarketSnapshotId is null
            || state.MarketId != state.MarketSnapshotId
            || state.MarketUpdatedUtc is null
            || !state.MarketSystem.Equals(
                state.StarSystem,
                StringComparison.OrdinalIgnoreCase)
            || !state.MarketStation.Equals(
                state.Station,
                StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<Observation>();
        }

        var result =
            new List<Observation>();

        foreach (CargoCommoditySnapshot item in cargo)
        {
            if (!state.MarketByCommodityId.TryGetValue(
                    item.CommodityId,
                    out MarketItemSnapshot? market)
                || market.SellPrice <= 0)
            {
                continue;
            }

            // A positive live SellPrice means Elite currently exposes this
            // commodity as sellable at the opened market. Unlike Ardent's
            // nearby importer contract, Market.json demand=0 is not treated as
            // an "infinite demand" transport convention here.
            int sellAmount =
                market.Demand > 0
                    ? Math.Min(
                        item.Count,
                        market.Demand)
                    : item.Count;

            var order =
                new TradeMarketOrder
                {
                    CommodityName =
                        item.CommodityId,
                    MarketId =
                        state.MarketSnapshotId.Value,
                    StationName =
                        state.MarketStation,
                    StationType =
                        "Current market",
                    DistanceToArrivalLs =
                        0,
                    // The commander is already docked here; landing-pad
                    // filtering is irrelevant for this zero-travel candidate.
                    MaxLandingPadSize =
                        3,
                    SystemAddress =
                        state.SystemAddress,
                    SystemName =
                        state.MarketSystem,
                    SystemX =
                        state.SystemX ?? 0,
                    SystemY =
                        state.SystemY ?? 0,
                    SystemZ =
                        state.SystemZ ?? 0,
                    SellToStationPrice =
                        market.SellPrice,
                    Demand =
                        market.Demand,
                    Stock =
                        market.Supply,
                    UpdatedAt =
                        state.MarketUpdatedUtc.Value,
                    ReferenceDistanceLy =
                        0
                };

            result.Add(
                new Observation(
                    order,
                    new CargoSaleLine
                    {
                        CommodityId =
                            item.CommodityId,
                        DisplayName =
                            string.IsNullOrWhiteSpace(
                                item.DisplayName)
                                ? item.CommodityId
                                : item.DisplayName,
                        CargoAmount =
                            item.Count,
                        SellAmount =
                            sellAmount,
                        SellPrice =
                            market.SellPrice,
                        Revenue =
                            checked(
                                (long)sellAmount
                                * market.SellPrice),
                        Demand =
                            market.Demand,
                        UpdatedAt =
                            state.MarketUpdatedUtc.Value
                    },
                    IsLocal: true));
        }

        return result;
    }

    private static IReadOnlyList<CargoSaleCandidate> BuildCandidates(
        IEnumerable<Observation> observations,
        int totalCargoUnits,
        int maxResults)
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        return observations
            .GroupBy(item =>
                item.Order.MarketId)
            .Select(group =>
            {
                Observation[] selected =
                    group
                        .GroupBy(item =>
                            item.Line.CommodityId,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(commodity =>
                            commodity
                                .OrderByDescending(item =>
                                    item.IsLocal)
                                .ThenByDescending(item =>
                                    item.Line.UpdatedAt)
                                .ThenByDescending(item =>
                                    item.Line.SellPrice)
                                .First())
                        .ToArray();

                Observation targetObservation =
                    selected
                        .OrderByDescending(item =>
                            item.IsLocal)
                        .ThenByDescending(item =>
                            item.Line.UpdatedAt)
                        .First();

                CargoSaleLine[] lines =
                    selected
                        .Select(item =>
                            item.Line)
                        .OrderByDescending(item =>
                            item.Revenue)
                        .ThenBy(item =>
                            item.DisplayName,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ToArray();

                TimeSpan worstAge =
                    lines
                        .Select(line =>
                            now > line.UpdatedAt
                                ? now - line.UpdatedAt
                                : TimeSpan.Zero)
                        .DefaultIfEmpty(
                            TimeSpan.Zero)
                        .Max();

                return new CargoSaleCandidate
                {
                    Target =
                        targetObservation.Order,
                    Lines =
                        lines,
                    TotalCargoUnits =
                        totalCargoUnits,
                    SellableUnits =
                        lines.Sum(line =>
                            line.SellAmount),
                    TotalRevenue =
                        lines.Sum(line =>
                            line.Revenue),
                    WorstDataAge =
                        worstAge,
                    IsCurrentMarket =
                        selected.Any(item =>
                            item.IsLocal)
                };
            })
            .Where(candidate =>
                candidate.SellableUnits > 0
                && candidate.TotalRevenue > 0)
            .OrderByDescending(candidate =>
                candidate.TotalRevenue)
            .ThenByDescending(candidate =>
                candidate.CoverageRatio)
            .ThenBy(candidate =>
                candidate.DistanceLy)
            .Take(
                Math.Max(
                    1,
                    maxResults))
            .ToArray();
    }
}
