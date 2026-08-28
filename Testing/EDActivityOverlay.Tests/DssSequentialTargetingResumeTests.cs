using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class DssSequentialTargetingResumeTests
{
    [Fact]
    public void SameBodyAcrossDssReentry_ResumesSequence()
    {
        DssPrototypeSessionContext context =
            CreateContext(
                systemAddress: 123,
                bodyId: 10,
                bodyName: "Test 10");

        bool same =
            DssPrototypeController.IsSameTargetingBody(
                previousSystemAddress: 123,
                previousBodyId: 10,
                previousBodyName: "Test 10",
                context: context);

        Assert.True(same);
    }

    [Fact]
    public void DifferentBody_ResetsSequence()
    {
        DssPrototypeSessionContext context =
            CreateContext(
                systemAddress: 123,
                bodyId: 11,
                bodyName: "Test 11");

        bool same =
            DssPrototypeController.IsSameTargetingBody(
                previousSystemAddress: 123,
                previousBodyId: 10,
                previousBodyName: "Test 10",
                context: context);

        Assert.False(same);
    }

    private static DssPrototypeSessionContext CreateContext(
        long systemAddress,
        int bodyId,
        string bodyName) =>
        new(
            "Test",
            "Test",
            systemAddress,
            bodyName,
            bodyId,
            1_000_000,
            56.817001,
            26,
            20,
            "Sensor_Expanded",
            3,
            1920,
            1080);
}
