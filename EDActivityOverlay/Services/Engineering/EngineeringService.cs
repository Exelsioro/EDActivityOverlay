using System.Collections.ObjectModel;
using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.Services.Engineering;

public sealed class EngineeringService : IJournalDataConsumer, IDisposable
{
    private readonly object sync = new();
    private readonly EngineeringRepository repository;
    private readonly MaterialAcquisitionAdvisor advisor = new();
    private readonly Dictionary<string, MaterialInventoryEntry> shipMaterials = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MaterialInventoryEntry> shipLocker = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MaterialInventoryEntry> backpack = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EngineerProgressEntry> engineers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<WishlistEntry> wishlist;
    private readonly List<TrackedMaterialEntry> trackedMaterials;
    private EngineeringSnapshot state = EngineeringSnapshot.Empty;
    private bool started;
    private bool disposed;

    private static readonly Lazy<EngineeringService> Shared = new(() => new EngineeringService());
    public static EngineeringService Instance => Shared.Value;

    public BlueprintCatalogService Catalog { get; }

    public EngineeringSnapshot Current
    {
        get
        {
            lock (sync)
            {
                return state;
            }
        }
    }

    public event EventHandler<EngineeringStateChangedEventArgs>? StateChanged;

    private EngineeringService()
        : this(new EngineeringRepository(), new BlueprintCatalogService())
    {
    }

    internal EngineeringService(EngineeringRepository repository, BlueprintCatalogService catalog)
    {
        this.repository = repository;
        Catalog = catalog;
        wishlist = this.repository.LoadWishlist().ToList();
        trackedMaterials = this.repository.LoadTrackedMaterials().ToList();
        Catalog.CatalogChanged += OnCatalogChanged;
        RebuildSnapshot(DateTimeOffset.UtcNow, persist: false);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (started)
        {
            return;
        }
        JournalMonitorService.Instance.Events.Register(this);
        started = true;
        _ = Catalog.LoadAsync();
        Logger.Logger.Info("Engineering service started.");
    }

    public void RefreshLocalization()
    {
        RebuildSnapshot(DateTimeOffset.UtcNow, persist: false);
        RaiseChanged();
    }

