using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class SharedRendererSurfaceTests
{
    [Fact]
    public void IndividualAndCompositeUseTheSameMainSurfaceClass()
    {
        string mainXaml = ReadProjectFile(
            "EDActivityOverlay",
            "Windows",
            "MainWindow.xaml");
        string mainCode = ReadProjectFile(
            "EDActivityOverlay",
            "Windows",
            "MainWindow.xaml.cs");
        string compositeCode = ReadProjectFile(
            "EDActivityOverlay",
            "Windows",
            "CompositeOverlayWindow.xaml.cs");

        Assert.Contains(
            "x:Name=\"MainPanelHost\"",
            mainXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "new MainOverlayPanelControl(",
            mainCode,
            StringComparison.Ordinal);
        Assert.Contains(
            "new MainOverlayPanelControl(controller)",
            compositeCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "x:Name=\"ActivitySelector\"",
            mainXaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainAndX52ShareOneActivityCatalog()
    {
        string navigation = ReadProjectFile(
            "EDActivityOverlay",
            "Windows",
            "MainWindow.ActivityNavigation.cs");
        string panel = ReadProjectFile(
            "EDActivityOverlay",
            "UserControls",
            "MainOverlayPanelControl.xaml.cs");

        Assert.Contains(
            "ActivityUiCatalog.All",
            navigation,
            StringComparison.Ordinal);
        Assert.Contains(
            "ActivityUiCatalog.All",
            panel,
            StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] relative)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string candidate = Path.Combine(
                [
                    directory.FullName,
                    .. relative
                ]);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new FileNotFoundException(
            string.Join(Path.DirectorySeparatorChar, relative));
    }
}