using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.UserControls;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows;

/// <summary>
/// Single-HWND overlay renderer. It can be used on the desktop and is the only
/// supported capture surface when VR compatibility is enabled.
/// </summary>
public partial class CompositeOverlayWindow : Window
{
    private readonly EDActivityOverlay.MainWindow controller;
    private readonly DispatcherTimer layoutTimer;
    private readonly MainOverlayPanelControl mainPanel;
    private readonly CompositeActivityHostControl activityHost;
    private readonly Dictionary<ActivityType, Point> compactActivityPositions = new();
    private bool interactive;
    private bool showCursor;
    private bool navigationBusy;
    private bool activityDragActive;
    private Point activityDragStart;
    private Point activityDragOrigin;
    private ComboBox? vrComboBoxSource;
    private VrComboBoxOption[] vrComboBoxOptions = [];
    private bool disposed;

    public CompositeOverlayWindow(EDActivityOverlay.MainWindow controller)
    {
        this.controller = controller;
        InitializeComponent();

        mainPanel = new MainOverlayPanelControl(controller);
        MainPanelHost.Content = mainPanel;

        activityHost = new CompositeActivityHostControl(controller);
        ActivityHost.Content = activityHost;

        PinnedRoutePanel.ConfigureHost(
            () => controller.TargetWindowHandle,
            controller.UnpinRouteOverlay,
            controller.ReturnControlToGameForNavigation);

        Loaded += OnLoaded;
        Closed += OnClosed;
        NotificationPanel.ContentChanged += OnNotificationContentChanged;
        PinnedRoutePanel.ContentChanged += OnPinnedRouteContentChanged;
        PinnedRoutePanel.PreferredHeightChanged += OnPinnedRouteHeightChanged;
        PinnedRoutePanel.NavigationBusyChanged += OnPinnedRouteNavigationBusyChanged;
        ShipStatusPanel.PreferredHeightChanged += OnShipStatusHeightChanged;
        activityHost.PresentationChanged += OnActivityPresentationChanged;
        activityHost.CompactDragRequested += OnActivityCompactDragRequested;
        PreviewMouseLeftButtonDown += OnVrComboBoxPreviewMouseLeftButtonDown;
        PreviewMouseMove += OnPreviewMouseMove;
        PreviewMouseLeftButtonUp += OnVrComboBoxPreviewMouseLeftButtonUp;
        PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        PreviewKeyDown += OnVrComboBoxPreviewKeyDown;
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;

        layoutTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        layoutTimer.Tick += LayoutTimer_Tick;
    }

    public void RefreshLocalization()
    {
        mainPanel.RefreshLocalization();
        ShipStatusPanel.RefreshLocalization();
        NotificationPanel.RefreshLocalization();
        PinnedRoutePanel.RefreshLocalization();
        activityHost.RefreshLocalization();
        RefreshLayout();
    }

    public Task BeginCargoSaleFromMiningAsync() => activityHost.BeginCargoSaleFromMiningAsync();

    public void RefreshWindowMode()
    {
        VrOverlaySupport.ApplyCompositeWindowIdentity(this);
        if (!VrOverlaySupport.IsEnabled)
        {
            HideVrComboBoxDropDown();
        }

        RefreshLayout();
    }

    public void ApplyInteractionMode(bool enabled, bool shouldShowCursor)
    {
        interactive = enabled;
        showCursor = shouldShowCursor;
        ApplyInteractionState();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowsAPI.SetupOverlayWindow(this);
        VrOverlaySupport.ApplyCompositeWindowIdentity(this);
        ShipStatusPanel.ApplySettings(SettingsService.Instance.Settings);
        PinnedRoutePanel.ApplySettings(SettingsService.Instance.Settings);
        ApplyInteractionState();
        layoutTimer.Start();
        RefreshLayout();
    }

    private void LayoutTimer_Tick(object? sender, EventArgs e) => RefreshLayout();

