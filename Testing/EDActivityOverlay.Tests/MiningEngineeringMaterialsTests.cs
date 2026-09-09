using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Mining;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class MiningEngineeringMaterialsTests
{
    [Fact]
    public void ProjectorUsesEngineeringRequirementsAsSourceOfTruth()
    {
        var engineering = new EngineeringSnapshot
        {
            Inventory = new Dictionary<string, MaterialInventoryEntry>
            {
                ["selenium"] = new(
                    "selenium",
                    "Selenium",
                    EngineeringMaterialCategory.Raw,
                    21)
            },
            Requirements =
            [
                new MaterialRequirement(
                    "selenium",
                    "Selenium",
                    EngineeringMaterialCategory.Raw,
                    32,
                    21)
            ]
        };

        MiningEngineeringMaterialsSnapshot result =
            MiningEngineeringMaterialProjector.Build(
                Guid.NewGuid(),
                [
                    new MiningMaterialSessionGain(
                        "selenium",
                        "Selenium",
                        3)
                ],
                engineering);

        MiningEngineeringMaterialProgress item =
            Assert.Single(result.Materials);

        Assert.Equal(3, item.GainedThisSession);
        Assert.Equal(21, item.Available);
        Assert.Equal(32, item.Required);
        Assert.Equal(11, item.Missing);
        Assert.True(item.IsEngineeringTarget);
        Assert.Equal(3, result.TargetMaterialsGained);
    }

    [Fact]
    public void ProjectorKeepsNonWishlistMiningMaterialsVisible()
    {
        var engineering = new EngineeringSnapshot
        {
            Inventory = new Dictionary<string, MaterialInventoryEntry>
            {
                ["iron"] = new(
                    "iron",
                    "Iron",
                    EngineeringMaterialCategory.Raw,
                    40)
            }
        };

        MiningEngineeringMaterialsSnapshot result =
            MiningEngineeringMaterialProjector.Build(
                Guid.NewGuid(),
                [
                    new MiningMaterialSessionGain(
                        "iron",
                        "Iron",
                        6)
                ],
                engineering);

        MiningEngineeringMaterialProgress item =
            Assert.Single(result.Materials);

        Assert.Equal(6, item.GainedThisSession);
        Assert.Equal(40, item.Available);
        Assert.Equal(0, item.Required);
        Assert.Equal(0, item.Missing);
        Assert.False(item.IsEngineeringTarget);
    }

    [Fact]
    public void ProjectorPrioritizesEngineeringTargets()
    {
        var engineering = new EngineeringSnapshot
        {
            Requirements =
            [
                new MaterialRequirement(
                    "selenium",
                    "Selenium",
                    EngineeringMaterialCategory.Raw,
                    20,
                    10)
            ]
        };

        MiningEngineeringMaterialsSnapshot result =
            MiningEngineeringMaterialProjector.Build(
                Guid.NewGuid(),
                [
                    new MiningMaterialSessionGain("iron", "Iron", 20),
                    new MiningMaterialSessionGain("selenium", "Selenium", 1)
                ],
                engineering);

        Assert.Equal("selenium", result.Materials[0].MaterialId);
        Assert.Equal("iron", result.Materials[1].MaterialId);
    }
}
