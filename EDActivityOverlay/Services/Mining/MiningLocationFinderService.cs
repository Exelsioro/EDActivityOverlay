using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace EDActivityOverlay.Services.Mining;

public enum MiningResSiteType
{
    None = 0,
    Low = 1,
    Regular = 2,
    High = 3,
    Hazardous = 4
}

public sealed record MiningLocationQuery
{
    public string ReferenceSystem { get; init; } = string.Empty;
    public double RadiusLy { get; init; } = 80;
    public IReadOnlyList<string> CommodityIds { get; init; } = Array.Empty<string>();
    public string RingClass { get; init; } = "Any";
    public int MinimumReserveRank { get; init; }
    public bool SpecialOnly { get; init; }
    public int MaxResults { get; init; } = 100;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ReferenceSystem))
            throw new ArgumentException("Reference system is required.", nameof(ReferenceSystem));
        if (RadiusLy <= 0 || RadiusLy > 5000)
            throw new ArgumentOutOfRangeException(nameof(RadiusLy));
        if (CommodityIds.Count == 0)
            throw new ArgumentException("Select at least one mining commodity.", nameof(CommodityIds));
        if (MaxResults < 1 || MaxResults > 500)
            throw new ArgumentOutOfRangeException(nameof(MaxResults));
    }
}

public sealed record MiningLocationSpecialSite(
    string SystemName,
    string RingName,
    string CommodityId,
    int OverlapMultiplier,
    MiningResSiteType ResType,
    string Source)
{
    public bool HasKnownOverlap => OverlapMultiplier >= 2;
    public bool HasRes => ResType != MiningResSiteType.None;
}

public sealed record MiningLocationQualitySite(
    string SystemName,
    string RingName,
    string CommodityId,
    double AverageContentPercent,
    string Source,
    string SourceUrl,
    DateTimeOffset ObservedUtc);

public sealed record MiningLocationCandidate
{
    public string SystemName { get; init; } = string.Empty;
    public string BodyName { get; init; } = string.Empty;
    public string RingName { get; init; } = string.Empty;
    public string RingClass { get; init; } = string.Empty;
    public string ReserveLevel { get; init; } = string.Empty;
    public double DistanceLy { get; init; }
    public double DistanceToArrivalLs { get; init; }
    public double? SystemX { get; init; }
    public double? SystemY { get; init; }
    public double? SystemZ { get; init; }
    public IReadOnlyDictionary<string, int> HotspotCounts { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<MiningLocationSpecialSite> SpecialSites { get; init; } =
        Array.Empty<MiningLocationSpecialSite>();
    public IReadOnlyList<MiningLocationQualitySite> QualitySites { get; init; } =
        Array.Empty<MiningLocationQualitySite>();
    public MiningLocationHistorySnapshot PersonalHistory { get; init; } =
        MiningLocationHistorySnapshot.Empty;
    public string PrimaryCommodityId { get; init; } = string.Empty;
    public int MarketReferencePrice { get; init; }
    public string BestSellCommodityId { get; init; } = string.Empty;
    public int BestSellPrice { get; init; }
    public string BestSellSystemName { get; init; } = string.Empty;
    public string BestSellStationName { get; init; } = string.Empty;
    public long BestSellDemand { get; init; }
    public double BestSellDistanceLy { get; init; }
    public double? BestSellDistanceToArrivalLs { get; init; }
    public int BestSellMaxLandingPadSize { get; init; }
    public DateTimeOffset BestSellUpdatedAt { get; init; }
    public int TargetScore { get; init; }
    public int ReserveScore { get; init; }
    public int QualityScore { get; init; }
    public int SpecialScore { get; init; }
    public int TravelScore { get; init; }
    public int MarketScore { get; init; }
    public int Score { get; init; }

    public bool HasKnownSpecial => SpecialSites.Count > 0;
    public bool HasMeasuredQuality => QualitySites.Count > 0;
    public bool HasPersonalHistory => PersonalHistory.Available;
    public bool UsesPersonalQuality => PersonalHistory.HasQualitySignal;
    public double BestMeasuredAverageContentPercent =>
        QualitySites
            .Select(site => site.AverageContentPercent)
            .DefaultIfEmpty(0)
            .Max();
    public bool HasDestinationMarket =>
        BestSellPrice > 0
        && !string.IsNullOrWhiteSpace(BestSellSystemName)
        && !string.IsNullOrWhiteSpace(BestSellStationName);
    public int HighestHotspotCount => HotspotCounts.Count == 0 ? 0 : HotspotCounts.Values.Max();
}

public sealed record MiningLocationSearchResult(
    IReadOnlyList<MiningLocationCandidate> Candidates,
    IReadOnlyList<string> Warnings);

public interface IMiningLocationProvider
{
    Task<IReadOnlyList<MiningLocationCandidate>> SearchAsync(
        MiningLocationQuery query,
        CancellationToken cancellationToken);
}

public interface IMiningSpecialSiteProvider
{
    Task<(IReadOnlyList<MiningLocationSpecialSite> Sites, IReadOnlyList<string> Warnings)> LoadAsync(
        CancellationToken cancellationToken);
}

public interface IMiningLocationQualityProvider
{
    Task<(IReadOnlyList<MiningLocationQualitySite> Sites, IReadOnlyList<string> Warnings)> LoadAsync(
        CancellationToken cancellationToken);
}

public sealed class MiningLocationFinderService
{
    private readonly IMiningLocationProvider locationProvider;
    private readonly IMiningSpecialSiteProvider specialSiteProvider;
    private readonly IMiningLocationMarketEnricher marketEnricher;
    private readonly IMiningLocationQualityProvider qualityProvider;
    private readonly IMiningLocationHistoryProvider historyProvider;

