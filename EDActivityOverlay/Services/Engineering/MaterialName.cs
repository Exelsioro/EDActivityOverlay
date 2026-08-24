using System.Globalization;
using System.Text;

namespace EDActivityOverlay.Services.Engineering;

internal static class MaterialName
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string source = value.Trim().Trim('$', ';');
        if (source.EndsWith("_name", StringComparison.OrdinalIgnoreCase))
        {
            source = source[..^5];
        }

        StringBuilder result = new(source.Length);
        foreach (char character in source.Normalize(NormalizationForm.FormD))
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark || !char.IsLetterOrDigit(character))
            {
                continue;
            }
            result.Append(char.ToLowerInvariant(character));
        }
        return result.ToString();
    }

    public static string Friendly(string? internalName)
    {
        if (string.IsNullOrWhiteSpace(internalName))
        {
            return Loc.Get("Loc_Unknown_material");
        }

        string value = internalName.Trim().Trim('$', ';');
        if (value.EndsWith("_name", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^5];
        }
        return string.Join(' ', value
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }
}
