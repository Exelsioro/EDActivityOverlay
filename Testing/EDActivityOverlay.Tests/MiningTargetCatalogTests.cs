using EDActivityOverlay.Services.Mining;
using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class MiningTargetCatalogTests
{
    [Fact]
    public void CatalogContainsAllCurrentMineableCommodities()
    {
        Assert.Equal(35, MiningTargetCatalog.Targets.Count);
        Assert.Equal(
            MiningTargetCatalog.Targets.Count,
            MiningTargetCatalog.Targets
                .Select(item => CommodityIdentity.Normalize(item.CommodityId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
    }

    [Theory]
    [InlineData("Low Temperature Diamonds", "LowTemperatureDiamond")]
    [InlineData("Void Opals", "Opal")]
    [InlineData("Platinum", "Platinum")]
    public void FindsLegacyDisplayNamesAndJournalIds(
        string value,
        string expectedId)
    {
        MiningTargetOption? result =
            MiningTargetCatalog.Find(value);

        Assert.NotNull(result);
        Assert.Equal(expectedId, result!.CommodityId);
    }

    [Fact]
    public void RussianLabelsDoNotChangeStoredCommodityId()
    {
        MiningTargetOption platinum =
            Assert.Single(
                MiningTargetCatalog.Targets,
                item => item.CommodityId == "Platinum");

        Assert.Equal(
            "Платина",
            MiningTargetCatalog.GetDisplayName(platinum, "ru-RU"));
        Assert.Equal("Platinum", platinum.CommodityId);
    }
}
