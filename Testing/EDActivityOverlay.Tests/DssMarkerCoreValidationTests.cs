using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssMarkerCoreValidationTests
{
    [Theory]
    [InlineData(0d, false)]
    [InlineData(111.22d, false)] // v18 confirmed probe-tail false centre, seq 188
    [InlineData(124.99d, false)]
    [InlineData(125d, true)]
    [InlineData(140.44d, true)] // dimmest saved real centre in the v18 set
    [InlineData(164.44d, true)]
    [InlineData(207.89d, true)]
    public void MarkerCoreLumaGateSeparatesKnownTailFromRealMarkers(
        double meanLuma,
        bool expected)
    {
        Assert.Equal(
            expected,
            DssHudGeometryDetector
                .IsMarkerCoreLumaAccepted(meanLuma));
    }
}
