using System;
using System.IO;
using System.Linq;
using EDActivityOverlay.Services.Engineering;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class MaterialTraderFinderTests
{
    [Fact]
    public void ArdentNearestResponseIsClassifiedAndOrdered()
    {
        string json =
            """
            [
              {
                "systemName":"Raw System",
                "stationName":"Raw Port",
                "primaryEconomy":"Refinery",
                "secondaryEconomy":"Industrial",
                "distance":12,
                "distanceToArrival":800,
                "maxLandingPadSize":3,
                "updatedAt":"2026-08-28T10:00:00Z"
              },
              {
                "systemName":"Encoded System",
                "stationName":"Encoded Port",
                "primaryEconomy":"HighTech",
                "secondaryEconomy":"",
                "distance":4,
                "distanceToArrival":1500,
                "maxLandingPadSize":3,
                "updatedAt":"2026-08-28T11:00:00Z"
              },
              {
                "systemName":"Manufactured System",
                "stationName":"Industrial Port",
                "primaryEconomy":"Industrial",
                "secondaryEconomy":"",
                "distance":8,
                "distanceToArrival":20,
                "maxLandingPadSize":2,
                "updatedAt":"2026-08-28T12:00:00Z"
              }
            ]
            """;

        MaterialTraderStation[] rows =
            MaterialTraderFinderService.ParseResults(
                    json)
                .ToArray();

        Assert.Equal(
            3,
            rows.Length);

        Assert.Equal(
            MaterialTraderType.Encoded,
            rows[0].Type);

        Assert.Equal(
            MaterialTraderType.Manufactured,
            rows[1].Type);

        Assert.Equal(
            MaterialTraderType.Raw,
            rows[2].Type);
    }

    [Theory]
    [InlineData("Extraction", "", MaterialTraderType.Raw)]
    [InlineData("Refinery", "", MaterialTraderType.Raw)]
    [InlineData("Industrial", "", MaterialTraderType.Manufactured)]
    [InlineData("High Tech", "", MaterialTraderType.Encoded)]
    [InlineData("Military", "", MaterialTraderType.Encoded)]
    public void EconomyMapsToExpectedTraderType(
        string primary,
        string secondary,
        MaterialTraderType expected)
    {
        Assert.Equal(
            expected,
            MaterialTraderFinderService.ClassifyTraderType(
                primary,
                secondary));
    }

    [Fact]
    public void EngineeringWindowContainsMaterialTraderTabAndRouteActions()
    {
        string xaml =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "EngineeringWindow.xaml"));

        string code =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "EngineeringWindow.MaterialTraders.cs"));

        Assert.Contains(
            "Loc_MATERIAL_TRADERS",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "MaterialTraderNeededOnlyCheckBox",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "JournalMonitorService.Instance.Current.StarSystem",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "EliteRouteNavigationService.Instance.PrepareAsync",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "EnableExperimentalRouteAutomation",
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
