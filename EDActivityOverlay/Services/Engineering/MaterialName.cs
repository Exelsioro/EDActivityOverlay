using System.Globalization;
using System.Text;

namespace EDActivityOverlay.Services.Engineering;

internal static class MaterialName
{
    // Coriolis blueprint ingredients use player-facing English names, while the
    // Journal uses Frontier material symbols. Most raw/manufactured symbols
    // normalize to the same identity, but encoded materials frequently do not.
    // Keep one canonical identity so inventory mutations and wishlist recipes
    // address the same material.
    private static readonly IReadOnlyDictionary<string, string> JournalMaterialAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Manufactured aliases whose Frontier symbols differ from display names.
            ["uncutfocuscrystals"] = "flawedfocuscrystals",
            ["fedproprietarycomposites"] = "proprietarycomposites",
            ["fedcorecomposites"] = "coredynamicscomposites",

            // Standard encoded-material aliases from Frontier/FDevIDs symbols.
            ["legacyfirmware"] = "specialisedlegacyfirmware",
            ["encryptedfiles"] = "unusualencryptedfiles",
            ["bulkscandata"] = "anomalousbulkscandata",
            ["disruptedwakeechoes"] = "atypicaldisruptedwakeechoes",
            ["scrambledemissiondata"] = "exceptionalscrambledemissiondata",
            ["shieldcyclerecordings"] = "distortedshieldcyclerecordings",
            ["consumerfirmware"] = "modifiedconsumerfirmware",
            ["encryptioncodes"] = "taggedencryptioncodes",
            ["scanarchives"] = "unidentifiedscanarchives",
            ["fsdtelemetry"] = "anomalousfsdtelemetry",
            ["archivedemissiondata"] = "irregularemissiondata",
            ["shieldsoakanalysis"] = "inconsistentshieldsoakanalysis",
            ["industrialfirmware"] = "crackedindustrialfirmware",
            ["symmetrickeys"] = "opensymmetrickeys",
            ["scandatabanks"] = "classifiedscandatabanks",
            ["wakesolutions"] = "strangewakesolutions",
            ["emissiondata"] = "unexpectedemissiondata",
            ["shielddensityreports"] = "untypicalshieldscans",
            ["securityfirmware"] = "securityfirmwarepatch",
            ["encryptionarchives"] = "atypicalencryptionarchives",
            ["encodedscandata"] = "divergentscandata",
            ["hyperspacetrajectories"] = "eccentrichyperspacetrajectories",
            ["shieldpatternanalysis"] = "aberrantshieldpatternanalysis",
            ["embeddedfirmware"] = "modifiedembeddedfirmware",
            ["adaptiveencryptors"] = "adaptiveencryptorscapture",
            ["classifiedscandata"] = "classifiedscanfragment",
            ["dataminedwake"] = "dataminedwakeexceptions",
            ["compactemissionsdata"] = "abnormalcompactemissionsdata",
            ["shieldfrequencydata"] = "peculiarshieldfrequencydata"
        };

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

        string normalized = result.ToString();
        return JournalMaterialAliases.TryGetValue(normalized, out string? canonical)
            ? canonical
            : normalized;
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
