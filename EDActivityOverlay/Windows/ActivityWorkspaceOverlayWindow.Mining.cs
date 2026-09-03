using System.Windows;
using System.Windows.Controls;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Navigation;
using EDActivityOverlay.UserControls;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows;

public partial class ActivityWorkspaceOverlayWindow
{
    private const double MiningCompactWidth = 420;
    private const double MiningCompactHeight = 525;
    private const double MiningWorkspaceMinWidth = 940;
    private const double MiningWorkspaceMinHeight = 600;

    private MiningWorkspaceControl? miningWorkspaceControl;
    private MiningAnalyticsWorkspaceControl? miningAnalyticsWorkspaceControl;
    private MiningLocationWorkspaceControl? miningLocationWorkspaceControl;
    private bool miningExclusiveInteraction;

    private bool IsMiningFullWorkspace =>
        activity == ActivityType.Mining
        && (miningAnalyticsWorkspaceControl?.Visibility == Visibility.Visible
            || miningLocationWorkspaceControl?.Visibility == Visibility.Visible);

    private void InitializeMiningWorkspace()
    {
        if (miningWorkspaceControl is not null)
        {
            return;
        }

        if (CompactPanel.Parent is not Grid root)
        {
            throw new InvalidOperationException(
                "Activity workspace root Grid was not found.");
        }

        miningWorkspaceControl = new MiningWorkspaceControl
        {
            Visibility = Visibility.Collapsed
        };
        miningAnalyticsWorkspaceControl = new MiningAnalyticsWorkspaceControl
        {
            Visibility = Visibility.Collapsed
        };
        miningLocationWorkspaceControl = new MiningLocationWorkspaceControl
        {
            Visibility = Visibility.Collapsed
        };

        miningWorkspaceControl.CloseRequested += CloseMiningWorkspaceRequested;
        miningWorkspaceControl.DragRequested += DragMiningCompactRequested;
        miningWorkspaceControl.FullRequested += OpenMiningAnalyticsRequested;
        miningWorkspaceControl.SellCargoRequested += SellMiningCargoRequested;
        miningWorkspaceControl.LocationsRequested += OpenMiningLocationsRequested;
        miningAnalyticsWorkspaceControl.BackRequested += CloseMiningAnalyticsRequested;
        miningAnalyticsWorkspaceControl.CloseRequested += CloseMiningWorkspaceRequested;
        miningLocationWorkspaceControl.BackRequested += CloseMiningLocationsRequested;
        miningLocationWorkspaceControl.CloseRequested += CloseMiningWorkspaceRequested;
        miningLocationWorkspaceControl.NavigateSystemRequested += NavigateMiningLocationRequested;
        root.Children.Add(miningWorkspaceControl);
        root.Children.Add(miningAnalyticsWorkspaceControl);
        root.Children.Add(miningLocationWorkspaceControl);
    }

    private void ShowMiningWorkspace(GameStateSnapshot state)
    {
        InitializeMiningWorkspace();
        if (miningWorkspaceControl is null
            || miningAnalyticsWorkspaceControl is null
            || miningLocationWorkspaceControl is null)
        {
            return;
        }

        if (fullExplorationVisible)
        {
            CloseFullExplorationView();
        }

        CompactPanel.Visibility = Visibility.Collapsed;
        FullExplorationPanel.Visibility = Visibility.Collapsed;
        miningWorkspaceControl.UpdateJournalState(state);
        miningAnalyticsWorkspaceControl.UpdateJournalState(state);
        miningLocationWorkspaceControl.UpdateJournalState(state);

        if (miningAnalyticsWorkspaceControl.Visibility == Visibility.Visible)
        {
            miningWorkspaceControl.Visibility = Visibility.Collapsed;
            miningLocationWorkspaceControl.Visibility = Visibility.Collapsed;
            ApplyMiningWorkspaceMode(full: true);
        }
        else if (miningLocationWorkspaceControl.Visibility == Visibility.Visible)
        {
            miningWorkspaceControl.Visibility = Visibility.Collapsed;
            miningAnalyticsWorkspaceControl.Visibility = Visibility.Collapsed;
            ApplyMiningWorkspaceMode(full: true);
        }
        else
        {
            miningAnalyticsWorkspaceControl.Visibility = Visibility.Collapsed;
            miningLocationWorkspaceControl.Visibility = Visibility.Collapsed;
            miningWorkspaceControl.Visibility = Visibility.Visible;
            ApplyMiningWorkspaceMode(full: false);
        }
    }

