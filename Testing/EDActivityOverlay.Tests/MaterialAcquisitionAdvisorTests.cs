using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Engineering;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class MaterialAcquisitionAdvisorTests
{
    [Fact]
    public void UsesSpecificHgeRuleForPharmaceuticalIsolators()
    {
        var advisor = new MaterialAcquisitionAdvisor();
        var requirement = new MaterialRequirement(
            "pharmaceuticalisolators",
            "Pharmaceutical Isolators",
            EngineeringMaterialCategory.Manufactured,
            10,
            2);

        MaterialAcquisitionAdvice advice = advisor.Create(requirement);

        Assert.Equal(8, advice.Missing);
        Assert.Contains("Outbreak", advice.Options[0].Instructions);
    }

    [Fact]
    public void InfersRawAndEncodedCategoriesWithoutInventory()
    {
        var advisor = new MaterialAcquisitionAdvisor();
        var empty = new Dictionary<string, MaterialInventoryEntry>();

        Assert.Equal(EngineeringMaterialCategory.Raw, advisor.InferCategory("tellurium", empty));
        Assert.Equal(EngineeringMaterialCategory.Raw, advisor.InferCategory("antimony", empty));
        Assert.Equal(EngineeringMaterialCategory.Encoded, advisor.InferCategory("dataminedwakeexceptions", empty));
        Assert.Equal(EngineeringMaterialCategory.Manufactured, advisor.InferCategory("chemicalmanipulators", empty));
    }

    [Fact]
    public void UsesOfficialRussianFallbackAndPreservesJournalLocalization()
    {
        Assert.Equal("Фармацевтические изоляционные материалы",
            EngineeringLocalization.MaterialName("pharmaceuticalisolators", "Pharmaceutical Isolators"));
        Assert.Equal("Официальное имя из журнала",
            EngineeringLocalization.MaterialName("pharmaceuticalisolators", "Официальное имя из журнала"));
    }

    [Theory]
    [InlineData("polonium", "HIP 36601", "C 1 A")]
    [InlineData("yttrium", "Outotz LS-K d8-3", "B 5 A")]
    [InlineData("selenium", "HR 3230", "3 A A")]
    public void ProvidesConcreteDestinationForTargetedRawMaterials(
        string material,
        string expectedSystem,
        string expectedLocation)
    {
        var advisor = new MaterialAcquisitionAdvisor();
        var requirement = new MaterialRequirement(material, material, EngineeringMaterialCategory.Raw, 10, 0);

        AcquisitionOption destination = advisor.Create(requirement).Options
            .First(option => !string.IsNullOrWhiteSpace(option.SystemName));

        Assert.Equal(expectedSystem, destination.SystemName);
        Assert.Contains(expectedLocation, destination.LocationName);
    }
}
