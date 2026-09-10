using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.UserControls;

/// <summary>
/// Composite-renderer counterpart of MainWindow's visible controller surface.
/// MainWindow remains the hidden application/controller HWND and owns hotkeys
/// and orchestration; this control forwards user intent to it.
/// </summary>
public partial class MainOverlayPanelControl : UserControl, IDisposable
{
    private sealed record ActivityOption(
        ActivityType Activity,
        string LabelKey)
    {
        public string Label => Loc.Get(LabelKey);
    }

    private static readonly (ActivityType Activity, string LabelKey)[] ActivityDefinitions =
    [
        (ActivityType.Trade, "Loc_Trade"),
        (ActivityType.Engineering, "Loc_Engineering"),
        (ActivityType.Exploration, "Loc_Exploration"),
        (ActivityType.Mining, "Loc_Mining")
    ];

    private readonly EDActivityOverlay.MainWindow controller;
    private readonly DispatcherTimer refreshTimer;
    private bool updatingSelection;
    private bool disposed;
    private ActivityType? renderedActivity;
    private bool collapsed;

    public double PreferredWidth => collapsed ? 170 : 235;
    public double PreferredHeight => collapsed ? 32 : 166;

    public MainOverlayPanelControl(EDActivityOverlay.MainWindow controller)
    {
        this.controller = controller;
        InitializeComponent();

        SettingsService.Instance.SettingsChanged += OnSettingsChanged;
        refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        refreshTimer.Tick += RefreshTimer_Tick;
        refreshTimer.Start();

        ApplySettings(SettingsService.Instance.Settings);
        RefreshLocalization();
        RefreshState();
    }

    public void RefreshLocalization()
    {
        ActivityType selected = controller.CompositeCurrentActivity;
        updatingSelection = true;
        ActivitySelector.ItemsSource = ActivityDefinitions
            .Select(definition =>
                new ActivityOption(
                    definition.Activity,
                    definition.LabelKey))
            .ToArray();
        ActivitySelector.SelectedItem = ActivitySelector.Items
            .OfType<ActivityOption>()
            .FirstOrDefault(option => option.Activity == selected);
        updatingSelection = false;
        renderedActivity = selected;
        RefreshState();
    }

    private void RefreshTimer_Tick(object? sender, EventArgs e) => RefreshState();

    private void RefreshState()
    {
        if (disposed)
        {
            return;
        }

        ActivityType current = controller.CompositeCurrentActivity;
        if (renderedActivity != current)
        {
            updatingSelection = true;
            ActivitySelector.SelectedItem = ActivitySelector.Items
                .OfType<ActivityOption>()
                .FirstOrDefault(option => option.Activity == current);
            updatingSelection = false;
            renderedActivity = current;
        }

        bool interactive = controller.CompositeInteractionActive;
        string stateText = interactive
            ? Loc.Get("Loc_INTERACTIVE")
            : Loc.Get("Loc_PASSIVE");
        InteractionStatusBadge.Text = stateText;
        CollapsedInteractionStatusBadge.Text = stateText;
        InteractionButton.IsEnabled = controller.CompositeInteractionEnabled;
        InteractionButton.Content = FormatHotkey(
            SettingsService.Instance.Settings.InteractiveHotkeyModifiers,
            SettingsService.Instance.Settings.InteractiveHotkeyKey);

        string activityLabel = ActivityDefinitions
            .First(definition => definition.Activity == current)
            .LabelKey;
        string visibleState = controller.CompositeOverlaysSuppressed
            ? Loc.Get("Loc_HIDDEN")
            : Loc.Get(activityLabel);
        AppSettings settings = SettingsService.Instance.Settings;
        InteractionHintText.Text = Loc.Format(
            "Loc_Main_Hint_Format",
            FormatHotkey(settings.ToggleHotkeyModifiers, settings.ToggleHotkeyKey),
            visibleState,
            FormatHotkey(settings.InteractiveHotkeyModifiers, settings.InteractiveHotkeyKey));
    }

    private void OnSettingsChanged(
        object? sender,
        SettingsChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnSettingsChanged(sender, e)));
            return;
        }

        ApplySettings(e.Settings);
        RefreshState();
    }

    private void ApplySettings(AppSettings settings)
    {
        OverlayChromeHelper.Apply(OverlayFrame, settings.OverlayChromeStyle);
        collapsed = settings.MainOverlayCollapsed;
        ExpandedControlContent.Visibility = collapsed
            ? Visibility.Collapsed
            : Visibility.Visible;
        CollapsedControlContent.Visibility = collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
        OverlayFrame.Padding = collapsed
            ? new Thickness(6, 4, 6, 4)
            : new Thickness(10, 7, 10, 7);
        Width = PreferredWidth;
        Height = PreferredHeight;
    }

    private void ActivitySelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!updatingSelection
            && ActivitySelector.SelectedItem is ActivityOption option)
        {
            controller.SelectActivity(option.Activity);
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) =>
        controller.OpenCompositeSettings();

    private void InteractionButton_Click(object sender, RoutedEventArgs e) =>
        controller.ToggleCompositeInteraction();

    private void CollapseButton_Click(object sender, RoutedEventArgs e) =>
        SettingsService.Instance.SetMainOverlayCollapsed(true);

    private void ExpandButton_Click(object sender, RoutedEventArgs e) =>
        SettingsService.Instance.SetMainOverlayCollapsed(false);

    private static string FormatHotkey(string modifiers, string key)
    {
        if (key.StartsWith("D", StringComparison.OrdinalIgnoreCase)
            && key.Length == 2
            && char.IsDigit(key[1]))
        {
            return $"{modifiers}+{key[1]}";
        }

        return $"{modifiers}+{key}";
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        refreshTimer.Stop();
        refreshTimer.Tick -= RefreshTimer_Tick;
        SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
    }
}
