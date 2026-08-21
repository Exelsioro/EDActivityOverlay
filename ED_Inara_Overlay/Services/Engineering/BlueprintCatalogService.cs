using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using ED_Inara_Overlay.Models;

namespace ED_Inara_Overlay.Services.Engineering;

public sealed class BlueprintCatalogService
{
    private const string BlueprintUrl =
        "https://raw.githubusercontent.com/EDCD/coriolis-data/master/modifications/blueprints.json";
    private const string ExperimentalUrl =
        "https://raw.githubusercontent.com/EDCD/coriolis-data/master/modifications/specials.json";
    private const string EngineerRecipeUrl =
        "https://raw.githubusercontent.com/EDDiscovery/EliteDangerousCore/master/EliteDangerous/FrontierData/Items/RecipesEngineering.cs";
    private readonly object sync = new();
    private readonly string cacheDirectory;
    private IReadOnlyList<BlueprintRecipe> recipes = StarterRecipes();

    public BlueprintCatalogService(string? cacheDirectory = null)
    {
        this.cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ED_Inara_Overlay",
            "catalogs");
    }

    public event EventHandler? CatalogChanged;

    public IReadOnlyList<BlueprintRecipe> Recipes
    {
        get
        {
            lock (sync)
            {
                return recipes;
            }
        }
    }

    public BlueprintRecipe? Find(string recipeId) =>
        Recipes.FirstOrDefault(recipe => string.Equals(recipe.Id, recipeId, StringComparison.OrdinalIgnoreCase));

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(cacheDirectory);
        string blueprintCache = Path.Combine(cacheDirectory, "coriolis-blueprints.json");
        string experimentalCache = Path.Combine(cacheDirectory, "coriolis-experimentals.json");
        string engineerRecipeCache = Path.Combine(cacheDirectory, "eddiscovery-engineer-recipes.cs");

        bool cacheLoaded = TryLoadFiles(blueprintCache, experimentalCache, engineerRecipeCache);
        try
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(12) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ED-Inara-Overlay/engineering-assistant");
            Task<string> blueprintsTask = client.GetStringAsync(BlueprintUrl, cancellationToken);
            Task<string> experimentalsTask = client.GetStringAsync(ExperimentalUrl, cancellationToken);
            Task<string> engineerRecipesTask = client.GetStringAsync(EngineerRecipeUrl, cancellationToken);
            await Task.WhenAll(blueprintsTask, experimentalsTask, engineerRecipesTask).ConfigureAwait(false);

            IReadOnlyList<BlueprintRecipe> parsed = Parse(
                blueprintsTask.Result, experimentalsTask.Result, engineerRecipesTask.Result);
            if (parsed.Count == 0)
            {
                throw new InvalidDataException("Coriolis engineering catalog did not contain recipes.");
            }

            await File.WriteAllTextAsync(blueprintCache, blueprintsTask.Result, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(experimentalCache, experimentalsTask.Result, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(engineerRecipeCache, engineerRecipesTask.Result, cancellationToken).ConfigureAwait(false);
            SetRecipes(parsed);
            Logger.Logger.Info($"Engineering catalog updated from Coriolis data: {parsed.Count} recipes.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            Logger.Logger.Warning($"Engineering catalog update unavailable; using {(cacheLoaded ? "cached" : "starter")} data: {ex.Message}");
        }
    }

    internal static IReadOnlyList<BlueprintRecipe> Parse(
        string blueprintJson,
        string? experimentalJson = null,
        string? engineerRecipeSource = null)
    {
        List<BlueprintRecipe> result = new();
        using (JsonDocument document = JsonDocument.Parse(blueprintJson))
        {
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                JsonElement blueprint = property.Value;
                string fdName = GetString(blueprint, "fdname", property.Name);
                string name = GetString(blueprint, "name", fdName);
                string module = GetModuleName(blueprint);
                if (!blueprint.TryGetProperty("grades", out JsonElement grades)
                    || grades.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (JsonProperty gradeProperty in grades.EnumerateObject())
                {
                    if (!int.TryParse(gradeProperty.Name, out int grade))
                    {
                        continue;
                    }
                    IReadOnlyList<BlueprintIngredient> ingredients = ParseIngredients(gradeProperty.Value);
                    if (ingredients.Count == 0)
                    {
                        continue;
                    }
                    result.Add(new BlueprintRecipe(
                        $"{fdName}:G{grade}", name, module, grade, false, ingredients));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(experimentalJson))
        {
            using JsonDocument document = JsonDocument.Parse(experimentalJson);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                JsonElement experimental = property.Value;
                IReadOnlyList<BlueprintIngredient> ingredients = ParseIngredients(experimental);
                if (ingredients.Count == 0)
                {
                    continue;
                }
                string edName = GetString(experimental, "edname", property.Name);
                result.Add(new BlueprintRecipe(
                    $"experimental:{edName}",
                    GetString(experimental, "name", edName),
                    "Experimental effect",
                    0,
                    true,
                    ingredients));
            }
        }

        IReadOnlyDictionary<string, IReadOnlyList<string>> engineerMap = ParseEngineerMap(engineerRecipeSource);
        return result
            .Select(recipe => engineerMap.TryGetValue(recipe.Id, out IReadOnlyList<string>? engineers)
                ? recipe with { Engineers = engineers }
                : recipe)
            .OrderBy(recipe => recipe.ModuleName)
            .ThenBy(recipe => recipe.BlueprintName)
            .ThenBy(recipe => recipe.Grade)
            .ToArray();
    }

    private bool TryLoadFiles(string blueprintPath, string experimentalPath, string engineerRecipePath)
    {
        try
        {
            if (!File.Exists(blueprintPath))
            {
                return false;
            }
            string blueprints = File.ReadAllText(blueprintPath);
            string? experimentals = File.Exists(experimentalPath) ? File.ReadAllText(experimentalPath) : null;
            string? engineerRecipes = File.Exists(engineerRecipePath) ? File.ReadAllText(engineerRecipePath) : null;
            IReadOnlyList<BlueprintRecipe> parsed = Parse(blueprints, experimentals, engineerRecipes);
            if (parsed.Count == 0)
            {
                return false;
            }
            SetRecipes(parsed);
            Logger.Logger.Info($"Engineering catalog loaded from cache: {parsed.Count} recipes.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Logger.Logger.Warning($"Engineering catalog cache is invalid: {ex.Message}");
            return false;
        }
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseEngineerMap(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        }

        // EDDiscovery keeps the exact engineer list on each ship recipe. Its fdname and
        // level correspond to Coriolis' blueprint fdname/Gn key, so the catalogs can be joined.
        const string pattern = "new\\s+EngineeringRecipe\\(\\s*\"[^\"]*\"\\s*,\\s*\"(?<fd>[^\"]+)\"\\s*,\\s*\"[^\"]*\"\\s*,\\s*[^,\\r\\n]+\\s*,\\s*(?<grade>[1-5])\\s*,\\s*\"(?<engineers>[^\"]*)\"";
        Dictionary<string, HashSet<string>> builders = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(source, pattern, RegexOptions.CultureInvariant))
        {
            string key = $"{match.Groups["fd"].Value}:G{match.Groups["grade"].Value}";
            if (!builders.TryGetValue(key, out HashSet<string>? names))
            {
                names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                builders[key] = names;
            }
            foreach (string name in match.Groups["engineers"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                names.Add(name);
            }
        }
        return builders.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.OrderBy(name => name).ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private void SetRecipes(IReadOnlyList<BlueprintRecipe> value)
    {
        lock (sync)
        {
            recipes = value;
        }
        CatalogChanged?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<BlueprintIngredient> ParseIngredients(JsonElement container)
    {
        if (!container.TryGetProperty("components", out JsonElement components)
            || components.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<BlueprintIngredient>();
        }

        List<BlueprintIngredient> result = new();
        foreach (JsonProperty component in components.EnumerateObject())
        {
            if (component.Value.TryGetInt32(out int count) && count > 0)
            {
                result.Add(new BlueprintIngredient(
                    MaterialName.Normalize(component.Name),
                    component.Name,
                    count));
            }
        }
        return result;
    }

    private static string GetModuleName(JsonElement blueprint)
    {
        if (!blueprint.TryGetProperty("modulename", out JsonElement module))
        {
            return "Module";
        }
        if (module.ValueKind == JsonValueKind.String)
        {
            return module.GetString() ?? "Module";
        }
        if (module.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in module.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                {
                    return item.GetString()!;
                }
            }
        }
        return "Module";
    }

    private static string GetString(JsonElement element, string property, string fallback) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static IReadOnlyList<BlueprintRecipe> StarterRecipes()
    {
        static BlueprintRecipe Recipe(string id, string name, string module, int grade,
            params (string Name, int Count)[] ingredients) =>
            new(id, name, module, grade, false,
                ingredients.Select(item => new BlueprintIngredient(
                    MaterialName.Normalize(item.Name),
                    item.Name,
                    item.Count)).ToArray());

        return new[]
        {
            Recipe("FSD_LongRange:G1", "Increased range", "Frame shift drive", 1,
                ("Atypical Disrupted Wake Echoes", 1)),
            Recipe("FSD_LongRange:G2", "Increased range", "Frame shift drive", 2,
                ("Atypical Disrupted Wake Echoes", 1), ("Chemical Processors", 1)),
            Recipe("FSD_LongRange:G3", "Increased range", "Frame shift drive", 3,
                ("Chemical Processors", 1), ("Phosphorus", 1), ("Strange Wake Solutions", 1)),
            Recipe("FSD_LongRange:G4", "Increased range", "Frame shift drive", 4,
                ("Chemical Distillery", 1), ("Eccentric Hyperspace Trajectories", 1), ("Manganese", 1)),
            Recipe("FSD_LongRange:G5", "Increased range", "Frame shift drive", 5,
                ("Arsenic", 1), ("Chemical Manipulators", 1), ("Datamined Wake Exceptions", 1)),
            Recipe("Engine_Dirty:G5", "Dirty", "Thrusters", 5,
                ("Cadmium", 1), ("Cracked Industrial Firmware", 1), ("Pharmaceutical Isolators", 1)),
            Recipe("PowerDistributor_HighFrequency:G5", "Charge enhanced", "Power distributor", 5,
                ("Chemical Manipulators", 1), ("Cracked Industrial Firmware", 1), ("Exquisite Focus Crystals", 1)),
            Recipe("PowerPlant_Boosted:G5", "Overcharged", "Power plant", 5,
                ("Chemical Manipulators", 1), ("Conductive Ceramics", 1), ("Tellurium", 1)),
            Recipe("Sensor_LightWeight:G5", "Lightweight", "Sensors", 5,
                ("Conductive Ceramics", 1), ("Proto Light Alloys", 1), ("Proto Radiolic Alloys", 1)),
            Recipe("ShieldGenerator_Reinforced:G5", "Reinforced", "Shield generator", 5,
                ("Arsenic", 1), ("Conductive Polymers", 1), ("Improvised Components", 1))
        };
    }
}
