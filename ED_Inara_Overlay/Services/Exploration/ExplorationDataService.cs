using System.Net.Http;
using System.Net.Http.Headers;
using ED_Inara_Overlay.Models;
using ED_Inara_Overlay.Services.Journal;

namespace ED_Inara_Overlay.Services.Exploration;

public sealed class ExplorationDataService : IDisposable
{
    private readonly object sync = new();
    private readonly HttpClient httpClient;
    private readonly ExplorationDataLoader loader;
    private CancellationTokenSource? requestCancellation;
    private ExplorationDataState state = ExplorationDataState.Idle;
    private string requestedSystem = string.Empty;
    private bool configuredOnlineEnabled;
    private bool configuredEdsmFallback;
    private int configuredCacheHours;
    private bool started;
    private bool disposed;

    public static ExplorationDataService Instance { get; } = new();

    public event EventHandler<ExplorationDataChangedEventArgs>? DataChanged;

    public ExplorationDataState Current
    {
        get { lock (sync) return state; }
    }

    private ExplorationDataService()
    {
        httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ED-Inara-Overlay", "1.0"));
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        loader = new ExplorationDataLoader(
            new ExplorationSystemCache(),
            new IExplorationSystemProvider[]
            {
                new SpanshExplorationProvider(httpClient),
                new EdsmExplorationProvider(httpClient)
            });
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (started) return;
        started = true;
        CaptureSettings();
        _ = Task.Run(() => StorageUsageService.CleanupExpiredCaches(
            TimeSpan.FromHours(Math.Clamp(configuredCacheHours, 1, 720))));
        JournalMonitorService.Instance.StateChanged += OnJournalStateChanged;
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;
        RequestSystem(JournalMonitorService.Instance.Current, force: true);
    }

    public void Stop()
    {
        if (!started) return;
        started = false;
        JournalMonitorService.Instance.StateChanged -= OnJournalStateChanged;
        SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
        lock (sync)
        {
            requestCancellation?.Cancel();
            requestCancellation?.Dispose();
            requestCancellation = null;
            requestedSystem = string.Empty;
        }
    }

    public void Refresh() => RequestSystem(JournalMonitorService.Instance.Current, force: true, refreshNetwork: true);

    private void OnJournalStateChanged(object? sender, GameStateChangedEventArgs e) => RequestSystem(e.State);

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (configuredOnlineEnabled == e.Settings.EnableOnlineExplorationData
            && configuredEdsmFallback == e.Settings.EnableEdsmFallback
            && configuredCacheHours == e.Settings.ExplorationCacheHours)
        {
            return;
        }
        CaptureSettings();
        _ = Task.Run(() => StorageUsageService.CleanupExpiredCaches(
            TimeSpan.FromHours(Math.Clamp(configuredCacheHours, 1, 720))));
        RequestSystem(JournalMonitorService.Instance.Current, force: true);
    }

    private void RequestSystem(GameStateSnapshot game, bool force = false, bool refreshNetwork = false)
    {
        bool enabled = SettingsService.Instance.Settings.EnableOnlineExplorationData;
        if (!enabled)
        {
            CancelCurrentRequest();
            SetState(new ExplorationDataState(ExplorationDataStatus.Disabled, null, string.Empty));
            return;
        }
        if (string.IsNullOrWhiteSpace(game.StarSystem))
        {
            CancelCurrentRequest();
            SetState(ExplorationDataState.Idle);
            return;
        }

        string key = $"{game.SystemAddress}|{game.StarSystem}";
        CancellationToken token;
        lock (sync)
        {
            if (!force && string.Equals(requestedSystem, key, StringComparison.OrdinalIgnoreCase)) return;
            requestedSystem = key;
            requestCancellation?.Cancel();
            requestCancellation?.Dispose();
            requestCancellation = new CancellationTokenSource();
            token = requestCancellation.Token;
            state = new ExplorationDataState(ExplorationDataStatus.Loading, null, string.Empty);
        }
        RaiseChanged();
        _ = LoadSystemAsync(game.SystemAddress, game.StarSystem, key, refreshNetwork, token);
    }

    private async Task LoadSystemAsync(
        long address,
        string name,
        string key,
        bool refreshNetwork,
        CancellationToken cancellationToken)
    {
        try
        {
            AppSettings settings = SettingsService.Instance.Settings;
            ExplorationSystemDataSnapshot? result = await loader.LoadAsync(
                address,
                name,
                TimeSpan.FromHours(Math.Clamp(settings.ExplorationCacheHours, 1, 720)),
                settings.EnableEdsmFallback,
                refreshNetwork,
                cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested || !IsCurrentRequest(key)) return;
            SetState(result is null
                ? new ExplorationDataState(ExplorationDataStatus.Unavailable, null, string.Empty)
                : new ExplorationDataState(ExplorationDataStatus.Available, result, string.Empty));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning($"Exploration enrichment failed: {ex.Message}");
            if (IsCurrentRequest(key))
            {
                SetState(new ExplorationDataState(ExplorationDataStatus.Unavailable, null, ex.Message));
            }
        }
    }

    private bool IsCurrentRequest(string key)
    {
        lock (sync) return string.Equals(requestedSystem, key, StringComparison.OrdinalIgnoreCase);
    }

    private void CaptureSettings()
    {
        AppSettings settings = SettingsService.Instance.Settings;
        configuredOnlineEnabled = settings.EnableOnlineExplorationData;
        configuredEdsmFallback = settings.EnableEdsmFallback;
        configuredCacheHours = settings.ExplorationCacheHours;
    }

    private void CancelCurrentRequest()
    {
        lock (sync)
        {
            requestedSystem = string.Empty;
            requestCancellation?.Cancel();
            requestCancellation?.Dispose();
            requestCancellation = null;
        }
    }

    private void SetState(ExplorationDataState value)
    {
        lock (sync) state = value;
        RaiseChanged();
    }

    private void RaiseChanged() => DataChanged?.Invoke(this, new ExplorationDataChangedEventArgs(Current));

    public void Dispose()
    {
        if (disposed) return;
        Stop();
        httpClient.Dispose();
        disposed = true;
    }
}
