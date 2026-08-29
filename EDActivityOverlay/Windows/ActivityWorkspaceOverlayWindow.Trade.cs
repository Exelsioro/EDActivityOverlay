using System.Windows;
using System.Windows.Controls;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Trading;
using EDActivityOverlay.UserControls;

namespace EDActivityOverlay.Windows;

public partial class ActivityWorkspaceOverlayWindow
{
    private const double TradeWorkspaceMinWidth = 1040;
    private const double TradeWorkspaceMinHeight = 620;

    private TradeWorkspaceControl? tradeWorkspaceControl;

    private void InitializeTradeWorkspace()
    {
        if (tradeWorkspaceControl is not null)
        {
            return;
        }

        if (CompactPanel.Parent is not Grid root)
        {
            throw new InvalidOperationException(
                "Activity workspace root Grid was not found.");
        }

        tradeWorkspaceControl =
            new TradeWorkspaceControl
            {
                Visibility = Visibility.Collapsed
            };

        tradeWorkspaceControl.CloseRequested +=
            CloseTradeWorkspaceRequested;

        tradeWorkspaceControl.PinRequested +=
            PinTradeRouteRequested;

        root.Children.Add(
            tradeWorkspaceControl);
    }

    private void ShowTradeWorkspace(
        GameStateSnapshot state)
    {
        if (tradeWorkspaceControl is null)
        {
            InitializeTradeWorkspace();
        }

        if (tradeWorkspaceControl is null)
        {
            return;
        }

        if (fullExplorationVisible)
        {
            CloseFullExplorationView();
        }

        CompactPanel.Visibility =
            Visibility.Collapsed;

        FullExplorationPanel.Visibility =
            Visibility.Collapsed;

        tradeWorkspaceControl.Visibility =
            Visibility.Visible;

        MinWidth =
            TradeWorkspaceMinWidth;

        MinHeight =
            TradeWorkspaceMinHeight;

        Width =
            Math.Min(
                1180,
                Math.Max(
                    TradeWorkspaceMinWidth,
                    SystemParameters.WorkArea.Width
                    * 0.82));

        Height =
            Math.Min(
                760,
                Math.Max(
                    TradeWorkspaceMinHeight,
                    SystemParameters.WorkArea.Height
                    * 0.80));

        tradeWorkspaceControl.UpdateJournalState(
            state);

        PositionOverlay();
    }

    private void LeaveTradeWorkspace()
    {
        if (tradeWorkspaceControl is null
            || tradeWorkspaceControl.Visibility
               != Visibility.Visible)
        {
            return;
        }

        tradeWorkspaceControl.Visibility =
            Visibility.Collapsed;

        MinWidth =
            0;

        MinHeight =
            0;

        Width =
            CompactWidth;

        Height =
            CompactHeight;

        CompactPanel.Visibility =
            Visibility.Visible;

        PositionOverlay();
    }

    private void RefreshTradeWorkspace(
        GameStateSnapshot state)
    {
        ShowTradeWorkspace(
            state);
    }

    private void CloseTradeWorkspaceRequested()
    {
        Close();
    }

    private void PinTradeRouteRequested(
        TradeRouteCandidate candidate)
    {
        if (parentWindow is null)
        {
            return;
        }

        parentWindow.OnPinRouteRequested(
            TradeRoutePresentationAdapter.ToPresentation(
                candidate));
    }

    private void DisposeTradeWorkspace()
    {
        if (tradeWorkspaceControl is null)
        {
            return;
        }

        tradeWorkspaceControl.CloseRequested -=
            CloseTradeWorkspaceRequested;

        tradeWorkspaceControl.PinRequested -=
            PinTradeRouteRequested;

        tradeWorkspaceControl.Dispose();
        tradeWorkspaceControl =
            null;
    }
}
