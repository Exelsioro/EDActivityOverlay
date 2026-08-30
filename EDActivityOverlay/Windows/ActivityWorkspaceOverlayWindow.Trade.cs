using System.Windows;
using System.Windows.Controls;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Trading;
using EDActivityOverlay.UserControls;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows;

public partial class ActivityWorkspaceOverlayWindow
{
    private const double TradeCompactWidth = 420;
    private const double TradeCompactHeight = 305;
    private const double TradeWorkspaceMinWidth = 1040;
    private const double TradeWorkspaceMinHeight = 620;

    private TradeWorkspaceControl? tradeWorkspaceControl;
    private bool tradeExclusiveInteraction;

    private bool IsTradeFullWorkspace =>
        activity == ActivityType.Trade
        && tradeWorkspaceControl?.IsFullMode
           == true;

    private void InitializeTradeWorkspace()
    {
        if (tradeWorkspaceControl is not null)
        {
            return;
        }

        if (CompactPanel.Parent
            is not Grid root)
        {
            throw new InvalidOperationException(
                "Activity workspace root Grid was not found.");
        }

        tradeWorkspaceControl =
            new TradeWorkspaceControl
            {
                Visibility =
                    Visibility.Collapsed
            };

        tradeWorkspaceControl.CloseRequested +=
            CloseTradeWorkspaceRequested;

        tradeWorkspaceControl.DragRequested +=
            DragTradeCompactRequested;

        tradeWorkspaceControl.ViewModeChanged +=
            TradeViewModeChanged;

        tradeWorkspaceControl.PinRequested +=
            PinTradeRouteRequested;

        tradeWorkspaceControl.RoundTripPinRequested +=
            PinRoundTripRouteRequested;

        tradeWorkspaceControl.ReroutePinUpdateRequested +=
            UpdatePinnedTradeRouteRequested;

        tradeWorkspaceControl.UnpinRequested +=
            UnpinTradeRouteRequested;

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

        tradeWorkspaceControl.UpdateJournalState(
            state);

        ApplyTradeWorkspaceMode(
            tradeWorkspaceControl.IsFullMode);
    }

    private void ApplyTradeWorkspaceMode(
        bool full)
    {
        if (full)
        {
            MinWidth =
                TradeWorkspaceMinWidth;

            MinHeight =
                TradeWorkspaceMinHeight;

            Rect tradeWorkArea =
                targetWindow != IntPtr.Zero
                    ? WindowsAPI.GetMonitorWorkArea(
                        targetWindow)
                    : SystemParameters.WorkArea;

            double availableWidth =
                tradeWorkArea.Width;

            double availableHeight =
                tradeWorkArea.Height;

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
                    1180,
                    Math.Max(
                        TradeWorkspaceMinWidth,
                        availableWidth
                        * 0.82));

            Height =
                Math.Min(
                    760,
                    Math.Max(
                        TradeWorkspaceMinHeight,
                        availableHeight
                        * 0.80));

            if (!tradeExclusiveInteraction)
            {
                tradeExclusiveInteraction =
                    true;

                parentWindow?
                    .BeginExclusiveOverlayInteraction();
            }
        }
        else
        {
            EndTradeExclusiveInteraction();

            MinWidth =
                0;

            MinHeight =
                0;

            Width =
                TradeCompactWidth;

            Height =
                TradeCompactHeight;
        }

        PositionOverlay();
    }

    private void TradeViewModeChanged(
        bool full)
    {
        ApplyTradeWorkspaceMode(
            full);

        parentWindow?
            .SetPinnedRouteSuppressedByTradeWorkspace(
                full);
    }

    private void LeaveTradeWorkspace()
    {
        if (tradeWorkspaceControl is null
            || tradeWorkspaceControl.Visibility
               != Visibility.Visible)
        {
            EndTradeExclusiveInteraction();

            return;
        }

        EndTradeExclusiveInteraction();

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

    private void DragTradeCompactRequested()
    {
        if (!interactive
            || IsTradeFullWorkspace)
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

                ApplyChrome();
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void PinTradeRouteRequested(
        TradeRouteCandidate candidate)
    {
        if (parentWindow is null)
        {
            return;
        }

        TradeRouteProgressTracker? tracker =
            parentWindow.OnPinRouteRequested(
                TradeRoutePresentationAdapter.ToPresentation(
                    candidate),
                keepTradeWorkspace:
                    true);

        tradeWorkspaceControl?.ActivatePinnedRoute(
            candidate,
            tracker);
    }
    private void PinRoundTripRouteRequested(
        TradeRoundTripCandidate candidate)
    {
        if (parentWindow is null)
        {
            return;
        }

        TradeRouteProgressTracker? tracker =
            parentWindow.OnPinRouteRequested(
                TradeRoutePresentationAdapter.ToPresentation(
                    candidate),
                keepTradeWorkspace:
                    true);

        tradeWorkspaceControl?.ActivatePinnedRoute(
            candidate,
            tracker);
    }
    private void UpdatePinnedTradeRouteRequested(
        TradeRouteCandidate candidate)
    {
        if (parentWindow is null)
        {
            return;
        }

        TradeRouteProgressTracker? tracker =
            parentWindow.OnPinRouteRequested(
                TradeRoutePresentationAdapter.ToPresentation(
                    candidate),
                keepTradeWorkspace:
                    true,
                preserveExecution:
                    true);

        tradeWorkspaceControl?.AttachExecutionTracker(
            tracker);
    }
    private void UnpinTradeRouteRequested()
    {
        parentWindow?.UnpinRouteOverlay();
    }

    public void ClearActiveTradeRouteFromHost()
    {
        tradeWorkspaceControl?.ClearActiveTradeRouteFromHost();
    }
    private void EndTradeExclusiveInteraction()
    {
        if (!tradeExclusiveInteraction)
        {
            return;
        }

        tradeExclusiveInteraction =
            false;

        parentWindow?
            .EndExclusiveOverlayInteraction();
    }

    private void DisposeTradeWorkspace()
    {
        EndTradeExclusiveInteraction();

        if (tradeWorkspaceControl is null)
        {
            return;
        }

        tradeWorkspaceControl.CloseRequested -=
            CloseTradeWorkspaceRequested;

        tradeWorkspaceControl.DragRequested -=
            DragTradeCompactRequested;

        tradeWorkspaceControl.ViewModeChanged -=
            TradeViewModeChanged;

        tradeWorkspaceControl.PinRequested -=
            PinTradeRouteRequested;

        tradeWorkspaceControl.RoundTripPinRequested -=
            PinRoundTripRouteRequested;

        tradeWorkspaceControl.ReroutePinUpdateRequested -=
            UpdatePinnedTradeRouteRequested;

        tradeWorkspaceControl.UnpinRequested -=
            UnpinTradeRouteRequested;

        tradeWorkspaceControl.Dispose();

        tradeWorkspaceControl =
            null;
    }
}
