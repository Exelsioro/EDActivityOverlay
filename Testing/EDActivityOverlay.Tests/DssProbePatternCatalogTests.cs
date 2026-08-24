using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Exploration;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssProbePatternCatalogTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(9)]
    [InlineData(12)]
    public void CreatesNumberedLayoutForEverySupportedTarget(int target)
    {
        DssProbePattern pattern = DssProbePatternCatalog.Get(target);

        Assert.Equal(target, pattern.EfficiencyTarget);
        Assert.Equal(target, pattern.Points.Count);
        Assert.Equal(Enumerable.Range(1, target), pattern.Points.Select(point => point.Sequence));
        Assert.All(pattern.Points, point =>
        {
            Assert.InRange(point.X, -1.25, 1.25);
            Assert.InRange(point.Y, -1.25, 1.25);
        });
    }

    [Fact]
    public void LargeLayoutsIncludeFarSideShots()
    {
        DssProbePattern pattern = DssProbePatternCatalog.Get(10);

        Assert.Contains(pattern.Points, point => point.Zone == DssAimZone.FarSide);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(20, 12)]
    public void ClampsTargetsToSupportedRange(int requested, int expected) =>
        Assert.Equal(expected, DssProbePatternCatalog.Get(requested).EfficiencyTarget);
}