    public MiningLocationFinderService()
        : this(
            new SpanshMiningLocationProvider(),
            new MiningCommunitySpecialSiteProvider(),
            new MiningLocationMarketEnrichmentService(),
            new MiningEdToolsQualityProvider(),
            new MiningSessionLocationHistoryProvider())
    {
    }

    internal MiningLocationFinderService(
        IMiningLocationProvider locationProvider,
        IMiningSpecialSiteProvider specialSiteProvider)
        : this(
            locationProvider,
            specialSiteProvider,
            new MiningLocationMarketEnrichmentService(),
            new NullMiningLocationQualityProvider(),
            new NullMiningLocationHistoryProvider())
    {
    }

    internal MiningLocationFinderService(
        IMiningLocationProvider locationProvider,
        IMiningSpecialSiteProvider specialSiteProvider,
        IMiningLocationMarketEnricher marketEnricher)
        : this(
            locationProvider,
            specialSiteProvider,
            marketEnricher,
            new NullMiningLocationQualityProvider(),
            new NullMiningLocationHistoryProvider())
    {
    }

    internal MiningLocationFinderService(
        IMiningLocationProvider locationProvider,
        IMiningSpecialSiteProvider specialSiteProvider,
        IMiningLocationMarketEnricher marketEnricher,
        IMiningLocationQualityProvider qualityProvider)
        : this(
            locationProvider,
            specialSiteProvider,
            marketEnricher,
            qualityProvider,
            new NullMiningLocationHistoryProvider())
    {
    }

    internal MiningLocationFinderService(
        IMiningLocationProvider locationProvider,
        IMiningSpecialSiteProvider specialSiteProvider,
        IMiningLocationMarketEnricher marketEnricher,
        IMiningLocationQualityProvider qualityProvider,
        IMiningLocationHistoryProvider historyProvider)
    {
        this.locationProvider = locationProvider ?? throw new ArgumentNullException(nameof(locationProvider));
        this.specialSiteProvider = specialSiteProvider ?? throw new ArgumentNullException(nameof(specialSiteProvider));
        this.marketEnricher = marketEnricher ?? throw new ArgumentNullException(nameof(marketEnricher));
        this.qualityProvider = qualityProvider ?? throw new ArgumentNullException(nameof(qualityProvider));
        this.historyProvider = historyProvider ?? throw new ArgumentNullException(nameof(historyProvider));
    }

