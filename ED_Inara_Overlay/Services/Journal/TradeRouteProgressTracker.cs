using System.Text.Json;
using ED_Inara_Overlay.Models;
using ED_Inara_Overlay.Services;
using ED_Inara_Overlay.Models.Trading;

namespace ED_Inara_Overlay.Services.Journal;

public enum TradeRouteStage
{
    FlyToBuy,
    Buy,
    FlyToSell,
    Sell,
    Completed
}

public sealed record TradeRouteProgress
{
    public TradeRouteStage Stage { get; init; }
    public int LegNumber { get; init; } = 1;
    public int LegCount { get; init; } = 1;
    public string Action { get; init; } = string.Empty;
    public string System { get; init; } = string.Empty;
    public string Station { get; init; } = string.Empty;
    public string Commodity { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public int PlannedPrice { get; init; }
    public int? CurrentMarketPrice { get; init; }
    public int RemainingJumps { get; init; }
    public long ActualProfit { get; init; }
    public bool IsInDanger { get; init; }
    public string Note { get; init; } = string.Empty;
}

public sealed class TradeRouteProgressChangedEventArgs(TradeRouteProgress progress) : EventArgs
{
    public TradeRouteProgress Progress { get; } = progress;
}

public sealed class TradeRouteProgressTracker : IDisposable
{
    private readonly JournalMonitorService journal;
    private readonly TradeRoute route;
    private readonly RouteLeg[] legs;
    private int currentLegIndex;
    private int purchased;
    private int sold;
    private long purchaseCost;
    private long saleRevenue;
    private long realizedProfit;
    private bool completed;
    private bool disposed;

    public event EventHandler<TradeRouteProgressChangedEventArgs>? ProgressChanged;
    public TradeRouteProgress Current { get; private set; } = new();

    public TradeRouteProgressTracker(TradeRoute route, JournalMonitorService? journal = null)
    {
        this.route = route;
        this.journal = journal ?? JournalMonitorService.Instance;
        legs = BuildLegs(route);
        this.journal.StateChanged += OnStateChanged;
        this.journal.JournalEventReceived += OnJournalEvent;
        Refresh(this.journal.Current);
    }

    private void OnStateChanged(object? sender, GameStateChangedEventArgs e) => Refresh(e.State);

    private void OnJournalEvent(object? sender, JournalEventReceivedEventArgs e)
    {
        if (completed || currentLegIndex >= legs.Length)
        {
            return;
        }

        RouteLeg leg = legs[currentLegIndex];
        if (e.EventName.Equals("MarketBuy", StringComparison.OrdinalIgnoreCase)
            && CommodityMatches(e.Data, leg.Commodity))
        {
            int count = GetInt(e.Data, "Count");
            int price = GetInt(e.Data, "BuyPrice");
            purchased += count;
            purchaseCost += (long)count * price;
        }
        else if (e.EventName.Equals("MarketSell", StringComparison.OrdinalIgnoreCase)
                 && CommodityMatches(e.Data, leg.Commodity))
        {
            int count = GetInt(e.Data, "Count");
            int price = GetInt(e.Data, "SellPrice");
            int averagePaid = GetInt(e.Data, "AvgPricePaid");
            sold += count;
            saleRevenue += (long)count * price;
            if (averagePaid > 0)
            {
                realizedProfit += (long)count * (price - averagePaid);
            }

            bool saleComplete = purchased > 0
                ? sold >= purchased
                : !HasCommodity(journal.Current, leg.Commodity);
            if (saleComplete)
            {
                if (currentLegIndex + 1 < legs.Length)
                {
                    currentLegIndex++;
                    purchased = 0;
                    sold = 0;
                }
                else
                {
                    completed = true;
                }
            }
        }

        Refresh(journal.Current);
    }

    private void Refresh(GameStateSnapshot state)
    {
        if (completed)
        {
            Current = new TradeRouteProgress
            {
                Stage = TradeRouteStage.Completed,
                LegNumber = legs.Length,
                LegCount = legs.Length,
                Action = Loc.Get("Loc_ROUTE_COMPLETE"),
                ActualProfit = realizedProfit != 0 ? realizedProfit : saleRevenue - purchaseCost,
                Note = Loc.Get("Loc_Search_for_the_next_route_or_unpin_this_one")
            };
            RaiseChanged();
            return;
        }

        RouteLeg leg = legs[currentLegIndex];
        bool hasCargo = HasCommodity(state, leg.Commodity) || purchased > sold;
        bool atOrigin = LocationMatches(state, leg.FromSystem, leg.FromStation);
        bool atDestination = LocationMatches(state, leg.ToSystem, leg.ToStation);

        TradeRouteStage stage;
        if (hasCargo)
        {
            stage = atDestination && state.Docked ? TradeRouteStage.Sell : TradeRouteStage.FlyToSell;
        }
        else
        {
            stage = atOrigin && state.Docked ? TradeRouteStage.Buy : TradeRouteStage.FlyToBuy;
        }

        bool buying = stage is TradeRouteStage.Buy or TradeRouteStage.FlyToBuy;
        string system = buying ? leg.FromSystem : leg.ToSystem;
        string station = buying ? leg.FromStation : leg.ToStation;
        int plannedPrice = buying ? leg.BuyPrice : leg.SellPrice;
        int? currentPrice = FindMarketPrice(state, leg.Commodity, buying);
        int cargoQuantity = FindCargoCount(state, leg.Commodity);
        int quantity = buying
            ? Math.Max(0, state.FreeCargo)
            : Math.Max(cargoQuantity, purchased - sold);

        Current = new TradeRouteProgress
        {
            Stage = stage,
            LegNumber = currentLegIndex + 1,
            LegCount = legs.Length,
            Action = stage switch
            {
                TradeRouteStage.Buy => Loc.Get("Loc_BUY_CARGO"),
                TradeRouteStage.Sell => Loc.Get("Loc_SELL_CARGO"),
                TradeRouteStage.FlyToSell => Loc.Get("Loc_FLY_TO_SELL"),
                _ => Loc.Get("Loc_FLY_TO_BUY")
            },
            System = system,
            Station = station,
            Commodity = leg.Commodity,
            Quantity = quantity,
            PlannedPrice = plannedPrice,
            CurrentMarketPrice = currentPrice,
            RemainingJumps = GetRemainingJumps(state, system),
            ActualProfit = realizedProfit != 0 ? realizedProfit : saleRevenue - purchaseCost,
            IsInDanger = state.IsInDanger,
            Note = BuildNote(stage, state, plannedPrice, currentPrice)
        };
        RaiseChanged();
    }

