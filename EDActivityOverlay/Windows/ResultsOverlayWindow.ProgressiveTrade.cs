using EDActivityOverlay.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EDActivityOverlay.Models.Trading;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows;

public partial class ResultsOverlayWindow
{
    private Grid? progressiveTradeToolbar;
    private TextBlock? progressiveTradeSummary;
    private ComboBox? progressiveTradeSort;

    private List<TradeRoute> progressiveTradeRoutes =
        new();

    private bool progressiveTradeSearching;
    private int progressiveTradeCompleted;
    private int progressiveTradeTotal;
    private int progressiveTradeFailed;
    private string progressiveRenderedFingerprint =
        string.Empty;

    public void DisplayProgressiveTradeRoutes(
        List<TradeRoute> routes,
        bool searching,
        int completed,
        int total,
        int failed)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(
                nameof(ResultsOverlayWindow));
        }

        progressiveTradeRoutes =
            routes.ToList();

        progressiveTradeSearching =
            searching;

        progressiveTradeCompleted =
            completed;

        progressiveTradeTotal =
            total;

        progressiveTradeFailed =
            failed;

        EnsureProgressiveTradeToolbar();
        RenderProgressiveTradeRoutes();
    }

    private void EnsureProgressiveTradeToolbar()
    {
        if (progressiveTradeToolbar is not null)
        {
            return;
        }

        if (ResultsFrame.Parent
            is not Grid rootGrid)
        {
            throw new InvalidOperationException(
                "Results overlay root grid was not found.");
        }

        rootGrid.RowDefinitions.Insert(
            1,
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        Grid.SetRow(
            ResultsFrame,
            2);

        progressiveTradeToolbar =
            new Grid
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        4)
            };

        progressiveTradeToolbar.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        progressiveTradeToolbar.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        progressiveTradeToolbar.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        165)
            });

        progressiveTradeSummary =
            new TextBlock
            {
                FontSize =
                    10,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        progressiveTradeSummary.SetResourceReference(
            TextBlock.ForegroundProperty,
            "SecondaryTextColorBrush");

        Grid.SetColumn(
            progressiveTradeSummary,
            0);

        progressiveTradeToolbar.Children.Add(
            progressiveTradeSummary);

        var sortLabel =
            new TextBlock
            {
                FontSize =
                    9,
                Margin =
                    new Thickness(
                        10,
                        0,
                        6,
                        0),
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        sortLabel.SetResourceReference(
            TextBlock.TextProperty,
            "Loc_TRADE_SORT");

        sortLabel.SetResourceReference(
            TextBlock.ForegroundProperty,
            "MutedTextColorBrush");

        Grid.SetColumn(
            sortLabel,
            1);

        progressiveTradeToolbar.Children.Add(
            sortLabel);

        progressiveTradeSort =
            new ComboBox
            {
                Height =
                    26
            };

        if (TryFindResource(
                "ComboBoxStyle")
            is Style comboStyle)
        {
            progressiveTradeSort.Style =
                comboStyle;
        }

        progressiveTradeSort.Items.Add(
            ResourceComboItem(
                "Loc_TRADE_SORT_PROFIT"));

        progressiveTradeSort.Items.Add(
            ResourceComboItem(
                "Loc_TRADE_SORT_PER_TON"));

        progressiveTradeSort.Items.Add(
            ResourceComboItem(
                "Loc_TRADE_SORT_DISTANCE"));

        progressiveTradeSort.SelectedIndex =
            0;

        progressiveTradeSort.SelectionChanged +=
            ProgressiveTradeSort_SelectionChanged;

        Grid.SetColumn(
            progressiveTradeSort,
            2);

        progressiveTradeToolbar.Children.Add(
            progressiveTradeSort);

        Grid.SetRow(
            progressiveTradeToolbar,
            1);

        rootGrid.Children.Add(
            progressiveTradeToolbar);
    }

    private ComboBoxItem ResourceComboItem(
        string resourceKey)
    {
        var item =
            new ComboBoxItem();

        item.SetResourceReference(
            ContentControl.ContentProperty,
            resourceKey);

        if (TryFindResource(
                "ComboBoxItemStyle")
            is Style itemStyle)
        {
            item.Style =
                itemStyle;
        }

        return
            item;
    }

    private void ProgressiveTradeSort_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (progressiveTradeRoutes.Count > 0)
        {
            RenderProgressiveTradeRoutes();
        }
    }

    private void RenderProgressiveTradeRoutes()
    {
        EnsureProgressiveTradeToolbar();

        IEnumerable<TradeRoute> ordered =
            progressiveTradeSort?.SelectedIndex switch
            {
                1 =>
                    progressiveTradeRoutes
                        .OrderByDescending(
                            route =>
                                route.FirstRoute.ProfitPerUnit)
                        .ThenByDescending(
                            route =>
                                route.TotalProfitPerTrip),

                2 =>
                    progressiveTradeRoutes
                        .OrderBy(
                            route =>
                                route.TotalRouteDistance)
                        .ThenByDescending(
                            route =>
                                route.TotalProfitPerTrip),

                _ =>
                    progressiveTradeRoutes
                        .OrderByDescending(
                            route =>
                                route.TotalProfitPerTrip)
                        .ThenByDescending(
                            route =>
                                route.FirstRoute.ProfitPerUnit)
                        .ThenBy(
                            route =>
                                route.TotalRouteDistance)
            };

        List<TradeRoute> sorted =
            ordered.ToList();

        string fingerprint =
            string.Join(
                "|",
                sorted
                    .Take(
                        6)
                    .Select(
                        route =>
                            $"{route.CardHeader.FromStation.System}/{route.CardHeader.FromStation.Name}"
                            + $">{route.CardHeader.ToStation.System}/{route.CardHeader.ToStation.Name}"
                            + $":{route.FirstRoute.BuyCommodity.Name}"
                            + $":{route.TotalProfitPerTrip}"
                            + $":{route.FirstRoute.ProfitPerUnit}"
                            + $":{route.TotalRouteDistance:F4}"));

        if (!fingerprint.Equals(
                progressiveRenderedFingerprint,
                StringComparison.Ordinal))
        {
            progressiveRenderedFingerprint =
                fingerprint;

            DisplayTradeRoutes(
                sorted);
        }

        int bestProfit =
            progressiveTradeRoutes.Count > 0
                ? progressiveTradeRoutes.Max(
                    route =>
                        route.TotalProfitPerTrip)
                : 0;

        string summary =
            Loc.Format(
                "Loc_TRADE_RESULTS_SUMMARY",
                progressiveTradeRoutes.Count,
                bestProfit);

        if (progressiveTradeSearching
            && progressiveTradeTotal > 0)
        {
            string progress =
                Loc.Format(
                    "Loc_TRADE_RESULTS_SEARCHING",
                    progressiveTradeCompleted,
                    progressiveTradeTotal);

            summary =
                $"{summary}  •  {progress}";
        }

        if (progressiveTradeFailed > 0)
        {
            summary =
                $"{summary}  •  !{progressiveTradeFailed}";
        }

        if (progressiveTradeSummary is not null)
        {
            progressiveTradeSummary.Text =
                summary;
        }
    }
}
