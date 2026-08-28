using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Engineering;

public enum MaterialTraderType
{
    Raw,
    Manufactured,
    Encoded
}

public sealed record MaterialTraderStation(
    MaterialTraderType Type,
    string SystemName,
    string StationName,
    string PrimaryEconomy,
    string SecondaryEconomy,
    double DistanceLy,
    double? DistanceToArrivalLs,
    int MaxLandingPadSize,
    DateTimeOffset? UpdatedUtc);

public sealed class MaterialTraderFinderService
{
    private static readonly HttpClient SharedHttpClient =
        new()
        {
            BaseAddress =
                new Uri(
                    "https://api.ardent-insight.com/"),
            Timeout =
                TimeSpan.FromSeconds(
                    12)
        };

    private readonly HttpClient httpClient;

    public MaterialTraderFinderService()
        : this(
            SharedHttpClient)
    {
    }

    internal MaterialTraderFinderService(
        HttpClient httpClient)
    {
        this.httpClient =
            httpClient;
    }

    public async Task<IReadOnlyList<MaterialTraderStation>> FindNearestAsync(
        string originSystem,
        IEnumerable<EngineeringMaterialCategory>? desiredCategories,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                originSystem))
        {
            return
                Array.Empty<MaterialTraderStation>();
        }

        HashSet<MaterialTraderType> desired =
            ResolveDesiredTypes(
                desiredCategories);

        string encodedSystem =
            Uri.EscapeDataString(
                originSystem.Trim());

        using HttpResponseMessage response =
            await httpClient.GetAsync(
                $"v2/system/name/{encodedSystem}/nearest/material-trader?minLandingPadSize=1",
                cancellationToken)
                .ConfigureAwait(
                    false);

        response.EnsureSuccessStatusCode();

        string json =
            await response.Content.ReadAsStringAsync(
                cancellationToken)
                .ConfigureAwait(
                    false);

        IReadOnlyList<MaterialTraderStation> candidates =
            ParseResults(
                json);

        List<MaterialTraderStation> result =
            new();

        foreach (MaterialTraderType type
                 in Enum.GetValues<MaterialTraderType>())
        {
            if (!desired.Contains(
                    type))
            {
                continue;
            }

            MaterialTraderStation? nearest =
                candidates
                    .Where(
                        station =>
                            station.Type
                            == type)
                    .OrderBy(
                        station =>
                            station.DistanceLy)
                    .ThenBy(
                        station =>
                            station.DistanceToArrivalLs
                            ?? double.MaxValue)
                    .FirstOrDefault();

            if (nearest is not null)
            {
                result.Add(
                    nearest);
            }
        }

        return result;
    }

    internal static IReadOnlyList<MaterialTraderStation> ParseResults(
        string json)
    {
        using JsonDocument document =
            JsonDocument.Parse(
                json);

        if (document.RootElement.ValueKind
            != JsonValueKind.Array)
        {
            return
                Array.Empty<MaterialTraderStation>();
        }

        List<MaterialTraderStation> result =
            new();

        foreach (JsonElement item
                 in document.RootElement.EnumerateArray())
        {
            string primaryEconomy =
                GetString(
                    item,
                    "primaryEconomy");

            string secondaryEconomy =
                GetString(
                    item,
                    "secondaryEconomy");

            MaterialTraderType? type =
                ClassifyTraderType(
                    primaryEconomy,
                    secondaryEconomy);

            if (type is null)
            {
                continue;
            }

            string systemName =
                GetString(
                    item,
                    "systemName");

            string stationName =
                GetString(
                    item,
                    "stationName");

            if (string.IsNullOrWhiteSpace(
                    systemName)
                || string.IsNullOrWhiteSpace(
                    stationName))
            {
                continue;
            }

            result.Add(
                new MaterialTraderStation(
                    type.Value,
                    systemName,
                    stationName,
                    primaryEconomy,
                    secondaryEconomy,
                    GetDouble(
                        item,
                        "distance"),
                    GetNullableDouble(
                        item,
                        "distanceToArrival"),
                    GetInt(
                        item,
                        "maxLandingPadSize"),
                    GetNullableDateTimeOffset(
                        item,
                        "updatedAt")));
        }

        return
            result
                .OrderBy(
                    station =>
                        station.DistanceLy)
                .ThenBy(
                    station =>
                        station.DistanceToArrivalLs
                        ?? double.MaxValue)
                .ToArray();
    }

    internal static MaterialTraderType? ClassifyTraderType(
        string primaryEconomy,
        string secondaryEconomy)
    {
        MaterialTraderType? primary =
            ClassifyEconomy(
                primaryEconomy);

        return
            primary
            ?? ClassifyEconomy(
                secondaryEconomy);
    }

    private static MaterialTraderType? ClassifyEconomy(
        string economy) =>
        NormalizeEconomy(
            economy) switch
        {
            "hightech"
                or "military" =>
                MaterialTraderType.Encoded,

            "industrial" =>
                MaterialTraderType.Manufactured,

            "refinery"
                or "extraction" =>
                MaterialTraderType.Raw,

            _ =>
                null
        };

    private static HashSet<MaterialTraderType> ResolveDesiredTypes(
        IEnumerable<EngineeringMaterialCategory>? categories)
    {
        HashSet<MaterialTraderType> result =
            new();

        if (categories is not null)
        {
            foreach (EngineeringMaterialCategory category
                     in categories)
            {
                switch (category)
                {
                    case EngineeringMaterialCategory.Raw:
                        result.Add(
                            MaterialTraderType.Raw);
                        break;

                    case EngineeringMaterialCategory.Manufactured:
                        result.Add(
                            MaterialTraderType.Manufactured);
                        break;

                    case EngineeringMaterialCategory.Encoded:
                        result.Add(
                            MaterialTraderType.Encoded);
                        break;
                }
            }
        }

        if (result.Count == 0)
        {
            result.UnionWith(
                Enum.GetValues<MaterialTraderType>());
        }

        return result;
    }

    private static string NormalizeEconomy(
        string value) =>
        new(
            value
                .Where(
                    char.IsLetterOrDigit)
                .Select(
                    char.ToLowerInvariant)
                .ToArray());

    private static string GetString(
        JsonElement element,
        string property) =>
        element.TryGetProperty(
            property,
            out JsonElement value)
        && value.ValueKind
           == JsonValueKind.String
            ? value.GetString()
              ?? string.Empty
            : string.Empty;

    private static double GetDouble(
        JsonElement element,
        string property) =>
        GetNullableDouble(
            element,
            property)
        ?? 0d;

    private static double? GetNullableDouble(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(
                property,
                out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind
            == JsonValueKind.Number
            && value.TryGetDouble(
                out double numeric))
        {
            return numeric;
        }

        if (value.ValueKind
            == JsonValueKind.String
            && double.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out numeric))
        {
            return numeric;
        }

        return null;
    }

    private static int GetInt(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(
                property,
                out JsonElement value))
        {
            return 0;
        }

        return
            value.TryGetInt32(
                out int result)
                ? result
                : 0;
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(
        JsonElement element,
        string property)
    {
        string raw =
            GetString(
                element,
                property);

        return
            DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset parsed)
                ? parsed
                : null;
    }
}
