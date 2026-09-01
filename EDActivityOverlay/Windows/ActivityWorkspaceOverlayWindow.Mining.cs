using System.Windows;
using System.Windows.Controls;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.UserControls;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows;

public partial class ActivityWorkspaceOverlayWindow
{
    private const double MiningCompactWidth = 420;
    private const double MiningCompactHeight = 500;
    private const double MiningWorkspaceMinWidth = 940;
    private const double MiningWorkspaceMinHeight = 600;

    private MiningWorkspaceControl? miningWorkspaceControl;
    private MiningAnalyticsWorkspaceControl? miningAnalyticsWorkspaceControl;
    private bool miningExclusiveInteraction;

    private bool IsMiningFullWorkspace =>
        activity == ActivityType.Mining
        && miningAnalyticsWorkspaceControl?.Visibility == Visibility.Visible;

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

        miningWorkspaceControl.CloseRequested += CloseMiningWorkspaceRequested;
        miningWorkspaceControl.DragRequested += DragMiningCompactRequested;
        miningWorkspaceControl.FullRequested += OpenMiningAnalyticsRequested;
        miningAnalyticsWorkspaceControl.BackRequested += CloseMiningAnalyticsRequested;
        miningAnalyticsWorkspaceControl.CloseRequested += CloseMiningWorkspaceRequested;
        root.Children.Add(miningWorkspaceControl);
        root.Children.Add(miningAnalyticsWorkspaceControl);
    }

    private void ShowMiningWorkspace(GameStateSnapshot state)
    {
        InitializeMiningWorkspace();
        if (miningWorkspaceControl is null || miningAnalyticsWorkspaceControl is null)
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

        if (IsMiningFullWorkspace)
        {
            miningWorkspaceControl.Visibility = Visibility.Collapsed;
            miningAnalyticsWorkspaceControl.Visibility = Visibility.Visible;
            ApplyMiningWorkspaceMode(full: true);
        }
        else
        {
            miningAnalyticsWorkspaceControl.Visibility = Visibility.Collapsed;
            miningWorkspaceControl.Visibility = Visibility.Visible;
            ApplyMiningWorkspaceMode(full: false);
        }
    }

    private void RefreshMiningWorkspace(GameStateSnapshot state) =>
        ShowMiningWorkspace(state);

    private void OpenMiningAnalyticsRequested()
    {
        if (miningWorkspaceControl is null || miningAnalyticsWorkspaceControl is null)
        {
            return;
        }

        miningWorkspaceControl.Visibility = Visibility.Collapsed;
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
                       || miningAnalyticsWorkspaceControl?.Visibility == Visibility.Visible;
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
    }
}