    public async Task<MiningLocationSearchResult> SearchAsync(
        MiningLocationQuery query,
        MiningMarketPriceSnapshot prices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(prices);
        query.Validate();

        IReadOnlyList<MiningLocationCandidate> raw =
            await locationProvider.SearchAsync(query, cancellationToken).ConfigureAwait(false);

        Task<(IReadOnlyList<MiningLocationSpecialSite> Sites, IReadOnlyList<string> Warnings)>
            specialTask = specialSiteProvider.LoadAsync(cancellationToken);
        Task<(IReadOnlyList<MiningLocationQualitySite> Sites, IReadOnlyList<string> Warnings)>
            qualityTask = qualityProvider.LoadAsync(cancellationToken);

        await Task.WhenAll(specialTask, qualityTask).ConfigureAwait(false);

        (IReadOnlyList<MiningLocationSpecialSite> specialSites, IReadOnlyList<string> specialWarnings) =
            await specialTask.ConfigureAwait(false);
        (IReadOnlyList<MiningLocationQualitySite> qualitySites, IReadOnlyList<string> qualityWarnings) =
            await qualityTask.ConfigureAwait(false);

        var specialsByRing = specialSites
            .GroupBy(
                item => MiningLocationKey.For(item.SystemName, item.RingName),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MiningLocationSpecialSite>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var qualityByRing = qualitySites
            .GroupBy(
                item => MiningLocationKey.For(item.SystemName, item.RingName),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MiningLocationQualitySite>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        IReadOnlyDictionary<string, MiningLocationHistorySnapshot> historyByRing;
        IReadOnlyList<string> historyWarnings;
        try
        {
            historyByRing = MiningLocationHistoryCalculator.CalculateByLocation(
                historyProvider.LoadRecent(),
                query.CommodityIds);
            historyWarnings = Array.Empty<string>();
        }
        catch (Exception ex)
        {
            historyByRing =
                new Dictionary<string, MiningLocationHistorySnapshot>(
                    StringComparer.OrdinalIgnoreCase);
            historyWarnings =
            [
                $"Personal mining history unavailable: {ex.Message}"
            ];
            Logger.Logger.Warning($"Mining location history unavailable: {ex}");
        }

        MiningLocationCandidate[] provisional = raw
            .Where(candidate => MiningLocationRanker.RingClassMatches(candidate.RingClass, query.RingClass))
            .Where(candidate => MiningLocationRanker.ReserveRank(candidate.ReserveLevel) >= query.MinimumReserveRank)
            .Select(candidate =>
            {
                string key = MiningLocationKey.For(
                    candidate.SystemName,
                    candidate.RingName);

                specialsByRing.TryGetValue(
                    key,
                    out IReadOnlyList<MiningLocationSpecialSite>? ringSpecials);
                qualityByRing.TryGetValue(
                    key,
                    out IReadOnlyList<MiningLocationQualitySite>? ringQuality);
                historyByRing.TryGetValue(
                    key,
                    out MiningLocationHistorySnapshot? personalHistory);

                string[] selected = query.CommodityIds
                    .Select(id => MiningTargetCatalog.Find(id)?.CommodityId ?? string.Empty)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToArray();

                IReadOnlyList<MiningLocationSpecialSite> relevant =
                    (ringSpecials ?? Array.Empty<MiningLocationSpecialSite>())
                    .Where(site => selected.Contains(site.CommodityId, StringComparer.OrdinalIgnoreCase))
                    .ToArray();

                IReadOnlyList<MiningLocationQualitySite> relevantQuality =
                    (ringQuality ?? Array.Empty<MiningLocationQualitySite>())
                    .Where(site => selected.Contains(site.CommodityId, StringComparer.OrdinalIgnoreCase))
                    .ToArray();

                return MiningLocationRanker.Rank(
                    query,
                    candidate with
                    {
                        SpecialSites = relevant,
                        QualitySites = relevantQuality,
                        PersonalHistory =
                            personalHistory ?? MiningLocationHistorySnapshot.Empty
                    },
                    prices);
            })
            .Where(candidate => !query.SpecialOnly || candidate.HasKnownSpecial)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.HasPersonalHistory)
            .ThenByDescending(candidate => candidate.PersonalHistory.AverageTonsPerHour)
            .ThenByDescending(candidate => candidate.SpecialScore)
            .ThenByDescending(candidate => candidate.ReserveScore)
            .ThenBy(candidate => candidate.DistanceLy)
            .ThenBy(candidate => candidate.DistanceToArrivalLs)
            .Take(query.MaxResults)
            .ToArray();

        IReadOnlyList<MiningLocationCandidate> enriched =
            await marketEnricher.EnrichAsync(
                query,
                provisional,
                cancellationToken).ConfigureAwait(false);

        MiningLocationCandidate[] ranked = enriched
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.HasPersonalHistory)
            .ThenByDescending(candidate => candidate.PersonalHistory.RateSessions)
            .ThenByDescending(candidate => candidate.PersonalHistory.AverageTonsPerHour)
            .ThenByDescending(candidate => candidate.QualityScore)
            .ThenByDescending(candidate => candidate.SpecialScore)
            .ThenByDescending(candidate => candidate.ReserveScore)
            .ThenByDescending(candidate => candidate.MarketScore)
            .ThenBy(candidate => candidate.DistanceLy)
            .ThenBy(candidate => candidate.DistanceToArrivalLs)
            .Take(query.MaxResults)
            .ToArray();

        string[] warnings = specialWarnings
            .Concat(qualityWarnings)
            .Concat(historyWarnings)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MiningLocationSearchResult(ranked, warnings);
    }
}

public static class MiningLocationRanker
{
    public static MiningLocationCandidate Rank(
        MiningLocationQuery query,
        MiningLocationCandidate candidate,
        MiningMarketPriceSnapshot prices)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(prices);

