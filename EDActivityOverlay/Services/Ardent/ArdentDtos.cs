using System.Text.Json.Serialization;

namespace EDActivityOverlay.Services.Ardent;

public sealed record ArdentSystemReference(string Name, long SystemAddress = 0)
{
    public bool HasAddress => SystemAddress != 0;
}

public sealed record ArdentSystemDto
{
    [JsonPropertyName("systemAddress")]
    public long SystemAddress { get; init; }

    [JsonPropertyName("systemName")]
    public string SystemName { get; init; } = string.Empty;

    [JsonPropertyName("systemX")]
    public double SystemX { get; init; }

    [JsonPropertyName("systemY")]
    public double SystemY { get; init; }

    [JsonPropertyName("systemZ")]
    public double SystemZ { get; init; }
}

public sealed record ArdentCommodityReportDto
{
    [JsonPropertyName("commodityName")]
    public string CommodityName { get; init; } = string.Empty;

        [JsonPropertyName("minBuyPrice")]
    public int? MinBuyPriceRaw { get; init; }

    [JsonIgnore]
    public int MinBuyPrice =>
        MinBuyPriceRaw
        ?? 0;

        [JsonPropertyName("maxSellPrice")]
    public int? MaxSellPriceRaw { get; init; }

    [JsonIgnore]
    public int MaxSellPrice =>
        MaxSellPriceRaw
        ?? 0;

        [JsonPropertyName("totalStock")]
    public long? TotalStockRaw { get; init; }

    [JsonIgnore]
    public long TotalStock =>
        TotalStockRaw
        ?? 0;

        [JsonPropertyName("totalDemand")]
    public long? TotalDemandRaw { get; init; }

    [JsonIgnore]
    public long TotalDemand =>
        TotalDemandRaw
        ?? 0;
}

public sealed record ArdentMarketOrderDto
{
    [JsonPropertyName("commodityName")]
    public string CommodityName { get; init; } = string.Empty;

    [JsonPropertyName("marketId")]
    public long MarketId { get; init; }

    [JsonPropertyName("stationName")]
    public string StationName { get; init; } = string.Empty;

    [JsonPropertyName("stationType")]
    public string StationType { get; init; } = string.Empty;

    [JsonPropertyName("distanceToArrival")]
    public double? DistanceToArrival { get; init; }

    [JsonPropertyName("maxLandingPadSize")]
    public int? MaxLandingPadSize { get; init; }

    [JsonPropertyName("systemAddress")]
    public long SystemAddress { get; init; }

    [JsonPropertyName("systemName")]
    public string SystemName { get; init; } = string.Empty;

    [JsonPropertyName("systemX")]
    public double SystemX { get; init; }

    [JsonPropertyName("systemY")]
    public double SystemY { get; init; }

    [JsonPropertyName("systemZ")]
    public double SystemZ { get; init; }

    [JsonPropertyName("buyPrice")]
    public int BuyPrice { get; init; }

    [JsonPropertyName("sellPrice")]
    public int SellPrice { get; init; }

    [JsonPropertyName("demand")]
    public long Demand { get; init; }

    [JsonPropertyName("stock")]
    public long Stock { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("distance")]
    public double? Distance { get; init; }
}