    private void RefreshMiningWorkspace(GameStateSnapshot state) =>
        ShowMiningWorkspace(state);

    private void OpenMiningAnalyticsRequested()
    {
        if (miningWorkspaceControl is null
            || miningAnalyticsWorkspaceControl is null
            || miningLocationWorkspaceControl is null)
        {
            return;
        }

        miningWorkspaceControl.Visibility = Visibility.Collapsed;
        miningLocationWorkspaceControl.Visibility = Visibility.Collapsed;
        miningAnalyticsWorkspaceControl.ReloadHistory();
        miningAnalyticsWorkspaceControl.UpdateJournalState(JournalMonitorService.Instance.Current);
        miningAnalyticsWorkspaceControl.Visibility = Visibility.Visible;
        ApplyMiningWorkspaceMode(full: true);
    }

    private void CloseMiningAnalyticsRequested()
    {
        if (miningWorkspaceControl is null || miningAnalyticsWorkspaceControl is null)
        {
            return;
        }

        miningAnalyticsWorkspaceControl.Visibility = Visibility.Collapsed;
        miningWorkspaceControl.Visibility = Visibility.Visible;
        miningWorkspaceControl.UpdateJournalState(JournalMonitorService.Instance.Current);
        ApplyMiningWorkspaceMode(full: false);
    }

    private void OpenMiningLocationsRequested()
    {
        if (miningWorkspaceControl is null
            || miningAnalyticsWorkspaceControl is null
            || miningLocationWorkspaceControl is null)
        {
            return;
        }

        miningWorkspaceControl.Visibility = Visibility.Collapsed;
        miningAnalyticsWorkspaceControl.Visibility = Visibility.Collapsed;
        miningLocationWorkspaceControl.UpdateJournalState(JournalMonitorService.Instance.Current);
        miningLocationWorkspaceControl.Visibility = Visibility.Visible;
        ApplyMiningWorkspaceMode(full: true);
    }

    private void CloseMiningLocationsRequested()
    {
        if (miningWorkspaceControl is null || miningLocationWorkspaceControl is null)
        {
            return;
        }

        miningLocationWorkspaceControl.Visibility = Visibility.Collapsed;
        miningWorkspaceControl.Visibility = Visibility.Visible;
        miningWorkspaceControl.UpdateJournalState(JournalMonitorService.Instance.Current);
        ApplyMiningWorkspaceMode(full: false);
    }

    private async void NavigateMiningLocationRequested(string targetSystem)
    {
        if (string.IsNullOrWhiteSpace(targetSystem) || targetWindow == IntPtr.Zero)
        {
            return;
        }

        bool automatic =
            SettingsService.Instance.Settings.EnableExperimentalRouteAutomation;

        EliteNavigationResult result =
            await EliteRouteNavigationService.Instance.PrepareAsync(
                targetSystem,
                targetWindow,
                automatic);

        if (result.Status == EliteNavigationStatus.Failed)
        {
            Logger.Logger.Warning(
                $"Mining location navigation failed for {targetSystem}: "
                + $"{result.MessageKey} {result.Detail}");
        }
    }

    private async void SellMiningCargoRequested()
    {
        if (parentWindow is null)
        {
            return;
        }

        await parentWindow.OpenTradeCargoSaleFromMiningAsync();
    }

    private void ApplyMiningWorkspaceMode(bool full)
    {
        if (full)
        {
            MinWidth = MiningWorkspaceMinWidth;
            MinHeight = MiningWorkspaceMinHeight;

            Rect workArea = targetWindow != IntPtr.Zero
                ? WindowsAPI.GetMonitorWorkArea(targetWindow)
                : SystemParameters.WorkArea;
            double availableWidth = workArea.Width;
            double availableHeight = workArea.Height;
            if (targetWindow != IntPtr.Zero
                && WindowsAPI.TryGetWindowRectDips(targetWindow, out WindowsAPI.RECT targetRect))
            {
                availableWidth = Math.Min(
                    availableWidth,
                    Math.Max(1, targetRect.Right - targetRect.Left));
                availableHeight = Math.Min(
                    availableHeight,
                    Math.Max(1, targetRect.Bottom - targetRect.Top));
            }

            Width = Math.Min(
                1120,
                Math.Max(MiningWorkspaceMinWidth, availableWidth * 0.82));
            Height = Math.Min(
                720,
                Math.Max(MiningWorkspaceMinHeight, availableHeight * 0.80));

            if (!miningExclusiveInteraction)
            {
                miningExclusiveInteraction = true;
                parentWindow?.BeginExclusiveOverlayInteraction();
            }
        }
        else
        {
            EndMiningExclusiveInteraction();
            MinWidth = 0;
            MinHeight = 0;
            Width = MiningCompactWidth;
            Height = MiningCompactHeight;
        }

        PositionOverlay();
    }

