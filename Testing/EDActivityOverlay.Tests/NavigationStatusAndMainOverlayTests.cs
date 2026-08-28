using System;
using System.IO;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Journal;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class NavigationStatusAndMainOverlayTests
{
    [Fact]
    public void JournalTracksCurrentStarClassFromFsdJump()
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
    public void ShipStatusReportsCurrentFuelStarWithoutRoute()
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
    public void ShipStatusFallsBackToCurrentNavRouteStarClass()
    {
        var state =
            new GameStateSnapshot
            {
                StarSystem =
                    "Dry Star",
                FuelMain =
                    20,
                FuelCapacityMain =
                    32,
                NavRoute =
                [
                    new NavRouteStar(
                        "Dry Star",
                        "L"),
                    new NavRouteStar(
                        "Next",
                        "K")
                ]
            };

        ShipStatusPresentation view =
            ShipStatusPresentationBuilder.Build(
                state);

        Assert.Equal(
            "L",
            view.CurrentStarClass);

        Assert.False(
            view.CurrentStarScoopable);
    }

    [Fact]
    public void MainOverlayLifecycleUsesProcessFocusAndCornerPosition()
    {
        string code =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "MainWindow.xaml.cs"));

        string xaml =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "MainWindow.xaml"));

        Assert.Contains(
            "WindowsAPI.IsWindowOwnedByProcess",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "PositionMainOverlayInCorner",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "WindowsAPI.SetTopmost(this, shouldBeTopmost);",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"OverlayFrame\" Margin=\"0\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ShipStatusWidgetHasLocalizedCurrentStarCaption()
    {
        string code =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "ShipStatusOverlayWindow.xaml.cs"));

        string xaml =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "ShipStatusOverlayWindow.xaml"));

        Assert.Contains(
            "Loc_SHIP_STATUS_CURRENT_CAPTION_FORMAT",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"CurrentSystemCaptionText\"",
            xaml,
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
