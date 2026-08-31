using System.Windows;
using System.Windows.Controls;
using EDActivityOverlay.Models;
using EDActivityOverlay.UserControls;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows;

public partial class ActivityWorkspaceOverlayWindow
{
    private const double MiningCompactWidth = 420;
    private const double MiningCompactHeight = 365;

    private MiningWorkspaceControl? miningWorkspaceControl;

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

        miningWorkspaceControl.CloseRequested += CloseMiningWorkspaceRequested;
        miningWorkspaceControl.DragRequested += DragMiningCompactRequested;
        root.Children.Add(miningWorkspaceControl);
    }

    private void ShowMiningWorkspace(GameStateSnapshot state)
    {
        InitializeMiningWorkspace();
        if (miningWorkspaceControl is null)
        {
            return;
        }

        if (fullExplorationVisible)
        {
            CloseFullExplorationView();
        }

        CompactPanel.Visibility = Visibility.Collapsed;
        FullExplorationPanel.Visibility = Visibility.Collapsed;
        miningWorkspaceControl.Visibility = Visibility.Visible;
        miningWorkspaceControl.UpdateJournalState(state);

        MinWidth = 0;
        MinHeight = 0;
        Width = MiningCompactWidth;
        Height = MiningCompactHeight;
        PositionOverlay();
    }

    private void RefreshMiningWorkspace(GameStateSnapshot state) =>
        ShowMiningWorkspace(state);

    private void LeaveMiningWorkspace()
    {
        if (miningWorkspaceControl is null
            || miningWorkspaceControl.Visibility != Visibility.Visible)
        {
            return;
        }

        miningWorkspaceControl.Visibility = Visibility.Collapsed;
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
        if (!interactive)
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

    private void DisposeMiningWorkspace()
    {
        if (miningWorkspaceControl is null)
        {
            return;
        }

        miningWorkspaceControl.CloseRequested -= CloseMiningWorkspaceRequested;
        miningWorkspaceControl.DragRequested -= DragMiningCompactRequested;
        miningWorkspaceControl.Dispose();
        miningWorkspaceControl = null;
    }
}
