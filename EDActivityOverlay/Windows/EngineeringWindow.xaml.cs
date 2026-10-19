using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Navigation;
using EDActivityOverlay.UserControls;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows;

public partial class EngineeringWindow : Window
{
    private readonly MainWindow? parentWindow;
    private readonly bool overlayMode;

    private IntPtr targetWindow;
    private string placement =
        "MiddleRight";
    private string chromeStyle =
        OverlayChromeStyles.Compact;
    private bool disposed;

    public bool IsOverlayMode =>
        overlayMode;

    public EngineeringWindow()
        : this(null)
    {
    }

    public EngineeringWindow(
        MainWindow? parentWindow)
    {
        this.parentWindow =
            parentWindow;

        overlayMode =
            parentWindow is not null;

        targetWindow =
            parentWindow?.TargetWindowHandle
            ?? IntPtr.Zero;

        InitializeComponent();

        if (overlayMode)
        {
            WindowStyle =
                WindowStyle.None;
            AllowsTransparency =
                true;
            ResizeMode =
                ResizeMode.NoResize;
            ShowInTaskbar =
                false;
            Topmost =
                true;
            WindowStartupLocation =
                WindowStartupLocation.Manual;

            Workspace.ConfigureOverlayPresentation();

            MinWidth =
                0;
            MinHeight =
                0;
            Width =
                Workspace.PreferredWidth;
            Height =
                Workspace.PreferredHeight;

            SetChromeStyle(
                SettingsService.Instance.Settings.OverlayChromeStyle);
        }

        Workspace.CloseRequested +=
            WorkspaceCloseRequested;
        Workspace.DragRequested +=
            WorkspaceDragRequested;
        Workspace.ViewModeChanged +=
            WorkspaceViewModeChanged;
        Workspace.PreferredSizeChanged +=
            WorkspacePreferredSizeChanged;

        Workspace.NavigateAsync =
            targetWindow != IntPtr.Zero
                ? NavigateSystemAsync
                : null;

        Loaded +=
            OnLoaded;
        Closed +=
            OnClosed;
    }

    private void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (!overlayMode)
        {
            return;
        }

        WindowsAPI.SetupOverlayWindow(
            this);

        PositionOverTarget();

