using EDActivityOverlay.Services.Exploration;
using EDActivityOverlay.Models;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class ExplorationAssistantTests
{
    [Fact]
    public void ValueCalculatorMatchesEdcorePost33EarthLikeFormula()
    {
        ExplorationValueEstimate value = ExplorationValueCalculator.Estimate(
            "Planet", "Earthlike body", true, 1, null);

        Assert.Equal(283_628, value.BaseScanValue);
        Assert.Equal(4_433_370, value.FirstDiscoveredAndMappedEfficientValue);
        Assert.Equal(value.FirstDiscoveryScanValue,
            ExplorationValueCalculator.SelectScanValue(value, wasDiscovered: false));
    }

    [Fact]
    public void ValueCalculatorSelectsDiscoveryAndMappingScenario()
    {
        ExplorationValueEstimate value = ExplorationValueCalculator.Estimate(
            "Planet", "Water world", false, 1, null);

        Assert.Equal(101_520, value.BaseScanValue);
        Assert.Equal(value.FirstMappedEfficientValue,
            ExplorationValueCalculator.SelectMappingValue(value, wasDiscovered: true, wasMapped: false, efficient: true));
        Assert.Equal(value.PreviouslyMappedValue,
            ExplorationValueCalculator.SelectMappingValue(value, wasDiscovered: true, wasMapped: true, efficient: false));
    }

    [Fact]
    public void CatalogRecognizesJournalGenusIdentifiers()
    {
        var estimate = ExobiologyCatalog.Estimate("$Codex_Ent_Bacterial_Genus_Name;", "Bacterium");

        Assert.Equal("bacterium", estimate.CatalogKey);
        Assert.Equal(500, estimate.ColonyRangeMeters);
        Assert.Equal(1_000_000, estimate.MinimumValue);
        Assert.Equal(8_418_000, estimate.MaximumValue);
    }

    [Fact]
    public void SurfaceNavigationCalculatesDistanceAndRelativeBearing()
    {
        SurfaceNavigationResult? result = SurfaceNavigationCalculator.Calculate(
            0, 0, 90, 1_000_000, 0, 0.01);

        Assert.NotNull(result);
        Assert.InRange(result!.DistanceMeters, 174, 176);
        Assert.InRange(result.BearingDegrees, 89.9, 90.1);
        Assert.InRange(result.RelativeTurnDegrees, -0.1, 0.1);
        Assert.InRange(result.EscapeBearingDegrees, 269.9, 270.1);
        Assert.InRange(Math.Abs(result.EscapeRelativeTurnDegrees), 179.9, 180.1);
        Assert.False(result.IsFarEnough(200));
    }

    [Fact]
    public void BioforgePredictionUsesEnvironmentAndConfirmedGenus()
    {
        var body = new ExplorationBodySnapshot(
            2, "Test 2", "", 20, false, false, false, false, 2,
            new[] { "Stratum" }, ExplorationInterest.None)
        {
            IsScanned = true,
            Landable = true,
            BodyType = "Planet",
            BodyClass = "High metal content world",
            Atmosphere = "Thin sulphur dioxide atmosphere",
            Volcanism = "No volcanism",
            SurfaceTemperatureKelvin = 200,
            SurfacePressureAtmospheres = 0.005,
            GravityG = 0.2
        };

        IReadOnlyList<ExobiologyPrediction> predictions =
            ExobiologyPredictionService.Instance.Predict(body, 5);

        Assert.NotEmpty(predictions);
        Assert.All(predictions, item => Assert.Equal("Stratum", item.Genus));
        Assert.All(predictions, item => Assert.InRange(item.RelativeProbability, 0, 1));
        Assert.Contains(predictions, item => item.Species.Contains("Stratum", StringComparison.OrdinalIgnoreCase));
    }
}
