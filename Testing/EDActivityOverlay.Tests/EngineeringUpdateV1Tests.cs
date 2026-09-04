using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Engineering;
using EDActivityOverlay.Services.Journal;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class EngineeringUpdateV1Tests : IDisposable
{
    private readonly string databasePath =
        Path.Combine(
            Path.GetTempPath(),
            $"ed-overlay-engineering-update-{Guid.NewGuid():N}.db");

    [Fact]
    public void EncodedJournalSymbolUpdatesCoriolisNamedRequirement()
    {
        using EngineeringService service =
            CreateService();

        BlueprintRecipe recipe =
            Recipe(
                "Encoded:G1",
                1,
                "atypicaldisruptedwakeechoes",
                ingredientCount: 3);

        service.Catalog.SetRecipesForTests(
            [recipe]);

        service.AddOrIncreaseWishlist(
            recipe,
            1);

        using JsonDocument initial =
            JsonDocument.Parse(
                """
                {
                  "Raw":[],
                  "Manufactured":[],
                  "Encoded":[{"Name":"disruptedwakeechoes","Count":1}]
                }
                """);

        service.OnJournalEvent(
            new JournalEventReceivedEventArgs(
                "Materials",
                DateTimeOffset.Parse(
                    "2026-09-04T10:00:00Z"),
                initial.RootElement.Clone()));

        MaterialRequirement before =
            service.Current.Requirements
                .Single(
                    item =>
                        item.MaterialId
                        == "atypicaldisruptedwakeechoes");

        Assert.Equal(1, before.Available);
        Assert.Equal(2, before.Missing);

        using JsonDocument collected =
            JsonDocument.Parse(
                """
                {
                  "Name":"disruptedwakeechoes",
                  "Category":"Encoded",
                  "Count":2
                }
                """);

        service.OnJournalEvent(
            new JournalEventReceivedEventArgs(
                "MaterialCollected",
                DateTimeOffset.Parse(
                    "2026-09-04T10:01:00Z"),
                collected.RootElement.Clone()));

        MaterialRequirement after =
            service.Current.Requirements
                .Single(
                    item =>
                        item.MaterialId
                        == "atypicaldisruptedwakeechoes");

        Assert.Equal(3, after.Available);
        Assert.Equal(0, after.Missing);
        Assert.Equal(
            EngineeringMaterialCategory.Encoded,
            after.Category);
    }

    [Fact]
    public void ManufacturedJournalAliasUpdatesRequirement()
    {
        using EngineeringService service =
            CreateService();

        BlueprintRecipe recipe =
            Recipe(
                "Manufactured:G1",
                1,
                "flawedfocuscrystals",
                ingredientCount: 2);

        service.Catalog.SetRecipesForTests(
            [recipe]);

        service.AddOrIncreaseWishlist(
            recipe,
            1);

        using JsonDocument initial =
            JsonDocument.Parse(
                """
                {
                  "Raw":[],
                  "Manufactured":[{"Name":"uncutfocuscrystals","Count":1}],
                  "Encoded":[]
                }
                """);

        service.OnJournalEvent(
            new JournalEventReceivedEventArgs(
                "Materials",
                DateTimeOffset.Parse(
                    "2026-09-04T11:00:00Z"),
                initial.RootElement.Clone()));

        using JsonDocument collected =
            JsonDocument.Parse(
                """
                {
                  "Name":"uncutfocuscrystals",
                  "Category":"Manufactured",
                  "Count":1
                }
                """);

        service.OnJournalEvent(
            new JournalEventReceivedEventArgs(
                "MaterialCollected",
                DateTimeOffset.Parse(
                    "2026-09-04T11:01:00Z"),
                collected.RootElement.Clone()));

        MaterialRequirement requirement =
            service.Current.Requirements
                .Single(
                    item =>
                        item.MaterialId
                        == "flawedfocuscrystals");

        Assert.Equal(2, requirement.Available);
        Assert.Equal(0, requirement.Missing);
        Assert.Equal(
            EngineeringMaterialCategory.Manufactured,
            requirement.Category);
    }

    [Theory]
    [InlineData("disruptedwakeechoes", "Atypical Disrupted Wake Echoes")]
    [InlineData("dataminedwake", "Datamined Wake Exceptions")]
    [InlineData("uncutfocuscrystals", "Flawed Focus Crystals")]
    [InlineData("fedcorecomposites", "Core Dynamics Composites")]
    public void FrontierJournalAndBlueprintNamesShareCanonicalMaterialIdentity(
        string journalName,
        string blueprintName)
    {
        Assert.Equal(
            MaterialName.Normalize(blueprintName),
            MaterialName.Normalize(journalName));
    }

    [Fact]
    public void FullGradePathUsesOneThroughFiveApplications()
    {
        using EngineeringService service =
            CreateService();

        BlueprintRecipe[] path =
        [
            Recipe("Path:G1", 1, "iron"),
            Recipe("Path:G2", 2, "nickel"),
            Recipe("Path:G3", 3, "arsenic"),
            Recipe("Path:G4", 4, "cadmium"),
            Recipe("Path:G5", 5, "tellurium")
        ];

        service.Catalog.SetRecipesForTests(
            path);

        service.AddGradePathToWishlist(
            path);

        WishlistEntry[] wishlist =
            service.Current.Wishlist
                .OrderBy(
                    item =>
                        service.Catalog.Find(
                            item.RecipeId)!.Grade)
                .ToArray();

        Assert.Equal(
            new[] { 1, 2, 3, 4, 5 },
            wishlist.Select(
                item =>
                    item.CraftCount)
                .ToArray());

        Assert.Equal(
            1,
            service.Current.Requirements
                .Single(
                    item =>
                        item.MaterialId
                        == "iron")
                .Required);

        Assert.Equal(
            5,
            service.Current.Requirements
                .Single(
                    item =>
                        item.MaterialId
                        == "tellurium")
                .Required);
    }

    [Fact]
    public void LiveMaterialCollectedUpdatesPinnedRequirementThroughJournalHub()
    {
        using EngineeringService service =
            CreateService();

        BlueprintRecipe recipe =
            Recipe(
                "Live:G1",
                1,
                "arsenic",
                ingredientCount: 3);

        service.Catalog.SetRecipesForTests(
            [recipe]);

        service.AddOrIncreaseWishlist(
            recipe,
            1);

        using JsonDocument initial =
            JsonDocument.Parse(
                """
                {
                  "Raw":[{"Name":"arsenic","Count":1}],
                  "Manufactured":[],
                  "Encoded":[]
                }
                """);

        service.OnJournalEvent(
            new JournalEventReceivedEventArgs(
                "Materials",
                DateTimeOffset.Parse(
                    "2026-08-29T10:00:00Z"),
                initial.RootElement.Clone()));

        var hub =
            new JournalEventHub();

        hub.Register(
            service);

        using JsonDocument collected =
            JsonDocument.Parse(
                """
                {
                  "Name":"arsenic",
                  "Category":"Raw",
                  "Count":2
                }
                """);

        hub.Publish(
            new JournalEventReceivedEventArgs(
                "MaterialCollected",
                DateTimeOffset.Parse(
                    "2026-08-29T10:01:00Z"),
                collected.RootElement.Clone()));

        MaterialRequirement requirement =
            service.Current.Requirements
                .Single(
                    item =>
                        item.MaterialId
                        == "arsenic");

        Assert.Equal(
            3,
            requirement.Available);

        Assert.Equal(
            0,
            requirement.Missing);
    }

    [Fact]
    public void ExperimentalEffectGetsEngineersFromCompatibleModuleRecipes()
    {
        string blueprints =
            """
            {
              "FSD_LongRange": {
                "fdname":"FSD_LongRange",
                "name":"Increased range",
                "modulename":"FrameShiftDrive",
                "grades":{
                  "5":{
                    "components":{
                      "Arsenic":1
                    }
                  }
                }
              }
            }
            """;

        string experimentals =
            """
            {
              "special_fsd_heavy": {
                "edname":"special_fsd_heavy",
                "name":"Mass Manager",
                "components":{
                  "Atypical Disrupted Wake Echoes":5
                }
              }
            }
            """;

        string engineerSource =
            """
            new EngineeringRecipe("Increased FSD Range", "FSD_LongRange", "1As", ItemData.ShipModule.ModuleTypes.FrameShiftDrive, 5, "Felicity Farseer,Elvira Martuuk" ),
            new EngineeringRecipe("Mass Manager", "special_fsd_heavy", ItemData.ShipModule.ModuleTypes.FrameShiftDrive, "5ADWE,3GA,1EHT"),
            """;

        BlueprintRecipe experimental =
            BlueprintCatalogService.Parse(
                    blueprints,
                    experimentals,
                    engineerSource)
                .Single(
                    recipe =>
                        recipe.IsExperimental);

        Assert.Equal(
            new[] { "Elvira Martuuk", "Felicity Farseer" },
            experimental.Engineers);
    }

    [Fact]
    public void EngineeringUiContainsExperimentalControlsAndGalaxyMapRouteHandoff()
    {
        string mainCode =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "EngineeringWindow.xaml.cs"));

        string traderCode =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "EngineeringWindow.MaterialTraders.cs"));

        string code =
            mainCode
            + Environment.NewLine
            + traderCode;

        string xaml =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "EngineeringWindow.xaml"));

        Assert.Contains(
            "EngineerExperimentalCombo",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "PinEngineerExperimental_Click",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "EliteRouteNavigationService.Instance.PrepareAsync",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "EnableExperimentalRouteAutomation",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "recipe.Ingredients.Select",
            mainCode,
            StringComparison.Ordinal);

        Assert.Contains(
            "BuildGradePathIngredientRows",
            mainCode,
            StringComparison.Ordinal);

        Assert.Contains(
            "recipe.Grade",
            mainCode,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExperimentalRecipesRemainSingleApplicationWhenPinned()
    {
        using EngineeringService service =
            CreateService();

        BlueprintRecipe experimental =
            new(
                "experimental:special_test",
                "Test effect",
                "Experimental effect",
                0,
                true,
                [
                    new BlueprintIngredient(
                        "iron",
                        "Iron",
                        4)
                ]);

        service.Catalog.SetRecipesForTests(
            [experimental]);

        service.AddOrIncreaseWishlist(
            experimental,
            1);

        WishlistEntry item =
            Assert.Single(
                service.Current.Wishlist);

        Assert.Equal(
            1,
            item.CraftCount);

        Assert.Equal(
            4,
            Assert.Single(
                    service.Current.Requirements)
                .Required);
    }

    public void Dispose()
    {
        foreach (string path
                 in new[]
                 {
                     databasePath,
                     databasePath + "-wal",
                     databasePath + "-shm"
                 })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private EngineeringService CreateService() =>
        new(
            new EngineeringRepository(
                databasePath),
            new BlueprintCatalogService(
                Path.Combine(
                    Path.GetTempPath(),
                    $"ed-overlay-catalog-{Guid.NewGuid():N}")));

    private static BlueprintRecipe Recipe(
        string id,
        int grade,
        string materialId,
        int ingredientCount = 1) =>
        new(
            id,
            "Test modification",
            "Test module",
            grade,
            false,
            [
                new BlueprintIngredient(
                    materialId,
                    materialId,
                    ingredientCount)
            ]);

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
