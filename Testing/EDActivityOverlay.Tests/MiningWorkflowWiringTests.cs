using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class MiningWorkflowWiringTests
{
    [Fact]
    public void MiningCanHandCurrentCargoToTradeCargoSale()
    {
        string mining =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "MiningWorkspaceControl.xaml.cs");
        string host =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "ActivityWorkspaceOverlayWindow.Mining.cs");
        string navigation =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "MainWindow.ActivityNavigation.cs");
        string bridge =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.MiningBridge.cs");

        Assert.Contains(
            "SellCargoRequested",
            mining,
            StringComparison.Ordinal);
        Assert.Contains(
            "OpenTradeCargoSaleFromMiningAsync",
            host,
            StringComparison.Ordinal);
        Assert.Contains(
            "ActivityType.Trade",
            navigation,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"cargo\"",
            bridge,
            StringComparison.Ordinal);
        Assert.Contains(
            "StartOrCancelSearchAsync",
            bridge,
            StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        string? root = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(root))
        {
            string candidate =
                Path.Combine(
                    new[] { root }
                        .Concat(parts)
                        .ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            root =
                Directory.GetParent(root)?.FullName;
        }

        throw new FileNotFoundException(
            string.Join(
                Path.DirectorySeparatorChar,
                parts));
    }
}
