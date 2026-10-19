using System.Windows;
using EDActivityOverlay.Services;

namespace EDActivityOverlay.Utils;

/// <summary>
/// VR-specific behavior for the composite renderer. Renderer selection is kept
/// separate: VR compatibility never creates or owns overlay surfaces itself.
/// </summary>
internal static class VrOverlaySupport
{
    public static bool IsEnabled =>
        ResolveEnabled(
            SettingsService.Instance.Settings.OverlayRenderMode,
            SettingsService.Instance.Settings.EnableVrOverlaySupport);

    internal static bool ResolveEnabled(
        string? renderMode,
        bool requested) =>
        requested && OverlayRenderModes.IsComposite(renderMode);

    public static void ApplyCompositeWindowIdentity(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (IsEnabled)
        {
            window.SetCurrentValue(Window.TitleProperty, "EDAO VR | Composite");
            window.ShowInTaskbar = true;
        }
        else
        {
            window.SetCurrentValue(Window.TitleProperty, "EDAO | Composite");
            window.ShowInTaskbar = false;
        }
    }
}
