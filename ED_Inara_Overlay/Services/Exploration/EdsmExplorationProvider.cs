using System.Net.Http;
using System.Text.Json;
using ED_Inara_Overlay.Models;

namespace ED_Inara_Overlay.Services.Exploration;

internal sealed class EdsmExplorationProvider(HttpClient httpClient) : IExplorationSystemProvider
{
    public string Name => "EDSM";

    public async Task<ExplorationSystemDataSnapshot?> GetSystemAsync(
        long systemAddress,
        string systemName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(systemName)) return null;
        string escapedName = Uri.EscapeDataString(systemName);
        Task<HttpResponseMessage> bodiesRequest = httpClient.GetAsync(
            $"https://www.edsm.net/api-system-v1/bodies?systemName={escapedName}", cancellationToken);
        Task<HttpResponseMessage> valuesRequest = httpClient.GetAsync(
            $"https://www.edsm.net/api-system-v1/estimated-value?systemName={escapedName}", cancellationToken);
        using HttpResponseMessage bodiesResponse = await bodiesRequest.ConfigureAwait(false);
        using HttpResponseMessage valuesResponse = await valuesRequest.ConfigureAwait(false);
        if (!bodiesResponse.IsSuccessStatusCode) return null;

        using JsonDocument bodiesDocument = JsonDocument.Parse(
            await bodiesResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        JsonElement root = bodiesDocument.RootElement;
        if (!root.TryGetProperty("bodies", out JsonElement bodyArray) || bodyArray.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var mappingValues = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long totalScanValue = 0;
        long totalMappingValue = 0;
        if (valuesResponse.IsSuccessStatusCode)
        {
            using JsonDocument valuesDocument = JsonDocument.Parse(
                await valuesResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            JsonElement valueRoot = valuesDocument.RootElement;
            totalScanValue = SpanshExplorationProvider.GetInt64(valueRoot, "estimatedValue");
            totalMappingValue = SpanshExplorationProvider.GetInt64(valueRoot, "estimatedValueMapped");
            if (valueRoot.TryGetProperty("valuableBodies", out JsonElement valuable) && valuable.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in valuable.EnumerateArray())
                {
                    string bodyName = SpanshExplorationProvider.GetString(item, "bodyName");
                    if (!string.IsNullOrWhiteSpace(bodyName))
                    {
                        mappingValues[bodyName] = SpanshExplorationProvider.GetInt64(item, "valueMax");
                    }
                }
            }
        }

        var bodies = new List<ExternalExplorationBodySnapshot>();
        DateTimeOffset updated = DateTimeOffset.MinValue;
        foreach (JsonElement body in bodyArray.EnumerateArray())
        {
            string name = SpanshExplorationProvider.GetString(body, "name");
            mappingValues.TryGetValue(name, out long mappingValue);
            string type = SpanshExplorationProvider.GetString(body, "type");
            string subtype = SpanshExplorationProvider.GetString(body, "subType");
            string terraformingState = SpanshExplorationProvider.GetString(body, "terraformingState");
            double earthMasses = SpanshExplorationProvider.GetDouble(body, "earthMasses");
            double solarMasses = SpanshExplorationProvider.GetDouble(body, "solarMasses");
            ExplorationValueEstimate localValues = ExplorationValueCalculator.Estimate(
                type,
                subtype,
                terraformingState.Contains("Terraform", StringComparison.OrdinalIgnoreCase),
                earthMasses,
                solarMasses);
            long scanValue = localValues.FirstDiscoveryScanValue;
            bool locallyCalculated = mappingValue <= 0;
            if (mappingValue <= 0) mappingValue = localValues.FirstDiscoveredAndMappedEfficientValue;
            DateTimeOffset bodyUpdated = SpanshExplorationProvider.ParseDate(body, "updateTime");
            if (bodyUpdated > updated) updated = bodyUpdated;
            bodies.Add(new ExternalExplorationBodySnapshot(
                SpanshExplorationProvider.GetInt32(body, "bodyId", -1),
                name,
                type,
                subtype,
                SpanshExplorationProvider.GetDouble(body, "distanceToArrival"),
                SpanshExplorationProvider.GetBoolean(body, "isLandable"),
                SpanshExplorationProvider.GetDouble(body, "gravity"),
                SpanshExplorationProvider.GetDouble(body, "surfaceTemperature"),
                SpanshExplorationProvider.GetString(body, "atmosphereType"),
                SpanshExplorationProvider.GetString(body, "volcanismType"),
                terraformingState,
                scanValue,
                mappingValue,
                0)
            {
                EarthMasses = earthMasses,
                SolarMasses = solarMasses,
                SurfacePressureAtmospheres = SpanshExplorationProvider.GetDouble(body, "surfacePressure"),
                ValuesCalculatedLocally = locallyCalculated
            });
        }

        if (totalScanValue <= 0) totalScanValue = bodies.Sum(body => body.EstimatedScanValue);
        if (totalMappingValue <= 0) totalMappingValue = bodies.Sum(body => body.EstimatedMappingValue);

        return new ExplorationSystemDataSnapshot(
            SpanshExplorationProvider.GetInt64(root, "id64", systemAddress),
            SpanshExplorationProvider.GetString(root, "name", systemName),
            Name,
            updated,
            DateTimeOffset.UtcNow,
            false,
            false,
            SpanshExplorationProvider.GetInt32(root, "bodyCount", bodies.Count),
            totalScanValue,
            totalMappingValue,
            null, null, null, false,
            bodies);
    }
}
