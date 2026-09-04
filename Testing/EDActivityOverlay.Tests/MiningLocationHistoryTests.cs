using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Mining;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class MiningLocationHistoryTests
{
    [Fact]
    public void OnlyConfirmedMatchingDestinationSessionsAreAggregated()
    {
        DateTimeOffset started =
            new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

        MiningSessionSnapshot confirmed = Session(
            started,
            "Lalande 34968",
            "Lalande 34968 AB 8 A Ring",
            confirmed: true,
            refinedPlatinum: 10,
            refinedOsmium: 0,
            prospectContents: [20, 25, 30, 35, 40]);

        MiningSessionSnapshot unconfirmed = Session(
            started.AddHours(1),
            "Lalande 34968",
            "Lalande 34968 AB 8 A Ring",
            confirmed: false,
            refinedPlatinum: 20,
            refinedOsmium: 0,
            prospectContents: [40, 40, 40, 40, 40]);

        MiningSessionSnapshot otherRing = Session(
            started.AddHours(2),
            "Lalande 34968",
            "Lalande 34968 AB 7 A Ring",
            confirmed: true,
            refinedPlatinum: 30,
            refinedOsmium: 0,
            prospectContents: [50, 50, 50, 50, 50]);

        IReadOnlyDictionary<string, MiningLocationHistorySnapshot> rows =
            MiningLocationHistoryCalculator.CalculateByLocation(
                [confirmed, unconfirmed, otherRing],
                ["Platinum"]);

        Assert.Equal(2, rows.Count);

        string key = MiningLocationKey.For(
            "Lalande 34968",
            "Lalande 34968 AB 8 A Ring");

        MiningLocationHistorySnapshot history = rows[key];
        Assert.Equal(1, history.Sessions);
        Assert.Equal(10, history.RefinedTons);
        Assert.Equal(5, history.ProspectedAsteroids);
    }

    [Fact]
    public void CalculatesWeightedRateHitContentAndRefinedComposition()
    {
        DateTimeOffset started =
            new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

        MiningSessionSnapshot first = Session(
            started,
            "Lalande 34968",
            "Lalande 34968 AB 8 A Ring",
            confirmed: true,
            refinedPlatinum: 8,
            refinedOsmium: 2,
            prospectContents: [20, 30, null]);

        MiningSessionSnapshot second = Session(
            started.AddHours(1),
            "Lalande 34968",
            "Lalande 34968 AB 8 A Ring",
            confirmed: true,
            refinedPlatinum: 7,
            refinedOsmium: 3,
            prospectContents: [40, null]);

        IReadOnlyDictionary<string, MiningLocationHistorySnapshot> rows =
            MiningLocationHistoryCalculator.CalculateByLocation(
                [first, second],
                ["Platinum"]);

        MiningLocationHistorySnapshot history = Assert.Single(rows).Value;

        // Sessions are 30 and 15 minutes: 20 t / 0.75 h.
        Assert.Equal(2, history.Sessions);
        Assert.Equal(2, history.RateSessions);
        Assert.Equal(20, history.RefinedTons);
        Assert.Equal(26.666, history.AverageTonsPerHour, 3);
        Assert.Equal(40, history.BestTonsPerHour, 3);

        Assert.Equal(5, history.ProspectedAsteroids);
        Assert.Equal(3, history.TargetBearingAsteroids);
        Assert.Equal(0.6, history.HitRate, 3);
        Assert.Equal(30, history.AverageTargetContentPercent, 3);
        Assert.True(history.HasQualitySignal);

        Assert.Collection(
            history.RefinedComposition,
            platinum =>
            {
                Assert.Equal("platinum", platinum.CommodityId);
                Assert.Equal(15, platinum.Tons);
                Assert.Equal(0.75, platinum.Share, 3);
            },
            osmium =>
            {
                Assert.Equal("osmium", osmium.CommodityId);
                Assert.Equal(5, osmium.Tons);
                Assert.Equal(0.25, osmium.Share, 3);
            });
    }

    [Fact]
    public void PersonalMeasuredQualityOverridesExternalSurveyAfterSampleGate()
    {
        MiningLocationQuery query = new()
        {
            ReferenceSystem = "Wolf 1241",
            RadiusLy = 100,
            CommodityIds = ["Platinum"],
            RingClass = "Metallic",
            MaxResults = 100
        };

        MiningLocationCandidate candidate = new()
        {
            SystemName = "Test A",
            RingName = "Test A 1 A Ring",
            RingClass = "Metallic",
            ReserveLevel = "Pristine",
            DistanceLy = 20,
            DistanceToArrivalLs = 500,
            HotspotCounts = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["Platinum"] = 1
            },
            QualitySites =
            [
                new MiningLocationQualitySite(
                    "Test A",
                    "1 A Ring",
                    "Platinum",
                    26,
                    "external",
                    "https://example.invalid",
                    DateTimeOffset.UtcNow)
            ],
            PersonalHistory = new MiningLocationHistorySnapshot
            {
                Sessions = 1,
                ProspectedAsteroids = 5,
                TargetBearingAsteroids = 3,
                AverageTargetContentPercent = 18
            }
        };

        MiningLocationCandidate ranked = MiningLocationRanker.Rank(
            query,
            candidate,
            Prices());

        Assert.True(ranked.UsesPersonalQuality);
        Assert.Equal(
            MiningLocationRanker.QualityScoreFor(18),
            ranked.QualityScore);
        Assert.NotEqual(
            MiningLocationRanker.QualityScoreFor(26),
            ranked.QualityScore);
    }

    private static MiningSessionSnapshot Session(
        DateTimeOffset started,
        string system,
        string ring,
        bool confirmed,
        int refinedPlatinum,
        int refinedOsmium,
        IReadOnlyList<double?> prospectContents)
    {
        int durationMinutes =
            started.Hour % 2 == 0
                ? 30
                : 15;
        DateTimeOffset ended = started.AddMinutes(durationMinutes);

        var prospects = new List<MiningProspectSnapshot>();
        for (int i = 0; i < prospectContents.Count; i++)
        {
            double? platinum = prospectContents[i];
            var materials = new List<MiningProspectMaterialSnapshot>();

            if (platinum.HasValue)
            {
                materials.Add(new MiningProspectMaterialSnapshot(
                    "platinum",
                    "Platinum",
                    platinum.Value));
            }
            else
            {
                materials.Add(new MiningProspectMaterialSnapshot(
                    "osmium",
                    "Osmium",
                    12));
            }

            prospects.Add(new MiningProspectSnapshot(
                i + 1,
                started.AddMinutes(i + 1),
                "High",
                100,
                string.Empty,
                string.Empty,
                materials));
        }

        var refinements = new List<MiningRefinementSnapshot>();
        int sequence = 1;

        for (int i = 0; i < refinedPlatinum; i++)
        {
            refinements.Add(new MiningRefinementSnapshot(
                sequence++,
                started.AddMinutes(5),
                "platinum",
                "Platinum"));
        }

        for (int i = 0; i < refinedOsmium; i++)
        {
            refinements.Add(new MiningRefinementSnapshot(
                sequence++,
                started.AddMinutes(6),
                "osmium",
                "Osmium"));
        }

        return new MiningSessionSnapshot(
            Guid.NewGuid(),
            MiningSessionState.Finished,
            started,
            ended,
            ended,
            MiningSessionEndReason.SupercruiseEntry,
            "Test Cmdr",
            42,
            system,
            8,
            "Lalande 34968 AB 8",
            ring,
            prospectContents.Count,
            4,
            0,
            refinedPlatinum + refinedOsmium,
            256,
            20,
            prospects,
            refinements)
        {
            DestinationContext = new MiningSessionDestinationContext
            {
                SystemName = system,
                BodyName = "AB 8",
                RingName = ring,
                Confirmed = confirmed,
                PrimaryCommodityId = "Platinum",
                TargetCommodityIds = ["Platinum"]
            }
        };
    }

    private static MiningMarketPriceSnapshot Prices()
    {
        var quote = new MiningMarketPriceQuote(
            "Platinum",
            60_000,
            70_000,
            80_000,
            3,
            DateTimeOffset.UtcNow);

        return new MiningMarketPriceSnapshot(
            1,
            "Wolf 1241",
            DateTimeOffset.UtcNow,
            false,
            string.Empty,
            new Dictionary<string, MiningMarketPriceQuote>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["Platinum"] = quote
            });
    }
}
