using System.IO;
using System.Text.Json;
using ED_Inara_Overlay.Models;

namespace ED_Inara_Overlay.Services.Exploration;

/// <summary>
/// Ranks likely Odyssey organisms from the local Canonn Bioforge histogram snapshot.
/// The result is a statistical hint, never evidence that an organism is present.
/// </summary>
public sealed class ExobiologyPredictionService
{
    private readonly Lazy<IReadOnlyList<SpeciesProfile>> profiles;

    public static ExobiologyPredictionService Instance { get; } = new();

    public ExobiologyPredictionService(string? dataDirectory = null)
    {
        string directory = dataDirectory ?? Path.Combine(
            AppContext.BaseDirectory, "Resources", "ExobiologyBioforge");
        profiles = new Lazy<IReadOnlyList<SpeciesProfile>>(
            () => LoadProfiles(directory), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IReadOnlyList<ExobiologyPrediction> Predict(
        ExplorationCatalogBody body,
        int maximumResults = 6) => Predict(
            body.Landable, body.Subtype, body.Atmosphere, body.Volcanism,
            body.SurfaceTemperatureKelvin, body.GravityG, body.SurfacePressureAtmospheres,
            body.BiologicalSignals, body.Genuses, maximumResults);

    public IReadOnlyList<ExobiologyPrediction> Predict(
        ExplorationBodySnapshot body,
        int maximumResults = 6) => Predict(
            body.Landable, body.BodyClass, body.Atmosphere, body.Volcanism,
            body.SurfaceTemperatureKelvin, body.GravityG, body.SurfacePressureAtmospheres,
            body.BiologicalSignals, body.Genuses, maximumResults);

    private IReadOnlyList<ExobiologyPrediction> Predict(
        bool landable,
        string bodyType,
        string atmosphere,
        string volcanism,
        double temperature,
        double gravity,
        double pressure,
        int biologicalSignals,
        IReadOnlyList<string> genuses,
        int maximumResults)
    {
        if (!landable || maximumResults <= 0) return Array.Empty<ExobiologyPrediction>();

        HashSet<string> confirmedGenuses = genuses
            .Select(NormalizeGenusIdentity)
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scored = new List<(SpeciesProfile Profile, double Score)>();
        foreach (SpeciesProfile profile in profiles.Value)
        {
            if (confirmedGenuses.Count > 0 && !confirmedGenuses.Contains(NormalizeGenusIdentity(profile.Genus))) continue;
            if (!TryScore(profile, bodyType, atmosphere, volcanism, temperature, gravity, pressure, out double score)) continue;
            scored.Add((profile, score));
        }

        // Colour variants share the same biological signal. Keep the strongest variant for each species.
        var bestSpecies = scored
            .GroupBy(item => item.Profile.Species, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Score).ThenByDescending(item => item.Profile.Count).First())
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Profile.Count)
            .Take(Math.Max(maximumResults, Math.Max(1, biologicalSignals)))
            .ToArray();
        if (bestSpecies.Length == 0) return Array.Empty<ExobiologyPrediction>();
        double total = bestSpecies.Sum(item => item.Score);
        return bestSpecies.Take(maximumResults).Select(item => new ExobiologyPrediction(
            item.Profile.Species,
            item.Profile.Variant,
            item.Profile.Genus,
            item.Profile.Identifier,
            item.Profile.ColonyRangeMeters,
            item.Profile.Reward,
            total > 0 ? item.Score / total : 0,
            item.Profile.Count)).ToArray();
    }

