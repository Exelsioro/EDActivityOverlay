using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EDActivityOverlay.Models;
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
    public async Task MissingTypesFallBackToNearbySystems()
    {
        using var handler =
            new StubHandler();

        using var client =
            new HttpClient(
                handler)
            {
                BaseAddress =
                    new Uri(
                        "https://api.ardent-insight.com/")
            };

        var service =
            new MaterialTraderFinderService(
                client);

        IReadOnlyList<MaterialTraderStation> result =
            await service.FindNearestAsync(
                "Origin",
                null,
                CancellationToken.None);

        Assert.Equal(
            3,
            result.Count);

        Assert.Contains(
            result,
            row =>
                row.Type
                == MaterialTraderType.Manufactured
                && row.SystemName
                   == "Nearest Industrial");

        Assert.Contains(
            result,
            row =>
                row.Type
                == MaterialTraderType.Encoded
                && row.SystemName
                   == "Encoded Nearby");

        Assert.Contains(
            result,
            row =>
                row.Type
                == MaterialTraderType.Raw
                && row.SystemName
                   == "Raw Nearby");

        Assert.Contains(
            handler.Requests,
            request =>
                request.Contains(
                    "/nearby?maxDistance=25",
                    StringComparison.Ordinal));

        Assert.Contains(
            handler.Requests,
            request =>
                request.Contains(
                    "/system/address/2/stations",
                    StringComparison.Ordinal));

        Assert.Contains(
            handler.Requests,
            request =>
                request.Contains(
                    "/system/address/3/stations",
                    StringComparison.Ordinal));
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

        string engineering =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "EngineeringWindow.xaml.cs"));

        string main =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "MainWindow.xaml.cs"));

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
            "PrepareEngineeringNavigationHandoff",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "EnableExperimentalRouteAutomation",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "EliteRouteNavigationService.Instance.PrepareAsync",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "PrepareEngineeringNavigationHandoff",
            engineering,
            StringComparison.Ordinal);

        Assert.Contains(
            "ReturnControlToGameForNavigation",
            main,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "ApplyInteractionMode(\n                    canInteract: true",
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

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } =
            new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path =
                request.RequestUri?.PathAndQuery
                ?? string.Empty;

            Requests.Add(
                path);

            string json =
                path switch
                {
                    "/v2/system/name/Origin/nearest/material-trader?minLandingPadSize=1" =>
                        """
                        [
                          {
                            "systemName":"Nearest Industrial",
                            "stationName":"Industrial Port",
                            "primaryEconomy":"Industrial",
                            "secondaryEconomy":"",
                            "distance":3,
                            "distanceToArrival":50,
                            "maxLandingPadSize":3
                          }
                        ]
                        """,

                    "/v2/system/name/Origin" =>
                        """
                        {
                          "systemAddress":1,
                          "systemName":"Origin",
                          "systemX":0,
                          "systemY":0,
                          "systemZ":0
                        }
                        """,

                    "/v2/system/name/Origin/nearby?maxDistance=25" =>
                        """
                        [
                          {
                            "systemAddress":2,
                            "systemName":"Encoded Nearby",
                            "systemX":5,
                            "systemY":0,
                            "systemZ":0,
                            "distance":5
                          },
                          {
                            "systemAddress":3,
                            "systemName":"Raw Nearby",
                            "systemX":7,
                            "systemY":0,
                            "systemZ":0,
                            "distance":7
                          }
                        ]
                        """,

                    "/v2/system/address/2/stations" =>
                        """
                        [
                          {
                            "stationName":"Encoded Port",
                            "primaryEconomy":"High Tech",
                            "secondaryEconomy":"",
                            "materialTrader":1,
                            "maxLandingPadSize":3,
                            "distanceToArrival":250
                          }
                        ]
                        """,

                    "/v2/system/address/3/stations" =>
                        """
                        [
                          {
                            "stationName":"Raw Port",
                            "primaryEconomy":"Refinery",
                            "secondaryEconomy":"",
                            "materialTrader":true,
                            "maxLandingPadSize":2,
                            "distanceToArrival":100
                          }
                        ]
                        """,

                    _ =>
                        "[]"
                };

            return
                Task.FromResult(
                    new HttpResponseMessage(
                        HttpStatusCode.OK)
                    {
                        Content =
                            new StringContent(
                                json,
                                Encoding.UTF8,
                                "application/json")
                    });
        }
    }
}
