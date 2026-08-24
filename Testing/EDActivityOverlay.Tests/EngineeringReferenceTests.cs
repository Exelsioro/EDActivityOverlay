using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Engineering;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class EngineeringReferenceTests
{
    [Fact]
    public void ShipEngineerCatalogContainsEveryEngineerWithActionableLocation()
    {
        Assert.Equal(38, EngineerCatalog.All.Count);
        Assert.Equal(38, EngineerCatalog.All.Select(engineer => engineer.Name).Distinct().Count());
        Assert.Equal(13, EngineerCatalog.All.Count(engineer => engineer.IsOnFoot));
        Assert.All(EngineerCatalog.All, engineer =>
        {
            Assert.False(string.IsNullOrWhiteSpace(engineer.SystemName));
            Assert.False(string.IsNullOrWhiteSpace(engineer.BaseName));
            Assert.StartsWith("https://elite-dangerous.fandom.com/wiki/", engineer.WikiUrl);
        });
    }

    [Fact]
    public void MaterialWikiBuildsConcreteArticleUrlFromCanonicalIngredient()
    {
        BlueprintRecipe recipe = new(
            "test", "test", "test", 1, false,
            [new BlueprintIngredient("chemicalmanipulators", "Chemical Manipulators", 1)]);

        string? url = MaterialWiki.GetArticleUrl(
            "chemicalmanipulators", "Манипуляторы для работы с химикатами", [recipe]);

        Assert.Equal(
            "https://elite-dangerous.fandom.com/wiki/Chemical_Manipulators",
            url);
    }
}