    private static bool TryScore(
        SpeciesProfile profile,
        string bodyType,
        string atmosphere,
        string volcanism,
        double temperature,
        double gravity,
        double pressure,
        out double score)
    {
        var factors = new List<double>(6);
        if (!AddMapFactor(profile.BodyTypes, bodyType, factors)
            || !AddMapFactor(profile.Atmospheres, atmosphere, factors)
            || !AddVolcanismFactor(profile.VolcanicBodyTypes, bodyType, volcanism, factors)
            || !AddBinFactor(profile.Temperatures, temperature, factors)
            || !AddBinFactor(profile.Gravities, gravity, factors)
            || !AddBinFactor(profile.Pressures, pressure, factors))
        {
            score = 0;
            return false;
        }
        if (factors.Count < 2)
        {
            score = 0;
            return false;
        }
        double geometricMean = Math.Pow(factors.Aggregate(1d, (value, factor) => value * factor), 1d / factors.Count);
        // A gentle prior prevents a one-off histogram bin from outranking well-supported observations.
        score = geometricMean * Math.Log10(Math.Max(10, profile.Count + 10));
        return score > 0;
    }

    private static bool AddMapFactor(
        IReadOnlyDictionary<string, double> histogram,
        string actual,
        ICollection<double> factors)
    {
        if (histogram.Count == 0 || string.IsNullOrWhiteSpace(actual)) return true;
        string normalized = NormalizeCondition(actual);
        KeyValuePair<string, double>? match = histogram.FirstOrDefault(item =>
            ConditionsMatch(NormalizeCondition(item.Key), normalized));
        if (match is null || match.Value.Value <= 0) return false;
        factors.Add(match.Value.Value / histogram.Values.Sum());
        return true;
    }

    private static bool AddVolcanismFactor(
        IReadOnlyDictionary<string, double> histogram,
        string bodyType,
        string volcanism,
        ICollection<double> factors)
    {
        if (histogram.Count == 0 || string.IsNullOrWhiteSpace(bodyType) || string.IsNullOrWhiteSpace(volcanism)) return true;
        string body = NormalizeCondition(bodyType);
        string volcanic = NormalizeCondition(volcanism);
        KeyValuePair<string, double>? match = histogram.FirstOrDefault(item =>
        {
            string[] pieces = item.Key.Split(" - ", 2, StringSplitOptions.TrimEntries);
            return pieces.Length == 2
                   && ConditionsMatch(NormalizeCondition(pieces[0]), body)
                   && ConditionsMatch(NormalizeCondition(pieces[1]), volcanic);
        });
        if (match is null || match.Value.Value <= 0) return false;
        factors.Add(match.Value.Value / histogram.Values.Sum());
        return true;
    }

    private static bool AddBinFactor(IReadOnlyList<HistogramBin> bins, double actual, ICollection<double> factors)
    {
        if (bins.Count == 0 || actual <= 0) return true;
        HistogramBin? match = bins.FirstOrDefault(bin => actual >= bin.Min && actual <= bin.Max && bin.Value > 0);
        if (match is null) return false;
        factors.Add(match.Value / bins.Sum(bin => bin.Value));
        return true;
    }

    private static IReadOnlyList<SpeciesProfile> LoadProfiles(string directory)
    {
        if (!Directory.Exists(directory)) return Array.Empty<SpeciesProfile>();
        var result = new List<SpeciesProfile>();
        foreach (string file in Directory.EnumerateFiles(directory, "*.json").OrderBy(path => path))
        {
            int colonyRange = ParseColonyRange(Path.GetFileName(file));
            string genus = GenusFromFile(Path.GetFileNameWithoutExtension(file));
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
            foreach (JsonProperty entry in document.RootElement.EnumerateObject())
            {
                JsonElement value = entry.Value;
                string fullName = GetString(value, "name");
                string[] nameParts = fullName.Split(" - ", 2, StringSplitOptions.TrimEntries);
                string species = nameParts[0];
                string variant = nameParts.Length > 1 ? nameParts[1] : string.Empty;
                JsonElement histograms = value.TryGetProperty("histograms", out JsonElement source)
                    ? source
                    : default;
                result.Add(new SpeciesProfile(
                    species, variant, genus, GetString(value, "fdevname", entry.Name), colonyRange,
                    GetInt32(value, "reward"), GetInt32(value, "count"),
                    ReadMap(histograms, "body_types"), ReadMap(histograms, "atmos_types"),
                    ReadMap(histograms, "volcanic_body_types"), ReadBins(histograms, "temperature"),
                    ReadBins(histograms, "gravity"), ReadBins(histograms, "pressure")));
            }
        }
        return result;
    }

