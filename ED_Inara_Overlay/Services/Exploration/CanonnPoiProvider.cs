using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using ED_Inara_Overlay.Models;

namespace ED_Inara_Overlay.Services.Exploration;

internal sealed class CanonnPoiProvider(HttpClient httpClient)
{
    private const string ThargoidSites = "https://docs.google.com/spreadsheets/d/e/2PACX-1vRFRhsa3g0tpYFkqyBR2HrfUjXfjW6gSRnnDhFtVtPlWtpuNAHKujI5fH6Lnh3ctt0SAyNywnesv8H_/pub?gid=1675294629&single=true&output=tsv";
    private const string GuardianSites = "https://drive.google.com/uc?id=1m8q9lE4_cAI8CotM-oaEm5RWHeeJjoil";
    private readonly SemaphoreSlim loadLock = new(1, 1);
    private IReadOnlyList<CanonnSite> sites = Array.Empty<CanonnSite>();
    private DateTimeOffset loadedUtc;

    public async Task<ExplorationPoiSnapshot?> GetNearestAsync(
        double x, double y, double z, CancellationToken token)
    {
        await EnsureLoadedAsync(token).ConfigureAwait(false);
        CanonnSite? nearest = sites.OrderBy(site => Distance(x, y, z, site.X, site.Y, site.Z)).FirstOrDefault();
        if (nearest is null) return null;
        double distance = Distance(x, y, z, nearest.X, nearest.Y, nearest.Z);
        return new ExplorationPoiSnapshot(
            "Canonn", nearest.System, nearest.Name, nearest.System, nearest.Category,
            string.Empty, nearest.Description, nearest.Url, 0, distance,
            nearest.X, nearest.Y, nearest.Z, loadedUtc);
    }

    private async Task EnsureLoadedAsync(CancellationToken token)
    {
        if (sites.Count > 0 && DateTimeOffset.UtcNow - loadedUtc < TimeSpan.FromHours(24)) return;
        await loadLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (sites.Count > 0 && DateTimeOffset.UtcNow - loadedUtc < TimeSpan.FromHours(24)) return;
            Task<string> thargoids = httpClient.GetStringAsync(ThargoidSites, token);
            Task<string> guardians = httpClient.GetStringAsync(GuardianSites, token);
            await Task.WhenAll(thargoids, guardians).ConfigureAwait(false);
            sites = ParseThargoids(await thargoids.ConfigureAwait(false))
                .Concat(ParseGuardians(await guardians.ConfigureAwait(false)))
                .ToArray();
            loadedUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            loadLock.Release();
        }
    }

    internal static IReadOnlyList<CanonnSite> ParseThargoids(string tsv)
    {
        return tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(1)
            .Select(line => line.TrimEnd('\r').Split('\t'))
            .Where(fields => fields.Length >= 7
                             && TryNumber(fields[2], out _) && TryNumber(fields[3], out _) && TryNumber(fields[4], out _))
            .Select(fields => new CanonnSite(
                fields[1], "Thargoid site", "Canonn · Thargoid", fields[5], fields[6],
                ParseNumber(fields[2]), ParseNumber(fields[3]), ParseNumber(fields[4])))
            .ToArray();
    }

    internal static IReadOnlyList<CanonnSite> ParseGuardians(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<CanonnSite>();
        return document.RootElement.EnumerateArray().Select(item => new CanonnSite(
                Text(item, "system"), "Guardian site", "Canonn · Guardian", Text(item, "instructions"),
                Text(item, "url"), Number(item, "x"), Number(item, "y"), Number(item, "z")))
            .Where(site => !string.IsNullOrWhiteSpace(site.System)).ToArray();
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static double Number(JsonElement item, string name) => ParseNumber(Text(item, name));
    private static bool TryNumber(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    private static double ParseNumber(string value) => TryNumber(value, out double result) ? result : 0;
    private static double Distance(double x1, double y1, double z1, double x2, double y2, double z2) =>
        Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2) + Math.Pow(z2 - z1, 2));

    internal sealed record CanonnSite(
        string System, string Name, string Category, string Description, string Url,
        double X, double Y, double Z);
}
