using EDActivityOverlay.Services;

namespace EDActivityOverlay.Utils;

/// <summary>
/// Keeps the existing desktop focus policy intact while allowing a VR runtime
/// to capture live WPF HWNDs after ordinary Windows focus moves away from Elite.
/// </summary>
internal static class OverlayVisibilityPolicy
{
    public static bool VrModeEnabled =>
        VrOverlaySupport.IsEnabled;

    public static bool TargetReady(
        bool windowExists,
        bool windowVisible,
        bool minimized) =>
        ResolveTargetReady(
            VrModeEnabled,
            windowExists,
            windowVisible,
            minimized);

    public static bool FocusAllowsPresentation(
        bool targetHasFocus,
        bool overlayHasFocus) =>
        ResolveFocusAllowsPresentation(
            VrModeEnabled,
            targetHasFocus,
            overlayHasFocus);

    internal static bool ResolveTargetReady(
        bool vrModeEnabled,
        bool windowExists,
        bool windowVisible,
        bool minimized) =>
        windowExists
        && (vrModeEnabled
            || (windowVisible && !minimized));

    internal static bool ResolveFocusAllowsPresentation(
        bool vrModeEnabled,
        bool targetHasFocus,
        bool overlayHasFocus) =>
        vrModeEnabled
        || targetHasFocus
        || overlayHasFocus;
}
