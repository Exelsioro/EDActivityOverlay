using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Exploration;

public sealed class SpanshRouteClient : IDisposable
{
    private readonly HttpClient httpClient;
    private CancellationTokenSource? activeRequest;

    public SpanshRouteCalculationState Current { get; private set; } = SpanshRouteCalculationState.Idle;
    public event EventHandler<SpanshRouteCalculationChangedEventArgs>? Changed;

    public SpanshRouteClient()
    {
        httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ED-Inara-Overlay", "1.0"));
    }

    public async Task<ExplorationRoutePlan> CalculateRoadToRichesAsync(SpanshRoadToRichesRequest request)
    {
        try
        {
            return await CalculateRoadToRichesCoreAsync(request).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SetState(new SpanshRouteCalculationState(SpanshRouteCalculationStatus.Failed, ex.Message, null));
            throw;
        }
    }

    private async Task<ExplorationRoutePlan> CalculateRoadToRichesCoreAsync(SpanshRoadToRichesRequest request)
    {
        ValidateParameters(request);
        activeRequest?.Cancel();
        activeRequest?.Dispose();
        activeRequest = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken token = activeRequest.Token;
        SetState(new SpanshRouteCalculationState(SpanshRouteCalculationStatus.Validating, string.Empty, null));
        string source = await ValidateSystemAsync(request.Source, token).ConfigureAwait(false);
        string destination = string.IsNullOrWhiteSpace(request.Destination)
            ? string.Empty
            : await ValidateSystemAsync(request.Destination, token).ConfigureAwait(false);

        SetState(new SpanshRouteCalculationState(SpanshRouteCalculationStatus.Calculating, string.Empty, null));
        var fields = new Dictionary<string, string>
        {
            ["from"] = source,
            ["range"] = request.JumpRange.ToString("0.##", CultureInfo.InvariantCulture),
            ["radius"] = request.Radius.ToString(CultureInfo.InvariantCulture),
            ["max_results"] = request.MaximumSystems.ToString(CultureInfo.InvariantCulture),
            ["max_distance"] = request.MaximumDistance.ToString(CultureInfo.InvariantCulture),
            ["min_value"] = request.MinimumValue.ToString(CultureInfo.InvariantCulture),
            ["use_mapping_value"] = request.UseMappingValue ? "1" : "0",
            ["loop"] = request.Loop ? "1" : "0",
            ["avoid_thargoids"] = request.AvoidThargoids ? "1" : "0"
        };
        if (!string.IsNullOrWhiteSpace(destination)) fields["to"] = destination;

        using HttpResponseMessage response = await httpClient.PostAsync(
            "https://spansh.co.uk/api/riches/route", new FormUrlEncodedContent(fields), token).ConfigureAwait(false);
        string json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ReadError(json, response.StatusCode.ToString()));
        json = await ResolveJobAsync(json, token).ConfigureAwait(false);
        ExplorationRoutePlan plan = SpanshRouteFileParser.ParseJson(json, "Spansh Road to Riches API");
        SetState(new SpanshRouteCalculationState(SpanshRouteCalculationStatus.Completed, string.Empty, plan));
        return plan;
    }

    private async Task<string> ValidateSystemAsync(string system, CancellationToken token)
    {
        string value = system.Trim();
        using HttpResponseMessage response = await httpClient.GetAsync(
            $"https://spansh.co.uk/api/systems?q={Uri.EscapeDataString(value)}", token).ConfigureAwait(false);
        string json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            string? exact = document.RootElement.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .FirstOrDefault(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(exact)) return exact;
        }
        throw new InvalidOperationException($"System '{value}' was not found in Spansh.");
    }

    private async Task<string> ResolveJobAsync(string json, CancellationToken token)
    {
        using (JsonDocument document = JsonDocument.Parse(json))
        {
            JsonElement root = document.RootElement;
            if (HasDirectRoute(root)) return json;
            if (!root.TryGetProperty("job", out JsonElement jobValue))
                throw new InvalidOperationException(ReadError(json, "Spansh did not return a route job."));
            string job = jobValue.ToString();
            for (int attempt = 0; attempt < 120; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                using HttpResponseMessage response = await httpClient.GetAsync(
                    $"https://spansh.co.uk/api/results/{Uri.EscapeDataString(job)}", token).ConfigureAwait(false);
                string result = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.Accepted) continue;
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ReadError(result, response.StatusCode.ToString()));
                using JsonDocument resultDocument = JsonDocument.Parse(result);
                JsonElement resultRoot = resultDocument.RootElement;
                if (resultRoot.TryGetProperty("error", out JsonElement error))
                    throw new InvalidOperationException(error.ToString());
                if (HasDirectRoute(resultRoot)
                    || Text(resultRoot, "status") == "ok"
                    || Text(resultRoot, "state") == "completed") return result;
            }
        }
        throw new TimeoutException("Spansh route calculation timed out.");
    }

    private static bool HasDirectRoute(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return true;
        if (root.ValueKind != JsonValueKind.Object) return false;
        foreach (string key in new[] { "systems", "system_jumps", "route", "jumps" })
            if (root.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.Array) return true;
        return root.TryGetProperty("result", out JsonElement result)
               && (result.ValueKind == JsonValueKind.Array || HasDirectRoute(result));
    }

    private static void ValidateParameters(SpanshRoadToRichesRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Source)) throw new ArgumentException("Source system is required.");
        if (request.JumpRange is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(request.JumpRange));
        if (request.Radius is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(request.Radius));
        if (request.MaximumSystems is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(request.MaximumSystems));
        if (request.MaximumDistance is < 1 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(request.MaximumDistance));
        if (request.MinimumValue < 0) throw new ArgumentOutOfRangeException(nameof(request.MinimumValue));
    }

    private void SetState(SpanshRouteCalculationState value)
    {
        Current = value;
        Changed?.Invoke(this, new SpanshRouteCalculationChangedEventArgs(value));
    }
    private static string ReadError(string json, string fallback)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return Text(document.RootElement, "error", fallback);
        }
        catch (JsonException) { return string.IsNullOrWhiteSpace(json) ? fallback : json; }
    }
    private static string Text(JsonElement root, string name, string fallback = "") =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement value)
            ? value.ToString() : fallback;

    public void Dispose()
    {
        activeRequest?.Cancel();
        activeRequest?.Dispose();
        httpClient.Dispose();
    }
}
