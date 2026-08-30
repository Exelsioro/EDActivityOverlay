using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeRoundTripUiTests
{
    [Fact]
    public void TradeWorkspaceExposesExplicitOneWayAndRoundTripModes()
    {
        string xaml =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml");

        Assert.Contains(
            "RouteModeComboBox",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "Loc_TRADE_MODE_ONE_WAY",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "Loc_TRADE_MODE_ROUND_TRIP",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTripRowsAndDetailsUseCycleProfitButSingleTradeLegDistance()
    {
        string code =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.RoundTrip.cs");

        Assert.Contains(
            "ProfitPerCycle",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "TradeLegDistanceLy",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "CycleDistanceLy",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTripPinUsesExistingTwoLegPresentationContract()
    {
        string host =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "ActivityWorkspaceOverlayWindow.Trade.cs");

        Assert.Contains(
            "RoundTripPinRequested",
            host,
            StringComparison.Ordinal);

        Assert.Contains(
            "TradeRoutePresentationAdapter.ToPresentation",
            host,
            StringComparison.Ordinal);
    }

    private static string ReadProjectFile(
        params string[] relative)
    {
        for (DirectoryInfo? directory =
                 new(
                     AppContext.BaseDirectory);
             directory is not null;
             directory =
                 directory.Parent)
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
                return
                    File.ReadAllText(
                        candidate);
            }
        }

        throw new FileNotFoundException(
            string.Join(
                Path.DirectorySeparatorChar,
                relative));
    }
}
