using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class DssMarkerCoreShapeValidationTests
{
    [Theory]
    // v19 uploaded session: coloured probe-tail candidates.
    [InlineData(0, 0, false)]
    [InlineData(19, 0, false)]
    // v18 known false probe tail: enough total white pixels, but almost all
    // of them are on one side of the candidate instead of a filled disk.
    [InlineData(94, 2, false)]
    // v19 bright-limb family: balanced enough, but too sparse to be a centre disk.
    [InlineData(56, 8, false)]
    // Lowest-density confirmed real marker in the uploaded v19 regression set.
    [InlineData(105, 7, true)]
    // Normal filled centre marker.
    [InlineData(197, 41, true)]
    public void MarkerCoreShape_SeparatesRecordedFalseFamilies(
        int neutralHits,
        int minimumQuadrantHits,
        bool expected)
    {
        bool actual =
            DssHudGeometryDetector.IsMarkerCoreShapeAccepted(
                neutralHits,
                minimumQuadrantHits,
                1d);

        Assert.Equal(expected, actual);
    }
}
