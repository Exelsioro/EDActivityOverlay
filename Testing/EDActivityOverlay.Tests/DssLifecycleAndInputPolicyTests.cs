using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class DssLifecycleAndInputPolicyTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void DssRuntimeFollowsRequestedAndVrMatrix(
        bool requested,
        bool vrEnabled,
        bool expected)
    {
        Assert.Equal(
            expected,
            DssLifecyclePolicy.ShouldRun(
                requested,
                vrEnabled));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void FirePollingAcceptsGameOrOverlayFocus(
        bool targetHasFocus,
        bool overlayHasFocus,
        bool expected)
    {
        Assert.Equal(
            expected,
            DssFireInputMonitor.AllowsPollingForFocus(
                targetHasFocus,
                overlayHasFocus));
    }

    [Theory]
    [InlineData(false, false, false, 1, 1)]
    [InlineData(true, false, false, 2, 2)]
    [InlineData(true, true, false, 1, 1)]
    [InlineData(true, true, false, 0, 1)]
    [InlineData(true, true, true, 0, 0)]
    [InlineData(false, true, false, 0, 0)]
    public void FireEventPulsesSurvivePollingAndDoNotDoubleCount(
        bool initialized,
        bool down,
        bool wasDown,
        int hookPulses,
        int expected)
    {
        Assert.Equal(
            expected,
            DssFireInputMonitor.CountPresses(
                initialized,
                down,
                wasDown,
                hookPulses));
    }

    [Theory]
    [InlineData(1, 1, 2)]
    [InlineData(2, 1, 2)]
    [InlineData(4, 2, 4)]
    [InlineData(1, -1, 1)]
    public void NativeHitReconciliationNeverRewindsOrDoubleAdvances(
        int currentStep,
        int nativeHits,
        int expectedStep)
    {
        Assert.Equal(
            expectedStep,
            DssPrototypeController.NextStepForNativeHits(
                currentStep,
                nativeHits));
    }
}