    private void OnSettingsChanged(
        object? sender,
        SettingsChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnSettingsChanged(sender, e)));
            return;
        }

        VrOverlaySupport.ApplyCompositeWindowIdentity(this);
        ShipStatusPanel.ApplySettings(e.Settings);
        PinnedRoutePanel.ApplySettings(e.Settings);
        activityHost.ApplySettings(e.Settings);
        RefreshLayout();
    }

    private void OnNotificationContentChanged(object? sender, EventArgs e) => RefreshLayout();
    private void OnPinnedRouteContentChanged(object? sender, EventArgs e) => RefreshLayout();
    private void OnPinnedRouteHeightChanged(object? sender, EventArgs e) => RefreshLayout();
    private void OnShipStatusHeightChanged(object? sender, EventArgs e) => RefreshLayout();

    private void OnActivityPresentationChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(RefreshLayout));

    private void OnActivityCompactDragRequested(object? sender, EventArgs e)
    {
        if (!interactive
            || activityHost.IsFullMode
            || activityHost.RenderedActivity is not ActivityType activity)
        {
            return;
        }

        Point current = Mouse.GetPosition(OverlayCanvas);
        double left = Canvas.GetLeft(ActivityHost);
        double top = Canvas.GetTop(ActivityHost);
        activityDragStart = current;
        activityDragOrigin = new Point(
            double.IsNaN(left) ? 0 : left,
            double.IsNaN(top) ? 0 : top);
        activityDragActive = true;
        Mouse.Capture(ActivityHost);
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!activityDragActive
            || e.LeftButton != MouseButtonState.Pressed
            || activityHost.RenderedActivity is not ActivityType activity)
        {
            return;
        }

        Point current = e.GetPosition(OverlayCanvas);
        double maxLeft = Math.Max(0, OverlayCanvas.Width - ActivityHost.Width);
        double maxTop = Math.Max(0, OverlayCanvas.Height - ActivityHost.Height);
        double left = Math.Clamp(
            activityDragOrigin.X + current.X - activityDragStart.X,
            0,
            maxLeft);
        double top = Math.Clamp(
            activityDragOrigin.Y + current.Y - activityDragStart.Y,
            0,
            maxTop);

        Canvas.SetLeft(ActivityHost, left);
        Canvas.SetTop(ActivityHost, top);
        compactActivityPositions[activity] = new Point(
            maxLeft > 0 ? left / maxLeft : 0,
            maxTop > 0 ? top / maxTop : 0);
        e.Handled = true;
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!activityDragActive)
        {
            return;
        }

        activityDragActive = false;
        if (Mouse.Captured == ActivityHost)
        {
            Mouse.Capture(null);
        }
    }

    private void OnPinnedRouteNavigationBusyChanged(
        object? sender,
        PinnedRouteNavigationBusyChangedEventArgs e)
    {
        navigationBusy = e.IsBusy;
        ApplyInteractionState();
    }

    private void RefreshLayout()
    {
        if (disposed || !OverlayRenderCoordinator.IsCompositeMode)
        {
            return;
        }

        IntPtr targetWindow = controller.TargetWindowHandle;
        bool targetExists = targetWindow != IntPtr.Zero && WindowsAPI.IsWindow(targetWindow);
        bool targetReady = OverlayVisibilityPolicy.TargetReady(
            targetExists,
            targetExists && WindowsAPI.IsWindowVisible(targetWindow),
            targetExists && WindowsAPI.IsIconic(targetWindow));

        IntPtr foreground = WindowsAPI.GetForegroundWindow();
        bool presentationFocused = OverlayVisibilityPolicy.FocusAllowsPresentation(
            foreground == targetWindow,
            WindowsAPI.IsOverlayWindow(foreground));

        if (!targetReady || !presentationFocused)
        {
            HideVrComboBoxDropDown();
            if (IsVisible)
            {
                Hide();
            }
            return;
        }

        if (!WindowsAPI.TryGetWindowRectDips(targetWindow, out WindowsAPI.RECT targetRect))
        {
            return;
        }

        double targetWidth = Math.Max(1, targetRect.Right - targetRect.Left);
        double targetHeight = Math.Max(1, targetRect.Bottom - targetRect.Top);
        ApplyBounds(targetRect, targetWidth, targetHeight);
        ApplyInteractionState();

        AppSettings settings = SettingsService.Instance.Settings;
        bool suppressAll = OverlayVisibilityState.SuppressAll;
        bool suppressActivity = OverlayVisibilityState.SuppressActivity;

        activityHost.SetActivity(controller.CompositeCurrentActivity);
        activityHost.SetPresentationEnabled(!suppressAll && !suppressActivity);
        ActivityHost.Visibility =
            !suppressAll && !suppressActivity && activityHost.HasContent
                ? Visibility.Visible
                : Visibility.Collapsed;
        MainPanelHost.Visibility = !suppressAll ? Visibility.Visible : Visibility.Collapsed;
        ShipStatusPanel.Visibility =
            !suppressAll && settings.EnableShipStatusWidget
                ? Visibility.Visible
                : Visibility.Collapsed;
        NotificationPanel.Visibility =
            !suppressAll && NotificationPanel.HasNotifications
                ? Visibility.Visible
                : Visibility.Collapsed;
        PinnedRoutePanel.Visibility =
            !suppressAll
            && !suppressActivity
            && PinnedRoutePanel.IsPinned
            && !PinnedRoutePanel.IsSuppressedByTradeWorkspace
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (MainPanelHost.Visibility == Visibility.Visible)
        {
            double mainTop = Math.Max(0, targetHeight - mainPanel.PreferredHeight - 18);
            Canvas.SetLeft(MainPanelHost, 18);
            Canvas.SetTop(MainPanelHost, mainTop);
            Panel.SetZIndex(MainPanelHost, 60);
        }

        if (ActivityHost.Visibility == Visibility.Visible)
        {
            PositionActivity(targetWidth, targetHeight, settings);
        }

        if (ShipStatusPanel.Visibility == Visibility.Visible)
        {
            PositionShipStatus(targetWidth, targetHeight, settings.ShipStatusWidgetPosition);
        }

        if (NotificationPanel.Visibility == Visibility.Visible)
        {
            PositionNotifications(targetWidth, targetHeight, settings);
        }

        if (PinnedRoutePanel.Visibility == Visibility.Visible)
        {
            PositionPinnedRoute(targetWidth, targetHeight, settings.PinnedRoutePosition);
        }

        RefreshVrComboBoxDropDownPosition();

        if (!IsVisible)
        {
            Show();
        }

        WindowsAPI.SetTopmost(this, VrOverlaySupport.IsEnabled || presentationFocused);
    }

    private void ApplyBounds(
        WindowsAPI.RECT targetRect,
        double targetWidth,
        double targetHeight)
    {
        if (Math.Abs(Left - targetRect.Left) > 0.5) Left = targetRect.Left;
        if (Math.Abs(Top - targetRect.Top) > 0.5) Top = targetRect.Top;
        if (Math.Abs(Width - targetWidth) > 0.5) Width = targetWidth;
        if (Math.Abs(Height - targetHeight) > 0.5) Height = targetHeight;

        OverlayCanvas.Width = targetWidth;
        OverlayCanvas.Height = targetHeight;
    }

    private void PositionActivity(
        double targetWidth,
        double targetHeight,
        AppSettings settings)
    {
        (double width, double height) = activityHost.GetPreferredSize(
            targetWidth,
            targetHeight);
        ActivityHost.Width = width;
        ActivityHost.Height = height;

        double left;
        double top;
        if (activityHost.IsFullMode)
        {
            left = Math.Max(0, (targetWidth - width) / 2d);
            top = Math.Max(0, (targetHeight - height) / 2d);
            Panel.SetZIndex(ActivityHost, 70);
        }
        else if (activityHost.RenderedActivity is ActivityType activity
                 && compactActivityPositions.TryGetValue(activity, out Point ratio))
        {
            left = Math.Clamp(
                ratio.X * Math.Max(0, targetWidth - width),
                0,
                Math.Max(0, targetWidth - width));
            top = Math.Clamp(
                ratio.Y * Math.Max(0, targetHeight - height),
                0,
                Math.Max(0, targetHeight - height));
            Panel.SetZIndex(ActivityHost, 50);
        }
        else
        {
            var localRect = new WindowsAPI.RECT
            {
                Left = 0,
                Top = 0,
                Right = (int)Math.Round(targetWidth),
                Bottom = (int)Math.Round(targetHeight)
            };
            string placement = OverlayLayoutHelper.GetOppositeSidePlacement(
                settings.PinnedRoutePosition);
            (left, top) = OverlayLayoutHelper.GetPinnedPosition(
                localRect,
                width,
                height,
                placement,
                18);
            left = Math.Clamp(left, 0, Math.Max(0, targetWidth - width));
            top = Math.Clamp(top, 0, Math.Max(0, targetHeight - height));
            Panel.SetZIndex(ActivityHost, 50);
        }

        Canvas.SetLeft(ActivityHost, left);
        Canvas.SetTop(ActivityHost, top);
    }

    private void ApplyInteractionState()
    {
        bool canInteract = interactive && !navigationBusy;
        if (!canInteract)
        {
            HideVrComboBoxDropDown();
        }

        OverlayCanvas.IsHitTestVisible = canInteract;
        PinnedRoutePanel.ApplyInteractionMode(canInteract);
        activityHost.ApplyInteractionMode(canInteract);
        WindowsAPI.SetClickThrough(this, !canInteract);

        if (canInteract && showCursor && IsVisible)
        {
            WindowsAPI.EnsureCursorVisibleOnWindow(this);
        }
    }

    private void PositionShipStatus(
        double targetWidth,
        double targetHeight,
        string placement)
    {
        var localRect = new WindowsAPI.RECT
        {
            Left = 0,
            Top = 0,
            Right = (int)Math.Round(targetWidth),
            Bottom = (int)Math.Round(targetHeight)
        };

        (double left, double top) = OverlayLayoutHelper.GetPinnedPosition(
            localRect,
            ShipStatusPanel.Width,
            ShipStatusPanel.PreferredHeight,
            placement,
            18);

        left = Math.Clamp(left, 0, Math.Max(0, targetWidth - ShipStatusPanel.Width));
        top = Math.Clamp(top, 0, Math.Max(0, targetHeight - ShipStatusPanel.PreferredHeight));
        Canvas.SetLeft(ShipStatusPanel, left);
        Canvas.SetTop(ShipStatusPanel, top);
        Panel.SetZIndex(ShipStatusPanel, 40);
    }

    private void PositionNotifications(
        double targetWidth,
        double targetHeight,
        AppSettings settings)
    {
        double width = NotificationPanel.Width;
        double height = Math.Max(NotificationPanel.ActualHeight, 80);
        double left = Math.Max(0, (targetWidth - width) / 2d);
        double top = settings.EnableShipStatusWidget
                     && settings.ShipStatusWidgetPosition.Equals(
                         "TopCenter",
                         StringComparison.OrdinalIgnoreCase)
            ? 118
            : 72;

        top = Math.Clamp(top, 0, Math.Max(0, targetHeight - height));
        Canvas.SetLeft(NotificationPanel, left);
        Canvas.SetTop(NotificationPanel, top);
        Panel.SetZIndex(NotificationPanel, 90);
    }

    private void PositionPinnedRoute(
        double targetWidth,
        double targetHeight,
        string placement)
    {
        int placementMaxWidth = placement.Equals(
                "TopCenter",
                StringComparison.OrdinalIgnoreCase)
            || placement.Equals(
                "BottomCenter",
                StringComparison.OrdinalIgnoreCase)
            ? OverlayLayoutSettings.PinnedMaxWidth
            : 400;

        double width = Math.Max(
            280,
            Math.Min(
                placementMaxWidth,
                targetWidth * OverlayLayoutSettings.PinnedWidthByTarget));
        PinnedRoutePanel.Width = width;

        var localRect = new WindowsAPI.RECT
        {
            Left = 0,
            Top = 0,
            Right = (int)Math.Round(targetWidth),
            Bottom = (int)Math.Round(targetHeight)
        };

        (double left, double top) = OverlayLayoutHelper.GetPinnedPosition(
            localRect,
            width,
            PinnedRoutePanel.PreferredHeight,
            placement);

        left = Math.Clamp(left, 0, Math.Max(0, targetWidth - width));
        top = Math.Clamp(top, 0, Math.Max(0, targetHeight - PinnedRoutePanel.PreferredHeight));
        Canvas.SetLeft(PinnedRoutePanel, left);
        Canvas.SetTop(PinnedRoutePanel, top);
        Panel.SetZIndex(PinnedRoutePanel, 30);
    }

    private void OnVrComboBoxPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!VrOverlaySupport.IsEnabled
            || !interactive
            || navigationBusy)
        {
            return;
        }

        DependencyObject? source =
            e.OriginalSource
            as DependencyObject;

        if (source is not null
            && IsVisualDescendantOf(
                source,
                VrComboBoxDropDownHost))
        {
            return;
        }

        ComboBox? combo =
            FindVisualAncestor<ComboBox>(
                source);

        if (combo is null
            || !combo.IsEnabled
            || !combo.IsVisible)
        {
            HideVrComboBoxDropDown();
            return;
        }

        // Prevent the normal WPF Popup from ever opening. Popup owns a
        // separate HWND and is invisible to SteamVR when only this Composite
        // window is captured.
        e.Handled = true;
        combo.IsDropDownOpen = false;

        if (ReferenceEquals(
                vrComboBoxSource,
                combo)
            && VrComboBoxDropDownHost.Visibility
                == Visibility.Visible)
        {
            HideVrComboBoxDropDown();
            return;
        }

        ShowVrComboBoxDropDown(
            combo);
    }

    private void OnVrComboBoxPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (VrComboBoxDropDownHost.Visibility
                != Visibility.Visible
            || vrComboBoxSource is null)
        {
            return;
        }

        DependencyObject? source =
            e.OriginalSource
            as DependencyObject;

        ListBoxItem? item =
            FindVisualAncestor<ListBoxItem>(
                source);

        if (item?.DataContext
            is not VrComboBoxOption option)
        {
            return;
        }

        vrComboBoxSource.SelectedItem =
            option.SourceItem;

        e.Handled =
            true;

        HideVrComboBoxDropDown();
    }

    private void OnVrComboBoxPreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (VrComboBoxDropDownHost.Visibility
                != Visibility.Visible)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            HideVrComboBoxDropDown();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter
            && vrComboBoxSource is not null
            && VrComboBoxDropDownList.SelectedItem
                is VrComboBoxOption option)
        {
            vrComboBoxSource.SelectedItem =
                option.SourceItem;
            HideVrComboBoxDropDown();
            e.Handled = true;
        }
    }

    private void ShowVrComboBoxDropDown(
        ComboBox combo)
    {
        VrComboBoxOption[] options =
            combo.Items
                .Cast<object>()
                .Select(
                    item =>
                        new VrComboBoxOption(
                            item,
                            GetVrComboBoxDisplayText(
                                combo,
                                item)))
                .ToArray();

        if (options.Length == 0)
        {
            HideVrComboBoxDropDown();
            return;
        }

        vrComboBoxSource =
            combo;
        vrComboBoxOptions =
            options;

        VrComboBoxDropDownList.ItemsSource =
            options;
        VrComboBoxDropDownList.SelectedIndex =
            combo.SelectedIndex;

        VrComboBoxDropDownHost.Width =
            Math.Max(
                combo.ActualWidth,
                120);
        VrComboBoxDropDownHost.Visibility =
            Visibility.Visible;

        Panel.SetZIndex(
            VrComboBoxDropDownHost,
            1000);

        RefreshVrComboBoxDropDownPosition();
    }

    private void RefreshVrComboBoxDropDownPosition()
    {
        if (VrComboBoxDropDownHost.Visibility
                != Visibility.Visible)
        {
            return;
        }

        ComboBox? combo =
            vrComboBoxSource;

        if (!VrOverlaySupport.IsEnabled
            || combo is null
            || !combo.IsVisible
            || !combo.IsLoaded)
        {
            HideVrComboBoxDropDown();
            return;
        }

        try
        {
            GeneralTransform transform =
                combo.TransformToAncestor(
                    OverlayCanvas);

            Point topLeft =
                transform.Transform(
                    new Point(0, 0));

            double width =
                Math.Max(
                    combo.ActualWidth,
                    120);

            double estimatedHeight =
                Math.Min(
                    320,
                    Math.Max(
                        36,
                        vrComboBoxOptions.Length
                        * 34
                        + 6));

            double canvasWidth =
                Math.Max(
                    1,
                    OverlayCanvas.Width);
            double canvasHeight =
                Math.Max(
                    1,
                    OverlayCanvas.Height);

            double left =
                Math.Clamp(
                    topLeft.X,
                    0,
                    Math.Max(
                        0,
                        canvasWidth - width));

            double below =
                topLeft.Y
                + combo.ActualHeight;

            double top =
                below + estimatedHeight
                    <= canvasHeight
                    ? below
                    : Math.Max(
                        0,
                        topLeft.Y
                        - estimatedHeight);

            VrComboBoxDropDownHost.Width =
                width;

            Canvas.SetLeft(
                VrComboBoxDropDownHost,
                left);
            Canvas.SetTop(
                VrComboBoxDropDownHost,
                top);
        }
        catch (InvalidOperationException)
        {
            HideVrComboBoxDropDown();
        }
    }

    private void HideVrComboBoxDropDown()
    {
        VrComboBoxDropDownHost.Visibility =
            Visibility.Collapsed;
        VrComboBoxDropDownList.ItemsSource =
            null;
        VrComboBoxDropDownList.SelectedIndex =
            -1;
        vrComboBoxSource =
            null;
        vrComboBoxOptions =
            [];
    }

    private static string GetVrComboBoxDisplayText(
        ComboBox combo,
        object item)
    {
        object? value =
            item is ComboBoxItem comboItem
                ? comboItem.Content
                : item;

        if (value is TextBlock textBlock)
        {
            return textBlock.Text;
        }

        if (!string.IsNullOrWhiteSpace(
                combo.DisplayMemberPath))
        {
            object? current =
                value;

            foreach (string member
                     in combo.DisplayMemberPath.Split(
                         '.',
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current =
                    current?.GetType()
                        .GetProperty(member)
                        ?.GetValue(current);

                if (current is null)
                {
                    break;
                }
            }

            if (current is not null)
            {
                return current.ToString()
                       ?? string.Empty;
            }
        }

        return value?.ToString()
               ?? string.Empty;
    }

    private static T? FindVisualAncestor<T>(
        DependencyObject? source)
        where T : DependencyObject
    {
        DependencyObject? current =
            source;

        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current =
                GetVisualOrLogicalParent(
                    current);
        }

        return null;
    }

    private static bool IsVisualDescendantOf(
        DependencyObject source,
        DependencyObject ancestor)
    {
        DependencyObject? current =
            source;

        while (current is not null)
        {
            if (ReferenceEquals(
                    current,
                    ancestor))
            {
                return true;
            }

            current =
                GetVisualOrLogicalParent(
                    current);
        }

        return false;
    }

    private static DependencyObject? GetVisualOrLogicalParent(
        DependencyObject source)
    {
        try
        {
            return VisualTreeHelper.GetParent(
                       source)
                   ?? LogicalTreeHelper.GetParent(
                       source);
        }
        catch (InvalidOperationException)
        {
            return LogicalTreeHelper.GetParent(
                source);
        }
    }

    private sealed record VrComboBoxOption(
        object SourceItem,
        string Text);

    private void OnClosed(object? sender, EventArgs e)
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        layoutTimer.Stop();
        layoutTimer.Tick -= LayoutTimer_Tick;
        SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
        NotificationPanel.ContentChanged -= OnNotificationContentChanged;
        PinnedRoutePanel.ContentChanged -= OnPinnedRouteContentChanged;
        PinnedRoutePanel.PreferredHeightChanged -= OnPinnedRouteHeightChanged;
        PinnedRoutePanel.NavigationBusyChanged -= OnPinnedRouteNavigationBusyChanged;
        ShipStatusPanel.PreferredHeightChanged -= OnShipStatusHeightChanged;
        activityHost.PresentationChanged -= OnActivityPresentationChanged;
        activityHost.CompactDragRequested -= OnActivityCompactDragRequested;
        PreviewMouseLeftButtonDown -= OnVrComboBoxPreviewMouseLeftButtonDown;
        PreviewMouseMove -= OnPreviewMouseMove;
        PreviewMouseLeftButtonUp -= OnVrComboBoxPreviewMouseLeftButtonUp;
        PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
        PreviewKeyDown -= OnVrComboBoxPreviewKeyDown;
        if (Mouse.Captured == ActivityHost)
        {
            Mouse.Capture(null);
        }
        mainPanel.Dispose();
        activityHost.Dispose();
        NotificationPanel.Dispose();
        PinnedRoutePanel.Dispose();
        ShipStatusPanel.Dispose();
    }
}
