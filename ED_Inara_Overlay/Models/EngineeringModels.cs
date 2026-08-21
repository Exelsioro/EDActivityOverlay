using System.Collections.ObjectModel;
using ED_Inara_Overlay.Services.Engineering;
using ED_Inara_Overlay.Services;

namespace ED_Inara_Overlay.Models;

public enum EngineeringMaterialCategory
{
    Unknown,
    Raw,
    Manufactured,
    Encoded,
    Item,
    Component,
    Data,
    Consumable
}

public sealed record MaterialInventoryEntry(
    string Id,
    string Name,
    EngineeringMaterialCategory Category,
    int Count,
    int? Maximum = null,
    DateTimeOffset? UpdatedUtc = null)
{
    public double FillRatio => Maximum is > 0 ? Math.Clamp((double)Count / Maximum.Value, 0, 1) : 0;
    public string CategoryName => EngineeringLocalization.CategoryName(Category);
}

public sealed record EngineerProgressEntry(
    long? EngineerId,
    string Name,
    string Progress,
    int Rank,
    int RankProgress);

public sealed record BlueprintIngredient(string MaterialId, string Name, int Count);

public sealed record BlueprintRecipe(
    string Id,
    string BlueprintName,
    string ModuleName,
    int Grade,
    bool IsExperimental,
    IReadOnlyList<BlueprintIngredient> Ingredients)
{
    public IReadOnlyList<string> Engineers { get; init; } = Array.Empty<string>();

    public string DisplayName => IsExperimental
        ? Loc.Format("Loc_Experimental_Format", CoriolisRussianLocalization.Translate(BlueprintName), Loc.Get("Loc_Experimental_Label"))
        : Loc.Format(
            "Loc_Grade_Format",
            CoriolisRussianLocalization.Translate(ModuleName),
            CoriolisRussianLocalization.Translate(BlueprintName),
            Grade);
}

public sealed record WishlistEntry(
    string Id,
    string RecipeId,
    string DisplayName,
    int CraftCount,
    DateTimeOffset CreatedUtc);

public sealed record TrackedMaterialEntry(
    string MaterialId,
    string DisplayName,
    EngineeringMaterialCategory Category,
    int TargetCount,
    DateTimeOffset CreatedUtc);

public sealed record MaterialRequirement(
    string MaterialId,
    string Name,
    EngineeringMaterialCategory Category,
    int Required,
    int Available)
{
    public int Missing => Math.Max(0, Required - Available);
    public bool IsComplete => Missing == 0;
    public string ProgressText => $"{Available}/{Required}";
    public string CategoryName => EngineeringLocalization.CategoryName(Category);
}

public sealed record AcquisitionOption(
    string Title,
    string Instructions,
    string? Condition = null,
    string? ExternalUrl = null,
    int Priority = 100,
    string? SystemName = null,
    string? LocationName = null)
{
    public string Destination => string.IsNullOrWhiteSpace(SystemName)
        ? string.Empty
        : string.IsNullOrWhiteSpace(LocationName) ? SystemName : $"{SystemName} / {LocationName}";
}

public sealed record MaterialAcquisitionAdvice(
    string MaterialId,
    string MaterialName,
    EngineeringMaterialCategory Category,
    int Missing,
    IReadOnlyList<AcquisitionOption> Options);

public sealed record EngineeringSnapshot
{
    public static EngineeringSnapshot Empty { get; } = new();

    public string Commander { get; init; } = string.Empty;
    public DateTimeOffset? UpdatedUtc { get; init; }
    public IReadOnlyDictionary<string, MaterialInventoryEntry> Inventory { get; init; } =
        new ReadOnlyDictionary<string, MaterialInventoryEntry>(new Dictionary<string, MaterialInventoryEntry>());
    public IReadOnlyDictionary<string, EngineerProgressEntry> Engineers { get; init; } =
        new ReadOnlyDictionary<string, EngineerProgressEntry>(new Dictionary<string, EngineerProgressEntry>());
    public IReadOnlyList<WishlistEntry> Wishlist { get; init; } = Array.Empty<WishlistEntry>();
    public IReadOnlyList<TrackedMaterialEntry> TrackedMaterials { get; init; } = Array.Empty<TrackedMaterialEntry>();
    public IReadOnlyList<MaterialRequirement> Requirements { get; init; } = Array.Empty<MaterialRequirement>();
    public IReadOnlyList<MaterialAcquisitionAdvice> Advice { get; init; } = Array.Empty<MaterialAcquisitionAdvice>();

    public int MaterialKinds => Inventory.Count;
    public int TotalMaterials => Inventory.Values.Sum(item => item.Count);
    public int MissingKinds => Requirements.Count(item => !item.IsComplete);
}

public sealed class EngineeringStateChangedEventArgs(EngineeringSnapshot state) : EventArgs
{
    public EngineeringSnapshot State { get; } = state;
}