    private static IReadOnlyDictionary<string, double> ReadMap(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(name, out JsonElement map)
            || map.ValueKind != JsonValueKind.Object) return new Dictionary<string, double>();
        return map.EnumerateObject().Where(item => item.Value.TryGetDouble(out _))
            .ToDictionary(item => item.Name, item => item.Value.GetDouble(), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<HistogramBin> ReadBins(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(name, out JsonElement array)
            || array.ValueKind != JsonValueKind.Array) return Array.Empty<HistogramBin>();
        return array.EnumerateArray()
            .Where(item => item.TryGetProperty("min", out JsonElement min) && min.TryGetDouble(out _)
                           && item.TryGetProperty("max", out JsonElement max) && max.TryGetDouble(out _)
                           && item.TryGetProperty("value", out JsonElement value) && value.TryGetDouble(out double count)
                           && count > 0)
            .Select(item => new HistogramBin(
                item.GetProperty("min").GetDouble(), item.GetProperty("max").GetDouble(),
                item.GetProperty("value").GetDouble()))
            .ToArray();
    }

    private static bool ConditionsMatch(string expected, string actual) =>
        expected.Equals(actual, StringComparison.OrdinalIgnoreCase)
        || expected.Length > 4 && actual.Contains(expected, StringComparison.OrdinalIgnoreCase)
        || actual.Length > 4 && expected.Contains(actual, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCondition(string value) => value.Trim().ToLowerInvariant()
        .Replace(" atmosphere", string.Empty, StringComparison.Ordinal)
        .Replace(" volcanism", string.Empty, StringComparison.Ordinal)
        .Replace("атмосфера", string.Empty, StringComparison.Ordinal)
        .Replace("вулканизм", string.Empty, StringComparison.Ordinal)
        .Replace("  ", " ", StringComparison.Ordinal)
        .Trim();

    internal static string NormalizeGenusIdentity(string value)
    {
        string normalized = value.ToLowerInvariant();
        foreach (string token in new[] { "bacterial", "bacterium", "aleoida", "cactoida", "clypeus", "concha", "electricae", "fonticulua", "frutexa", "fumerola", "fungoida", "osseus", "recepta", "stratum", "tubus", "tussock" })
        {
            if (normalized.Contains(token, StringComparison.Ordinal)) return token == "bacterial" ? "bacterium" : token;
        }
        return normalized.Trim(' ', '$', ';');
    }

    private static int ParseColonyRange(string fileName)
    {
        string part = fileName.Split('_').LastOrDefault() ?? string.Empty;
        return int.TryParse(part.Replace("m.json", string.Empty, StringComparison.OrdinalIgnoreCase), out int value)
            ? value : 0;
    }

    private static string GenusFromFile(string fileName) => fileName.Split('_')[0] switch
    {
        "bacterium" => "Bacterium",
        string value => char.ToUpperInvariant(value[0]) + value[1..]
    };

    private static string GetString(JsonElement element, string name, string fallback = "") =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback : fallback;

    private static int GetInt32(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result) ? result : 0;

    private sealed record SpeciesProfile(
        string Species, string Variant, string Genus, string Identifier, int ColonyRangeMeters,
        int Reward, int Count, IReadOnlyDictionary<string, double> BodyTypes,
        IReadOnlyDictionary<string, double> Atmospheres, IReadOnlyDictionary<string, double> VolcanicBodyTypes,
        IReadOnlyList<HistogramBin> Temperatures, IReadOnlyList<HistogramBin> Gravities,
        IReadOnlyList<HistogramBin> Pressures);

    private sealed record HistogramBin(double Min, double Max, double Value);
}
