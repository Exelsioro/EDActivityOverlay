using System.Windows;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Utils;
using EDActivityOverlay.Windows.Renderers;

namespace EDActivityOverlay;

public partial class MainWindow
{
    private bool renderModeInitialized;
    private bool compositeRenderingApplied;

    internal ActivityType CompositeCurrentActivity => currentActivity;
    internal bool CompositeInteractionEnabled => interactionModeEnabled;
    internal bool CompositeInteractionActive =>
        exclusiveOverlayInteraction
        || (interactionModeEnabled && interactiveModeActive);
    internal bool CompositeOverlaysSuppressed => overlaysSuppressedByHotkey;
    internal bool CompositeActivitySuppressed => OverlayVisibilityState.SuppressActivity;

    private void InitializeRenderModeSurfaces()
    {
        OverlayRenderCoordinator.Attach(this);
        ApplyOverlayRenderModeFromSettings(force: true);
    }

    private void ApplyOverlayRenderModeFromSettings(bool force = false)
    {
        bool composite = OverlayRenderCoordinator.IsCompositeMode;
        if (!force
            && renderModeInitialized
            && compositeRenderingApplied == composite)
        {
            OverlayRenderCoordinator.RefreshMode();
            return;
        }

        renderModeInitialized = true;
        compositeRenderingApplied = composite;

        if (composite)
        {
            CloseIndividualPrimarySurfaces();
            CloseOverlayWindows();
            CloseEngineeringOverlay();
            CloseActivityWorkspace();

            if (IsVisible)
            {
                Hide();
            }

            OverlayRenderCoordinator.RefreshMode();
            Logger.Logger.Info(
                "Overlay renderer switched to Composite; individual overlay HWNDs closed.");
            return;
        }

        OverlayRenderCoordinator.RefreshMode();
        EnsureIndividualPrimarySurfaces();
        EnsureIndividualPinnedRouteSurface();
        Logger.Logger.Info("Overlay renderer switched to Individual windows.");
    }

    private void EnsureIndividualPrimarySurfaces()
    {
        if (notificationOverlayWindow is null)
        {
            notificationOverlayWindow = new NotificationIndividualWindow(targetWindow);
        }
        else
        {
            notificationOverlayWindow.SetTargetWindow(targetWindow);
        }

        if (shipStatusOverlayWindow is null)
        {
            shipStatusOverlayWindow = new ShipStatusIndividualWindow(targetWindow);
            shipStatusOverlayWindow.SetContextSuppression(null);
        }
        else
        {
            shipStatusOverlayWindow.SetTargetWindow(targetWindow);
        }
    }

    private void EnsureIndividualPinnedRouteSurface()
    {
        if (!PinnedRoutePresentationService.Instance.Current.IsPinned)
        {
            return;
        }

        if (pinnedRouteOverlay is null)
        {
            pinnedRouteOverlay = new PinnedRouteIndividualWindow(this);
        }

        pinnedRouteOverlay.SetTargetWindow(targetWindow, targetProcessId);
        pinnedRouteOverlay.SetPlacement(SettingsService.Instance.Settings.PinnedRoutePosition);
        pinnedRouteOverlay.SetSuppressedByTradeWorkspace(pinnedRouteSuppressedByTradeWorkspace);
        pinnedRouteOverlay.ApplyInteractionMode(
            interactionModeEnabled && interactiveModeActive,
            showCursorWhenInteractive);
    }

    private void CloseIndividualPrimarySurfaces()
    {
        if (notificationOverlayWindow is not null)
        {
            notificationOverlayWindow.Close();
            notificationOverlayWindow = null;
        }

        if (shipStatusOverlayWindow is not null)
        {
            shipStatusOverlayWindow.Close();
            shipStatusOverlayWindow = null;
        }

        if (pinnedRouteOverlay is not null)
        {
            pinnedRouteOverlay.Close();
            pinnedRouteOverlay = null;
        }
    }

    internal void EnsureControllerLoadedForComposite()
    {
        if (IsLoaded)
        {
            if (IsVisible)
            {
                Hide();
            }
            return;
        }

        double previousOpacity = Opacity;
        bool previousTaskbar = ShowInTaskbar;
        Opacity = 0;
        ShowInTaskbar = false;
        Show();
        Hide();
        Opacity = previousOpacity;
        ShowInTaskbar = previousTaskbar;
    }

    internal void OpenCompositeSettings()
    {
        if (OperatingSystem.IsWindows()
            && Application.Current is App app)
        {
            app.ShowOverlaySettingsWindow();
        }
    }

    internal void HideCompositeActivity()
    {
        activityHiddenByHotkey = true;
        OverlayVisibilityState.SuppressActivity = true;
        UpdateInteractionStatusUi();
    }

    internal void ToggleCompositeInteraction()
    {
        if (interactionModeEnabled)
        {
            ToggleInteractiveModeFromHotkey();
        }
    }
}
