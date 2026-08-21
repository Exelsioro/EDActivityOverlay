using ED_Inara_Overlay.Models;
using ED_Inara_Overlay.Services.Engineering;
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class EngineeringCatalogTests
{
    [Fact]
    public void CoriolisCatalogParsesGradesAndNormalizesIngredients()
    {
        string json = """
            {
              "FSD_LongRange": {
                "fdname": "FSD_LongRange",
                "name": "Increased range",
                "modulename": ["Frame shift drive", "FSD"],
                "grades": {
                  "5": {
                    "components": {
                      "Arsenic": 1,
                      "Chemical Manipulators": 1,
                      "Datamined Wake Exceptions": 1
                    }
                  }
                }
              }
            }
            """;

        BlueprintRecipe recipe = Assert.Single(BlueprintCatalogService.Parse(json));

        Assert.Equal("FSD_LongRange:G5", recipe.Id);
        Assert.Equal("Frame shift drive", recipe.ModuleName);
        Assert.Equal("Increased range", recipe.BlueprintName);
        Assert.Equal(5, recipe.Grade);
        Assert.Contains(recipe.Ingredients, item =>
            item.MaterialId == "chemicalmanipulators"
            && item.Name == "Chemical Manipulators"
            && item.Count == 1);
    }

    [Fact]
    public void ExperimentalEffectsAreIncluded()
    {
        string blueprints = """{"Test":{"name":"Test","grades":{}}}""";
        string experimentals = """
            {
              "special_mass_manager": {
                "edname": "special_mass_manager",
                "name": "Mass Manager",
                "components": { "Atypical Disrupted Wake Echoes": 5 }
              }
            }
            """;

        BlueprintRecipe recipe = Assert.Single(BlueprintCatalogService.Parse(blueprints, experimentals));

        Assert.True(recipe.IsExperimental);
        Assert.Equal("experimental:special_mass_manager", recipe.Id);
    }

    [Fact]
    public void EngineerAssignmentsAreJoinedByBlueprintIdAndGrade()
    {
        string blueprints = """
            {"FSD_LongRange":{"fdname":"FSD_LongRange","name":"Increased range","modulename":"Frame shift drive","grades":{"5":{"components":{"Arsenic":1}}}}}
            """;
        string engineerSource = """
            new EngineeringRecipe("Increased Range", "FSD_LongRange", "1As", ItemData.ShipModule.ModuleTypes.FrameShiftDrive, 5, "Felicity Farseer,Elvira Martuuk" ),
            """;

        BlueprintRecipe recipe = Assert.Single(BlueprintCatalogService.Parse(blueprints, null, engineerSource));

        Assert.Equal(["Elvira Martuuk", "Felicity Farseer"], recipe.Engineers);
    }

    [Theory]
    [InlineData("$DataminedWakeExceptions_Name;", "dataminedwakeexceptions")]
    [InlineData("Chemical Manipulators", "chemicalmanipulators")]
    [InlineData("Atypical_Disrupted-Wake Echoes", "atypicaldisruptedwakeechoes")]
    public void MaterialNamesUseStableJournalCompatibleKeys(string input, string expected)
    {
        Assert.Equal(expected, MaterialName.Normalize(input));
    }
}
