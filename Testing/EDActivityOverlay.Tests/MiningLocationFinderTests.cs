using EDActivityOverlay.Services.Mining;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class MiningLocationFinderTests
{
    [Fact]
    public void HazardousResOutranksKnownDoubleOverlap()
    {
        MiningLocationQuery query = Query();
        MiningMarketPriceSnapshot prices = Prices();

        MiningLocationCandidate haz = MiningLocationRanker.Rank(
            query,
            Candidate() with
            {
                SpecialSites =
                [
                    new MiningLocationSpecialSite(
                        "Test A",
                        "1 A Ring",
                        "Platinum",
                        0,
                        MiningResSiteType.Hazardous,
                        "test")
                ]
            },
            prices);

        MiningLocationCandidate overlap = MiningLocationRanker.Rank(
            query,
            Candidate() with
            {
                SpecialSites =
                [
                    new MiningLocationSpecialSite(
                        "Test A",
                        "1 A Ring",
                        "Platinum",
                        2,
                        MiningResSiteType.None,
                        "test")
                ]
            },
            prices);

        Assert.True(haz.Score > overlap.Score);
        Assert.True(haz.SpecialScore > overlap.SpecialScore);
    }

    [Fact]
    public void KnownTripleOverlapOutranksUnconfirmedThreeHotspotCount()
    {
        MiningLocationQuery query = Query();
        MiningMarketPriceSnapshot prices = Prices();

        MiningLocationCandidate known = MiningLocationRanker.Rank(
            query,
            Candidate() with
            {
                SpecialSites =
                [
                    new MiningLocationSpecialSite(
                        "Test A",
                        "1 A Ring",
                        "Platinum",
                        3,
                        MiningResSiteType.None,
                        "test")
                ]
            },
            prices);

        MiningLocationCandidate unconfirmed = MiningLocationRanker.Rank(
            query,
            Candidate() with
            {
                HotspotCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Platinum"] = 3
                }
            },
            prices);

        Assert.True(known.Score > unconfirmed.Score);
        Assert.False(unconfirmed.HasKnownSpecial);
        Assert.Equal(3, unconfirmed.HighestHotspotCount);
    }

    [Fact]
    public void ReserveRequirementRanksPristineAboveMajor()
    {
        Assert.Equal(4, MiningLocationRanker.ReserveRank("Pristine"));
        Assert.Equal(3, MiningLocationRanker.ReserveRank("MajorResources"));
        Assert.Equal(2, MiningLocationRanker.ReserveRank("Common"));
        Assert.Equal(1, MiningLocationRanker.ReserveRank("LowResources"));
        Assert.Equal(0, MiningLocationRanker.ReserveRank("Depleted"));
    }

    [Fact]
    public void CommunityCsvParsersKeepOverlapAndResSemanticsSeparate()
    {
        const string overlaps =
            "System,Body,Material,Overlap\n"
            + "Omicron Capricorni B,B 1 A Ring,Platinum,2x\n";
        const string res =
            "System,Body,Material,RES\n"
            + "Lalande 34968,AB 8 A Ring,Platinum,Hazardous\n";

        MiningLocationSpecialSite overlap =
            Assert.Single(MiningCommunitySpecialSiteProvider.ParseOverlapCsv(overlaps));
        MiningLocationSpecialSite haz =
            Assert.Single(MiningCommunitySpecialSiteProvider.ParseResCsv(res));

        Assert.Equal(2, overlap.OverlapMultiplier);
        Assert.Equal(MiningResSiteType.None, overlap.ResType);
        Assert.Equal(0, haz.OverlapMultiplier);
        Assert.Equal(MiningResSiteType.Hazardous, haz.ResType);
    }

    private static MiningLocationQuery Query() =>
        new()
        {
            ReferenceSystem = "Wolf 1241",
            RadiusLy = 100,
            CommodityIds = ["Platinum"],
            RingClass = "Metallic",
            MinimumReserveRank = 0,
            MaxResults = 100
        };

    private static MiningLocationCandidate Candidate() =>
        new()
        {
            SystemName = "Test A",
            RingName = "Test A 1 A Ring",
            RingClass = "Metallic",
            ReserveLevel = "Pristine",
            DistanceLy = 20,
            DistanceToArrivalLs = 500,
            HotspotCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Platinum"] = 1
            }
        };

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
            new Dictionary<string, MiningMarketPriceQuote>(StringComparer.OrdinalIgnoreCase)
            {
                ["Platinum"] = quote
            });
    }
}
