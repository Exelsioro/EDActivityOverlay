using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeTravelTimeJournalContractTests
{
    [Fact]
    public void GameStateStoresLoadoutUnladenMass()
    {
        string model =
            ReadProjectFile(
                "EDActivityOverlay",
                "Models",
                "GameStateSnapshot.TradeTravel.cs");

        Assert.Contains(
            "UnladenMassTonnes",
            model,
            StringComparison.Ordinal);

        string reducer =
            ReadProjectFile(
                "EDActivityOverlay",
                "Services",
                "Journal",
                "JournalStateReducer.cs");

        Assert.Contains(
            "\"UnladenMass\"",
            reducer,
            StringComparison.Ordinal);

        Assert.Contains(
            "UnladenMassTonnes",
            reducer,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EstimatorUsesEliteMaxJumpRangeInsteadOfAnFsdCatalogue()
    {
        string estimator =
            ReadProjectFile(
                "EDActivityOverlay",
                "Services",
                "Trading",
                "TradeTravelTimeEstimator.cs");

        Assert.Contains(
            "ship.MaxJumpRangeLy",
            estimator,
            StringComparison.Ordinal);

        Assert.Contains(
            "ship.UnladenMassTonnes",
            estimator,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "FrameShiftDriveCatalogue",
            estimator,
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
