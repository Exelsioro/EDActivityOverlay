using System.Windows;
using System.Windows.Controls;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Hardware;
using EDActivityOverlay.Utils;
using EDActivityOverlay.Windows;

namespace EDActivityOverlay;

public partial class MainWindow
{
    private sealed record ActivityOption(ActivityType Activity, string LabelKey)
    {
        public string Label => Loc.Get(LabelKey);
    }

    private static readonly ActivityOption[] ActivityOptions =
    [
        new(ActivityType.Trade, "Loc_Trade"),
        new(ActivityType.Engineering, "Loc_Engineering"),
        new(ActivityType.Exploration, "Loc_Exploration"),
        new(ActivityType.Mining, "Loc_Mining")
    ];

    private ActivityType currentActivity = ActivityType.Trade;
    private ActivityWorkspaceOverlayWindow? activityWorkspaceWindow;
    private bool restoreActivityWorkspaceVisible;
    private bool updatingActivitySelector;
    private bool activityHiddenByHotkey;

    private void ActivitySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!updatingActivitySelector && ActivitySelector.SelectedItem is ActivityOption option)
        {
            SelectActivity(option.Activity);
        }
    }

    private void ActivitySettingsButton_Click(object sender, RoutedEventArgs e)
    {
        CloseEngineeringOverlay();
        CloseActivityWorkspace();
        if (OperatingSystem.IsWindows() && Application.Current is App app) app.ShowOverlaySettingsWindow();
    }

    public void SelectActivity(ActivityType activity)
    {
        if (targetWindow == IntPtr.Zero || !WindowsAPI.IsWindow(targetWindow)) return;

        if (OperatingSystem.IsWindows() && Application.Current is App app) app.CloseOverlaySettingsWindow();

        if (OverlayVisibilityState.SuppressAll)
        {
            OverlayVisibilityState.SuppressAll = false;
            overlaysSuppressedByHotkey = false;
        }

        activityHiddenByHotkey = false;
        OverlayVisibilityState.SuppressActivity = false;
        currentActivity = activity;
        X52IntegrationService.Instance.SetActivity(activity);
        switch (activity)
        {
            case ActivityType.Trade:
                CloseOverlayWindows();
                CloseEngineeringOverlay();
                EnsureJournalWorkspaceVisible(ActivityType.Trade);
                break;
            case ActivityType.Engineering:
                CloseOverlayWindows();
                CloseActivityWorkspace();
                EnsureEngineeringWorkspaceVisible();
                break;
            case ActivityType.Exploration:
            case ActivityType.Mining:
                CloseOverlayWindows();
                CloseEngineeringOverlay();
                EnsureJournalWorkspaceVisible(activity);
                break;
        }

        UpdateActivityNavigationUi();
        UpdateOverlayInteractionModes();
        Logger.Logger.Info($"Activity workspace selected: {activity}");
    }

    public async Task OpenTradeCargoSaleFromMiningAsync()
    {
        SelectActivity(
            ActivityType.Trade);

        if (activityWorkspaceWindow is null)
        {
            return;
        }

        await activityWorkspaceWindow.BeginCargoSaleFromMiningAsync();
    }

    private void OnX52ControlRequested(object? sender, X52ControlEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            int currentIndex = Array.FindIndex(ActivityOptions, option => option.Activity == currentActivity);
            switch (e.Action)
            {
                case X52ControlAction.PreviousActivity:
                    SelectActivity(ActivityOptions[(currentIndex - 1 + ActivityOptions.Length) % ActivityOptions.Length].Activity);
                    break;
                case X52ControlAction.NextActivity:
                    SelectActivity(ActivityOptions[(currentIndex + 1) % ActivityOptions.Length].Activity);
                    break;
                case X52ControlAction.ToggleActivity:
                    ToggleActivityFromHotkey(currentActivity);
                    break;
                case X52ControlAction.ToggleInteraction:
                    ToggleInteractiveModeFromHotkey();
                    break;
                case X52ControlAction.ToggleOverlay:
                    PerformToggleAction();
                    break;
            }
        }));

    private void ToggleActivityFromHotkey(ActivityType activity)
    {
        bool isVisible = activity == currentActivity && activity switch
        {
            ActivityType.Trade => activityWorkspaceWindow?.IsVisible == true
                                  || pinnedRouteOverlay?.IsVisible == true,
            ActivityType.Engineering => engineeringOverlayWindow?.IsVisible == true,
            ActivityType.Exploration or ActivityType.Mining => activityWorkspaceWindow?.IsVisible == true,
            _ => false
        };

        if (!isVisible && activity == currentActivity && activityHiddenByHotkey)
        {
            activityHiddenByHotkey = false;
            OverlayVisibilityState.SuppressActivity = false;
            RestoreCurrentActivityWindows();
            UpdateInteractionStatusUi();
            return;
        }

        if (!isVisible)
        {
            SelectActivity(activity);
            return;
        }

        activityHiddenByHotkey = true;
        OverlayVisibilityState.SuppressActivity = true;
        if (activity == ActivityType.Trade)
        {
            activityWorkspaceWindow?.Hide();
            pinnedRouteOverlay?.Hide();
        }
        else if (activity == ActivityType.Engineering)
        {
            // Closing also releases exclusive interaction if the full assistant was open.
            CloseEngineeringOverlay();
        }
        else
        {
            activityWorkspaceWindow?.Hide();
        }
        UpdateInteractionStatusUi();
    }

    private void RestoreCurrentActivityWindows()
    {
        if (currentActivity == ActivityType.Trade)
        {
            if (activityWorkspaceWindow is { IsLoaded: true })
            {
                activityWorkspaceWindow.Show();
            }
            else if (!isPinnedRouteActive)
            {
                EnsureJournalWorkspaceVisible(
                    ActivityType.Trade);
            }

            if (isPinnedRouteActive
                && !pinnedRouteSuppressedByTradeWorkspace
                && pinnedRouteOverlay is { IsLoaded: true })
            {
                pinnedRouteOverlay.Show();
            }
        }
        else if (currentActivity == ActivityType.Engineering)
        {
            EnsureEngineeringWorkspaceVisible();
        }
        else if (activityWorkspaceWindow is { IsLoaded: true })
        {
            activityWorkspaceWindow.Show();
        }
        else
        {
            EnsureJournalWorkspaceVisible(currentActivity);
        }
    }

    private void EnsureTradeWorkspaceVisible() =>
        EnsureJournalWorkspaceVisible(
            ActivityType.Trade);

    private void EnsureEngineeringWorkspaceVisible()
    {
        if (engineeringOverlayWindow is not { IsLoaded: true })
        {
            engineeringOverlayWindow = new EngineeringWindow(this);
            engineeringOverlayWindow.SetTargetWindow(targetWindow);
            engineeringOverlayWindow.SetPlacement(GetEngineeringOverlayPlacement());
            engineeringOverlayWindow.Show();
        }
        else if (!engineeringOverlayWindow.IsVisible)
        {
            engineeringOverlayWindow.Show();
        }
        engineeringOverlayWindow.ApplyInteractionMode(interactionModeEnabled && interactiveModeActive, showCursorWhenInteractive);
    }

    private void EnsureJournalWorkspaceVisible(ActivityType activity)
    {
        if (activityWorkspaceWindow is not { IsLoaded: true })
        {
            activityWorkspaceWindow = new ActivityWorkspaceOverlayWindow(this, activity);
            activityWorkspaceWindow.Closed += (_, _) => activityWorkspaceWindow = null;
            activityWorkspaceWindow.SetTargetWindow(targetWindow);
            activityWorkspaceWindow.SetPlacement(GetEngineeringOverlayPlacement());
            activityWorkspaceWindow.Show();
        }
        else
        {
            activityWorkspaceWindow.SetActivity(activity);
            if (!activityWorkspaceWindow.IsVisible) activityWorkspaceWindow.Show();
        }
        activityWorkspaceWindow.ApplyInteractionMode(interactionModeEnabled && interactiveModeActive, showCursorWhenInteractive);
    }

    private void CloseActivityWorkspace()
    {
        activityWorkspaceWindow?.Close();
        activityWorkspaceWindow = null;
    }

    private void UpdateActivityNavigationUi()
    {
        if (ActivitySelector == null) return;
        updatingActivitySelector = true;
        ActivitySelector.ItemsSource = null;
        ActivitySelector.ItemsSource = ActivityOptions;
        ActivitySelector.SelectedItem = ActivityOptions.First(option => option.Activity == currentActivity);
        updatingActivitySelector = false;
    }

    public void RefreshLocalization()
    {
        UpdateActivityNavigationUi();
        UpdateJournalStatusUi(Services.Journal.JournalMonitorService.Instance.Current);
        UpdateInteractionStatusUi();
        tradeRouteWindow?.RefreshLocalization();
        resultsOverlayWindow?.RefreshLocalization();
        pinnedRouteOverlay?.RefreshLocalization();
        activityWorkspaceWindow?.RefreshLocalization();
        engineeringOverlayWindow?.RefreshLocalization();
        notificationOverlayWindow?.RefreshLocalization();
        shipStatusOverlayWindow?.RefreshLocalization();
    }
}
