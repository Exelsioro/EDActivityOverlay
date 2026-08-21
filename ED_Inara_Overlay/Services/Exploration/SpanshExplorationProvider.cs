using System.Net.Http;
using System.IO;
using System.Text.Json;
using ED_Inara_Overlay.Models;

namespace ED_Inara_Overlay.Services.Exploration;

internal sealed class SpanshExplorationProvider(HttpClient httpClient) : IExplorationSystemProvider
{
    public string Name => "Spansh";

    public async Task<ExplorationSystemDataSnapshot?> GetSystemAsync(
        long systemAddress,
        string systemName,
        CancellationToken cancellationToken)
    {
        if (systemAddress <= 0) return null;
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"https://spansh.co.uk/api/system/{systemAddress}", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("record", out JsonElement record)
            || record.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var bodies = new List<ExternalExplorationBodySnapshot>();
        if (record.TryGetProperty("bodies", out JsonElement bodyArray) && bodyArray.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement body in bodyArray.EnumerateArray())
            {
                long bodyAddress = GetInt64(body, "id64");
                int bodyId = DecodeBodyId(systemAddress, bodyAddress);
                string type = GetString(body, "type");
                string subtype = GetString(body, "subtype");
                string terraformingState = GetString(body, "terraforming_state");
                double earthMasses = GetDouble(body, "earth_masses");
                double solarMasses = GetDouble(body, "solar_masses");
                long scanValue = GetInt64(body, "estimated_scan_value");
                long mappingValue = GetInt64(body, "estimated_mapping_value");
                ExplorationValueEstimate localValues = ExplorationValueCalculator.Estimate(
                    type,
                    subtype,
                    terraformingState.Contains("Terraform", StringComparison.OrdinalIgnoreCase),
                    earthMasses,
                    solarMasses);
                bool locallyCalculated = scanValue <= 0 || type.Equals("Planet", StringComparison.OrdinalIgnoreCase) && mappingValue <= 0;
                if (scanValue <= 0) scanValue = localValues.FirstDiscoveryScanValue;
                if (mappingValue <= 0)
                {
                    mappingValue = localValues.FirstDiscoveredAndMappedEfficientValue;
                }
                bodies.Add(new ExternalExplorationBodySnapshot(
                    bodyId,
                    GetString(body, "name"),
                    type,
                    subtype,
                    GetDouble(body, "distance_to_arrival"),
                    GetBoolean(body, "is_landable"),
                    GetDouble(body, "gravity"),
                    GetDouble(body, "surface_temperature"),
                    GetString(body, "atmosphere"),
                    GetString(body, "volcanism_type", GetString(body, "volcanism")),
                    terraformingState,
                    scanValue,
                    mappingValue,
                    GetArrayLength(body, "landmarks"))
                {
                    EarthMasses = earthMasses,
                    SolarMasses = solarMasses,
                    SurfacePressureAtmospheres = GetDouble(body, "surface_pressure"),
                    ValuesCalculatedLocally = locallyCalculated
                });
            }
        }

        DateTimeOffset sourceUpdated = ParseDate(record, "updated_at");
        return new ExplorationSystemDataSnapshot(
            GetInt64(record, "id64", systemAddress),
            GetString(record, "name", systemName),
            Name,
            sourceUpdated,
            DateTimeOffset.UtcNow,
            false,
            false,
            GetInt32(record, "body_count", bodies.Count),
            PositiveOrFallback(GetInt64(record, "estimated_scan_value"), bodies.Sum(body => body.EstimatedScanValue)),
            PositiveOrFallback(GetInt64(record, "estimated_mapping_value"), bodies.Sum(body => body.EstimatedMappingValue)),
            GetNullableDouble(record, "x"),
            GetNullableDouble(record, "y"),
            GetNullableDouble(record, "z"),
            GetBoolean(record, "needs_permit"),
            bodies);
    }

    private static int DecodeBodyId(long systemAddress, long bodyAddress)
    {
        if (bodyAddress < systemAddress) return -1;
        long value = (bodyAddress - systemAddress) >> 55;
        return value is >= 0 and <= int.MaxValue ? (int)value : -1;
    }

    internal static string GetString(JsonElement element, string property, string fallback = "") =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    internal static long GetInt64(JsonElement element, string property, long fallback = 0) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetInt64(out long result) ? result : fallback;

    internal static int GetInt32(JsonElement element, string property, int fallback = 0) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int result) ? result : fallback;

    internal static double GetDouble(JsonElement element, string property, double fallback = 0) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetDouble(out double result) ? result : fallback;

    internal static double? GetNullableDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetDouble(out double result) ? result : null;

    internal static bool GetBoolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();

    private static int GetArrayLength(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength()
            : 0;

    private static long PositiveOrFallback(long value, long fallback) => value > 0 ? value : fallback;

    internal static DateTimeOffset ParseDate(JsonElement element, string property)
    {
        string text = GetString(element, property);
        return DateTimeOffset.TryParse(text, out DateTimeOffset result) ? result.ToUniversalTime() : DateTimeOffset.MinValue;
    }
}
