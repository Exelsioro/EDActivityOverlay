using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssHorizonLongGapIsolationTests
{
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(5, 0, true)]
    [InlineData(0, 5, true)]
    [InlineData(6, 0, false)]
    [InlineData(0, 6, false)]
    [InlineData(11, 2, false)]
    public void ExtendedTangentContinuation_GatesLongStructure(
        int leftLongestRun,
        int rightLongestRun,
        bool expected)
    {
        Assert.Equal(
            expected,
            DssHudGeometryDetector
                .IsExtendedHorizonContinuationAccepted(
                    leftLongestRun,
                    rightLongestRun));
    }
}
