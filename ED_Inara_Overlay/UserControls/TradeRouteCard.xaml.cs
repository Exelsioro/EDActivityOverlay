using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ED_Inara_Overlay.Models.Trading;
using ED_Inara_Overlay.Utils;
using ED_Inara_Overlay.Services;

namespace ED_Inara_Overlay.UserControls
{
    /// <summary>
    /// Trade Route Card UserControl - displays a single trade route with one or two legs
    /// </summary>
    public partial class TradeRouteCard : UserControl
    {
        private TradeRoute? tradeRoute;
        private string chromeStyle = OverlayChromeStyles.Compact;
        
        // Event for when pin route is clicked
        public event EventHandler<TradeRoute>? PinRouteRequested;

        public TradeRoute? TradeRoute
        {
            get { return tradeRoute; }
            set
            {
                tradeRoute = value;
                PopulateContent();
            }
        }

        public TradeRouteCard()
        {
            InitializeComponent();
        }

        public TradeRouteCard(TradeRoute tradeRoute) : this()
        {
            this.TradeRoute = tradeRoute;
        }

        public void SetChromeStyle(string? value)
        {
            chromeStyle = OverlayChromeStyles.Normalize(value);
            bool minimal = chromeStyle == OverlayChromeStyles.Minimal;
            OverlayChromeHelper.Apply(MainBorder, chromeStyle);
            InnerBorder.BorderThickness = minimal ? new Thickness(0) : new Thickness(1);
            InnerBorder.Margin = minimal ? new Thickness(0) : new Thickness(2);
            InnerBorder.Opacity = minimal ? 1 : 0.7;
            HeaderStrip.Background = minimal ? Brushes.Transparent : (Brush)FindResource("HighlightBackgroundBrush");
            FooterStrip.Background = minimal ? Brushes.Transparent : (Brush)FindResource("HighlightBackgroundBrush");
            HeaderStrip.BorderThickness = minimal ? new Thickness(0, 0, 0, 1) : new Thickness(0, 0, 0, 1);
            FooterStrip.BorderThickness = minimal ? new Thickness(0, 1, 0, 0) : new Thickness(0, 1, 0, 0);
            ApplyDynamicChrome(ContentStackPanel);
        }

        public void RefreshLocalization() => PopulateContent();

        private void PopulateContent()
        {
            if (tradeRoute == null)
            {
                ContentStackPanel.Children.Clear();
                return;
            }

            // Clear existing content
            ContentStackPanel.Children.Clear();

            // Update header and footer information
            UpdateHeaderFooter();

            // Set minimum height based on route type (further reduced for more compact layout)
            this.MinHeight = tradeRoute.IsRoundTrip ? 220 : 140;

            // Add first leg
            ContentStackPanel.Children.Add(BuildEliteDangerousLegSection(tradeRoute.FirstRoute, tradeRoute.CardHeader.FromStation, tradeRoute.CardHeader.ToStation, Loc.Get("Loc_Primary_Route"), true));

            // Add round trip leg if exists
            if (tradeRoute.IsRoundTrip && tradeRoute.SecondRoute != null)
            {
                ContentStackPanel.Children.Add(CreateEliteDangerousSpacer());
                ContentStackPanel.Children.Add(BuildEliteDangerousLegSection(tradeRoute.SecondRoute, tradeRoute.CardHeader.ToStation, tradeRoute.CardHeader.FromStation, Loc.Get("Loc_Return_Route"), false));
            }
            ApplyDynamicChrome(ContentStackPanel);
        }

        #region Elite Dangerous Inspired UI Methods

        private void UpdateHeaderFooter()
        {
            if (tradeRoute == null) return;

            // Update route type
            RouteTypeLabel.Text = tradeRoute.IsRoundTrip ? Loc.Get("Loc_Round_Trip_Route") : Loc.Get("Loc_One_Way_Route");

            // Update distance
            DistanceLabel.Text = Loc.Format("Loc_Distance_Ly_Format", tradeRoute.TotalRouteDistance);

            // Update last update
            LastUpdateLabel.Text = string.IsNullOrEmpty(tradeRoute.LastUpdate) ? 
                Loc.Get("Loc_Last_Updated_Unknown") :
                Loc.Format("Loc_Last_Updated_Format", tradeRoute.LastUpdate);

            // Update total profit
            TotalProfitLabel.Text = Loc.Format("Loc_Credits_Format", tradeRoute.TotalProfitPerTrip);
        }

