using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Exploration;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class SpanshRouteFileParserTests
{
    [Fact]
    public void ParsesDirectRoadToRichesApiResponse()
    {
        const string json = """
            {"status":"ok","result":{"systems":[
              {"system":"Sol","bodies":[{"name":"Sol A 1","estimated_scan_value":1000,"estimated_mapping_value":2000}]},
              {"name":"Sirius","bodies":[]}
            ]}}
            """;

        ExplorationRoutePlan route = SpanshRouteFileParser.ParseJson(json);

        Assert.Equal(2, route.Stops.Count);
        Assert.Equal("Sol", route.Stops[0].System);
        Assert.Equal(3000, route.Stops[0].EstimatedValue);
        Assert.Equal("Sirius", route.Stops[1].System);
    }

    [Fact]
    public void ParsesRoadToRichesCsvAndGroupsBodiesBySystem()
    {
        string path = Path.Combine(Path.GetTempPath(), $"spansh-r2r-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path, "System Name,Body Name,Estimated Scan Value,Estimated Mapping Value\nA,A 1,100,200\nA,A 2,300,400\nB,B 1,500,600\n");

            ExplorationRoutePlan route = SpanshRouteFileParser.Parse(path);

            Assert.Equal("RoadToRiches", route.Kind);
            Assert.Equal(2, route.Stops.Count);
            Assert.Equal(2, route.Stops[0].Bodies.Count);
            Assert.Equal(1_000, route.Stops[0].EstimatedValue);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParsesGalaxyRouteJsonFlags()
    {
        string path = Path.Combine(Path.GetTempPath(), $"spansh-route-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{"result":{"system_jumps":[{"system":"Sol","distance":0},{"system":"Jackson's Lighthouse","distance":42,"neutron_star":true,"must_refuel":true}]}}""");

            ExplorationRoutePlan route = SpanshRouteFileParser.Parse(path);

            Assert.Equal("Travel", route.Kind);
            Assert.True(route.Stops[1].Neutron);
            Assert.True(route.Stops[1].Refuel);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
