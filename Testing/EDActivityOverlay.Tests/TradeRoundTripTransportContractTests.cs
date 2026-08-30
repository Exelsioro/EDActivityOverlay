using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeRoundTripTransportContractTests
{
    [Fact]
    public void RoundTripUsesDirectionalSystemSidesInsteadOfGenericSystemCommodities()
    {
        string service = ReadProjectFile(
            "EDActivityOverlay",
            "Services",
            "Trading",
            "TradeRoundTripSearchService.cs");

        Assert.Contains("ITradeSystemTradeSidesProvider", service, StringComparison.Ordinal);
        Assert.Contains("GetSystemExportsAsync", service, StringComparison.Ordinal);
        Assert.Contains("GetSystemImportsAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ITradeOriginMarketProvider bulkProvider", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GetSystemOrdersAsync(", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ArdentClientAllowsQuotedNumericValues()
    {
        string client = ReadProjectFile(
            "EDActivityOverlay",
            "Services",
            "Ardent",
            "ArdentApiClient.cs");

        Assert.Contains("JsonNumberHandling.AllowReadingFromString", client, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] relative)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string candidate = Path.Combine([directory.FullName, .. relative]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new FileNotFoundException(
            string.Join(Path.DirectorySeparatorChar, relative));
    }
}
