using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class DssMarkerEndpointValidationTests
{
    [Theory]
    // Strongest continuation seen after confirmed real centres in the v20
    // calibration set remains safely below both rejection thresholds.
    [InlineData(0.318, 6.84, 80, 1.0, true)]
    [InlineData(0.25, 12.0, 80, 1.0, true)]
    [InlineData(0.60, 6.0, 80, 1.0, true)]
    // Recorded wide probe-tail false centres: the apparent guide continues
    // strongly after the candidate and must be rejected.
    [InlineData(0.955, 73.0, 80, 1.0, false)]
    [InlineData(0.955, 39.0, 80, 1.0, false)]
    [InlineData(0.591, 74.0, 80, 1.0, false)]
    // If C is too close to the frame edge to observe a long enough post-C
    // segment, endpoint validation abstains instead of causing a false loss.
    [InlineData(0.95, 80.0, 20, 1.0, true)]
    public void MarkerEndpoint_SeparatesRecordedTailContinuation(
        double support,
        double averageContrast,
        double spanPixels,
        double scale,
        bool expected)
    {
        bool actual =
            DssHudGeometryDetector.IsMarkerEndpointAccepted(
                support,
                averageContrast,
                spanPixels,
                scale);

        Assert.Equal(expected, actual);
    }
}