        string[] selected = query.CommodityIds
            .Select(id => MiningTargetCatalog.Find(id)?.CommodityId ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string[] matched = selected
            .Where(id => candidate.HotspotCounts.ContainsKey(id))
            .ToArray();

        int maxSignalCount = matched
            .Select(id => candidate.HotspotCounts.TryGetValue(id, out int count) ? count : 0)
            .DefaultIfEmpty(0)
            .Max();

        // Keep the complete score interpretable and bounded to 100:
        // target 25 + reserves 15 + measured quality 20 + special 25
        // + travel 10 + market 5.
        int targetScore = matched.Length == 0
            ? 0
            : Math.Min(
                25,
                18
                + Math.Max(0, matched.Length - 1) * 2
                + Math.Min(4, Math.Max(0, maxSignalCount - 1) * 2));

        int reserveScore = ReserveRank(candidate.ReserveLevel) switch
        {
            >= 4 => 15,
            3 => 10,
            2 => 5,
            1 => 2,
            _ => 0
        };

        MiningLocationQualitySite[] relevantQuality = candidate.QualitySites
            .Where(site => selected.Contains(site.CommodityId, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        double externalMeasuredAverage = relevantQuality
            .Select(site => site.AverageContentPercent)
            .DefaultIfEmpty(0)
            .Max();

        // Exact-ring EDAO observations are authoritative once the local sample
        // reaches the minimum credibility gate. Until then the larger external
        // survey remains the quality-score fallback.
        double measuredAverage = candidate.PersonalHistory.HasQualitySignal
            ? candidate.PersonalHistory.AverageTargetContentPercent
            : externalMeasuredAverage;

        int qualityScore = QualityScoreFor(measuredAverage);

        MiningLocationSpecialSite[] relevantSpecials = candidate.SpecialSites
            .Where(site => selected.Contains(site.CommodityId, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        MiningResSiteType bestRes = relevantSpecials
            .Select(site => site.ResType)
            .DefaultIfEmpty(MiningResSiteType.None)
            .Max();

        int resScore = bestRes switch
        {
            MiningResSiteType.Hazardous => 20,
            MiningResSiteType.High => 16,
            MiningResSiteType.Regular => 11,
            MiningResSiteType.Low => 6,
            _ => 0
        };

        int bestOverlap = relevantSpecials
            .Select(site => site.OverlapMultiplier)
            .DefaultIfEmpty(0)
            .Max();

        int overlapScore = bestOverlap switch
        {
            >= 3 => 5,
            2 => 3,
            _ => 0
        };

        int specialScore = Math.Min(25, resScore + overlapScore);

        double radius = Math.Max(1, query.RadiusLy);
        int distanceScore = (int)Math.Round(
            7 * (1 - Math.Clamp(candidate.DistanceLy / radius, 0, 1)),
            MidpointRounding.AwayFromZero);

        int arrivalScore = candidate.DistanceToArrivalLs switch
        {
            <= 0 => 0,
            <= 500 => 3,
            <= 2_000 => 2,
            <= 10_000 => 1,
            _ => 0
        };

        int travelScore = distanceScore + arrivalScore;

        string primary = matched
            .OrderByDescending(id =>
                prices.TryGet(id, out MiningMarketPriceQuote? quote)
                    ? quote!.ReferenceSellPrice
                    : 0)
            .ThenByDescending(id =>
                candidate.HotspotCounts.TryGetValue(id, out int count)
                    ? count
                    : 0)
            .FirstOrDefault()
            ?? matched.FirstOrDefault()
            ?? string.Empty;

        int primaryPrice = prices.TryGet(primary, out MiningMarketPriceQuote? primaryQuote)
            ? primaryQuote!.ReferenceSellPrice
            : 0;

        int highestSelectedPrice = selected
            .Select(id => prices.TryGet(id, out MiningMarketPriceQuote? quote)
                ? quote!.ReferenceSellPrice
                : 0)
            .DefaultIfEmpty(0)
            .Max();

        int marketScore = primaryPrice > 0 && highestSelectedPrice > 0
            ? (int)Math.Round(
                5d * primaryPrice / highestSelectedPrice,
                MidpointRounding.AwayFromZero)
            : 0;

        int score = Math.Clamp(
            targetScore
            + reserveScore
            + qualityScore
            + specialScore
            + travelScore
            + marketScore,
            0,
            100);

        return candidate with
        {
            PrimaryCommodityId = primary,
            MarketReferencePrice = primaryPrice,
            TargetScore = targetScore,
            ReserveScore = reserveScore,
            QualityScore = qualityScore,
            SpecialScore = specialScore,
            TravelScore = travelScore,
            MarketScore = marketScore,
            Score = score
        };
    }

    internal static int QualityScoreFor(double averageContentPercent) =>
        averageContentPercent switch
        {
            >= 26 => 20,
            >= 24 => 18,
            >= 22 => 16,
            >= 20 => 14,
            >= 18 => 10,
            > 0 => 6,
            _ => 0
        };

    public static int ReserveRank(string? reserveLevel)
    {
        string value = reserveLevel?.Trim() ?? string.Empty;
        if (value.Contains("Pristine", StringComparison.OrdinalIgnoreCase)) return 4;
        if (value.Contains("Major", StringComparison.OrdinalIgnoreCase)) return 3;
        if (value.Contains("Common", StringComparison.OrdinalIgnoreCase)) return 2;
        if (value.Contains("Low", StringComparison.OrdinalIgnoreCase)) return 1;
        return 0;
    }

    public static bool RingClassMatches(string? actual, string? requested)
    {
        string wanted = Compact(requested);
        if (wanted.Length == 0 || wanted == "any")
            return true;
        return Compact(actual).Contains(wanted, StringComparison.OrdinalIgnoreCase);
    }

    private static string Compact(string? value) =>
        new((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
}

public sealed class SpanshMiningLocationProvider : IMiningLocationProvider
{
    private static readonly Uri Endpoint = new("https://spansh.co.uk/api/bodies/search");
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private readonly HttpClient httpClient;

    public SpanshMiningLocationProvider()
        : this(SharedHttpClient)
    {
    }

    internal SpanshMiningLocationProvider(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<IReadOnlyList<MiningLocationCandidate>> SearchAsync(
        MiningLocationQuery query,
        CancellationToken cancellationToken)
    {
        query.Validate();

        var merged = new Dictionary<string, MiningLocationCandidate>(StringComparer.OrdinalIgnoreCase);
        string[] targetNames = query.CommodityIds
            .Select(id => MiningTargetCatalog.Find(id))
            .Where(option => option is not null && !string.IsNullOrWhiteSpace(option.CommodityId))
            .Select(option => option!.EnglishName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MiningTargetSelector.MaxTargets)
            .ToArray();

        foreach (string targetName in targetNames)
        {
            IReadOnlyList<MiningLocationCandidate> rows =
                await SearchTargetAsync(query, targetName, cancellationToken).ConfigureAwait(false);

            foreach (MiningLocationCandidate row in rows)
            {
                string key = MiningLocationKey.For(row.SystemName, row.RingName);
                if (!merged.TryGetValue(key, out MiningLocationCandidate? existing))
                {
                    merged[key] = row;
                    continue;
                }

                var counts = new Dictionary<string, int>(
                    existing.HotspotCounts,
                    StringComparer.OrdinalIgnoreCase);

                foreach ((string commodityId, int count) in row.HotspotCounts)
                {
                    counts[commodityId] = Math.Max(
                        counts.TryGetValue(commodityId, out int oldCount) ? oldCount : 0,
                        count);
                }

                merged[key] = existing with
                {
                    RingClass = string.IsNullOrWhiteSpace(existing.RingClass) ? row.RingClass : existing.RingClass,
                    ReserveLevel = MiningLocationRanker.ReserveRank(row.ReserveLevel)
                        > MiningLocationRanker.ReserveRank(existing.ReserveLevel)
                            ? row.ReserveLevel
                            : existing.ReserveLevel,
                    DistanceLy = Math.Min(existing.DistanceLy, row.DistanceLy),
                    DistanceToArrivalLs = existing.DistanceToArrivalLs > 0
                        ? Math.Min(existing.DistanceToArrivalLs, row.DistanceToArrivalLs > 0
                            ? row.DistanceToArrivalLs
                            : existing.DistanceToArrivalLs)
                        : row.DistanceToArrivalLs,
                    HotspotCounts = counts
                };
            }
        }

        return merged.Values.ToArray();
    }

    private async Task<IReadOnlyList<MiningLocationCandidate>> SearchTargetAsync(
        MiningLocationQuery query,
        string targetName,
        CancellationToken cancellationToken)
    {
        object payload = new
        {
            filters = new Dictionary<string, object>
            {
                ["distance"] = new { min = 0.0, max = query.RadiusLy },
                ["ring_signals"] = new object[]
                {
                    new
                    {
                        comparison = "<=>",
                        count = new[] { 1, 9999 },
                        name = new[] { targetName }
                    }
                }
            },
            reference_system = query.ReferenceSystem.Trim(),
            sort = new object[] { new { distance = new { direction = "asc" } } },
            size = 500,
            page = 0
        };

        string json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        await RequestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using HttpResponseMessage response =
                await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Spansh body search failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}.",
                    null,
                    response.StatusCode);
            }

            return ParseResponse(body);
        }
        finally
        {
            RequestGate.Release();
        }
    }

    internal static IReadOnlyList<MiningLocationCandidate> ParseResponse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out JsonElement results)
            || results.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MiningLocationCandidate>();
        }

        var rows = new List<MiningLocationCandidate>();
        foreach (JsonElement body in results.EnumerateArray())
        {
            string systemName = Text(body, "system_name");
            string bodyName = Text(body, "name");
            string reserve = Text(body, "reserve_level");
            double distance = Number(body, "distance");
            double arrival = Number(body, "distance_to_arrival");
            double? x = NullableNumber(body, "system_x");
            double? y = NullableNumber(body, "system_y");
            double? z = NullableNumber(body, "system_z");

            if (!body.TryGetProperty("rings", out JsonElement rings)
                || rings.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement ring in rings.EnumerateArray())
            {
                string ringName = Text(ring, "name");
                if (string.IsNullOrWhiteSpace(systemName) || string.IsNullOrWhiteSpace(ringName))
                    continue;

                var signals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (ring.TryGetProperty("signals", out JsonElement signalArray)
                    && signalArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement signal in signalArray.EnumerateArray())
                    {
                        MiningTargetOption? option = MiningTargetCatalog.Find(Text(signal, "name"));
                        if (option is null || string.IsNullOrWhiteSpace(option.CommodityId))
                            continue;

                        int count = Math.Max(1, Integer(signal, "count", 1));
                        signals[option.CommodityId] = Math.Max(
                            signals.TryGetValue(option.CommodityId, out int previous) ? previous : 0,
                            count);
                    }
                }

                if (signals.Count == 0)
                    continue;

                rows.Add(new MiningLocationCandidate
                {
                    SystemName = systemName,
                    BodyName = bodyName,
                    RingName = ringName,
                    RingClass = Text(ring, "type"),
                    ReserveLevel = reserve,
                    DistanceLy = distance,
                    DistanceToArrivalLs = arrival,
                    SystemX = x,
                    SystemY = y,
                    SystemZ = z,
                    HotspotCounts = signals
                });
            }
        }

