using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class MainControlCompactPersistenceHotfixTests
{
    [Fact]
    public void CompactControlUsesValidThicknessConstructors()
    {
        string code =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "MainWindow.CompactControl.cs"));

        Assert.DoesNotContain(
            "new Thickness(6, 4)",
            code,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "new Thickness(10, 7)",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "SettingsService.Instance.SetMainOverlayCollapsed",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CompactStateIsPersistedAndRestored()
    {
        string settings =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Services",
                    "SettingsService.cs"));

        string main =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "MainWindow.xaml.cs"));

        Assert.Contains(
            "public bool MainOverlayCollapsed",
            settings,
            StringComparison.Ordinal);

        Assert.Contains(
            "SetMainOverlayCollapsed",
            settings,
            StringComparison.Ordinal);

        Assert.Contains(
            "RestoreMainOverlayCollapsedState();",
            main,
            StringComparison.Ordinal);
    }

    private static string FindProjectFile(
        params string[] relative)
    {
        for (
            DirectoryInfo? directory =
                new(
                    AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            string candidate =
                Path.Combine(
                    [
                        directory.FullName,
                        .. relative
                    ]);

            if (File.Exists(
                    candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            string.Join(
                Path.DirectorySeparatorChar,
                relative));
    }
}
