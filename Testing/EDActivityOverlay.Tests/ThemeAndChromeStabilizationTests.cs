using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class ThemeAndChromeStabilizationTests
{
    private static readonly string[] RequiredThemeColors =
    [
        "PrimaryBackgroundColor",
        "SecondaryBackgroundColor",
        "HighlightBackgroundColor",
        "BorderColor",
        "ButtonBackgroundColor",
        "PrimaryColor",
        "AccentColor",
        "SuccessColor",
        "FailureColor",
        "SecondaryTextColor",
        "MutedTextColor",
        "PrimaryTextColor"
    ];

    [Fact]
    public void EveryBuiltInThemeDefinesEveryCoreColorExactlyOnce()
    {
        string themeDirectory =
            Path.Combine(
                FindRepositoryRoot(),
                "EDActivityOverlay",
                "Themes");

        string[] themes =
            Directory.GetFiles(
                themeDirectory,
                "*.xml");

        Assert.Equal(
            4,
            themes.Length);

        foreach (string theme in themes)
        {
            XDocument document =
                XDocument.Load(
                    theme);

            var colors =
                document
                    .Descendants("Color")
                    .Select(element =>
                        new
                        {
                            Key =
                                (string?)element.Attribute(
                                    "Key"),
                            Value =
                                (string?)element.Attribute(
                                    "Value")
                        })
                    .Where(item =>
                        item.Key is not null)
                    .ToArray();

            Assert.Equal(
                colors.Length,
                colors
                    .Select(item => item.Key)
                    .Distinct(StringComparer.Ordinal)
                    .Count());

            foreach (string required
                     in RequiredThemeColors)
            {
                var entry =
                    Assert.Single(
                        colors,
                        item =>
                            item.Key == required);

                Assert.Matches(
                    "^#[0-9A-Fa-f]{8}$",
                    entry.Value
                    ?? string.Empty);
            }
        }
    }

    [Fact]
    public void TradeCompactSurfaceParticipatesInCompactAndMinimalChrome()
    {
        string repository =
            FindRepositoryRoot();

        string control =
            File.ReadAllText(
                Path.Combine(
                    repository,
                    "EDActivityOverlay",
                    "UserControls",
                    "TradeWorkspaceControl.xaml.cs"));

        string host =
            File.ReadAllText(
                Path.Combine(
                    repository,
                    "EDActivityOverlay",
                    "Windows",
                    "ActivityWorkspaceOverlayWindow.xaml.cs"));

        Assert.Contains(
            "OverlayChromeHelper.Apply(",
            control,
            StringComparison.Ordinal);

        Assert.Contains(
            "CompactTradePanel",
            control,
            StringComparison.Ordinal);

        Assert.Contains(
            "tradeWorkspaceControl?.SetChromeStyle(",
            host,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TradeWorkspaceUsesThemeResourcesInsteadOfLiteralPanelColors()
    {
        string xaml =
            File.ReadAllText(
                Path.Combine(
                    FindRepositoryRoot(),
                    "EDActivityOverlay",
                    "UserControls",
                    "TradeWorkspaceControl.xaml"));

        var hardCodedColor =
            new Regex(
                @"(?:Foreground|Background|BorderBrush)\s*=\s*""#[0-9A-Fa-f]{3,8}""",
                RegexOptions.CultureInvariant);

        Assert.False(
            hardCodedColor.IsMatch(
                xaml),
            "Trade workspace contains a hard-coded panel color.");

        Assert.DoesNotContain(
            "Foreground=\"White\"",
            xaml,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "Foreground=\"Black\"",
            xaml,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "DynamicResource PrimaryBackgroundColorBrush",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "DynamicResource PrimaryTextColorBrush",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "DynamicResource AccentColorBrush",
            xaml,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory =
                 new(
                     AppContext.BaseDirectory);
             directory is not null;
             directory =
                 directory.Parent)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "EDActivityOverlay",
                        "EDActivityOverlay.csproj")))
            {
                return
                    directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Repository root was not found.");
    }
}
