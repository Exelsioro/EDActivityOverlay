using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Exploration;

/// <summary>
/// Compact, offline subset of the Canonn Bioforge statistics published by
/// Elite Dangerous Warboard (MIT). Values are ranges across known variants;
/// they are deliberately presented as estimates until the species is scanned.
/// </summary>
internal static class ExobiologyCatalog
{
    private sealed record Entry(string Key, int Range, long Min, long Max, params string[] Aliases);

    private static readonly Entry[] Entries =
    {
        new("aleoida", 150, 3_385_200, 12_934_900, "aleoid", "aleoida"),
        new("bacterium", 500, 1_000_000, 8_418_000, "bacterium", "bacteria", "bacterial"),
        new("cactoida", 300, 2_483_600, 16_202_800, "cactoid", "cactoida"),
        new("clypeus", 150, 8_418_000, 16_202_800, "clypeus"),
        new("concha", 150, 2_352_400, 19_010_800, "concha"),
        new("electricae", 1000, 6_284_600, 6_284_600, "electricae", "electrica"),
        new("fonticulua", 500, 1_000_000, 20_000_000, "fonticulua"),
        new("frutexa", 150, 1_632_500, 10_326_000, "frutexa"),
        new("fumerola", 100, 6_284_600, 16_202_800, "fumerola"),
        new("fungoida", 300, 1_670_100, 3_703_200, "fungoid", "fungoida"),
        new("osseus", 800, 1_483_000, 12_934_900, "osseus"),
        new("recepta", 150, 12_934_900, 16_202_800, "recepta"),
        new("stratum", 500, 1_362_000, 19_010_800, "stratum"),
        new("tubus", 800, 2_415_500, 11_873_200, "tubus"),
        new("tussock", 200, 1_000_000, 19_010_800, "tussock")
    };

    public static BiologyEstimateSnapshot Estimate(string identifier, string displayName)
    {
        string searchable = (identifier + " " + displayName).ToLowerInvariant();
        Entry? match = Entries.FirstOrDefault(entry =>
            entry.Aliases.Any(alias => searchable.Contains(alias, StringComparison.OrdinalIgnoreCase)));
        return match is null
            ? new BiologyEstimateSnapshot(displayName, string.Empty, 0, 0, 0)
            : new BiologyEstimateSnapshot(displayName, match.Key, match.Range, match.Min, match.Max);
    }

    public static int GetColonyRange(string identifier, string displayName) =>
        Estimate(identifier, displayName).ColonyRangeMeters;
}
