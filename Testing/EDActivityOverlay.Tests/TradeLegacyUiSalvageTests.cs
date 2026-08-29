using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeLegacyUiSalvageTests
{
    [Fact]
    public void StructuralSalvageIsInstalled()
    {
        string xaml =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "TradeRouteWindow.xaml"));

        string partial =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "TradeRouteWindow.LegacySalvage.cs"));

        Assert.Contains("Width=\"560\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Loaded=\"TradeRouteWindow_SalvageLoaded\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TradeSearchSessionState", partial, StringComparison.Ordinal);
        Assert.Contains("CaptureTradeSearchSession", partial, StringComparison.Ordinal);
        Assert.Contains("CountActiveTradeAdvancedFilters", partial, StringComparison.Ordinal);
    }

    private static string FindProjectFile(params string[] relative)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string candidate =
                Path.Combine(
                    [
                        directory.FullName,
                        .. relative
                    ]);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            string.Join(Path.DirectorySeparatorChar, relative));
    }
}
