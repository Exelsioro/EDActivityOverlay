using EDActivityOverlay.Services.Exploration;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class OrganicGenusIdentityTests
{
    [Theory]
    [InlineData("$Codex_Ent_Aleoids_Genus_Name;", "aleoida", 150)]
    [InlineData("$Codex_Ent_Bacterial_Genus_Name;", "bacterium", 500)]
    [InlineData("$Codex_Ent_Cactoid_Genus_Name;", "cactoida", 300)]
    [InlineData("$Codex_Ent_Clepeus_Genus_Name;;", "clypeus", 150)]
    [InlineData("$Codex_Ent_Clypeus_Genus_Name;", "clypeus", 150)]
    [InlineData("$Codex_Ent_Conchas_Genus_Name;", "concha", 150)]
    [InlineData("$Codex_Ent_Electricae_Genus_Name;", "electricae", 1000)]
    [InlineData("$Codex_Ent_Fonticulus_Genus_Name;", "fonticulua", 500)]
    [InlineData("$Codex_Ent_Shrubs_Genus_Name;", "frutexa", 150)]
    [InlineData("$Codex_Ent_Fumerolas_Genus_Name;", "fumerola", 100)]
    [InlineData("$Codex_Ent_Fungoids_Genus_Name;", "fungoida", 300)]
    [InlineData("$Codex_Ent_Osseus_Genus_Name;", "osseus", 800)]
    [InlineData("$Codex_Ent_Recepta_Genus_Name;", "recepta", 150)]
    [InlineData("$Codex_Ent_Stratum_Genus_Name;", "stratum", 500)]
    [InlineData("$Codex_Ent_Tubus_Genus_Name;", "tubus", 800)]
    [InlineData("$Codex_Ent_Tussocks_Genus_Name;", "tussock", 200)]
    public void CanonicalJournalIdWorksWithRussianDisplayName(string identifier, string key, int distance)
    {
        Assert.Equal(key, ExobiologyPredictionService.NormalizeGenusIdentity(identifier));
        var estimate = ExobiologyCatalog.Estimate(identifier, "Локализованное название");
        Assert.Equal(key, estimate.CatalogKey);
        Assert.Equal(distance, estimate.ColonyRangeMeters);
    }

    [Fact]
    public void JournalIdentityWinsOverConflictingDisplayName()
    {
        Assert.Equal("clypeus", ExobiologyCatalog.Estimate(
            "$Codex_Ent_Clepeus_Genus_Name;;", "Stratum").CatalogKey);
    }

    [Fact]
    public void UnsupportedGenusDoesNotInventSpacing()
    {
        Assert.Equal(0, ExobiologyCatalog.GetColonyRange("$Unknown_Genus;", "Неизвестный род"));
        Assert.Equal("unknown_genus", ExobiologyPredictionService.NormalizeGenusIdentity("$Unknown_Genus;"));
    }
}
