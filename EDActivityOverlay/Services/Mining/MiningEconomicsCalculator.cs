using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Mining;

public sealed record MiningEconomicsSnapshot(
    int PricedCargoTons,
    long EstimatedCargoValue,
    int PricedRefinedTons,
    long EstimatedSessionValue,
    long EstimatedCreditsPerHour)
{
    public bool HasCargoEstimate => PricedCargoTons > 0 && EstimatedCargoValue > 0;
    public bool HasSessionEstimate => PricedRefinedTons > 0 && EstimatedSessionValue > 0;
}

public static class MiningEconomicsCalculator
{
    public static MiningEconomicsSnapshot Calculate(
        MiningSessionSnapshot session,
        GameStateSnapshot state,
        MiningMarketPriceSnapshot prices,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(prices);

        int pricedCargo = 0;
        long cargoValue = 0;

        foreach (CargoCommoditySnapshot item in state.CargoByCommodityId.Values)
        {
            if (item.Count <= 0)
            {
                continue;
            }

            string commodityId =
                MiningTargetCatalog.Find(item.CommodityId)?.CommodityId
                ?? MiningTargetCatalog.Find(item.DisplayName)?.CommodityId
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(commodityId)
                || !prices.TryGet(commodityId, out MiningMarketPriceQuote? quote))
            {
                continue;
            }

            pricedCargo = checked(pricedCargo + item.Count);
            cargoValue = checked(
                cargoValue
                + (long)item.Count * quote!.ReferenceSellPrice);
        }

        int pricedRefined = 0;
        long sessionValue = 0;
        foreach ((string commodityId, int tons) in session.RefinedByCommodity)
        {
            if (tons <= 0
                || !prices.TryGet(commodityId, out MiningMarketPriceQuote? quote))
            {
                continue;
            }

            pricedRefined = checked(pricedRefined + tons);
            sessionValue = checked(
                sessionValue
                + (long)tons * quote!.ReferenceSellPrice);
        }

        DateTimeOffset effectiveNow = now ?? DateTimeOffset.UtcNow;
        TimeSpan duration = session.State == MiningSessionState.Idle
            ? TimeSpan.Zero
            : (session.EndedUtc
               ?? (session.IsActive ? effectiveNow : session.LastActivityUtc))
              - session.StartedUtc;

        long creditsPerHour = 0;
        if (sessionValue > 0
            && duration >= TimeSpan.FromMinutes(5))
        {
            creditsPerHour = checked(
                (long)Math.Round(
                    sessionValue / duration.TotalHours,
                    MidpointRounding.AwayFromZero));
        }

        return new MiningEconomicsSnapshot(
            pricedCargo,
            cargoValue,
            pricedRefined,
            sessionValue,
            creditsPerHour);
    }
}
