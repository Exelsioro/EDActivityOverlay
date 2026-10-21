using System.Net;
using System.Text;
using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Exploration;
using EDActivityOverlay.Services.Journal;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class ExplorationMissingDataRegressionTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("Not terraformable", false)]
    [InlineData("Non-terraformable", false)]
    [InlineData("NotTerraformable", false)]
    [InlineData("Unknown terraforming state", false)]
    [InlineData("Terraformable", true)]
    [InlineData("Terraforming", true)]
    [InlineData("Terraformed", true)]
    [InlineData("Candidate for terraforming", true)]
    [InlineData("  terraformable  ", true)]
    public void TerraformingRequiresAnExplicitPositiveState(string? value, bool expected) =>
        Assert.Equal(expected, TerraformingStatus.IsPositive(value));

    [Theory]
    [InlineData(false, "missing")]
    [InlineData(false, "null")]
    [InlineData(false, "zero")]
    [InlineData(false, "positive")]
    [InlineData(false, "negative")]
    [InlineData(true, "missing")]
    [InlineData(true, "null")]
    [InlineData(true, "zero")]
    [InlineData(true, "positive")]
    [InlineData(true, "negative")]
    public async Task ProvidersDistinguishMissingPhysicsFromMeasuredVacuum(bool edsm, string scenario)
    {
        string fields = scenario switch
        {
            "null" => "\"gravity\":null,\"surface_temperature\":null,\"surfaceTemperature\":null,\"surface_pressure\":null,\"surfacePressure\":null",
            "zero" => "\"surface_pressure\":0,\"surfacePressure\":0",
            "positive" => "\"gravity\":1.2,\"surface_temperature\":250,\"surfaceTemperature\":250,\"surface_pressure\":0.5,\"surfacePressure\":0.5",
            "negative" => "\"gravity\":-1,\"surface_temperature\":-5,\"surfaceTemperature\":-5,\"surface_pressure\":-1,\"surfacePressure\":-1",
            _ => "\"unused\":null"
        };
        ExternalExplorationBodySnapshot body = await ReadBody(edsm, "Not terraformable", fields);
        ExplorationCatalogBody row = Catalog(body);
        ExplorationPhysicalMeasurements actual = ExplorationPhysicalMeasurements.From(row);

        Assert.False(row.Terraformable);
        Assert.False(row.Highlights.HasFlag(ExplorationBodyHighlights.Terraformable));
        Assert.Equal(scenario is "zero" or "positive", body.SurfacePressureKnown);
        Assert.Equal(scenario == "positive" ? 1.2 : (double?)null, actual.GravityG);
        Assert.Equal(scenario == "positive" ? 250 : (double?)null, actual.TemperatureKelvin);
        Assert.Equal(scenario == "positive" ? 0.5 : scenario == "zero" ? 0 : (double?)null, actual.PressureAtmospheres);
    }

    [Theory]
    [InlineData(false, "Not terraformable", false)]
    [InlineData(true, "Not terraformable", false)]
    [InlineData(false, "Terraformable", true)]
    [InlineData(true, "Candidate for terraforming", true)]
    public async Task ProviderValueEstimatesUseTheSameTerraformingDecisionAsTheCatalog(bool edsm, string state, bool positive)
    {
        ExternalExplorationBodySnapshot body = await ReadBody(edsm, state, "\"earth_masses\":1,\"earthMasses\":1");
        var expected = ExplorationValueCalculator.Estimate("Planet", "High metal content world", positive, 1, 0);
        Assert.Equal(expected.FirstDiscoveryScanValue, body.EstimatedScanValue);
        Assert.Equal(expected.FirstDiscoveredAndMappedEfficientValue, body.EstimatedMappingValue);
        Assert.Equal(positive, Catalog(body).Terraformable);
    }

    [Fact]
    public void PartialJournalScanRetainsMeasuredVacuumAndExternalPressureCannotOverwriteIt()
    {
        var reducer = new JournalStateReducer();
        reducer.ApplyJournalLine("""{"event":"Location","StarSystem":"Test","SystemAddress":123}""");
        reducer.ApplyJournalLine("""{"event":"Scan","BodyID":7,"BodyName":"Test 7","PlanetClass":"Rocky body","SurfaceGravity":9.80665,"SurfaceTemperature":250,"SurfacePressure":0}""");
        reducer.ApplyJournalLine("""{"event":"Scan","BodyID":7,"BodyName":"Test 7","PlanetClass":"Rocky body","SurfacePressure":null}""");
        ExplorationBodySnapshot local = Assert.Single(reducer.Current.ExplorationBodies);
        Assert.True(local.SurfacePressureKnown);
        var row = Catalog(External() with { SurfacePressureAtmospheres = 0.9, SurfacePressureKnown = true }, reducer.Current);
        var measurements = ExplorationPhysicalMeasurements.From(row);
        Assert.Equal(0d, measurements.PressureAtmospheres);
        Assert.Equal(1d, measurements.GravityG);
        Assert.Equal(250d, measurements.TemperatureKelvin);
    }

    [Fact]
    public void UnscannedBodyCanBeEnrichedWithoutInventingPressure()
    {
        var reducer = new JournalStateReducer();
        reducer.ApplyJournalLine("""{"event":"Location","StarSystem":"Test","SystemAddress":123}""");
        reducer.ApplyJournalLine("""{"event":"Scan","BodyID":7,"BodyName":"Test 7","PlanetClass":"Rocky body"}""");
        ExplorationCatalogBody row = Catalog(External(), reducer.Current);
        Assert.Null(ExplorationPhysicalMeasurements.From(row).PressureAtmospheres);
    }

    [Theory]
    [InlineData(0, null, null)]
    [InlineData(0, true, 0d)]
    [InlineData(1, null, 1d)]
    [InlineData(1, false, null)]
    [InlineData(-1, true, null)]
    public void LegacyCacheDoesNotTurnAnUnmarkedZeroIntoMeasuredVacuum(double pressure, bool? known, double? expected)
    {
        ExternalExplorationBodySnapshot legacy = External() with
        {
            SurfacePressureAtmospheres = pressure,
            SurfacePressureKnown = known
        };
        string json = JsonSerializer.Serialize(legacy);
        if (known is null) json = json.Replace("\"SurfacePressureKnown\":null,", "");
        var restored = JsonSerializer.Deserialize<ExternalExplorationBodySnapshot>(json)!;
        Assert.Equal(expected, ExplorationPhysicalMeasurements.From(Catalog(restored)).PressureAtmospheres);
    }

    [Theory]
    [InlineData("Not terraformable", false)]
    [InlineData("Terraformable", true)]
    public void JournalScanAndEarningsUseExplicitTerraformingState(string terraform, bool expected)
    {
        string scan = $$"""{"event":"Scan","BodyID":7,"BodyName":"Test 7","PlanetClass":"High metal content world","TerraformState":"{{terraform}}","MassEM":1,"WasDiscovered":false}""";
        var reducer = new JournalStateReducer();
        reducer.ApplyJournalLine(scan);
        var body = Assert.Single(reducer.Current.ExplorationBodies);
        Assert.Equal(expected, body.Terraformable);
        Assert.Equal(expected ? ExplorationInterest.Terraformable : ExplorationInterest.None, body.Interest);
        var values = ExplorationValueCalculator.Estimate("Planet", "High metal content world", expected, 1, 0);
        Assert.Equal(values.FirstDiscoveryScanValue,
            ExplorationEarningsService.CalculateForJournalLines([scan]).UniversalCartographicsEstimate);
    }

    private static ExplorationCatalogBody Catalog(ExternalExplorationBodySnapshot body, GameStateSnapshot? journal = null)
    {
        var system = new ExplorationSystemDataSnapshot(123, "Test", "Spansh", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            false, false, 1, 0, 0, null, null, null, false, [body]);
        return Assert.Single(ExplorationSystemCatalogBuilder.Build(journal ?? new() { StarSystem = "Test" },
            new(ExplorationDataStatus.Available, system, ""), ExplorationSpoilerModes.FullCatalog).Bodies);
    }

    private static ExternalExplorationBodySnapshot External() => new(7, "Test 7", "Planet", "Rocky body",
        0, true, 0, 0, "", "", "", 0, 0, 0);

    private static async Task<ExternalExplorationBodySnapshot> ReadBody(bool edsm, string terraform, string fields)
    {
        string body = $$"""{"id64":{{123 + (7L << 55)}},"bodyId":7,"name":"Test 7","type":"Planet","subtype":"High metal content world","subType":"High metal content world","terraforming_state":"{{terraform}}","terraformingState":"{{terraform}}",{{fields}}} """;
        string system = $$"""{"id64":123,"name":"Test","bodies":[{{body}}]}""";
        string payload = edsm ? system : $$"""{"record":{{system}}}""";
        using var client = new HttpClient(new Handler(payload));
        IExplorationSystemProvider provider = edsm ? new EdsmExplorationProvider(client) : new SpanshExplorationProvider(client);
        return Assert.Single((await provider.GetSystemAsync(123, "Test", CancellationToken.None))!.Bodies);
    }

    private sealed class Handler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.AbsoluteUri.Contains("estimated-value") ? "{}" : payload,
                    Encoding.UTF8, "application/json")
            });
    }
}
