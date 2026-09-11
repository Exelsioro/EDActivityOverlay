using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Navigation;
using EDActivityOverlay.UserControls;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows;

public partial class ActivityWorkspaceOverlayWindow : Window
{
    private const double CompactWidth = 420;
    private const double CompactHeight = 350;
    private const double ExplorationFullMinWidth = 1040;
    private const double ExplorationFullMinHeight = 660;
    private const double ExplorationFullMaxWidth = 1180;
    private const double ExplorationFullMaxHeight = 760;

    private readonly MainWindow? parentWindow;
    private readonly DispatcherTimer updateTimer;
    private readonly ExplorationWorkspaceControl explorationWorkspaceControl;

    private IntPtr targetWindow;
    private ActivityType activity;
    private bool interactive;
    private bool showCursorWhenInteractive;
    private string placement = "MiddleRight";
    private string chromeStyle = OverlayChromeStyles.Compact;
    private bool hasManualPosition;
    private double manualXRatio;
    private double manualYRatio;
    private bool disposed;
    private bool explorationExclusiveInteraction;

    private bool fullExplorationVisible =>
        explorationWorkspaceControl.IsFullMode;

    public ActivityWorkspaceOverlayWindow(
        ActivityType initialActivity)
        : this(
            null,
            initialActivity)
    {
    }

    public ActivityWorkspaceOverlayWindow(
        MainWindow? parentWindow,
        ActivityType initialActivity)
    {
        this.parentWindow =
            parentWindow;

        activity =
            initialActivity;

        InitializeComponent();

        explorationWorkspaceControl =
            new ExplorationWorkspaceControl
            {
                Visibility =
                    Visibility.Collapsed
            };

        explorationWorkspaceControl.NavigateAsync =
            NavigateExplorationSystemAsync;

        explorationWorkspaceControl.DragRequested +=
            DragExplorationCompactRequested;

        explorationWorkspaceControl.ViewModeChanged +=
            ExplorationViewModeChanged;

        WorkspaceRoot.Children.Add(
            explorationWorkspaceControl);

        InitializeTradeWorkspace();
        InitializeMiningWorkspace();

        SetChromeStyle(
            SettingsService.Instance.Settings.OverlayChromeStyle);

        Loaded +=
            OnLoaded;

        Closed +=
            OnClosed;

        JournalMonitorService.Instance.StateChanged +=
            OnJournalStateChanged;

        updateTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromMilliseconds(150)
            };

        updateTimer.Tick +=
            UpdateTimer_Tick;

        updateTimer.Start();