        private UIElement BuildEliteDangerousLegSection(TradeLeg leg, Station fromStation, Station toStation, string routeTitle, bool isPrimary)
        {
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Section header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Route info
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Commodity info

            // Section header
            var sectionHeader = CreateSectionHeader(routeTitle);
            Grid.SetRow(sectionHeader, 0);
            mainGrid.Children.Add(sectionHeader);

            // Route information panel
            var routeInfoPanel = CreateRouteInfoPanel(fromStation, toStation);
            Grid.SetRow(routeInfoPanel, 1);
            mainGrid.Children.Add(routeInfoPanel);

            // Commodity information panel
            var commodityInfoPanel = CreateCommodityInfoPanel(leg);
            Grid.SetRow(commodityInfoPanel, 2);
            mainGrid.Children.Add(commodityInfoPanel);

            return mainGrid;
        }

        private UIElement CreateSectionHeader(string title)
        {
          
            var border = new Border
            {
                Tag = "SectionHeader",
                BorderThickness = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 0, 6)
            };
            border.SetResourceReference(Border.BackgroundProperty, "PrimaryBackgroundColorBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "PrimaryColorBrush");
            var textBlock = new TextBlock
            {
                Text = title,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Segoe UI")
            };
            textBlock.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextColorBrush");

            border.Child = textBlock;
            return border;
        }

        private UIElement CreateRouteInfoPanel(Station fromStation, Station toStation)
        {
            var border = new Border
            {
                Tag = "DetailSurface",
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 6)
            };
            border.SetResourceReference(Border.BackgroundProperty, "SecondaryBackgroundColorBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "PrimaryColorBrush");
            var grid = new Grid();
            // Create columns for origin, arrow, and destination
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Origin
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Arrow
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Destination

            // From station
            var fromPanel = CreateStationPanel(Loc.Get("Loc_Origin"), fromStation, true);
            Grid.SetColumn(fromPanel, 0);
            grid.Children.Add(fromPanel);

            // Arrow separator (horizontal arrow)
            var arrow = new TextBlock
            {
                Text = "→",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 10, 0)
            };
            arrow.SetResourceReference(TextBlock.ForegroundProperty, "AccentColorBrush");

            Grid.SetColumn(arrow, 1);
            grid.Children.Add(arrow);

            // To station  
            var toPanel = CreateStationPanel(Loc.Get("Loc_Destination"), toStation, false);
            Grid.SetColumn(toPanel, 2);
            grid.Children.Add(toPanel);

            border.Child = grid;
            return border;
        }

        private UIElement CreateStationPanel(string label, Station station, bool isOrigin)
        {
            var stackPanel = new StackPanel
            {
                Margin = new Thickness(0, 2, 0, 2)
            };

            // Label
            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Segoe UI")
            };
            labelText.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextColorBrush");

            stackPanel.Children.Add(labelText);

            // Combined station info grid - system name, station name, and distance all on same row
            var infoGrid = new Grid();
            infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Single row for all elements
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // System name
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Station name
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Distance

            // System name (clickable)
            var systemButton = CreateClickableSystemName(station.System);
            Grid.SetColumn(systemButton, 0);
            Grid.SetRow(systemButton, 0);
            infoGrid.Children.Add(systemButton);

