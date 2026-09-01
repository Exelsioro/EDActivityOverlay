using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Mining;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class MiningMarketTargetTests
{
    [Fact]
    public void MultiTargetAdvisorAcceptsAnySelectedCommodityWithoutPriceWeighting()
    {
        var prospect = new MiningProspectSnapshot(
            1,
            DateTimeOffset.UtcNow,
            "High",
            100,
            string.Empty,
            string.Empty,
            new[]
            {
                new MiningProspectMaterialSnapshot("Gold", "Gold", 49),
                new MiningProspectMaterialSnapshot("Platinum", "Platinum", 31)
            });

        MiningProspectAdvice advice = MiningProspectorAdvisor.Evaluate(
            prospect,
            new[] { "Platinum", "Gold" },
            30);

        Assert.Equal(MiningProspectDecision.Mine, advice.Decision);
        Assert.True(advice.TargetFound);
        Assert.Equal("Gold", advice.TargetCommodity);
        Assert.Equal(49d, advice.TargetProportion.GetValueOrDefault());
    }

    [Fact]
    public void AutoSelectionUsesRingCompatibilityThenMarketPrice()
    {
        var settings = new AppSettings
        {
            MiningAutoSelectTargets = true
        };
        var ring = new MiningRingContextSnapshot(
            42,
            "Test",
            "Test A Ring",
            "eRingClass_Metalic",
            "$PristineResources;",
            Array.Empty<string>());
        var quotes = new Dictionary<string, MiningMarketPriceQuote>(StringComparer.OrdinalIgnoreCase)
        {
            ["Platinum"] = new("Platinum", 70000, 71000, 80000, 3, DateTimeOffset.UtcNow),
            ["Gold"] = new("Gold", 40000, 41000, 45000, 3, DateTimeOffset.UtcNow),
            ["Musgravite"] = new("Musgravite", 250000, 250000, 300000, 3, DateTimeOffset.UtcNow)
        };
        var prices = new MiningMarketPriceSnapshot(
            42,
            "Test",
            DateTimeOffset.UtcNow,
            false,
            string.Empty,
            quotes);

        MiningTargetSelection selection = MiningTargetSelector.Select(settings, ring, prices);

        Assert.Equal(new[] { "Platinum", "Gold" }, selection.CommodityIds);
        Assert.DoesNotContain("Musgravite", selection.CommodityIds);
    }

    [Fact]
    public void DssHotspotsConstrainAutoCandidatesWhenRecognized()
    {
        var ring = new MiningRingContextSnapshot(
            42,
            "Test",
            "Test A Ring",
            "eRingClass_Metalic",
            "$PristineResources;",
            new[] { "Platinum" });

        Assert.Equal(new[] { "Platinum" }, MiningTargetSelector.GetAutoCandidates(ring));
    }

    [Fact]
    public void RingContextReadsScanAndSaaHotspot()
    {
        using var service = new MiningRingContextService();
        Apply(service, "Location", """
            {"SystemAddress":42,"StarSystem":"Test"}
            """);
        Apply(service, "Scan", """
            {
              "SystemAddress":42,
              "ReserveLevel":"$PristineResources;",
              "Rings":[{"Name":"Test 1 A Ring","RingClass":"eRingClass_Metalic"}]
            }
            """);
        Apply(service, "SAASignalsFound", """
            {
              "SystemAddress":42,
              "BodyName":"Test 1 A Ring",
              "Signals":[{"Type":"$SAA_SignalType_Platinum;","Count":1}]
            }
            """);

        MiningRingContextSnapshot context = service.Resolve("Test 1 A Ring", 42, "Test");

        Assert.Equal("eRingClass_Metalic", context.RingClass);
        Assert.Equal("$PristineResources;", context.ReserveLevel);
        Assert.Contains("Platinum", context.HotspotCommodityIds);
    }


    [Fact]
    public void RingContextInfersSingleRingFromParentBody()
    {
        using var service = new MiningRingContextService();
        Apply(service, "Location", """
            {"SystemAddress":42,"StarSystem":"Test"}
            """);
        Apply(service, "Scan", """
            {
              "SystemAddress":42,
              "ReserveLevel":"$PristineResources;",
              "Rings":[{"Name":"Test 1 A Ring","RingClass":"eRingClass_Metalic"}]
            }
            """);

        MiningRingContextSnapshot context = service.Resolve(
            string.Empty,
            "Test 1",
            42,
            "Test");

        Assert.Equal("Test 1 A Ring", context.RingName);
        Assert.Equal("eRingClass_Metalic", context.RingClass);
    }

    [Fact]
    public void RingContextUsesOnlyHotspotRingWhenParentHasMultipleRings()
    {
        using var service = new MiningRingContextService();
        Apply(service, "Location", """
            {"SystemAddress":42,"StarSystem":"Test"}
            """);
        Apply(service, "Scan", """
            {
              "SystemAddress":42,
              "ReserveLevel":"$PristineResources;",
              "Rings":[
                {"Name":"Test 1 A Ring","RingClass":"eRingClass_Metalic"},
                {"Name":"Test 1 B Ring","RingClass":"eRingClass_Rocky"}
              ]
            }
            """);
        Apply(service, "SAASignalsFound", """
            {
              "SystemAddress":42,
              "BodyName":"Test 1 B Ring",
              "Signals":[{"Type":"$SAA_SignalType_Monazite;","Count":1}]
            }
            """);

        MiningRingContextSnapshot context = service.Resolve(
            string.Empty,
            "Test 1",
            42,
            "Test");

        Assert.Equal("Test 1 B Ring", context.RingName);
        Assert.Contains("Monazite", context.HotspotCommodityIds);
    }

    private static void Apply(MiningRingContextService service, string eventName, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        service.OnJournalEvent(new JournalEventReceivedEventArgs(
            eventName,
            DateTimeOffset.UtcNow,
            document.RootElement.Clone()));
    }
}
