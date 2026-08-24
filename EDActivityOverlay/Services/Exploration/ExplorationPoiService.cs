using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.Services.Exploration;

public sealed class ExplorationPoiService : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly CanonnPoiProvider canonnProvider;
    private readonly ExplorationPoiCache cache = new();
    private CancellationTokenSource? cancellation;
    private string requestKey = string.Empty;
    private bool started;

    public static ExplorationPoiService Instance { get; } = new();
    public ExplorationPoiState Current { get; private set; } = ExplorationPoiState.Idle;
    public event EventHandler<ExplorationPoiChangedEventArgs>? PoiChanged;

    private ExplorationPoiService()
    {
        httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ED-Inara-Overlay", "1.0"));
        canonnProvider = new CanonnPoiProvider(httpClient);
    }

    public void Start()
    {
        if (started) return;
        started = true;
        JournalMonitorService.Instance.StateChanged += OnStateChanged;
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;
        Request(JournalMonitorService.Instance.Current, false);
    }

    public void Refresh() => Request(JournalMonitorService.Instance.Current, true);

    private void OnStateChanged(object? sender, GameStateChangedEventArgs e) => Request(e.State, false);
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => Request(JournalMonitorService.Instance.Current, true);

    private void Request(GameStateSnapshot game, bool force)
    {
        AppSettings settings = SettingsService.Instance.Settings;
        if (!settings.EnableOnlineExplorationData || !settings.EnableExplorationPoiData)
        {
            Cancel();
            SetState(new ExplorationPoiState(ExplorationPoiStatus.Disabled, null, string.Empty));
            return;
        }
        if (game.SystemX is not { } x || game.SystemY is not { } y || game.SystemZ is not { } z)
        {
            SetState(ExplorationPoiState.Idle);
            return;
        }
        int rating = Math.Clamp(settings.ExplorationPoiMinRating, 0, 10);
        string key = $"{game.SystemAddress}|{x:0.###}|{y:0.###}|{z:0.###}|{rating}";
        if (!force && key == requestKey) return;
        if (!force && cache.TryGet(key, TimeSpan.FromHours(24), out ExplorationPoiState cached))
        {
            requestKey = key;
            SetState(cached);
            return;
        }
        requestKey = key;
        Cancel(keepKey: true);
        cancellation = new CancellationTokenSource();
        CancellationToken token = cancellation.Token;
        SetState(new ExplorationPoiState(ExplorationPoiStatus.Loading, Current.Nearest, string.Empty));
        _ = LoadAsync(x, y, z, rating, key, token);
    }

    private async Task LoadAsync(double x, double y, double z, int minRating, string key, CancellationToken token)
    {
        try
        {
            Task<(ExplorationPoiSnapshot? Poi, string Error)> edAstroTask = TryLoadEdAstroAsync(x, y, z, minRating, token);
            Task<(ExplorationPoiSnapshot? Poi, string Error)> canonnTask = TryLoadCanonnAsync(x, y, z, token);
            await Task.WhenAll(edAstroTask, canonnTask).ConfigureAwait(false);
            (ExplorationPoiSnapshot? poi, string edAstroError) = await edAstroTask.ConfigureAwait(false);
            (ExplorationPoiSnapshot? canonn, string canonnError) = await canonnTask.ConfigureAwait(false);
            if (!token.IsCancellationRequested && requestKey == key)
            {
                string error = string.Join(" | ", new[] { edAstroError, canonnError }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
                if (poi is null && canonn is null)
                {
                    SetState(new ExplorationPoiState(ExplorationPoiStatus.Unavailable, Current.Nearest, error));
                    return;
                }
                var available = new ExplorationPoiState(ExplorationPoiStatus.Available, poi, error)
                    { NearestCanonn = canonn };
                cache.Put(key, available);
                SetState(available);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.Logger.Warning($"Exploration POI lookup failed: {ex.Message}");
            if (!token.IsCancellationRequested && requestKey == key)
                SetState(new ExplorationPoiState(ExplorationPoiStatus.Unavailable, Current.Nearest, ex.Message));
        }
    }

    private async Task<(ExplorationPoiSnapshot? Poi, string Error)> TryLoadEdAstroAsync(
        double x, double y, double z, int minRating, CancellationToken token)
    {
        try
        {
            string UrlNumber(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
            string url = $"https://edastro.com/gec/json/nearest/{UrlNumber(x)}/{UrlNumber(y)}/{UrlNumber(z)}/{minRating}";
            using HttpResponseMessage response = await httpClient.GetAsync(url, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
            JsonElement root = document.RootElement;
            double[] coordinates = ReadCoordinates(root);
            double distance = Distance(x, y, z, coordinates[0], coordinates[1], coordinates[2]);
            return (new ExplorationPoiSnapshot(
                "EDAstro GEC", GetTextOrNumber(root, "id"), GetString(root, "name"),
                GetString(root, "galMapSearch"), GetString(root, "type"), GetString(root, "region"),
                GetString(root, "summary"), GetString(root, "poiUrl"), GetDouble(root, "rating"),
                distance, coordinates[0], coordinates[1], coordinates[2], DateTimeOffset.UtcNow), string.Empty);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Logger.Logger.Warning($"EDAstro POI lookup failed: {ex.Message}");
            return (null, $"EDAstro: {ex.Message}");
        }
    }

    private async Task<(ExplorationPoiSnapshot? Poi, string Error)> TryLoadCanonnAsync(
        double x, double y, double z, CancellationToken token)
    {
        try
        {
            return (await canonnProvider.GetNearestAsync(x, y, z, token).ConfigureAwait(false), string.Empty);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Logger.Logger.Warning($"Canonn POI lookup failed: {ex.Message}");
            return (null, $"Canonn: {ex.Message}");
        }
    }

    private void SetState(ExplorationPoiState state)
    {
        Current = state;
        PoiChanged?.Invoke(this, new ExplorationPoiChangedEventArgs(state));
    }

    private void Cancel(bool keepKey = false)
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
        if (!keepKey) requestKey = string.Empty;
    }

    private static double[] ReadCoordinates(JsonElement root)
    {
        var result = new double[3];
        if (!root.TryGetProperty("coordinates", out JsonElement array) || array.ValueKind != JsonValueKind.Array) return result;
        int index = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (index >= 3) break;
            result[index++] = item.TryGetDouble(out double value) ? value : 0;
        }
        return result;
    }

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static string GetTextOrNumber(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) ? value.ToString() : string.Empty;
    private static double GetDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.TryGetDouble(out double result) ? result : 0;
    private static double Distance(double x1, double y1, double z1, double x2, double y2, double z2) =>
        Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2) + Math.Pow(z2 - z1, 2));

    public void Dispose()
    {
        if (started)
        {
            JournalMonitorService.Instance.StateChanged -= OnStateChanged;
            SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
            started = false;
        }
        Cancel();
        httpClient.Dispose();
    }
}
