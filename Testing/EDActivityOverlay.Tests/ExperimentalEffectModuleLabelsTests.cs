using System;
using System.IO;
using System.Linq;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Engineering;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class ExperimentalEffectModuleLabelsTests
{
    [Fact]
    public void SameNamedExperimentalEffectsAreDisambiguatedByModule()
    {
        const string blueprints =
            """
            {}
            """;

        const string experimentals =
            """
            {
              "special_test_powerplant": {
                "edname":"special_test_powerplant",
                "name":"Thermal Spread",
                "components":{
                  "Iron":2
                }
              },
              "special_test_shield": {
                "edname":"special_test_shield",
                "name":"Thermal Spread",
                "components":{
                  "Nickel":3
                }
              }
            }
            """;

        const string engineeringRecipes =
            """
            new EngineeringRecipe("Thermal Spread", "special_test_powerplant", ItemData.ShipModule.ModuleTypes.PowerPlant, "2I"),
            new EngineeringRecipe("Thermal Spread", "special_test_shield", ItemData.ShipModule.ModuleTypes.ShieldGenerator, "3N"),
            """;

        BlueprintRecipe[] effects =
            BlueprintCatalogService.Parse(
                    blueprints,
                    experimentals,
                    engineeringRecipes)
                .Where(
                    recipe =>
                        recipe.IsExperimental)
                .OrderBy(
                    recipe =>
                        recipe.Id)
                .ToArray();

        Assert.Equal(
            2,
            effects.Length);

        BlueprintRecipe powerPlant =
            effects.Single(
                recipe =>
                    recipe.Id
                    == "experimental:special_test_powerplant");

        BlueprintRecipe shield =
            effects.Single(
                recipe =>
                    recipe.Id
                    == "experimental:special_test_shield");

        Assert.Equal(
            "Power Plant",
            powerPlant.ModuleName);

        Assert.Equal(
            "Shield Generator",
            shield.ModuleName);

        Assert.NotEqual(
            powerPlant.ModuleName,
            shield.ModuleName);

        string modelSource =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Models",
                    "EngineeringModels.cs"));

        Assert.Contains(
            "LocalizedExperimentalModuleName",
            modelSource,
            StringComparison.Ordinal);

        Assert.Contains(
            "Loc_Experimental_Format",
            modelSource,
            StringComparison.Ordinal);

        Assert.Equal(
            "iron",
            Assert.Single(
                    powerPlant.Ingredients)
                .MaterialId);

        Assert.Equal(
            2,
            Assert.Single(
                    powerPlant.Ingredients)
                .Count);

        Assert.Equal(
            "nickel",
            Assert.Single(
                    shield.Ingredients)
                .MaterialId);

        Assert.Equal(
            3,
            Assert.Single(
                    shield.Ingredients)
                .Count);
    }

    [Fact]
    public void MultiModuleExperimentalEffectShowsAllApplicableModules()
    {
        const string experimentals =
            """
            {
              "special_test_weapon": {
                "edname":"special_test_weapon",
                "name":"Test Weapon Effect",
                "components":{
                  "Iron":1
                }
              }
            }
            """;

        const string engineeringRecipes =
            """
            new EngineeringRecipe("Test Weapon Effect", "special_test_weapon", "BeamLaser,MultiCannon", "1I"),
            """;

        BlueprintRecipe effect =
            Assert.Single(
                BlueprintCatalogService.Parse(
                        "{}",
                        experimentals,
                        engineeringRecipes)
                    .Where(
                        recipe =>
                            recipe.IsExperimental));

        Assert.Contains(
            "Beam Laser",
            effect.ModuleName,
            StringComparison.Ordinal);

        Assert.Contains(
            "Multi Cannon",
            effect.ModuleName,
            StringComparison.Ordinal);

        Assert.Contains(
            " / ",
            effect.ModuleName,
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