    public void AddOrIncreaseWishlist(BlueprintRecipe recipe, int craftCount)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        craftCount = Math.Clamp(craftCount, 1, 99);
        lock (sync)
        {
            WishlistEntry? existing = wishlist.FirstOrDefault(item =>
                string.Equals(item.RecipeId, recipe.Id, StringComparison.OrdinalIgnoreCase));
            WishlistEntry updated = existing is null
                ? new WishlistEntry(Guid.NewGuid().ToString("N"), recipe.Id, recipe.DisplayName, craftCount, DateTimeOffset.UtcNow)
                : existing with { CraftCount = Math.Clamp(existing.CraftCount + craftCount, 1, 999), DisplayName = recipe.DisplayName };
            if (existing is not null)
            {
                wishlist.Remove(existing);
            }
            wishlist.Add(updated);
            repository.UpsertWishlist(updated);
            RebuildSnapshotLocked(DateTimeOffset.UtcNow, persist: false);
        }
        RaiseChanged();
    }

    public void AddGradePathToWishlist(
        IEnumerable<BlueprintRecipe> recipes,
        int pathCount = 1)
    {
        ArgumentNullException.ThrowIfNull(
            recipes);

        BlueprintRecipe[] path =
            recipes
                .Where(
                    recipe =>
                        !recipe.IsExperimental)
                .OrderBy(
                    recipe =>
                        recipe.Grade)
                .ToArray();

        if (path.Length == 0)
        {
            return;
        }

        pathCount =
            Math.Clamp(
                pathCount,
                1,
                99);

        lock (sync)
        {
            foreach (BlueprintRecipe recipe
                     in path)
            {
                int applications =
                    Math.Clamp(
                        Math.Max(
                            1,
                            recipe.Grade)
                        * pathCount,
                        1,
                        999);

                WishlistEntry? existing =
                    wishlist.FirstOrDefault(
                        item =>
                            string.Equals(
                                item.RecipeId,
                                recipe.Id,
                                StringComparison.OrdinalIgnoreCase));

                WishlistEntry updated =
                    existing is null
                        ? new WishlistEntry(
                            Guid.NewGuid()
                                .ToString(
                                    "N"),
                            recipe.Id,
                            recipe.DisplayName,
                            applications,
                            DateTimeOffset.UtcNow)
                        : existing with
                        {
                            CraftCount =
                                Math.Clamp(
                                    existing.CraftCount
                                    + applications,
                                    1,
                                    999),
                            DisplayName =
                                recipe.DisplayName
                        };

                if (existing is not null)
                {
                    wishlist.Remove(
                        existing);
                }

                wishlist.Add(
                    updated);

                repository.UpsertWishlist(
                    updated);
            }

            RebuildSnapshotLocked(
                DateTimeOffset.UtcNow,
                persist: false);
        }

        RaiseChanged();
    }
    public void SetWishlistCount(string wishlistId, int craftCount)
    {
        lock (sync)
        {
            WishlistEntry? existing = wishlist.FirstOrDefault(item => item.Id == wishlistId);
            if (existing is null)
            {
                return;
            }
            WishlistEntry updated = existing with { CraftCount = Math.Clamp(craftCount, 1, 999) };
            wishlist.Remove(existing);
            wishlist.Add(updated);
            repository.UpsertWishlist(updated);
            RebuildSnapshotLocked(DateTimeOffset.UtcNow, persist: false);
        }
        RaiseChanged();
    }

    public void RemoveWishlist(string wishlistId)
    {
        lock (sync)
        {
            WishlistEntry? existing = wishlist.FirstOrDefault(item => item.Id == wishlistId);
            if (existing is null)
            {
                return;
            }
            wishlist.Remove(existing);
            repository.RemoveWishlist(wishlistId);
            RebuildSnapshotLocked(DateTimeOffset.UtcNow, persist: false);
        }
        RaiseChanged();
    }

    public void ToggleTrackedMaterial(MaterialInventoryEntry material)
    {
        ArgumentNullException.ThrowIfNull(material);
        lock (sync)
        {
            TrackedMaterialEntry? existing = trackedMaterials.FirstOrDefault(item =>
                item.MaterialId.Equals(material.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                trackedMaterials.Remove(existing);
                repository.RemoveTrackedMaterial(existing.MaterialId);
            }
            else
            {
                TrackedMaterialEntry tracked = new(
                    material.Id,
                    material.Name,
                    material.Category,
                    Math.Max(10, material.Count + 10),
                    DateTimeOffset.UtcNow);
                trackedMaterials.Add(tracked);
                repository.UpsertTrackedMaterial(tracked);
            }
            RebuildSnapshotLocked(DateTimeOffset.UtcNow, persist: false);
        }
        RaiseChanged();
    }

    public void OnJournalEvent(
        JournalEventReceivedEventArgs journalEvent)
    {
        bool changed;

        lock (sync)
        {
            changed =
                ApplyJournalEvent(
                    journalEvent.EventName,
                    journalEvent.Timestamp,
                    journalEvent.Data);

            if (changed)
            {
                RebuildSnapshotLocked(
                    journalEvent.Timestamp,
                    persist: true);
            }
        }

        if (changed)
        {
            RaiseChanged();

            if (journalEvent.Origin
                == JournalEventOrigin.Live
                && IsInventoryMutationEvent(
                    journalEvent.EventName))
            {
                Logger.Logger.Info(
                    $"Engineering inventory updated from live journal event: {journalEvent.EventName}");
            }
        }
    }
    public void OnCompanionFile(CompanionFileReceivedEventArgs companionFile)
    {
        bool changed = false;
        lock (sync)
        {
            if (companionFile.FileName.Equals("Backpack.json", StringComparison.OrdinalIgnoreCase))
            {
                ReplaceOdysseyInventory(companionFile.Data, backpack);
                changed = true;
            }
            else if (companionFile.FileName.Equals("ShipLocker.json", StringComparison.OrdinalIgnoreCase))
            {
                ReplaceOdysseyInventory(companionFile.Data, shipLocker);
                changed = true;
            }

            if (changed)
            {
                RebuildSnapshotLocked(companionFile.Timestamp, persist: true);
            }
        }
        if (changed)
        {
            RaiseChanged();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        if (started)
        {
            JournalMonitorService.Instance.Events.Unregister(this);
            started = false;
        }
        Catalog.CatalogChanged -= OnCatalogChanged;
        disposed = true;
    }

    private bool ApplyJournalEvent(
        string eventName,
        DateTimeOffset timestamp,
        JsonElement root)
    {
        switch (eventName.ToLowerInvariant())
        {
            case "loadgame":
                LoadCommander(
                    GetString(
                        root,
                        "Commander"));
                return true;

            case "materials":
                ReplaceShipMaterials(
                    root,
                    timestamp);
                return true;

            case "materialcollected":
                ApplyShipDelta(
                    root,
                    "Name",
                    "Count",
                    +1,
                    timestamp);
                return true;

            case "materialdiscarded":
                ApplyShipDelta(
                    root,
                    "Name",
                    "Count",
                    -1,
                    timestamp);
                return true;

            case "materialtrade":
                if (root.TryGetProperty(
                        "Paid",
                        out JsonElement paid))
                {
                    ApplyShipDelta(
                        paid,
                        "Material",
                        "Quantity",
                        -1,
                        timestamp);
                }

                if (root.TryGetProperty(
                        "Received",
                        out JsonElement received))
                {
                    ApplyShipDelta(
                        received,
                        "Material",
                        "Quantity",
                        +1,
                        timestamp);
                }

                return true;

            case "engineercraft":
                SubtractArray(
                    root,
                    "Ingredients",
                    shipMaterials,
                    timestamp);

                SubtractMaterialContainer(
                    root,
                    "Materials",
                    shipMaterials,
                    timestamp);

                return true;

            case "synthesis":
                SubtractMaterialContainer(
                    root,
                    "Materials",
                    shipMaterials,
                    timestamp);
                return true;

            case "technologybroker":
                SubtractMaterialContainer(
                    root,
                    "Materials",
                    shipMaterials,
                    timestamp);
                return true;

            case "engineercontribution":
                if (GetString(
                        root,
                        "Type")
                    .Equals(
                        "Material",
                        StringComparison.OrdinalIgnoreCase))
                {
                    ApplyShipDelta(
                        root,
                        "Material",
                        "Quantity",
                        -1,
                        timestamp);
                }

                return true;

            case "missioncompleted":
                AddArray(
                    root,
                    "MaterialsReward",
                    shipMaterials,
                    timestamp);
                return true;

            case "engineerprogress":
                ApplyEngineerProgress(
                    root);
                return true;

            case "shiplocker":
                if (HasInventoryArrays(
                        root))
                {
                    ReplaceOdysseyInventory(
                        root,
                        shipLocker);

                    return true;
                }

                return false;

            case "backpack":
                if (HasInventoryArrays(
                        root))
                {
                    ReplaceOdysseyInventory(
                        root,
                        backpack);

                    return true;
                }

                return false;

            case "backpackchange":
                AddArray(
                    root,
                    "Added",
                    backpack,
                    timestamp);

                SubtractArray(
                    root,
                    "Removed",
                    backpack,
                    timestamp);

                return true;

            case "buymicroresources":
            case "buymicroresource":
                ApplyMicroResourcePurchase(
                    root,
                    +1,
                    timestamp);
                return true;

            case "sellmicroresources":
            case "sellmicroresource":
                ApplyMicroResourcePurchase(
                    root,
                    -1,
                    timestamp);
                return true;

            case "trademicroresources":
                SubtractArray(
                    root,
                    "Offered",
                    shipLocker,
                    timestamp);

                ApplyReceivedMicroResource(
                    root,
                    timestamp);

                return true;

            case "upgradesuit":
            case "upgradeweapon":
            case "applyweaponmods":
            case "applysuitmods":
                SubtractArray(
                    root,
                    "Resources",
                    shipLocker,
                    timestamp);

                SubtractArray(
                    root,
                    "Ingredients",
                    shipLocker,
                    timestamp);

                return true;

            default:
                return false;
        }
    }

    private static bool IsInventoryMutationEvent(
        string eventName) =>
        eventName.ToLowerInvariant()
            is "materials"
            or "materialcollected"
            or "materialdiscarded"
            or "materialtrade"
            or "engineercraft"
            or "synthesis"
            or "technologybroker"
            or "engineercontribution"
            or "missioncompleted"
            or "shiplocker"
            or "backpack"
            or "backpackchange"
            or "buymicroresources"
            or "buymicroresource"
            or "sellmicroresources"
            or "sellmicroresource"
            or "trademicroresources"
            or "upgradesuit"
            or "upgradeweapon"
            or "applyweaponmods"
            or "applysuitmods";
    private void LoadCommander(string commander)
    {
        if (string.IsNullOrWhiteSpace(commander) || string.Equals(state.Commander, commander, StringComparison.Ordinal))
        {
            return;
        }
        shipMaterials.Clear();
        shipLocker.Clear();
        backpack.Clear();
        engineers.Clear();
        var cached = repository.LoadCommanderState(commander);
        foreach ((string key, MaterialInventoryEntry value) in cached.Inventory)
        {
            DestinationFor(value.Category)[key] = value;
        }
        foreach ((string key, EngineerProgressEntry value) in cached.Engineers)
        {
            engineers[key] = value;
        }
        state = state with { Commander = commander };
    }

    private void ReplaceShipMaterials(JsonElement root, DateTimeOffset timestamp)
    {
        shipMaterials.Clear();
        ReadInventoryArray(root, "Raw", EngineeringMaterialCategory.Raw, shipMaterials, timestamp);
        ReadInventoryArray(root, "Manufactured", EngineeringMaterialCategory.Manufactured, shipMaterials, timestamp);
        ReadInventoryArray(root, "Encoded", EngineeringMaterialCategory.Encoded, shipMaterials, timestamp);
    }

    private static void ReplaceOdysseyInventory(
        JsonElement root,
        Dictionary<string, MaterialInventoryEntry> destination)
    {
        destination.Clear();
        DateTimeOffset timestamp = GetTimestamp(root);
        ReadInventoryArray(root, "Items", EngineeringMaterialCategory.Item, destination, timestamp);
        ReadInventoryArray(root, "Components", EngineeringMaterialCategory.Component, destination, timestamp);
        ReadInventoryArray(root, "Data", EngineeringMaterialCategory.Data, destination, timestamp);
        ReadInventoryArray(root, "Consumables", EngineeringMaterialCategory.Consumable, destination, timestamp);
    }

    private void ApplyShipDelta(JsonElement element, string nameProperty, string countProperty, int direction, DateTimeOffset timestamp)
    {
        string rawName = GetString(element, nameProperty);
        string id = MaterialName.Normalize(rawName);
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }
        int amount = GetInt(element, countProperty, 1) * direction;
        EngineeringMaterialCategory category = ParseCategory(GetString(element, "Category"));
        if (category == EngineeringMaterialCategory.Unknown)
        {
            category = advisor.InferCategory(id, MergeInventory());
        }
        ApplyDelta(shipMaterials, id, DisplayName(element, nameProperty, rawName), category, amount, timestamp);
    }

    private void ApplyMicroResourcePurchase(JsonElement root, int direction, DateTimeOffset timestamp)
    {
        if (root.TryGetProperty("MicroResources", out JsonElement resources) && resources.ValueKind == JsonValueKind.Array)
        {
            ApplyArray(resources, shipLocker, direction, timestamp);
            return;
        }
        ApplyElement(root, shipLocker, direction, timestamp);
    }

    private void ApplyReceivedMicroResource(JsonElement root, DateTimeOffset timestamp)
    {
        string rawName = GetString(root, "Received");
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return;
        }
        JsonElement categorySource = root;
        string id = MaterialName.Normalize(rawName);
        ApplyDelta(
            shipLocker,
            id,
            GetString(root, "Received_Localised", MaterialName.Friendly(rawName)),
            ParseCategory(GetString(categorySource, "Category")),
            GetInt(root, "Count", 1),
            timestamp);
    }

    private static void AddArray(JsonElement root, string property, Dictionary<string, MaterialInventoryEntry> destination, DateTimeOffset timestamp)
    {
        if (root.TryGetProperty(property, out JsonElement items) && items.ValueKind == JsonValueKind.Array)
        {
            ApplyArray(items, destination, +1, timestamp);
        }
    }

    private static void SubtractArray(
        JsonElement root,
        string property,
        Dictionary<string, MaterialInventoryEntry> destination,
        DateTimeOffset timestamp)
    {
        if (root.TryGetProperty(
                property,
                out JsonElement items)
            && items.ValueKind
               == JsonValueKind.Array)
        {
            ApplyArray(
                items,
                destination,
                -1,
                timestamp);
        }
    }

    private static void SubtractMaterialContainer(
        JsonElement root,
        string property,
        Dictionary<string, MaterialInventoryEntry> destination,
        DateTimeOffset timestamp)
    {
        if (!root.TryGetProperty(
                property,
                out JsonElement materials))
        {
            return;
        }

        if (materials.ValueKind
            == JsonValueKind.Array)
        {
            ApplyArray(
                materials,
                destination,
                -1,
                timestamp);

            return;
        }

        if (materials.ValueKind
            != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty material
                 in materials.EnumerateObject())
        {
            if (!material.Value.TryGetInt32(
                    out int count)
                || count <= 0)
            {
                continue;
            }

            string id =
                MaterialName.Normalize(
                    material.Name);

            if (string.IsNullOrWhiteSpace(
                    id))
            {
                continue;
            }

            ApplyDelta(
                destination,
                id,
                MaterialName.Friendly(
                    material.Name),
                EngineeringMaterialCategory.Unknown,
                -count,
                timestamp);
        }
    }

    private static void ApplyArray(
        JsonElement items,
        Dictionary<string, MaterialInventoryEntry> destination,
        int direction,
        DateTimeOffset timestamp)
    {
        foreach (JsonElement item in items.EnumerateArray())
        {
            ApplyElement(item, destination, direction, timestamp);
        }
    }

    private static void ApplyElement(
        JsonElement item,
        Dictionary<string, MaterialInventoryEntry> destination,
        int direction,
        DateTimeOffset timestamp)
    {
        string rawName = GetString(item, "Name");
        string id = MaterialName.Normalize(rawName);
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }
        ApplyDelta(
            destination,
            id,
            DisplayName(item, "Name", rawName),
            ParseCategory(GetString(item, "Category", GetString(item, "Type"))),
            GetInt(item, "Count", GetInt(item, "Quantity", 1)) * direction,
            timestamp);
    }

    private static void ApplyDelta(
        Dictionary<string, MaterialInventoryEntry> destination,
        string id,
        string name,
        EngineeringMaterialCategory category,
        int amount,
        DateTimeOffset timestamp)
    {
        destination.TryGetValue(id, out MaterialInventoryEntry? existing);
        int count = Math.Max(0, (existing?.Count ?? 0) + amount);
        destination[id] = new MaterialInventoryEntry(
            id,
            string.IsNullOrWhiteSpace(name) ? existing?.Name ?? MaterialName.Friendly(id) : name,
            category == EngineeringMaterialCategory.Unknown ? existing?.Category ?? category : category,
            count,
            existing?.Maximum,
            timestamp);
    }

    private void ApplyEngineerProgress(JsonElement root)
    {
        if (root.TryGetProperty("Engineers", out JsonElement collection) && collection.ValueKind == JsonValueKind.Array)
        {
            engineers.Clear();
            foreach (JsonElement item in collection.EnumerateArray())
            {
                ApplySingleEngineer(item);
            }
            return;
        }
        ApplySingleEngineer(root);
    }

    private void ApplySingleEngineer(JsonElement root)
    {
        string name = GetString(root, "Engineer");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        engineers[MaterialName.Normalize(name)] = new EngineerProgressEntry(
            GetNullableLong(root, "EngineerID"),
            name,
            GetString(root, "Progress"),
            GetInt(root, "Rank"),
            GetInt(root, "RankProgress"));
    }

    private void RebuildSnapshot(DateTimeOffset timestamp, bool persist)
    {
        lock (sync)
        {
            RebuildSnapshotLocked(timestamp, persist);
        }
    }

    private void RebuildSnapshotLocked(DateTimeOffset timestamp, bool persist)
    {
        Dictionary<string, MaterialInventoryEntry> inventory = MergeInventory();
        List<MaterialRequirement> requirements = new();
        foreach (IGrouping<string, (BlueprintIngredient Ingredient, int Multiplier)> group in wishlist
            .Select(item => (Item: item, Recipe: Catalog.Find(item.RecipeId)))
            .Where(pair => pair.Recipe is not null)
            .SelectMany(pair => pair.Recipe!.Ingredients.Select(ingredient => (Ingredient: ingredient, Multiplier: pair.Item.CraftCount)))
            .GroupBy(pair => pair.Ingredient.MaterialId, StringComparer.OrdinalIgnoreCase))
        {
            BlueprintIngredient first = group.First().Ingredient;
            int required = group.Sum(item => item.Ingredient.Count * item.Multiplier);
            int available = inventory.TryGetValue(group.Key, out MaterialInventoryEntry? item) ? item.Count : 0;
            EngineeringMaterialCategory category = advisor.InferCategory(group.Key, inventory);
            string localizedName = EngineeringLocalization.MaterialName(group.Key, item?.Name ?? first.Name);
            requirements.Add(new MaterialRequirement(group.Key, localizedName, category, required, available));
        }
        foreach (TrackedMaterialEntry tracked in trackedMaterials)
        {
            int available = inventory.TryGetValue(tracked.MaterialId, out MaterialInventoryEntry? item) ? item.Count : 0;
            MaterialRequirement? existing = requirements.FirstOrDefault(requirement =>
                requirement.MaterialId.Equals(tracked.MaterialId, StringComparison.OrdinalIgnoreCase));
            int required = Math.Max(existing?.Required ?? 0, tracked.TargetCount);
            if (existing is not null)
            {
                requirements.Remove(existing);
            }
            requirements.Add(new MaterialRequirement(
                tracked.MaterialId,
                EngineeringLocalization.MaterialName(tracked.MaterialId, item?.Name ?? tracked.DisplayName),
                item?.Category ?? tracked.Category,
                required,
                available));
        }
        requirements = requirements
            .OrderBy(requirement => requirement.IsComplete)
            .ThenByDescending(requirement => requirement.Missing)
            .ThenBy(requirement => requirement.Name)
            .ToList();
        MaterialAcquisitionAdvice[] advice = requirements
            .Where(requirement => !requirement.IsComplete)
            .Select(advisor.Create)
            .ToArray();

        WishlistEntry[] localizedWishlist = wishlist
            .Select(item => Catalog.Find(item.RecipeId) is BlueprintRecipe recipe
                ? item with { DisplayName = recipe.DisplayName }
                : item)
            .OrderBy(item => item.CreatedUtc)
            .ToArray();

        state = state with
        {
            UpdatedUtc = timestamp,
            Inventory = new ReadOnlyDictionary<string, MaterialInventoryEntry>(inventory),
            Engineers = new ReadOnlyDictionary<string, EngineerProgressEntry>(
                new Dictionary<string, EngineerProgressEntry>(engineers, StringComparer.OrdinalIgnoreCase)),
            Wishlist = localizedWishlist,
            TrackedMaterials = trackedMaterials.ToArray(),
            Requirements = requirements,
            Advice = advice
        };
        if (persist)
        {
            repository.SaveCommanderState(state.Commander, inventory.Values, engineers.Values);
        }
    }

    private Dictionary<string, MaterialInventoryEntry> MergeInventory()
    {
        Dictionary<string, MaterialInventoryEntry> merged = new(StringComparer.OrdinalIgnoreCase);
        foreach (Dictionary<string, MaterialInventoryEntry> source in new[] { shipMaterials, shipLocker, backpack })
        {
            foreach (MaterialInventoryEntry item in source.Values)
            {
                if (merged.TryGetValue(item.Id, out MaterialInventoryEntry? existing))
                {
                    merged[item.Id] = existing with
                    {
                        Count = existing.Count + item.Count,
                        Name = EngineeringLocalization.MaterialName(item.Id, PreferName(existing.Name, item.Name)),
                        UpdatedUtc = item.UpdatedUtc ?? existing.UpdatedUtc
                    };
                }
                else
                {
                    merged[item.Id] = item with { Name = EngineeringLocalization.MaterialName(item.Id, item.Name) };
                }
            }
        }
        return merged;
    }

    private Dictionary<string, MaterialInventoryEntry> DestinationFor(EngineeringMaterialCategory category) => category switch
    {
        EngineeringMaterialCategory.Raw or EngineeringMaterialCategory.Manufactured or EngineeringMaterialCategory.Encoded => shipMaterials,
        _ => shipLocker
    };

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        RebuildSnapshot(DateTimeOffset.UtcNow, persist: false);
        RaiseChanged();
    }

    private void RaiseChanged() => StateChanged?.Invoke(this, new EngineeringStateChangedEventArgs(Current));

    private static void ReadInventoryArray(
        JsonElement root,
        string property,
        EngineeringMaterialCategory category,
        Dictionary<string, MaterialInventoryEntry> destination,
        DateTimeOffset timestamp)
    {
        if (!root.TryGetProperty(property, out JsonElement items) || items.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        foreach (JsonElement item in items.EnumerateArray())
        {
            string rawName = GetString(item, "Name");
            string id = MaterialName.Normalize(rawName);
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }
            destination[id] = new MaterialInventoryEntry(
                id,
                EngineeringLocalization.MaterialName(id, DisplayName(item, "Name", rawName)),
                category,
                GetInt(item, "Count"),
                null,
                timestamp);
        }
    }

    private static bool HasInventoryArrays(JsonElement root) =>
        root.TryGetProperty("Items", out _) || root.TryGetProperty("Components", out _)
        || root.TryGetProperty("Data", out _) || root.TryGetProperty("Consumables", out _);

    private static string PreferName(string left, string right) =>
        right.Contains(' ') && !left.Contains(' ') ? right : left;

    private static string DisplayName(JsonElement element, string nameProperty, string rawName)
    {
        string localizedProperty = nameProperty + "_Localised";
        return GetString(element, localizedProperty, MaterialName.Friendly(rawName));
    }

    private static EngineeringMaterialCategory ParseCategory(string value)
    {
        string normalized = value.Trim().Trim('$', ';');
        int marker = normalized.LastIndexOf('_');
        if (marker >= 0)
        {
            normalized = normalized[(marker + 1)..];
        }
        return Enum.TryParse(normalized, true, out EngineeringMaterialCategory category)
            ? category
            : EngineeringMaterialCategory.Unknown;
    }

    private static string GetString(JsonElement element, string property, string fallback = "") =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static int GetInt(JsonElement element, string property, int fallback = 0) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : fallback;

    private static long? GetNullableLong(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetInt64(out long result)
            ? result
            : null;

    private static DateTimeOffset GetTimestamp(JsonElement root) =>
        DateTimeOffset.TryParse(GetString(root, "timestamp"), out DateTimeOffset result)
            ? result
            : DateTimeOffset.UtcNow;
}