        return rows;
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static int Integer(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out int result)
            ? result
            : fallback;

    private static double Number(JsonElement root, string name) =>
        NullableNumber(root, name) ?? 0;

    private static double? NullableNumber(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
            return number;
        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return number;
        return null;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(35)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EDActivityOverlay/MiningLocationFinder");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }
}

public sealed class MiningCommunitySpecialSiteProvider : IMiningSpecialSiteProvider
{
    private static readonly Uri OverlapsUri = new(
        "https://raw.githubusercontent.com/Viper-Dude/EliteMining/main/app/data/overlaps.csv");
    private static readonly Uri ResUri = new(
        "https://raw.githubusercontent.com/Viper-Dude/EliteMining/main/app/data/res_sites.csv");
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly HttpClient httpClient;
    private readonly SemaphoreSlim gate = new(1, 1);
    private IReadOnlyList<MiningLocationSpecialSite> cached = Array.Empty<MiningLocationSpecialSite>();
    private DateTimeOffset cachedAt = DateTimeOffset.MinValue;

    public MiningCommunitySpecialSiteProvider()
        : this(SharedHttpClient)
    {
    }

    internal MiningCommunitySpecialSiteProvider(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<(IReadOnlyList<MiningLocationSpecialSite> Sites, IReadOnlyList<string> Warnings)> LoadAsync(
        CancellationToken cancellationToken)
    {
        if (cached.Count > 0 && DateTimeOffset.UtcNow - cachedAt < CacheTtl)
            return (cached, Array.Empty<string>());

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cached.Count > 0 && DateTimeOffset.UtcNow - cachedAt < CacheTtl)
                return (cached, Array.Empty<string>());

            var warnings = new List<string>();
            var rows = new List<MiningLocationSpecialSite>();

            try
            {
                string overlapCsv =
                    await httpClient.GetStringAsync(OverlapsUri, cancellationToken).ConfigureAwait(false);
                rows.AddRange(ParseOverlapCsv(overlapCsv));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Overlap community data unavailable: {ex.Message}");
            }

            try
            {
                string resCsv =
                    await httpClient.GetStringAsync(ResUri, cancellationToken).ConfigureAwait(false);
                rows.AddRange(ParseResCsv(resCsv));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"RES community data unavailable: {ex.Message}");
            }

