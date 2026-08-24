using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EDActivityOverlay.Utils;

public static class OverlayChromeStyles
{
    public const string Compact = "Compact";
    public const string Minimal = "Minimal";

    public static string Normalize(string? value) => value switch
    {
        Minimal => Minimal,
        _ => Compact
    };
}

public static class OverlayChromeHelper
{
    public static void Apply(
        Border content,
        string? style)
    {
        string normalizedStyle = OverlayChromeStyles.Normalize(style);
        if (normalizedStyle == OverlayChromeStyles.Minimal)
        {
            content.Background = Brushes.Transparent;
        }
        else
        {
            content.SetResourceReference(Border.BackgroundProperty, "PrimaryBackgroundColorBrush");
        }
        content.SetResourceReference(Border.BorderBrushProperty, "BorderColorBrush");
        content.BorderThickness = normalizedStyle == OverlayChromeStyles.Minimal
            ? new Thickness(2, 0, 0, 0)
            : new Thickness(1);
    }
}