        WindowsAPI.SetTopmost(
            this,
            true);
    }

    private void OnClosed(
        object? sender,
        EventArgs e)
    {
        if (disposed)
        {
            return;
        }

        disposed =
            true;

        if (Workspace.IsFullMode)
        {
            parentWindow?
                .EndExclusiveOverlayInteraction();
        }

        Workspace.CloseRequested -=
            WorkspaceCloseRequested;
        Workspace.DragRequested -=
            WorkspaceDragRequested;
        Workspace.ViewModeChanged -=
            WorkspaceViewModeChanged;
        Workspace.PreferredSizeChanged -=
            WorkspacePreferredSizeChanged;

        Workspace.NavigateAsync =
            null;

        Workspace.Dispose();

        parentWindow?
            .OnEngineeringOverlayClosed();
    }

    public void SetTargetWindow(
        IntPtr window)
    {
        targetWindow =
            window;

        Workspace.NavigateAsync =
            targetWindow != IntPtr.Zero
                ? NavigateSystemAsync
                : null;

        if (IsLoaded)
        {
            PositionOverTarget();
        }
    }

    public void SetPlacement(
        string value)
    {
        placement =
            string.IsNullOrWhiteSpace(
                value)
                ? "MiddleRight"
                : value;

        PositionOverTarget();
    }

    public void SetChromeStyle(
        string value)
    {
        chromeStyle =
            OverlayChromeStyles.Normalize(
                value);

        Workspace.SetChromeStyle(
            chromeStyle);

        ApplyWindowSurface();
    }

    private void ApplyWindowSurface()
    {
        if (!overlayMode)
        {
            SetResourceReference(
                Window.BackgroundProperty,
                "PrimaryBackgroundColorBrush");

            return;
        }

        bool transparentSurface =
            !Workspace.IsFullMode
            || chromeStyle
               == OverlayChromeStyles.Minimal;

        if (transparentSurface)
        {
            Background =
                System.Windows.Media.Brushes.Transparent;
        }
        else
        {
            SetResourceReference(
                Window.BackgroundProperty,
                "PrimaryBackgroundColorBrush");
        }
    }

    public void ApplyInteractionMode(
        bool canInteract,
        bool showCursor)
    {
        if (!overlayMode
            || !IsLoaded)
        {
            return;
        }

        WindowsAPI.SetClickThrough(
            this,
            !canInteract);

        IsHitTestVisible =
            canInteract;

        ForceCursor =
            !canInteract;

        Cursor =
            canInteract
            && showCursor
                ? Cursors.Arrow
                : Cursors.None;

        Workspace.ApplyInteractionHint(
            canInteract);

        if (canInteract
            && showCursor)
        {
            WindowsAPI.EnsureCursorVisibleOnWindow(
                this);
        }
        else
        {
            WindowsAPI.RestoreCursorVisibility();
        }

        WindowsAPI.SetTopmost(
            this,
            true);
    }

    private void PositionOverTarget()
    {
        ApplyWindowSurface();

        if (!overlayMode
            || targetWindow == IntPtr.Zero
            || !WindowsAPI.TryGetWindowRectDips(
                targetWindow,
                out WindowsAPI.RECT rect))
        {
            return;
        }

        double targetWidth =
            rect.Right - rect.Left;
        double targetHeight =
            rect.Bottom - rect.Top;

        if (Workspace.IsFullMode)
        {
            Width =
                Math.Min(
                    EngineeringWorkspaceControl.FullMaxWidth,
                    Math.Max(
                        MinWidth,
                        targetWidth - 64));

            Height =
                Math.Min(
                    EngineeringWorkspaceControl.FullMaxHeight,
                    Math.Max(
                        MinHeight,
                        targetHeight - 64));

            Left =
                rect.Left
                + (targetWidth - Width)
                  / 2.0;

            Top =
                rect.Top
                + (targetHeight - Height)
                  / 2.0;

            return;
        }

        Width =
            Workspace.PreferredWidth;
        Height =
            Workspace.PreferredHeight;

        Rect workArea =
            WindowsAPI.GetMonitorWorkArea(
                targetWindow);

        (double left, double top) =
            OverlayLayoutHelper.GetPinnedPosition(
                rect,
                Width,
                Height,
                placement,
                16);

        OverlayLayoutHelper.ClampPosition(
            ref left,
            ref top,
            Width,
            Height,
            workArea,
            10,
            10);

        Left =
            left;
        Top =
            top;
    }

    private void WorkspaceCloseRequested() =>
        Close();

    private void WorkspaceDragRequested()
    {
        if (!overlayMode)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void WorkspaceViewModeChanged(
        bool full)
    {
        if (!overlayMode)
        {
            return;
        }

        if (full)
        {
            MinWidth =
                EngineeringWorkspaceControl.FullMinWidth;
            MinHeight =
                EngineeringWorkspaceControl.FullMinHeight;

            PositionOverTarget();

            parentWindow?
                .BeginExclusiveOverlayInteraction();

            Activate();

            IntPtr handle =
                new WindowInteropHelper(
                    this).Handle;

            if (handle != IntPtr.Zero)
            {
                WindowsAPI.TryActivateWindow(
                    handle);
            }

            WindowsAPI.EnsureCursorVisibleOnWindow(
                this);
        }
        else
        {
            parentWindow?
                .EndExclusiveOverlayInteraction();

            MinWidth =
                0;
            MinHeight =
                0;
            Width =
                Workspace.PreferredWidth;
            Height =
                Workspace.PreferredHeight;

            PositionOverTarget();
        }

        ApplyWindowSurface();
    }

    private void WorkspacePreferredSizeChanged()
    {
        if (!overlayMode
            || Workspace.IsFullMode)
        {
            return;
        }

        Width =
            Workspace.PreferredWidth;
        Height =
            Workspace.PreferredHeight;

        PositionOverTarget();
    }

    private async Task<EliteNavigationResult> NavigateSystemAsync(
        string targetSystem,
        bool automatic,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                targetSystem)
            || targetWindow == IntPtr.Zero)
        {
            return new EliteNavigationResult(
                EliteNavigationStatus.Failed,
                targetSystem,
                "Loc_NAVIGATION_GAME_NOT_FOUND");
        }

        if (parentWindow is not null)
        {
            parentWindow
                .ReturnControlToGameForNavigation();
        }
        else
        {
            if (overlayMode
                && IsLoaded)
            {
                WindowsAPI.SetClickThrough(
                    this,
                    true);
            }

            WindowsAPI.RestoreCursorVisibility();

            WindowsAPI.TryActivateWindow(
                targetWindow);
        }

        return await EliteRouteNavigationService.Instance.PrepareAsync(
            targetSystem,
            targetWindow,
            automatic,
            cancellationToken);
    }

    public void RefreshLocalization() =>
        Workspace.RefreshLocalization();
}
