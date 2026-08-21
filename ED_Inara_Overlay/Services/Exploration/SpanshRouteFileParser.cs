using System.Globalization;
using System.IO;
using System.Text.Json;
using ED_Inara_Overlay.Models;
using Microsoft.VisualBasic.FileIO;

namespace ED_Inara_Overlay.Services.Exploration;

public static class SpanshRouteFileParser
{
    public static ExplorationRoutePlan Parse(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        IReadOnlyList<ExplorationRouteStop> stops = Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase)
            ? ParseCsv(path)
            : ParseJson(path);
        if (stops.Count == 0) throw new InvalidDataException("The route file contains no systems.");
        string kind = stops.Any(stop => stop.Bodies.Count > 0) ? "RoadToRiches" : "Travel";
        return new ExplorationRoutePlan(Path.GetFileName(path), kind, DateTimeOffset.UtcNow, 0, stops);
    }

    public static ExplorationRoutePlan ParseJson(string json, string sourceName = "Spansh API")
    {
        using JsonDocument document = JsonDocument.Parse(json);
        IReadOnlyList<ExplorationRouteStop> stops = ParseJson(document.RootElement);
        if (stops.Count == 0) throw new InvalidDataException("The route response contains no systems.");
        string kind = stops.Any(stop => stop.Bodies.Count > 0) ? "RoadToRiches" : "Travel";
        return new ExplorationRoutePlan(sourceName, kind, DateTimeOffset.UtcNow, 0, stops);
    }

    private static IReadOnlyList<ExplorationRouteStop> ParseCsv(string path)
    {
        using var parser = new TextFieldParser(path) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        string[] headers = parser.ReadFields() ?? Array.Empty<string>();
        var rows = new List<Dictionary<string, string>>();
        while (!parser.EndOfData)
        {
            string[] values = parser.ReadFields() ?? Array.Empty<string>();
            rows.Add(headers.Select((header, index) => (header, value: index < values.Length ? values[index] : string.Empty))
                .ToDictionary(item => item.header.Trim(), item => item.value.Trim(), StringComparer.OrdinalIgnoreCase));
        }
        bool roadToRiches = headers.Any(header => header.Equals("Body Name", StringComparison.OrdinalIgnoreCase));
        if (!roadToRiches)
        {
            return rows.Select(row => new ExplorationRouteStop(
                    First(row, "System Name", "system"), Array.Empty<ExplorationRouteBody>(),
                    Number(row, "Distance"), Yes(row, "Neutron Star"), Yes(row, "Refuel"), Yes(row, "Inject")))
                .Where(stop => !string.IsNullOrWhiteSpace(stop.System)).ToArray();
        }
        return rows.Where(row => !string.IsNullOrWhiteSpace(First(row, "System Name", "system")))
            .GroupBy(row => First(row, "System Name", "system"), StringComparer.OrdinalIgnoreCase)
            .Select(group => new ExplorationRouteStop(group.Key,
                group.Select(row => new ExplorationRouteBody(
                        First(row, "Body Name"), Integer(row, "Estimated Scan Value"), Integer(row, "Estimated Mapping Value")))
                    .Where(body => !string.IsNullOrWhiteSpace(body.Name)).ToArray(),
                0, false, false, false)).ToArray();
    }

    private static IReadOnlyList<ExplorationRouteStop> ParseJson(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return ParseJson(document.RootElement);
    }

    private static IReadOnlyList<ExplorationRouteStop> ParseJson(JsonElement root)
    {
        JsonElement result = root.TryGetProperty("result", out JsonElement wrapped)
            ? wrapped : root;
        if (result.ValueKind == JsonValueKind.Array) return ParseRichesArray(result);
        if (result.ValueKind != JsonValueKind.Object) return Array.Empty<ExplorationRouteStop>();
        foreach (string name in new[] { "systems", "result", "route" })
        {
            if (result.TryGetProperty(name, out JsonElement array) && array.ValueKind == JsonValueKind.Array)
                return ParseRichesArray(array);
        }
        if (result.TryGetProperty("system_jumps", out JsonElement systemJumps)) return ParseJumpArray(systemJumps);
        if (result.TryGetProperty("jumps", out JsonElement jumps)) return ParseJumpArray(jumps);
        return Array.Empty<ExplorationRouteStop>();
    }

    private static IReadOnlyList<ExplorationRouteStop> ParseRichesArray(JsonElement array) => array.EnumerateArray()
        .Select(item => new ExplorationRouteStop(
            JsonString(item, "name", JsonString(item, "system_name", JsonString(item, "system"))),
            ReadBodies(item), JsonDouble(item, "distance"), false, false, false))
        .Where(stop => !string.IsNullOrWhiteSpace(stop.System)).ToArray();

    private static IReadOnlyList<ExplorationRouteStop> ParseJumpArray(JsonElement array) => array.ValueKind != JsonValueKind.Array
        ? Array.Empty<ExplorationRouteStop>()
        : array.EnumerateArray().Select(item => new ExplorationRouteStop(
                JsonString(item, "system", JsonString(item, "name")), Array.Empty<ExplorationRouteBody>(),
                JsonDouble(item, "distance"), JsonBool(item, "neutron_star") || JsonBool(item, "has_neutron"),
                JsonBool(item, "must_refuel"), JsonBool(item, "must_inject")))
            .Where(stop => !string.IsNullOrWhiteSpace(stop.System)).ToArray();

    private static IReadOnlyList<ExplorationRouteBody> ReadBodies(JsonElement item)
    {
        if (!item.TryGetProperty("bodies", out JsonElement bodies) || bodies.ValueKind != JsonValueKind.Array)
            return Array.Empty<ExplorationRouteBody>();
        return bodies.EnumerateArray().Select(body => new ExplorationRouteBody(
                JsonString(body, "name"), JsonInt64(body, "estimated_scan_value"),
                JsonInt64(body, "estimated_mapping_value")))
            .Where(body => !string.IsNullOrWhiteSpace(body.Name)).ToArray();
    }

    private static string First(IReadOnlyDictionary<string, string> row, params string[] names) =>
        names.Select(name => row.TryGetValue(name, out string? value) ? value : string.Empty)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    private static bool Yes(IReadOnlyDictionary<string, string> row, string name) =>
        First(row, name).Equals("Yes", StringComparison.OrdinalIgnoreCase)
        || First(row, name).Equals("True", StringComparison.OrdinalIgnoreCase);
    private static double Number(IReadOnlyDictionary<string, string> row, string name) =>
        double.TryParse(First(row, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;
    private static long Integer(IReadOnlyDictionary<string, string> row, string name) =>
        double.TryParse(First(row, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? (long)value : 0;
    private static string JsonString(JsonElement item, string name, string fallback = "") =>
        item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static double JsonDouble(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.TryGetDouble(out double result) ? result : 0;
    private static long JsonInt64(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.TryGetInt64(out long result) ? result : 0;
    private static bool JsonBool(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
}
