namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Single source of truth for the experimental DSS runtime. Composite rendering
/// by itself does not disable DSS; VR compatibility does because the CV HUD is
/// calibrated against the desktop game mirror.
/// </summary>
internal static class DssLifecyclePolicy
{
    public static bool ShouldRun(
        bool requested,
        bool vrEnabled) =>
        requested && !vrEnabled;
}
