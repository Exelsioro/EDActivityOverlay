using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace EDActivityOverlay.Services.Mining;

public sealed class MiningEdToolsQualityProvider : IMiningLocationQualityProvider
{
    internal const string SourceUrl = "https://edtools.cc/list?ord=24";

    private static readonly Uri Endpoint = new(SourceUrl);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(12);
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private static readonly Regex RowRegex = new(
        @"<tr\b[^>]*>(?<row>.*?)</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex CellRegex = new(
        @"<t[dh]\b[^>]*>(?<cell>.*?)</t[dh]>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TagRegex = new(
        @"<[^>]+>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex YieldRegex = new(
        @"(?<ring>.+?\bRing)\s*(?:ovr(?:\s*x\d+)?)?\s*:\s*(?<yield>\d+(?:[.,]\d+)?)\s*%",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient httpClient;
    private readonly SemaphoreSlim gate = new(1, 1);
    private IReadOnlyList<MiningLocationQualitySite> cached =
        Array.Empty<MiningLocationQualitySite>();
    private DateTimeOffset cachedAt = DateTimeOffset.MinValue;

    public MiningEdToolsQualityProvider()
        : this(SharedHttpClient)
    {
    }

    internal MiningEdToolsQualityProvider(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<(IReadOnlyList<MiningLocationQualitySite> Sites, IReadOnlyList<string> Warnings)> LoadAsync(
        CancellationToken cancellationToken)
    {
        if (cached.Count > 0 && DateTimeOffset.UtcNow - cachedAt < CacheTtl)
        {
            return (cached, Array.Empty<string>());
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cached.Count > 0 && DateTimeOffset.UtcNow - cachedAt < CacheTtl)
            {
                return (cached, Array.Empty<string>());
            }

            try
            {
                string html = await httpClient
                    .GetStringAsync(Endpoint, cancellationToken)
                    .ConfigureAwait(false);

                DateTimeOffset observedUtc = DateTimeOffset.UtcNow;
                MiningLocationQualitySite[] rows =
                    ParseHtml(html, observedUtc).ToArray();

                if (rows.Length == 0)
                {
                    return (
                        Array.Empty<MiningLocationQualitySite>(),
                        ["E:D Tools high-yield list returned no parseable Platinum rows."]);
                }

                cached = rows;
                cachedAt = observedUtc;
                return (cached, Array.Empty<string>());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return (
                    Array.Empty<MiningLocationQualitySite>(),
                    [$"E:D Tools high-yield data unavailable: {ex.Message}"]);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    internal static IReadOnlyList<MiningLocationQualitySite> ParseHtml(
        string html,
        DateTimeOffset? observedUtc = null)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return Array.Empty<MiningLocationQualitySite>();
        }

        DateTimeOffset observed = observedUtc ?? DateTimeOffset.UtcNow;
        var rows = new List<MiningLocationQualitySite>();

        foreach (Match rowMatch in RowRegex.Matches(html))
        {
            string row = rowMatch.Groups["row"].Value;
            string[] cells = CellRegex.Matches(row)
                .Cast<Match>()
                .Select(match => CleanCell(match.Groups["cell"].Value))
                .Where(cell => !string.IsNullOrWhiteSpace(cell))
                .ToArray();

            if (cells.Length < 3)
            {
                continue;
            }

            string system = cells[1];
            if (system.StartsWith("Image ", StringComparison.OrdinalIgnoreCase))
            {
                system = system[6..].Trim();
            }

            Match yieldMatch = YieldRegex.Match(cells[2]);
            if (!yieldMatch.Success
                || string.IsNullOrWhiteSpace(system))
            {
                continue;
            }

            string ring = yieldMatch.Groups["ring"].Value.Trim();
            string rawYield = yieldMatch.Groups["yield"].Value.Replace(',', '.');

            if (!double.TryParse(
                    rawYield,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double average)
                || average <= 0
                || average > 100)
            {
                continue;
            }

            rows.Add(new MiningLocationQualitySite(
                system,
                ring,
                "Platinum",
                average,
                "E:D Tools Platinum high-yield survey",
                SourceUrl,
                observed));
        }

        return rows
            .GroupBy(
                row => MiningLocationKey.For(row.SystemName, row.RingName),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(row => row.AverageContentPercent)
                .First())
            .ToArray();
    }

    private static string CleanCell(string html)
    {
        string withoutTags = TagRegex.Replace(html, " ");
        string decoded = WebUtility.HtmlDecode(withoutTags);

        return string.Join(
            " ",
            decoded.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries))
            .Trim();
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "EDActivityOverlay/MiningLocationQuality");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        return client;
    }
}

internal sealed class NullMiningLocationQualityProvider : IMiningLocationQualityProvider
{
    public Task<(IReadOnlyList<MiningLocationQualitySite> Sites, IReadOnlyList<string> Warnings)> LoadAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult((
            (IReadOnlyList<MiningLocationQualitySite>)Array.Empty<MiningLocationQualitySite>(),
            (IReadOnlyList<string>)Array.Empty<string>()));
}
