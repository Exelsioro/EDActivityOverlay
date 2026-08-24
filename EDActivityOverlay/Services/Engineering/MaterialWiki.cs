namespace EDActivityOverlay.Services.Engineering;

public static class MaterialWiki
{
    private const string BaseUrl = "https://elite-dangerous.fandom.com/wiki/";

    public static string? GetArticleUrl(
        string materialId,
        string fallbackName,
        IEnumerable<Models.BlueprintRecipe> recipes)
    {
        string? canonicalName = recipes
            .SelectMany(recipe => recipe.Ingredients)
            .FirstOrDefault(ingredient => ingredient.MaterialId.Equals(
                materialId, StringComparison.OrdinalIgnoreCase))?.Name;

        if (string.IsNullOrWhiteSpace(canonicalName) && IsLatin(fallbackName))
        {
            canonicalName = fallbackName;
        }
        if (string.IsNullOrWhiteSpace(canonicalName))
        {
            return null;
        }

        string article = canonicalName.Trim().Replace(' ', '_');
        return BaseUrl + Uri.EscapeDataString(article).Replace("%27", "'");
    }

    private static bool IsLatin(string value) => value.Any(char.IsLetter)
        && value.Where(char.IsLetter).All(character => character <= '\u024F');
}