    private void LeaveMiningWorkspace()
    {
        bool visible = miningWorkspaceControl?.Visibility == Visibility.Visible
                       || miningAnalyticsWorkspaceControl?.Visibility == Visibility.Visible
                       || miningLocationWorkspaceControl?.Visibility == Visibility.Visible;
        if (!visible)
        {
            EndMiningExclusiveInteraction();
            return;
        }

        EndMiningExclusiveInteraction();
        if (miningWorkspaceControl is not null)
        {
            miningWorkspaceControl.Visibility = Visibility.Collapsed;
        }
        if (miningAnalyticsWorkspaceControl is not null)
        {
            miningAnalyticsWorkspaceControl.Visibility = Visibility.Collapsed;
        }
        if (miningLocationWorkspaceControl is not null)
        {
            miningLocationWorkspaceControl.Visibility = Visibility.Collapsed;
        }

        MinWidth = 0;
        MinHeight = 0;
        Width = CompactWidth;
        Height = CompactHeight;
        CompactPanel.Visibility = Visibility.Visible;
        PositionOverlay();
    }

    private void CloseMiningWorkspaceRequested() => Close();

    private void DragMiningCompactRequested()
    {
        if (!interactive || IsMiningFullWorkspace)
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
                double availableX = Math.Max(
                    1,
                    rect.Right - rect.Left - ActualWidth);
                double availableY = Math.Max(
                    1,
                    rect.Bottom - rect.Top - ActualHeight);

                manualXRatio = Math.Clamp(
                    (Left - rect.Left) / availableX,
                    0,
                    1);
                manualYRatio = Math.Clamp(
                    (Top - rect.Top) / availableY,
                    0,
                    1);
                hasManualPosition = true;
                ApplyChrome();
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void EndMiningExclusiveInteraction()
    {
        if (!miningExclusiveInteraction)
        {
            return;
        }

        miningExclusiveInteraction = false;
        parentWindow?.EndExclusiveOverlayInteraction();
    }

    private void DisposeMiningWorkspace()
    {
        EndMiningExclusiveInteraction();

        if (miningWorkspaceControl is not null)
        {
            miningWorkspaceControl.CloseRequested -= CloseMiningWorkspaceRequested;
            miningWorkspaceControl.DragRequested -= DragMiningCompactRequested;
            miningWorkspaceControl.FullRequested -= OpenMiningAnalyticsRequested;
            miningWorkspaceControl.SellCargoRequested -= SellMiningCargoRequested;
            miningWorkspaceControl.LocationsRequested -= OpenMiningLocationsRequested;
            miningWorkspaceControl.Dispose();
            miningWorkspaceControl = null;
        }

        if (miningAnalyticsWorkspaceControl is not null)
        {
            miningAnalyticsWorkspaceControl.BackRequested -= CloseMiningAnalyticsRequested;
            miningAnalyticsWorkspaceControl.CloseRequested -= CloseMiningWorkspaceRequested;
            miningAnalyticsWorkspaceControl.Dispose();
            miningAnalyticsWorkspaceControl = null;
        }

        if (miningLocationWorkspaceControl is not null)
        {
            miningLocationWorkspaceControl.BackRequested -= CloseMiningLocationsRequested;
            miningLocationWorkspaceControl.CloseRequested -= CloseMiningWorkspaceRequested;
            miningLocationWorkspaceControl.NavigateSystemRequested -= NavigateMiningLocationRequested;
            miningLocationWorkspaceControl.Dispose();
            miningLocationWorkspaceControl = null;
        }
    }
}
