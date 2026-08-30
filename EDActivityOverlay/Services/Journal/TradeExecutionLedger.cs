namespace EDActivityOverlay.Services.Journal;

public sealed class TradeExecutionLedger
{
    public TradeExecutionLedger(
        int plannedQuantity)
    {
        PlannedQuantity =
            Math.Max(
                0,
                plannedQuantity);
    }

    public int PlannedQuantity { get; private set; }

    public int PurchasedQuantity { get; private set; }

    public int SoldQuantity { get; private set; }

    public long PurchaseCost { get; private set; }

    public long SaleRevenue { get; private set; }

    public long RealizedProfit { get; private set; }

    public int AverageBuyPrice =>
        PurchasedQuantity <= 0
            ? 0
            : checked(
                (int)Math.Round(
                    PurchaseCost
                    / (double)PurchasedQuantity,
                    MidpointRounding.AwayFromZero));

    public int AverageSellPrice =>
        SoldQuantity <= 0
            ? 0
            : checked(
                (int)Math.Round(
                    SaleRevenue
                    / (double)SoldQuantity,
                    MidpointRounding.AwayFromZero));

    public int RemainingPurchasedQuantity =>
        Math.Max(
            0,
            PurchasedQuantity
            - SoldQuantity);

    public bool HasTransactions =>
        PurchasedQuantity > 0
        || SoldQuantity > 0;

    public void SetPlannedQuantity(
        int quantity)
    {
        PlannedQuantity =
            Math.Max(
                PurchasedQuantity,
                Math.Max(
                    0,
                    quantity));
    }

    public void ApplyBuy(
        int count,
        int unitPrice)
    {
        if (count <= 0
            || unitPrice <= 0)
        {
            return;
        }

        PurchasedQuantity =
            checked(
                PurchasedQuantity
                + count);

        PurchaseCost =
            checked(
                PurchaseCost
                + (long)count
                  * unitPrice);

        if (PurchasedQuantity
            > PlannedQuantity)
        {
            PlannedQuantity =
                PurchasedQuantity;
        }
    }

    public void ApplySell(
        int count,
        int unitPrice,
        int averagePricePaid = 0)
    {
        if (count <= 0
            || unitPrice <= 0)
        {
            return;
        }

        SoldQuantity =
            checked(
                SoldQuantity
                + count);

        SaleRevenue =
            checked(
                SaleRevenue
                + (long)count
                  * unitPrice);

        int basis =
            averagePricePaid > 0
                ? averagePricePaid
                : AverageBuyPrice;

        if (basis > 0)
        {
            RealizedProfit =
                checked(
                    RealizedProfit
                    + (long)count
                      * (unitPrice - basis));
        }
    }

    public long ProjectedProfit(
        int plannedBuyPrice,
        int plannedSellPrice)
    {
        if (plannedSellPrice <= 0)
        {
            return RealizedProfit;
        }

        int targetQuantity =
            Math.Max(
                PlannedQuantity,
                PurchasedQuantity);

        int remainingToBuy =
            Math.Max(
                0,
                targetQuantity
                - PurchasedQuantity);

        int remainingToSell =
            Math.Max(
                0,
                targetQuantity
                - SoldQuantity);

        long projectedCost =
            checked(
                PurchaseCost
                + (long)remainingToBuy
                  * Math.Max(
                      0,
                      plannedBuyPrice));

        long projectedRevenue =
            checked(
                SaleRevenue
                + (long)remainingToSell
                  * plannedSellPrice);

        return checked(
            projectedRevenue
            - projectedCost);
    }
}
