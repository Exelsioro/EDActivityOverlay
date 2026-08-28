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

    private static readonly int[] NearbyFallbackRadiiLy =
    [
        25,
        50,
        100,
        250,
        500
    ];

    private const int StationProbeBatchSize = 12;

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

        string nearestJson =
            await GetJsonAsync(
                $"v2/system/name/{encodedSystem}/nearest/material-trader?minLandingPadSize=1",
                cancellationToken)
                .ConfigureAwait(
                    false);

        IReadOnlyList<MaterialTraderStation> nearestCandidates =
            ParseResults(
                nearestJson);

        Dictionary<MaterialTraderType, MaterialTraderStation> selected =
            new();

        foreach (MaterialTraderType type
                 in desired)
        {
            MaterialTraderStation? nearest =
                nearestCandidates
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
                selected[type] =
                    nearest;
            }
        }

        HashSet<MaterialTraderType> missing =
            desired
                .Where(
                    type =>
                        !selected.ContainsKey(
                            type))
                .ToHashSet();

        if (missing.Count > 0)
        {
            IReadOnlyList<MaterialTraderStation> fallback =
                await FindMissingViaNearbyAsync(
                    originSystem.Trim(),
                    missing,
                    cancellationToken)
                    .ConfigureAwait(
                        false);

            foreach (MaterialTraderStation station
                     in fallback)
            {
                selected[station.Type] =
                    station;
            }
        }

        return
            Enum.GetValues<MaterialTraderType>()
                .Where(
                    type =>
                        desired.Contains(
                            type)
                        && selected.ContainsKey(
                            type))
                .Select(
                    type =>
                        selected[type])
                .ToArray();
    }

    private async Task<IReadOnlyList<MaterialTraderStation>> FindMissingViaNearbyAsync(
        string originSystem,
        HashSet<MaterialTraderType> missingTypes,
        CancellationToken cancellationToken)
    {
        ArdentSystem origin =
            await GetSystemAsync(
                originSystem,
                cancellationToken)
                .ConfigureAwait(
                    false);

        Dictionary<MaterialTraderType, MaterialTraderStation> found =
            new();

        HashSet<MaterialTraderType> remaining =
            new(
                missingTypes);

        HashSet<long> inspectedSystems =
            new();

        foreach (int radiusLy
                 in NearbyFallbackRadiiLy)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<ArdentSystem> nearby =
                await GetNearbySystemsAsync(
                    originSystem,
                    radiusLy,
                    origin,
                    cancellationToken)
                    .ConfigureAwait(
                        false);

            ArdentSystem[] pending =
                nearby
                    .Where(
                        system =>
                            system.SystemAddress != origin.SystemAddress
                            && !inspectedSystems.Contains(
                                system.SystemAddress))
                    .OrderBy(
                        system =>
                            system.DistanceLy)
                    .ToArray();

            if (pending.Length == 0)
            {
                continue;
            }

            for (int index = 0;
                 index < pending.Length
                 && remaining.Count > 0;
                 index += StationProbeBatchSize)
            {
                ArdentSystem[] batch =
                    pending
                        .Skip(
                            index)
                        .Take(
                            StationProbeBatchSize)
                        .ToArray();

                Task<IReadOnlyList<MaterialTraderStation>>[] tasks =
                    batch
                        .Select(
                            system =>
                                GetMaterialTraderStationsAsync(
                                    system,
                                    cancellationToken))
                        .ToArray();

                IReadOnlyList<MaterialTraderStation>[] results =
                    await Task.WhenAll(
                        tasks)
                        .ConfigureAwait(
                            false);

                foreach (ArdentSystem system
                         in batch)
                {
                    inspectedSystems.Add(
                        system.SystemAddress);
                }

                Dictionary<MaterialTraderType, MaterialTraderStation> bestInBatch =
                    results
                        .SelectMany(
                            rows =>
                                rows)
                        .Where(
                            station =>
                                remaining.Contains(
                                    station.Type))
                        .GroupBy(
                            station =>
                                station.Type)
                        .ToDictionary(
                            group =>
                                group.Key,
                            group =>
                                group
                                    .OrderBy(
                                        station =>
                                            station.DistanceLy)
                                    .ThenBy(
                                        station =>
                                            station.DistanceToArrivalLs
                                            ?? double.MaxValue)
                                    .First());

                foreach ((MaterialTraderType type,
                          MaterialTraderStation station)
                         in bestInBatch)
                {
                    found[type] =
                        station;

                    remaining.Remove(
                        type);
                }
            }

            if (remaining.Count == 0)
            {
                break;
            }

            // Ardent /nearby returns at most 1000 systems and has no paging.
            // If the returned set is already saturated, larger radii normally
            // repeat the same nearest 1000 systems. We still continue through
            // the configured radii in case a smaller pass was not saturated.
            if (nearby.Count >= 1000)
            {
                Logger.Logger.Info(
                    $"Material trader nearby fallback reached Ardent's 1000-system result cap at {radiusLy} ly.");
            }
        }

        if (remaining.Count > 0)
        {
            Logger.Logger.Warning(
                $"Material trader nearby fallback did not resolve types within 500 ly / Ardent nearby result cap: {string.Join(", ", remaining)}.");
        }

        return
            found.Values
                .OrderBy(
                    station =>
                        station.DistanceLy)
                .ToArray();
    }

    private async Task<ArdentSystem> GetSystemAsync(
        string systemName,
        CancellationToken cancellationToken)
    {
        string encoded =
            Uri.EscapeDataString(
                systemName);

        string json =
            await GetJsonAsync(
                $"v2/system/name/{encoded}",
                cancellationToken)
                .ConfigureAwait(
                    false);

        using JsonDocument document =
            JsonDocument.Parse(
                json);

        ArdentSystem? parsed =
            ParseSystem(
                document.RootElement,
                origin: null);

        return
            parsed
            ?? throw new InvalidOperationException(
                $"Ardent did not return coordinates for system '{systemName}'.");
    }

    private async Task<IReadOnlyList<ArdentSystem>> GetNearbySystemsAsync(
        string originSystem,
        int radiusLy,
        ArdentSystem origin,
        CancellationToken cancellationToken)
    {
        string encoded =
            Uri.EscapeDataString(
                originSystem);

        string json =
            await GetJsonAsync(
                $"v2/system/name/{encoded}/nearby?maxDistance={radiusLy}",
                cancellationToken)
                .ConfigureAwait(
                    false);

        using JsonDocument document =
            JsonDocument.Parse(
                json);

        if (document.RootElement.ValueKind
            != JsonValueKind.Array)
        {
            return
                Array.Empty<ArdentSystem>();
        }

        List<ArdentSystem> systems =
            new();

        foreach (JsonElement item
                 in document.RootElement.EnumerateArray())
        {
            ArdentSystem? parsed =
                ParseSystem(
                    item,
                    origin);

            if (parsed is not null)
            {
                systems.Add(
                    parsed);
            }
        }

        return
            systems
                .OrderBy(
                    system =>
                        system.DistanceLy)
                .ToArray();
    }

    private async Task<IReadOnlyList<MaterialTraderStation>> GetMaterialTraderStationsAsync(
        ArdentSystem system,
        CancellationToken cancellationToken)
    {
        try
        {
            string json =
                await GetJsonAsync(
                    $"v2/system/address/{system.SystemAddress}/stations",
                    cancellationToken)
                    .ConfigureAwait(
                        false);

            return
                ParseStationResults(
                    json,
                    system);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"Material trader fallback station lookup failed for {system.SystemName}: {ex.Message}");

            return
                Array.Empty<MaterialTraderStation>();
        }
    }

    private async Task<string> GetJsonAsync(
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response =
            await httpClient.GetAsync(
                relativeUrl,
                cancellationToken)
                .ConfigureAwait(
                    false);

        response.EnsureSuccessStatusCode();

        return
            await response.Content.ReadAsStringAsync(
                cancellationToken)
                .ConfigureAwait(
                    false);
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

    internal static IReadOnlyList<MaterialTraderStation> ParseStationResults(
        string json,
        string systemName,
        double distanceLy)
    {
        var system =
            new ArdentSystem(
                0,
                systemName,
                0,
                0,
                0,
                distanceLy);

        return
            ParseStationResults(
                json,
                system);
    }

    private static IReadOnlyList<MaterialTraderStation> ParseStationResults(
        string json,
        ArdentSystem system)
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
            if (!GetFlag(
                    item,
                    "materialTrader"))
            {
                continue;
            }

            int maxLandingPadSize =
                GetInt(
                    item,
                    "maxLandingPadSize");

            if (maxLandingPadSize < 1)
            {
                continue;
            }

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

            string stationName =
                GetString(
                    item,
                    "stationName");

            if (string.IsNullOrWhiteSpace(
                    stationName))
            {
                continue;
            }

            result.Add(
                new MaterialTraderStation(
                    type.Value,
                    system.SystemName,
                    stationName,
                    primaryEconomy,
                    secondaryEconomy,
                    system.DistanceLy,
                    GetNullableDouble(
                        item,
                        "distanceToArrival"),
                    maxLandingPadSize,
                    GetNullableDateTimeOffset(
                        item,
                        "updatedAt")));
        }

        return
            result;
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

    private static ArdentSystem? ParseSystem(
        JsonElement element,
        ArdentSystem? origin)
    {
        long systemAddress =
            GetLong(
                element,
                "systemAddress");

        string systemName =
            GetString(
                element,
                "systemName");

        double? x =
            GetNullableDouble(
                element,
                "systemX");

        double? y =
            GetNullableDouble(
                element,
                "systemY");

        double? z =
            GetNullableDouble(
                element,
                "systemZ");

        if (systemAddress == 0
            || string.IsNullOrWhiteSpace(
                systemName)
            || x is null
            || y is null
            || z is null)
        {
            return null;
        }

        double distance =
            origin is null
                ? 0d
                : Math.Sqrt(
                    Math.Pow(
                        x.Value
                        - origin.X,
                        2)
                    + Math.Pow(
                        y.Value
                        - origin.Y,
                        2)
                    + Math.Pow(
                        z.Value
                        - origin.Z,
                        2));

        return
            new ArdentSystem(
                systemAddress,
                systemName,
                x.Value,
                y.Value,
                z.Value,
                distance);
    }

    private static bool GetFlag(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(
                property,
                out JsonElement value))
        {
            return false;
        }

        return
            value.ValueKind switch
            {
                JsonValueKind.True =>
                    true,

                JsonValueKind.Number =>
                    value.TryGetInt32(
                        out int numericFlag)
                    && numericFlag != 0,

                JsonValueKind.String =>
                    bool.TryParse(
                        value.GetString(),
                        out bool booleanFlag)
                        ? booleanFlag
                        : int.TryParse(
                            value.GetString(),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int stringNumericFlag)
                          && stringNumericFlag != 0,

                _ =>
                    false
            };
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

    private static long GetLong(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(
                property,
                out JsonElement value))
        {
            return 0;
        }

        if (value.ValueKind
            == JsonValueKind.Number
            && value.TryGetInt64(
                out long numeric))
        {
            return numeric;
        }

        if (value.ValueKind
            == JsonValueKind.String
            && long.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out numeric))
        {
            return numeric;
        }

        return 0;
    }

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

        if (value.ValueKind
            == JsonValueKind.Number
            && value.TryGetInt32(
                out int result))
        {
            return result;
        }

        if (value.ValueKind
            == JsonValueKind.String
            && int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out result))
        {
            return result;
        }

        return 0;
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

    private sealed record ArdentSystem(
        long SystemAddress,
        string SystemName,
        double X,
        double Y,
        double Z,
        double DistanceLy);
}
