using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Engineering;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class EngineeringMaterialCategoryTests
{
    [Theory]
    [InlineData("shieldcyclerecordings")]
    [InlineData("Distorted Shield Cycle Recordings")]
    public void DistortedShieldCycleRecordingsAreEncoded(
        string materialId)
    {
        var advisor =
            new MaterialAcquisitionAdvisor();

        var inventory =
            new Dictionary<string, MaterialInventoryEntry>(
                StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            EngineeringMaterialCategory.Encoded,
            advisor.InferCategory(
                materialId,
                inventory));
    }

    [Fact]
    public void CanonicalInventoryEntryKeepsEncodedCategoryForJournalAlias()
    {
        var advisor = new MaterialAcquisitionAdvisor();
        var inventory = new Dictionary<string, MaterialInventoryEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["distortedshieldcyclerecordings"] = new("distortedshieldcyclerecordings", "Distorted Shield Cycle Recordings", EngineeringMaterialCategory.Encoded, 2)
        };
        Assert.Equal(EngineeringMaterialCategory.Encoded, advisor.InferCategory("shieldcyclerecordings", inventory));
    }
}
