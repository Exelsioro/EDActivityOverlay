using System.Xml.Linq;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class ThemeStyleTests
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
    public void BuiltInThemeDefinesEveryCoreColor()
    {
        string repository = FindRepositoryRoot();
        XDocument document = XDocument.Load(Path.Combine(
            repository, "EDActivityOverlay", "Themes", "DefaultOrangeTheme.xml"));
        string[] keys = document.Descendants("Color")
            .Select(element => (string?)element.Attribute("Key"))
            .Where(key => key is not null)
            .Cast<string>()
            .ToArray();

        foreach (string required in RequiredThemeColors)
        {
            Assert.Contains(required, keys, StringComparer.Ordinal);
        }
    }

    [Fact]
    public void SettingsTabsUseThemeAwareStyles()
    {
        string repository = FindRepositoryRoot();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument styles = XDocument.Load(Path.Combine(
            repository, "EDActivityOverlay", "Resources", "UIStyles.xaml"));
        string[] expectedStyles = ["EliteTabControlStyle", "EliteTabItemStyle"];

        foreach (string styleKey in expectedStyles)
        {
            XElement style = Assert.Single(
                styles.Descendants(),
                element => (string?)element.Attribute(x + "Key") == styleKey);
            string markup = style.ToString(SaveOptions.DisableFormatting);
            Assert.Contains("DynamicResource", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("StaticResource Primary", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("StaticResource Accent", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("StaticResource Border", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("StaticResource Highlight", markup, StringComparison.Ordinal);
        }

        string settingsMarkup = File.ReadAllText(Path.Combine(
            repository, "EDActivityOverlay", "Windows", "SettingsWindow.xaml"));
        Assert.Contains("Style=\"{DynamicResource EliteTabControlStyle}\"", settingsMarkup, StringComparison.Ordinal);
        Assert.Equal(6, settingsMarkup.Split("Style=\"{DynamicResource EliteTabItemStyle}\"", StringSplitOptions.None).Length - 1);

        string engineeringMarkup = File.ReadAllText(Path.Combine(
            repository, "EDActivityOverlay", "Windows", "EngineeringWindow.xaml"));
        Assert.Contains("Style=\"{DynamicResource EliteTabControlStyle}\"", engineeringMarkup, StringComparison.Ordinal);
        Assert.Equal(5, engineeringMarkup.Split("Style=\"{DynamicResource EliteTabItemStyle}\"", StringSplitOptions.None).Length - 1);

        XElement dataGridRowStyle = Assert.Single(styles.Descendants(),
            element => element.Name.LocalName == "Style"
                && (string?)element.Attribute("TargetType") == "DataGridRow"
                && element.Attribute(x + "Key") is null);
        string dataGridRowMarkup = dataGridRowStyle.ToString(SaveOptions.DisableFormatting);
        Assert.Contains("Property=\"IsMouseOver\"", dataGridRowMarkup, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsSelected\"", dataGridRowMarkup, StringComparison.Ordinal);
        Assert.Contains("DynamicResource ButtonBackgroundColorBrush", dataGridRowMarkup, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsAreGroupedWithoutAStandaloneX52Tab()
    {
        string repository = FindRepositoryRoot();
        XDocument settings = XDocument.Load(Path.Combine(
            repository, "EDActivityOverlay", "Windows", "SettingsWindow.xaml"));
        string markup = settings.ToString(SaveOptions.DisableFormatting);

        Assert.Equal(6, settings.Descendants().Count(element => element.Name.LocalName == "TabItem"));
        Assert.Contains("Loc_CONTROLS", markup, StringComparison.Ordinal);
        Assert.Contains("Loc_EXPERIMENTAL", markup, StringComparison.Ordinal);
        Assert.Contains("Loc_GAME_DATA", markup, StringComparison.Ordinal);
        Assert.Contains("Loc_Exploration", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"{DynamicResource Loc_X52}\"", markup, StringComparison.Ordinal);

        string[] retainedControls =
        [
            "EnableX52SupportCheckBox",
            "X52StatusText",
            "JournalDirectoryTextBox",
            "StorageUsageText",
            "EnableOnlineExplorationDataCheckBox",
            "EnableExperimentalRouteAutomationCheckBox"
        ];

        foreach (string control in retainedControls)
        {
            Assert.Single(settings.Descendants(), element =>
                (string?)element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name") == control);
        }
    }

    [Fact]
    public void AllScrollBarsUseTheRuntimeThemeAwareStyle()
    {
        string repository = FindRepositoryRoot();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument styles = XDocument.Load(Path.Combine(
            repository, "EDActivityOverlay", "Resources", "UIStyles.xaml"));

        XElement customStyle = Assert.Single(
            styles.Descendants(),
            element => (string?)element.Attribute(x + "Key") == "CustomScrollBarStyle");
        string customMarkup = customStyle.ToString(SaveOptions.DisableFormatting);
        Assert.Contains("DynamicResource BorderColorBrush", customMarkup, StringComparison.Ordinal);
        Assert.Contains("DynamicResource AccentColorBrush", customMarkup, StringComparison.Ordinal);
        Assert.Contains("Orientation", customMarkup, StringComparison.Ordinal);
        Assert.Contains("IsMouseOver", customMarkup, StringComparison.Ordinal);
        Assert.Contains("IsDragging", customMarkup, StringComparison.Ordinal);

        XElement implicitStyle = Assert.Single(
            styles.Descendants(),
            element => element.Name.LocalName == "Style"
                && (string?)element.Attribute("TargetType") == "ScrollBar"
                && element.Attribute(x + "Key") is null
                && ((string?)element.Attribute("BasedOn"))?.Contains("CustomScrollBarStyle", StringComparison.Ordinal) == true);
        Assert.NotNull(implicitStyle);

        XElement pageButtonStyle = Assert.Single(styles.Descendants(),
            element => (string?)element.Attribute(x + "Key") == "ScrollPageButtonStyle");
        string pageButtonMarkup = pageButtonStyle.ToString(SaveOptions.DisableFormatting);
        Assert.Contains("Background=\"Transparent\"", pageButtonMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("IsMouseOver", pageButtonMarkup, StringComparison.Ordinal);

        XElement implicitViewerStyle = Assert.Single(styles.Descendants(),
            element => element.Name.LocalName == "Style"
                && (string?)element.Attribute("TargetType") == "ScrollViewer"
                && element.Attribute(x + "Key") is null
                && ((string?)element.Attribute("BasedOn"))?.Contains("CustomScrollViewerStyle", StringComparison.Ordinal) == true);
        Assert.NotNull(implicitViewerStyle);
    }

    [Fact]
    public void AllCheckBoxesUseThemeColorsIncludingHoverAndDisabledStates()
    {
        string repository = FindRepositoryRoot();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument styles = XDocument.Load(Path.Combine(
            repository, "EDActivityOverlay", "Resources", "UIStyles.xaml"));

        XElement customStyle = Assert.Single(styles.Descendants(),
            element => (string?)element.Attribute(x + "Key") == "CheckBoxStyle");
        string markup = customStyle.ToString(SaveOptions.DisableFormatting);
        Assert.Contains("DynamicResource AccentColorBrush", markup, StringComparison.Ordinal);
        Assert.Contains("DynamicResource MutedTextColorBrush", markup, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsMouseOver\"", markup, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsEnabled\"", markup, StringComparison.Ordinal);

        XElement implicitStyle = Assert.Single(styles.Descendants(),
            element => element.Name.LocalName == "Style"
                && (string?)element.Attribute("TargetType") == "CheckBox"
                && element.Attribute(x + "Key") is null
                && ((string?)element.Attribute("BasedOn"))?.Contains("CheckBoxStyle", StringComparison.Ordinal) == true);
        Assert.NotNull(implicitStyle);
    }

    [Fact]
    public void OverlayWindowsUseTheSharedCompactAndMinimalChrome()
    {
        string repository = FindRepositoryRoot();
        string helper = File.ReadAllText(Path.Combine(
            repository, "EDActivityOverlay", "Utils", "OverlayChromeHelper.cs"));
        Assert.Contains("new Thickness(2, 0, 0, 0)", helper, StringComparison.Ordinal);
        Assert.Contains("new Thickness(1)", helper, StringComparison.Ordinal);
        Assert.Contains("PrimaryBackgroundColorBrush", helper, StringComparison.Ordinal);
        Assert.Contains("BorderColorBrush", helper, StringComparison.Ordinal);

        string[] sharedChromeOwners =
        [
            "ActivityWorkspaceOverlayWindow.xaml.cs",
            "EngineeringWindow.xaml.cs",
            "MainWindow.xaml.cs",
            "PinnedRouteOverlay.xaml.cs",
            "ResultsOverlayWindow.xaml.cs",
            "ShipStatusOverlayWindow.xaml.cs",
            "TradeRouteWindow.xaml.cs"
        ];

        foreach (string file in sharedChromeOwners)
        {
            string markup = File.ReadAllText(Path.Combine(repository, "EDActivityOverlay", "Windows", file));
            Assert.Contains("OverlayChromeHelper.Apply", markup, StringComparison.Ordinal);
        }

        string routeCard = File.ReadAllText(Path.Combine(
            repository, "EDActivityOverlay", "UserControls", "TradeRouteCard.xaml.cs"));
        Assert.Contains("OverlayChromeHelper.Apply", routeCard, StringComparison.Ordinal);

        string notifications = File.ReadAllText(Path.Combine(
            repository, "EDActivityOverlay", "Windows", "NotificationOverlayWindow.xaml"));
        Assert.Contains("Binding=\"{Binding ChromeStyle}\" Value=\"Minimal\"", notifications, StringComparison.Ordinal);
        Assert.Contains("BorderThickness\" Value=\"2,0,0,0\"", notifications, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EDActivityOverlay", "EDActivityOverlay.csproj")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