            // Station name with "@ " prefix
            var stationText = new TextBlock
            {
                Text = $"{station.Name} \n{station.StationType}",
                FontSize = 10,
                FontFamily = new FontFamily("Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0)
            };
            stationText.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextColorBrush");

            Grid.SetColumn(stationText, 1);
            Grid.SetRow(stationText, 0);
            infoGrid.Children.Add(stationText);

            // Distance from star
            var distanceText = new TextBlock
            {
                Text = Loc.Format("Loc_Distance_Ls_Format", station.DistanceFromStar),
                FontSize = 10,
                FontFamily = new FontFamily("Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(5, 0, 0, 0)
            };
            distanceText.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextColorBrush");

            Grid.SetColumn(distanceText, 2);
            Grid.SetRow(distanceText, 0);
            infoGrid.Children.Add(distanceText);

            stackPanel.Children.Add(infoGrid);

            return stackPanel;
        }

        private UIElement CreateClickableSystemName(string systemName)
        {
            var textBlock = new TextBlock
            {
                Text = systemName,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Segoe UI"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand
            };
            textBlock.SetResourceReference(FrameworkElement.StyleProperty, "ClickableTextStyle");

            // Add click handler for clipboard
            textBlock.MouseLeftButtonUp += (s, e) => CopyToClipboard(systemName);

            return textBlock;
        }

        private UIElement CreateCommodityInfoPanel(TradeLeg leg)
        {
            var border = new Border
            {
                Tag = "DetailSurface",
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 6)
            };
            border.SetResourceReference(Border.BackgroundProperty, "SecondaryBackgroundColorBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "PrimaryColorBrush");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Commodity name
            var commodityPanel = CreateInfoField(Loc.Get("Loc_Commodity"), leg.BuyCommodity.Name, Color.FromRgb(0xFF, 0xFF, 0xFF));
            Grid.SetColumn(commodityPanel, 0);
            grid.Children.Add(commodityPanel);

            // Buy price
            var buyPricePanel = CreateInfoField(Loc.Get("Loc_Buy"), Loc.Format("Loc_Credits_Format", leg.BuyCommodity.Price), Color.FromRgb(0xFF, 0x80, 0x80));
            Grid.SetColumn(buyPricePanel, 1);
            grid.Children.Add(buyPricePanel);

            // Sell price
            var sellPricePanel = CreateInfoField(Loc.Get("Loc_Sell"), Loc.Format("Loc_Credits_Format", leg.SellCommodity.Price), Color.FromRgb(0x80, 0xFF, 0x80));
            Grid.SetColumn(sellPricePanel, 2);
            grid.Children.Add(sellPricePanel);

            // Profit
            var profit = leg.SellCommodity.Price - leg.BuyCommodity.Price;
            var profitPanel = CreateInfoField(Loc.Get("Loc_Profit"), Loc.Format("Loc_Credits_Format", profit), Color.FromRgb(0x00, 0xFF, 0x00));
            Grid.SetColumn(profitPanel, 3);
            grid.Children.Add(profitPanel);

            border.Child = grid;
            return border;
        }

        private void ApplyDynamicChrome(DependencyObject root)
        {
            bool minimal = chromeStyle == OverlayChromeStyles.Minimal;
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);
                if (child is Border border && border.Tag is string role)
                {
                    if (minimal)
                    {
                        border.Background = Brushes.Transparent;
                        border.BorderThickness = role == "SectionHeader"
                            ? new Thickness(0, 0, 0, 1)
                            : new Thickness(0);
                    }
                    else
                    {
                        border.SetResourceReference(Border.BackgroundProperty,
                            role == "SectionHeader" ? "PrimaryBackgroundColorBrush" : "SecondaryBackgroundColorBrush");
                        border.BorderThickness = role == "SectionHeader"
                            ? new Thickness(0, 0, 0, 2)
                            : new Thickness(1);
                    }
                }
                ApplyDynamicChrome(child);
            }
        }

        private UIElement CreateInfoField(string label, string value, Color valueColor)
        {
            var stackPanel = new StackPanel
            {
                Margin = new Thickness(0, 0, 10, 0)
            };

            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Segoe UI")
            };
            labelText.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextColorBrush");

            stackPanel.Children.Add(labelText);

            var valueText = new TextBlock
            {
                Text = value,
                Foreground = new SolidColorBrush(valueColor),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Segoe UI"),
                Margin = new Thickness(0, 2, 0, 0)
            };
            stackPanel.Children.Add(valueText);

            return stackPanel;
        }

        private UIElement CreateEliteDangerousSpacer()
        {

            var border = new Border
            {
                Height = 2,
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0, 0x00, 0xB4, 0xFF), 0),
                        new GradientStop((Color)Application.Current.Resources["PrimaryTextColor"], 0.5),
                        new GradientStop(Color.FromArgb(0, 0x00, 0xB4, 0xFF), 1)
                    }
                },
                Margin = new Thickness(0, 8, 0, 8)
            };

            return border;
        }

        private void CopyToClipboard(string text)
        {
            try
            {
                Clipboard.SetText(text);
                Logger.Logger.LogUserAction($"System name copied to clipboard: {text}");
                
                // Visual feedback - briefly change color
                ShowClipboardFeedback();
            }
            catch (Exception ex)
            {
                Logger.Logger.Error($"Failed to copy to clipboard: {ex.Message}");
            }
        }

        private void ShowClipboardFeedback()
        {
            // Create a brief visual feedback (you could enhance this with animations)
            var originalBrush = MainBorder.BorderBrush;
            MainBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x00));
            
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            timer.Tick += (s, e) => {
                MainBorder.BorderBrush = originalBrush;
                timer.Stop();
            };
            timer.Start();
        }
        
        private void PinRouteButton_Click(object sender, RoutedEventArgs e)
        {
            if (tradeRoute != null)
            {
                Logger.Logger.LogUserAction($"Pin route button clicked for route: {tradeRoute.CardHeader.FromStation.System} -> {tradeRoute.CardHeader.ToStation.System}");
                
                // Raise the event to notify parent windows
                PinRouteRequested?.Invoke(this, tradeRoute);
                
                // Visual feedback
                var button = sender as Button;
                if (button != null)
                {
                    var originalContent = button.Content;
                    button.Content = Loc.Get("Loc_Pinned_Check");
                    button.IsEnabled = false;
                    
                    // Reset after brief delay
                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(1000)
                    };
                    timer.Tick += (s, args) => {
                        button.Content = originalContent;
                        button.IsEnabled = true;
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
        }

        #endregion
    }
}
