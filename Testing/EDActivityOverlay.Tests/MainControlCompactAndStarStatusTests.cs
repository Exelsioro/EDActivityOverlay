using System;
using System.IO;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Journal;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class MainControlCompactAndStarStatusTests
{
    [Fact]
    public void JournalTracksCurrentStarClass()
    {
        var reducer =
            new JournalStateReducer();

        reducer.ApplyJournalLine(
            """
            {"event":"FSDJump","StarSystem":"Fuel Star","StarClass":"K","FuelLevel":20}
            """);

        Assert.Equal(
            "K",
            reducer.Current.CurrentStarClass);
    }

    [Fact]
    public void CurrentStarFuelStatusWorksWithoutRoute()
    {
        var state =
            new GameStateSnapshot
            {
                StarSystem =
                    "Fuel Star",
                CurrentStarClass =
                    "G",
                FuelMain =
                    20,
                FuelCapacityMain =
                    32
            };

        ShipStatusPresentation view =
            ShipStatusPresentationBuilder.Build(
                state);

        Assert.Equal(
            "G",
            view.CurrentStarClass);

        Assert.True(
            view.CurrentStarScoopable);
    }

    [Fact]
    public void MainControlHasCompactModeAndNoLocationRow()
    {
        string xaml =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "MainWindow.xaml"));

        Assert.Contains(
            "x:Name=\"CollapsedControlContent\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "CollapseMainOverlayButton_Click",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "ExpandMainOverlayButton_Click",
            xaml,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "LocationStatusText",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainControlUsesFullMonitorCornerAndProcessFocus()
    {
        string code =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "MainWindow.xaml.cs"));

        string compact =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "MainWindow.CompactControl.cs"));

        string windowsApi =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Utils",
                    "WindowsAPI.cs"));

        Assert.Contains(
            "WindowsAPI.IsWindowOwnedByProcess",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "GetMonitorBounds",
            compact,
            StringComparison.Ordinal);

        Assert.Contains(
            "monitorInfo.rcMonitor",
            windowsApi,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BothModeBadgesAreUpdatedTogether()
    {
        string code =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "MainWindow.xaml.cs"));

        Assert.Contains(
            "InteractionStatusBadge.Text =",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "CollapsedInteractionStatusBadge.Text =",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "stateText",
            code,
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