            cached = Combine(rows);
            cachedAt = DateTimeOffset.UtcNow;
            return (cached, warnings);
        }
        finally
        {
            gate.Release();
        }
    }

    internal static IReadOnlyList<MiningLocationSpecialSite> ParseOverlapCsv(string csv)
    {
        var rows = new List<MiningLocationSpecialSite>();
        foreach (string[] fields in CsvRows(csv).Skip(1))
        {
            if (fields.Length < 4)
                continue;

            MiningTargetOption? option = MiningTargetCatalog.Find(fields[2]);
            if (option is null || string.IsNullOrWhiteSpace(option.CommodityId))
                continue;

            string raw = fields[3].Trim().TrimEnd('x', 'X');
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int multiplier)
                || multiplier < 2)
            {
                continue;
            }

            rows.Add(new MiningLocationSpecialSite(
                fields[0].Trim(),
                fields[1].Trim(),
                option.CommodityId,
                multiplier,
                MiningResSiteType.None,
                "EliteMining community overlap list"));
        }

        return rows;
    }

    internal static IReadOnlyList<MiningLocationSpecialSite> ParseResCsv(string csv)
    {
        var rows = new List<MiningLocationSpecialSite>();
        foreach (string[] fields in CsvRows(csv).Skip(1))
        {
            if (fields.Length < 4)
                continue;

            MiningTargetOption? option = MiningTargetCatalog.Find(fields[2]);
            if (option is null || string.IsNullOrWhiteSpace(option.CommodityId))
                continue;

            MiningResSiteType type = ParseResType(fields[3]);
            if (type == MiningResSiteType.None)
                continue;

            rows.Add(new MiningLocationSpecialSite(
                fields[0].Trim(),
                fields[1].Trim(),
                option.CommodityId,
                0,
                type,
                "EliteMining community RES list"));
        }

        return rows;
    }

    private static IReadOnlyList<MiningLocationSpecialSite> Combine(
        IEnumerable<MiningLocationSpecialSite> source)
    {
        return source
            .GroupBy(
                site => $"{MiningLocationKey.For(site.SystemName, site.RingName)}|{site.CommodityId}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                MiningLocationSpecialSite first = group.First();
                return first with
                {
                    OverlapMultiplier = group.Max(item => item.OverlapMultiplier),
                    ResType = group.Max(item => item.ResType),
                    Source = string.Join(
                        " + ",
                        group.Select(item => item.Source).Distinct(StringComparer.OrdinalIgnoreCase))
                };
            })
            .ToArray();
    }

    private static MiningResSiteType ParseResType(string? value)
    {
        string text = value?.Trim() ?? string.Empty;
        if (text.Contains("Haz", StringComparison.OrdinalIgnoreCase))
            return MiningResSiteType.Hazardous;
        if (text.Contains("High", StringComparison.OrdinalIgnoreCase))
            return MiningResSiteType.High;
        if (text.Equals("RES", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Regular", StringComparison.OrdinalIgnoreCase))
            return MiningResSiteType.Regular;
        if (text.Contains("Low", StringComparison.OrdinalIgnoreCase))
            return MiningResSiteType.Low;
        return MiningResSiteType.None;
    }

    private static IEnumerable<string[]> CsvRows(string csv)
    {
        using var reader = new StringReader(csv ?? string.Empty);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            yield return ParseCsvLine(line);
        }
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;

        for (int index = 0; index < line.Length; index++)
        {
            char ch = line[index];
            if (ch == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (ch == ',' && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }
}

internal static class MiningLocationKey
{
    public static string For(string? systemName, string? ringName)
    {
        string system = Normalize(systemName);
        string ring = ringName?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(systemName)
            && ring.StartsWith(systemName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            ring = ring[systemName.Trim().Length..].Trim();
        }

        return $"{system}|{Normalize(ring)}";
    }

    private static string Normalize(string? value) =>
        string.Join(
            " ",
            (value ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .ToLowerInvariant();
}
