using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Engineering;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class EngineeringServiceTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"ed-overlay-engineering-{Guid.NewGuid():N}.db");

    [Fact]
    public void JournalInventoryAndWishlistProduceLiveDeficits()
    {
        using var service = CreateService();
        Apply(service, "LoadGame", """{"Commander":"Test CMDR"}""");
        Apply(service, "Materials", """
            {
              "Raw":[{"Name":"arsenic","Count":4}],
              "Manufactured":[{"Name":"chemicalmanipulators","Count":0}],
              "Encoded":[{"Name":"dataminedwakeexceptions","Count":1}]
            }
            """);

        BlueprintRecipe recipe = service.Catalog.Find("FSD_LongRange:G5")!;
        service.AddOrIncreaseWishlist(recipe, 2);

        EngineeringSnapshot state = service.Current;
        Assert.Equal("Test CMDR", state.Commander);
        Assert.Equal(4, state.Inventory["arsenic"].Count);
        Assert.Equal("Мышьяк", state.Inventory["arsenic"].Name);
        Assert.Equal("Манипуляторы для работы с химикатами", state.Requirements.Single(item => item.MaterialId == "chemicalmanipulators").Name);
        Assert.Equal(2, state.Requirements.Single(item => item.MaterialId == "chemicalmanipulators").Missing);
        Assert.Equal(1, state.Requirements.Single(item => item.MaterialId == "dataminedwakeexceptions").Missing);
        Assert.Contains(state.Advice, item => item.MaterialId == "chemicalmanipulators");
    }

    [Fact]
    public void ShipLockerAndBackpackAreCombinedWithoutLosingCategory()
    {
        using var service = CreateService();
        Apply(service, "ShipLocker", """{"Components":[{"Name":"graphene","Name_Localised":"Graphene","Count":5}],"Data":[],"Items":[],"Consumables":[]}""");
        Apply(service, "Backpack", """{"Components":[{"Name":"graphene","Name_Localised":"Graphene","Count":2}],"Data":[],"Items":[],"Consumables":[]}""");

        MaterialInventoryEntry graphene = service.Current.Inventory["graphene"];
        Assert.Equal(7, graphene.Count);
        Assert.Equal(EngineeringMaterialCategory.Component, graphene.Category);
    }

    [Fact]
    public void TradesAndCraftingAdjustMaterialCounts()
    {
        using var service = CreateService();
        Apply(service, "Materials", """{"Raw":[],"Manufactured":[{"Name":"chemicalprocessors","Count":10}],"Encoded":[{"Name":"strangewakesolutions","Count":3}]}""");
        Apply(service, "MaterialTrade", """{"Paid":{"Material":"chemicalprocessors","Category":"Manufactured","Quantity":6},"Received":{"Material":"chemicalmanipulators","Category":"Manufactured","Quantity":1}}""");
        Apply(service, "EngineerCraft", """{"Ingredients":[{"Name":"strangewakesolutions","Count":1}]}""");

        Assert.Equal(4, service.Current.Inventory["chemicalprocessors"].Count);
        Assert.Equal(1, service.Current.Inventory["chemicalmanipulators"].Count);
        Assert.Equal(2, service.Current.Inventory["strangewakesolutions"].Count);
    }

    [Fact]
    public void MaterialCanBeTrackedWithoutBlueprintAndPersists()
    {
        using (var service = CreateService())
        {
            Apply(service, "Materials", """{"Raw":[{"Name":"tellurium","Count":3}],"Manufactured":[],"Encoded":[]}""");
            service.ToggleTrackedMaterial(service.Current.Inventory["tellurium"]);

            TrackedMaterialEntry tracked = Assert.Single(service.Current.TrackedMaterials);
            Assert.Equal("tellurium", tracked.MaterialId);
            Assert.Equal(13, tracked.TargetCount);
            MaterialRequirement requirement = Assert.Single(service.Current.Requirements);
            Assert.Equal(10, requirement.Missing);
        }

        using var restored = CreateService();
        Assert.Equal("tellurium", Assert.Single(restored.Current.TrackedMaterials).MaterialId);
    }

    [Fact]
    public void PersistedSnapshotRemovesMaterialsMissingFromLatestState()
    {
        var repository = new EngineeringRepository(databasePath);
        repository.SaveCommanderState(
            "Test CMDR",
            new[]
            {
                new MaterialInventoryEntry(
                    "arsenic",
                    "Arsenic",
                    EngineeringMaterialCategory.Raw,
                    8,
                    300,
                    DateTimeOffset.Parse("2026-08-19T10:00:00Z"))
            },
            Array.Empty<EngineerProgressEntry>());

        repository.SaveCommanderState(
            "Test CMDR",
            Array.Empty<MaterialInventoryEntry>(),
            Array.Empty<EngineerProgressEntry>());

        Assert.Empty(repository.LoadCommanderState("Test CMDR").Inventory);
    }

    public void Dispose()
    {
        foreach (string path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private EngineeringService CreateService() => new(
        new EngineeringRepository(databasePath),
        new BlueprintCatalogService(Path.Combine(Path.GetTempPath(), $"ed-overlay-catalog-{Guid.NewGuid():N}")));

    private static void Apply(EngineeringService service, string eventName, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        service.OnJournalEvent(new JournalEventReceivedEventArgs(
            eventName,
            DateTimeOffset.Parse("2026-08-19T10:00:00Z"),
            document.RootElement.Clone()));
    }
}
