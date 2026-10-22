using System.Xml.Linq;
using EDActivityOverlay.Services.Exploration;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class OverlayReadabilityTests
{
    [Theory]
    [InlineData("spacing", "Loc_EXOBIO_SPACING_UNKNOWN_SHORT")]
    [InlineData("history", "Loc_EXOBIO_HISTORY_UNKNOWN_SHORT")]
    [InlineData("position", "Loc_EXOBIO_POSITION_UNKNOWN_SHORT")]
    [InlineData("ready", "Loc_EXOBIO_SPACING_READY_SHORT")]
    [InlineData("move", "Loc_EXOBIO_MOVE_MORE_SHORT_FORMAT")]
    public void ExobioHeadlineExplainsTheActualState(string scenario, string expectedKey)
    {
        SurfaceNavigationResult? nearest = scenario == "position" ? null
            : new SurfaceNavigationResult(scenario == "ready" ? 600 : 100, 0, 0);
        var separation = new OrganicSampleSeparation(nearest, scenario == "history" ? 1 : 2,
            2, scenario == "spacing" ? 0 : 500);
        OrganicSampleGuidancePresentation result = OrganicSampleGuidancePresentation.Create(separation);
        Assert.Equal(expectedKey, result.HeadlineKey);
        Assert.NotEqual(result.HeadlineKey, result.DetailKey);
    }

    [Fact]
    public void ActionUsesThePlannedExitDistanceInsteadOfNearestCircleDeficit()
    {
        var separation = new OrganicSampleSeparation(new(400, 0, 0), 2, 2, 500)
        {
            EscapeDistanceMeters = 300
        };
        OrganicSampleGuidancePresentation result = OrganicSampleGuidancePresentation.Create(separation);
        Assert.Equal(300d, Assert.Single(result.HeadlineArguments));
        Assert.Equal(new object?[] { 400d, 500 }, result.DetailArguments);
    }

    [Fact]
    public void ExobioActionAndSupportingMeasurementsHaveSeparateThemeAwareStyles()
    {
        XDocument view = Read("UserControls", "ExplorationWorkspaceControl.xaml");
        Assert.Equal("{DynamicResource OverlayActionTextStyle}", Named(view, "SurfaceNavigationText").Attribute("Style")?.Value);
        Assert.Equal("{DynamicResource OverlayDetailTextStyle}", Named(view, "SurfaceNavigationDetailText").Attribute("Style")?.Value);
    }

    [Fact]
    public void EngineeringTableInstructionAndHelpOccupySeparateRows()
    {
        XDocument view = Read("UserControls", "EngineeringWorkspaceControl.xaml");
        XElement table = Named(view, "AdviceGrid");
        XElement inspector = Named(view, "AdviceDetailPanel");
        XElement help = Named(view, "AdviceHelpPanel");
        Assert.Same(table.Parent, inspector.Parent);
        Assert.Same(table.Parent, help.Parent);
        Assert.Equal("1", table.Attribute("Grid.Row")?.Value);
        Assert.Equal("2", inspector.Attribute("Grid.Row")?.Value);
        Assert.Equal("3", help.Attribute("Grid.Row")?.Value);
        Assert.Equal(4, table.Parent!.Element(Wpf + "Grid.RowDefinitions")!.Elements().Count());
        Assert.Equal("{Binding SelectedItem, ElementName=AdviceGrid}", inspector.Attribute("DataContext")?.Value);
        Assert.Contains(inspector.Descendants(Wpf + "TextBlock"),
            text => text.Attribute("Text")?.Value == "{Binding Instructions}");
    }

    [Fact]
    public void InspectorDoesNotCompeteForSpaceWithFullMaterialHelp()
    {
        XElement inspector = Named(Read("UserControls", "EngineeringWorkspaceControl.xaml"), "AdviceDetailPanel");
        Assert.Contains(inspector.Descendants(Wpf + "DataTrigger"), trigger =>
            trigger.Attribute("Binding")?.Value == "{Binding Visibility, ElementName=AdviceHelpPanel}"
            && trigger.Attribute("Value")?.Value == "Visible"
            && trigger.Elements(Wpf + "Setter").Any(setter =>
                setter.Attribute("Property")?.Value == "Visibility" && setter.Attribute("Value")?.Value == "Collapsed"));
    }

    [Fact]
    public void NumericCellStyleDoesNotOverrideThemeSelectionForeground()
    {
        XDocument styles = Read("Resources", "UIStyles.xaml");
        XElement style = styles.Descendants(Wpf + "Style").Single(element =>
            element.Attribute(X + "Key")?.Value == "OverlayNumericCellTextStyle");
        Assert.DoesNotContain(style.Elements(Wpf + "Setter"), setter => setter.Attribute("Property")?.Value == "Foreground");
        Assert.Contains(style.Elements(Wpf + "Setter"), setter =>
            setter.Attribute("Property")?.Value == "TextAlignment" && setter.Attribute("Value")?.Value == "Right");
    }

    [Theory]
    [InlineData("DefaultOrangeTheme.xml")]
    [InlineData("DefaultBlueTheme.xml")]
    [InlineData("GreenTheme.xml")]
    [InlineData("RedTheme.xml")]
    public void BuiltInSecondaryTextHasReadableContrastAgainstItsPanelPalette(string themeFile)
    {
        var colors = Read("Themes", themeFile).Descendants("Color")
            .ToDictionary(element => element.Attribute("Key")!.Value, element => element.Attribute("Value")!.Value);
        double foreground = Luminance(colors["SecondaryTextColor"]);
        foreach (string key in new[] { "PrimaryBackgroundColor", "SecondaryBackgroundColor", "HighlightBackgroundColor" })
        {
            double background = Luminance(colors[key]);
            // Palette-only regression check; arbitrary scenery behind transparent overlays is not covered.
            double contrast = (Math.Max(foreground, background) + 0.05) / (Math.Min(foreground, background) + 0.05);
            Assert.True(contrast >= 4.5, $"{themeFile}: secondary text contrast against {key} is {contrast:0.00}.");
        }
    }

    private static double Luminance(string argb)
    {
        string rgb = argb.TrimStart('#')[^6..];
        double Linear(int offset)
        {
            double channel = Convert.ToInt32(rgb.Substring(offset, 2), 16) / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(0) + 0.7152 * Linear(2) + 0.0722 * Linear(4);
    }

    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static XElement Named(XDocument document, string name) => document.Descendants()
        .Single(element => element.Attribute(X + "Name")?.Value == name);

    private static XDocument Read(string directoryName, string fileName)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "EDActivityOverlay", directoryName, fileName);
            if (File.Exists(path)) return XDocument.Load(path);
        }
        throw new FileNotFoundException(fileName);
    }
}
