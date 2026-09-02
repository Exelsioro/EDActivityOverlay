using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Trading;

internal static class TradeLocationMatcher
{
    public static bool IsAtMarket(
        GameStateSnapshot state,
        long marketId,
        string? system,
        string? station)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.Docked)
        {
            return false;
        }

        if (marketId > 0)
        {
            // Docked/Location journal identity describes where the ship is now.
            // Market.json can legitimately still describe the station visited before it,
            // so only use MarketSnapshotId as a fallback when no docked MarketID exists.
            if (state.MarketId is { } dockedMarketId
                && dockedMarketId > 0)
            {
                if (dockedMarketId == marketId)
                {
                    return true;
                }
            }
            else if (state.MarketSnapshotId is { } snapshotMarketId
                     && snapshotMarketId > 0
                     && snapshotMarketId == marketId)
            {
                return true;
            }
        }

        // Provider MarketID data is useful but must not make an exact journal
        // system/station identity look remote when an upstream row is stale.
        return TextMatches(
                   state.StarSystem,
                   system)
               && (string.IsNullOrWhiteSpace(
                       station)
                   || TextMatches(
                       state.Station,
                       station));
    }

    private static bool TextMatches(
        string? left,
        string? right) =>
        string.Equals(
            CommodityIdentity.Normalize(
                left ?? string.Empty),
            CommodityIdentity.Normalize(
                right ?? string.Empty),
            StringComparison.OrdinalIgnoreCase);
}