        RefreshContent(
            JournalMonitorService.Instance.Current);
    }

    public void SetActivity(
        ActivityType value)
    {
        if (activity == ActivityType.Exploration
            && value != ActivityType.Exploration
            && explorationWorkspaceControl.IsFullMode)
        {
            explorationWorkspaceControl.CloseFullView();
        }

        if (activity == ActivityType.Trade
            && value != ActivityType.Trade)
        {
            LeaveTradeWorkspace();
        }

        if (activity == ActivityType.Mining
            && value != ActivityType.Mining)
        {
            LeaveMiningWorkspace();
        }

        activity =
            value;

        RefreshContent(
            JournalMonitorService.Instance.Current);
    }

    public void SetTargetWindow(
        IntPtr windowHandle)
    {
        targetWindow =
            windowHandle;

        PositionOverlay();
    }

    public void SetPlacement(
        string value)
    {
        placement =
            value;

        hasManualPosition =
            false;

        PositionOverlay();
    }

    public void SetChromeStyle(
        string? value)
    {
        chromeStyle =
            OverlayChromeStyles.Normalize(
                value);

        ApplyChrome();
    }

    private void ApplyChrome()
    {
        explorationWorkspaceControl.SetChromeStyle(
            chromeStyle);

        tradeWorkspaceControl?.SetChromeStyle(
            chromeStyle);

        miningWorkspaceControl?.SetChromeStyle(
            chromeStyle);
    }

    public void ApplyInteractionMode(
        bool enabled,
        bool showCursor)
    {
        interactive =
            enabled;

        showCursorWhenInteractive =
            showCursor;

        WindowsAPI.SetClickThrough(
            this,
            !enabled);

        IsHitTestVisible =
            enabled;

        ForceCursor =
            !enabled;

        Cursor =
            enabled
            && showCursor
                ? Cursors.Arrow
                : Cursors.None;

        if (enabled
            && showCursor
            && IsVisible)
        {
            WindowsAPI.EnsureCursorVisibleOnWindow(
                this);
        }
    }

    private void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        WindowsAPI.SetupOverlayWindow(
            this);

        ApplyInteractionMode(
            interactive,
            showCursorWhenInteractive);

        PositionOverlay();
    }

    private void OnJournalStateChanged(
        object? sender,
        GameStateChangedEventArgs e) =>
        Dispatcher.BeginInvoke(
            new Action(
                () =>
                    RefreshContent(
                        e.State)));

    private void RefreshContent(
        GameStateSnapshot state)
    {
        if (activity == ActivityType.Trade)
        {
            explorationWorkspaceControl.Visibility =
                Visibility.Collapsed;

            RefreshTradeWorkspace(
                state);

            return;
        }

        if (activity == ActivityType.Mining)
        {
            explorationWorkspaceControl.Visibility =
                Visibility.Collapsed;

            RefreshMiningWorkspace(
                state);

            return;
        }

        explorationWorkspaceControl.Visibility =
            Visibility.Visible;

        explorationWorkspaceControl.UpdateJournalState(
            state);
    }

    private void ExplorationViewModeChanged(
        bool full)
    {
        ApplyExplorationWorkspaceMode(
            full);
    }

    private void ApplyExplorationWorkspaceMode(
        bool full)
    {
        if (full)
        {
            MinWidth =
                ExplorationFullMinWidth;

            MinHeight =
                ExplorationFullMinHeight;

            Rect workArea =
                targetWindow != IntPtr.Zero
                    ? WindowsAPI.GetMonitorWorkArea(
                        targetWindow)
                    : SystemParameters.WorkArea;

            double availableWidth =
                workArea.Width;

            double availableHeight =
                workArea.Height;

            if (targetWindow != IntPtr.Zero
                && WindowsAPI.TryGetWindowRectDips(
                    targetWindow,
                    out WindowsAPI.RECT targetRect))
            {
                availableWidth =
                    Math.Min(
                        availableWidth,
                        Math.Max(
                            1,
                            targetRect.Right
                            - targetRect.Left));

                availableHeight =
                    Math.Min(
                        availableHeight,
                        Math.Max(
                            1,
                            targetRect.Bottom
                            - targetRect.Top));
            }

            Width =
                Math.Min(
                    ExplorationFullMaxWidth,
                    Math.Max(
                        ExplorationFullMinWidth,
                        availableWidth
                        * 0.86));

            Height =
                Math.Min(
                    ExplorationFullMaxHeight,
                    Math.Max(
                        ExplorationFullMinHeight,
                        availableHeight
                        * 0.86));

            if (!explorationExclusiveInteraction)
            {
                explorationExclusiveInteraction =
                    true;

                parentWindow?
                    .BeginExclusiveOverlayInteraction();
            }

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
            EndExplorationExclusiveInteraction();

            MinWidth =
                0;

            MinHeight =
                0;

            Width =
                CompactWidth;

            Height =
                CompactHeight;
        }

        PositionOverlay();
    }

    private void EndExplorationExclusiveInteraction()
    {
        if (!explorationExclusiveInteraction)
        {
            return;
        }

        explorationExclusiveInteraction =
            false;

        parentWindow?
            .EndExclusiveOverlayInteraction();
    }

    private void CloseFullExplorationView()
    {
        explorationWorkspaceControl.CloseFullView();
    }

    private async Task<EliteNavigationResult>
        NavigateExplorationSystemAsync(
            string targetSystem,
            bool confirmAutomatically,
            CancellationToken cancellationToken)
    {
        return await EliteRouteNavigationService.Instance
            .PrepareAsync(
                targetSystem,
                targetWindow,
                confirmAutomatically,
                cancellationToken);
    }

    private void DragExplorationCompactRequested()
    {
        if (!interactive
            || explorationWorkspaceControl.IsFullMode)
        {
            return;
        }

        try
        {
            DragMove();

            if (targetWindow != IntPtr.Zero
                && WindowsAPI.TryGetWindowRectDips(
                    targetWindow,
                    out WindowsAPI.RECT rect))
            {
                double availableX =
                    Math.Max(
                        1,
                        rect.Right
                        - rect.Left
                        - ActualWidth);

                double availableY =
                    Math.Max(
                        1,
                        rect.Bottom
                        - rect.Top
                        - ActualHeight);

                manualXRatio =
                    Math.Clamp(
                        (Left - rect.Left)
                        / availableX,
                        0,
                        1);

                manualYRatio =
                    Math.Clamp(
                        (Top - rect.Top)
                        / availableY,
                        0,
                        1);

                hasManualPosition =
                    true;
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void RefreshLocalization()
    {
        explorationWorkspaceControl.RefreshLocalization();
        tradeWorkspaceControl?.RefreshLocalization();
        miningWorkspaceControl?.RefreshLocalization();
        miningLocationWorkspaceControl?.RefreshLocalization();

        RefreshContent(
            JournalMonitorService.Instance.Current);
    }

    private void UpdateTimer_Tick(
        object? sender,
        EventArgs e)
    {
        if (disposed
            || targetWindow == IntPtr.Zero)
        {
            return;
        }

        if (OverlayVisibilityState.SuppressAll
            || OverlayVisibilityState.SuppressActivity)
        {
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        if (!WindowsAPI.IsWindow(
                targetWindow))
        {
            Close();
            return;
        }

        PositionOverlay();

        IntPtr foreground =
            WindowsAPI.GetForegroundWindow();

        WindowsAPI.GetWindowThreadProcessId(
            targetWindow,
            out uint targetProcessId);

        bool focused =
            OverlayVisibilityPolicy.FocusAllowsPresentation(
                WindowsAPI.IsWindowOwnedByProcess(
                    foreground,
                    targetProcessId),
                WindowsAPI.IsOverlayWindow(
                    foreground));

        bool visible =
            OverlayVisibilityPolicy.TargetReady(
                true,
                WindowsAPI.IsWindowVisible(
                    targetWindow),
                WindowsAPI.IsIconic(
                    targetWindow))
            && focused;

        if (visible
            && !IsVisible)
        {
            Show();
        }
        else if (!visible
                 && IsVisible)
        {
            Hide();
        }

        if (IsVisible
            && IsLoaded)
        {
            WindowsAPI.SetTopmost(
                this,
                focused);
        }
    }

    private void PositionOverlay()
    {
        if (targetWindow == IntPtr.Zero
            || !WindowsAPI.TryGetWindowRectDips(
                targetWindow,
                out WindowsAPI.RECT rect))
        {
            return;
        }

        double targetWidth =
            rect.Right
            - rect.Left;

        double targetHeight =
            rect.Bottom
            - rect.Top;

        if (fullExplorationVisible
            || IsTradeFullWorkspace
            || IsMiningFullWorkspace)
        {
            Width =
                Math.Min(
                    Math.Max(
                        ExplorationFullMaxWidth,
                        TradeWorkspaceMinWidth),
                    Math.Max(
                        MinWidth,
                        targetWidth - 64));

            Height =
                Math.Min(
                    Math.Max(
                        ExplorationFullMaxHeight,
                        TradeWorkspaceMinHeight),
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

        Rect workArea =
            WindowsAPI.GetMonitorWorkArea(
                targetWindow);

        double left;
        double top;

        if (hasManualPosition)
        {
            left =
                rect.Left
                + (Math.Max(
                    0,
                    rect.Right
                    - rect.Left
                    - Width)
                   * manualXRatio);

            top =
                rect.Top
                + (Math.Max(
                    0,
                    rect.Bottom
                    - rect.Top
                    - Height)
                   * manualYRatio);
        }
        else
        {
            (left, top) =
                OverlayLayoutHelper.GetPinnedPosition(
                    rect,
                    Width,
                    Height,
                    placement,
                    16);
        }

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

        EndExplorationExclusiveInteraction();

        explorationWorkspaceControl.DragRequested -=
            DragExplorationCompactRequested;

        explorationWorkspaceControl.ViewModeChanged -=
            ExplorationViewModeChanged;

        explorationWorkspaceControl.NavigateAsync =
            null;

        explorationWorkspaceControl.Dispose();

        DisposeTradeWorkspace();
        DisposeMiningWorkspace();

        updateTimer.Stop();

        updateTimer.Tick -=
            UpdateTimer_Tick;

        JournalMonitorService.Instance.StateChanged -=
            OnJournalStateChanged;
    }
}