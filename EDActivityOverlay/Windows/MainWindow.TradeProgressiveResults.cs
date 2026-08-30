using System.Collections.Generic;
using EDActivityOverlay.Models.Trading;
using EDActivityOverlay.Windows;

namespace EDActivityOverlay;

public partial class MainWindow
{
    public void ShowProgressiveTradeResults(
        List<TradeRoute> tradeRoutes,
        bool searching,
        int completed,
        int total,
        int failed)
    {
        bool needNewInstance =
            resultsOverlayWindow is null
            || !resultsOverlayWindow.IsLoaded
            || resultsOverlayWindow.Tag?.ToString()
               == "disposed";

        if (needNewInstance)
        {
            Logger.Logger.Info(
                "Creating progressive ResultsOverlayWindow instance");

            resultsOverlayWindow =
                new ResultsOverlayWindow(
                    this);
        }

        if (resultsOverlayWindow is null)
        {
            return;
        }

        resultsOverlayWindow.SetTargetWindow(
            targetWindow,
            targetProcessId);

        resultsOverlayWindow.ApplyInteractionMode(
            interactionModeEnabled
            && interactiveModeActive,
            showCursorWhenInteractive);

        resultsOverlayWindow.DisplayProgressiveTradeRoutes(
            tradeRoutes,
            searching,
            completed,
            total,
            failed);

        if (!resultsOverlayWindow.IsVisible)
        {
            resultsOverlayWindow.Show();
        }

        isResultsActive =
            true;
    }
}