    private static RouteLeg[] BuildLegs(TradeRoute route)
    {
        var result = new List<RouteLeg>
        {
            new(
                route.CardHeader.FromStation.System,
                route.CardHeader.FromStation.Name,
                route.CardHeader.ToStation.System,
                route.CardHeader.ToStation.Name,
                route.FirstRoute.BuyCommodity.Name,
                route.FirstRoute.BuyCommodity.Price,
                route.FirstRoute.SellCommodity.Price)
        };

        if (route.IsRoundTrip && route.SecondRoute is not null)
        {
            result.Add(new RouteLeg(
                route.CardHeader.ToStation.System,
                route.CardHeader.ToStation.Name,
                route.CardHeader.FromStation.System,
                route.CardHeader.FromStation.Name,
                route.SecondRoute.BuyCommodity.Name,
                route.SecondRoute.BuyCommodity.Price,
                route.SecondRoute.SellCommodity.Price));
        }
        return result.ToArray();
    }

    private static bool LocationMatches(GameStateSnapshot state, string system, string station) =>
        TextMatches(state.StarSystem, system)
        && (string.IsNullOrWhiteSpace(station) || TextMatches(state.Station, station));

    private static bool HasCommodity(GameStateSnapshot state, string commodity) => FindCargoCount(state, commodity) > 0;

    private static int FindCargoCount(GameStateSnapshot state, string commodity) =>
        state.Cargo.FirstOrDefault(item => TextMatches(item.Key, commodity)).Value;

    private static int? FindMarketPrice(GameStateSnapshot state, string commodity, bool buying)
    {
        if (!state.Docked
            || !TextMatches(state.StarSystem, state.MarketSystem)
            || !TextMatches(state.Station, state.MarketStation)
            || state.MarketUpdatedUtc is not { } updated
            || DateTimeOffset.UtcNow - updated > TimeSpan.FromHours(1))
        {
            return null;
        }
        MarketItemSnapshot? item = state.Market.Values.FirstOrDefault(value => TextMatches(value.Name, commodity));
        if (item is null)
        {
            return null;
        }
        int price = buying ? item.BuyPrice : item.SellPrice;
        return price > 0 ? price : null;
    }

    private static int GetRemainingJumps(GameStateSnapshot state, string destination)
    {
        if (state.NavRoute.Count == 0)
        {
            return 0;
        }
        int destinationIndex = state.NavRoute.ToList().FindIndex(star => TextMatches(star.System, destination));
        return destinationIndex > 0 ? destinationIndex : Math.Max(0, state.NavRoute.Count - 1);
    }

    private static string BuildNote(TradeRouteStage stage, GameStateSnapshot state, int planned, int? current)
    {
        if (state.IsInDanger)
        {
            return Loc.Get("Loc_DANGER_flight_alerts_have_priority");
        }
        if (current is { } marketPrice && planned > 0)
        {
            double difference = (marketPrice - planned) * 100d / planned;
            return Loc.Format("Loc_Market_Note_Format", marketPrice, planned, difference);
        }
        return stage is TradeRouteStage.Buy or TradeRouteStage.Sell
            ? Loc.Get("Loc_Open_Commodity_Market_to_validate_the_current_price")
            : state.NavRoute.Count > 0 ? Loc.Get("Loc_Game_route_detected") : Loc.Get("Loc_Plot_the_destination_in_Galaxy_Map");
    }

    private static bool CommodityMatches(JsonElement data, string commodity)
    {
        string value = GetString(data, "Type_Localised");
        if (string.IsNullOrWhiteSpace(value)) value = GetString(data, "Type");
        return TextMatches(value, commodity);
    }

    private static bool TextMatches(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().Trim('$').Replace("_name;", string.Empty, StringComparison.OrdinalIgnoreCase);

    private static int GetInt(JsonElement data, string property) =>
        data.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int result) ? result : 0;

    private static string GetString(JsonElement data, string property) =>
        data.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private void RaiseChanged() => ProgressChanged?.Invoke(this, new TradeRouteProgressChangedEventArgs(Current));

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        journal.StateChanged -= OnStateChanged;
        journal.JournalEventReceived -= OnJournalEvent;
    }

    private sealed record RouteLeg(
        string FromSystem,
        string FromStation,
        string ToSystem,
        string ToStation,
        string Commodity,
        int BuyPrice,
        int SellPrice);
}
