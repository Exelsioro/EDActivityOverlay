using ED_Inara_Overlay.Models;
using ED_Inara_Overlay.Services.Journal;

namespace ED_Inara_Overlay.Services.Hardware;

public sealed class X52IntegrationService : IDisposable
{
    private readonly object sync = new();
    private readonly object outputSync = new();
    private readonly X52SoftButtonFilter softButtonFilter = new();
    private DirectOutputClient? client;
    private X52IntegrationState state = X52IntegrationState.Disabled;
    private ActivityType activity = ActivityType.Trade;
    private string[] lastLines = [];
    private Dictionary<int, bool> lastLeds = [];
    private Timer? animationTimer;
    private Timer? inputTimer;
    private long animationStep;
    private bool started;
    private bool disposed;

    public static X52IntegrationService Instance { get; } = new();

    public event EventHandler<X52StateChangedEventArgs>? StateChanged;
    public event EventHandler<X52ControlEventArgs>? ControlRequested;

    public X52IntegrationState Current { get { lock (sync) return state; } }

    private X52IntegrationService()
    {
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (started) return;
        started = true;
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;
        JournalMonitorService.Instance.StateChanged += OnJournalStateChanged;
        animationTimer = new Timer(_ => OnAnimationTick(), null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
        inputTimer = new Timer(_ => OnInputTick(), null, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
        ApplySettings(forceReconnect: true);
    }

    public void SetActivity(ActivityType value)
    {
        activity = value;
        RefreshOutput(force: true);
    }

    public void Reconnect()
    {
        if (!started) Start();
        ApplySettings(forceReconnect: true);
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => ApplySettings(forceReconnect: false);

    private void ApplySettings(bool forceReconnect)
    {
        AppSettings settings = SettingsService.Instance.Settings;
        if (!settings.EnableX52MfdControls)
        {
            softButtonFilter.Reset();
        }
        if (!settings.EnableX52Support)
        {
            Disconnect();
            SetState(X52IntegrationState.Disabled);
            return;
        }
        if (client is not null && !forceReconnect)
        {
            RefreshOutput(force: true);
            return;
        }

        Disconnect();
        string driverPath = DirectOutputClient.FindDriverPath();
        if (string.IsNullOrWhiteSpace(driverPath))
        {
            SetState(new X52IntegrationState(X52ConnectionStatus.DriverMissing, string.Empty, string.Empty));
            return;
        }
        try
        {
            var connectedClient = new DirectOutputClient();
            connectedClient.DeviceAvailabilityChanged += OnDeviceAvailabilityChanged;
            connectedClient.PageActivated += OnPageActivated;
            connectedClient.SoftButtonsChanged += OnSoftButtonsChanged;
            connectedClient.InitializeClient();
            client = connectedClient;
            SetState(new X52IntegrationState(
                connectedClient.HasDevice ? X52ConnectionStatus.Connected : X52ConnectionStatus.WaitingForDevice,
                connectedClient.DriverPath,
                string.Empty));
            RefreshOutput(force: true);
            Logger.Logger.Info($"X52 DirectOutput initialized: device={connectedClient.HasDevice}, driver={connectedClient.DriverPath}");
        }
        catch (Exception ex)
        {
            Disconnect();
            SetState(new X52IntegrationState(X52ConnectionStatus.Error, driverPath, ex.Message));
            Logger.Logger.Warning($"X52 DirectOutput initialization failed: {ex.Message}");
        }
    }

    private void OnJournalStateChanged(object? sender, GameStateChangedEventArgs e) => RefreshOutput(e.State);

    private void OnInputTick()
    {
        if (SettingsService.Instance.Settings.EnableX52MfdControls
            && softButtonFilter.ProcessPending(Environment.TickCount64) is { } action)
        {
            EmitControlAction(action, "timer");
        }
    }

    private void OnAnimationTick()
    {
        GameStateSnapshot game = JournalMonitorService.Instance.Current;
        if (!game.FsdCharging && !game.IsInDanger && !game.LowFuel && !game.OverHeating)
        {
            return;
        }

        Interlocked.Increment(ref animationStep);
        RefreshOutput(game);
    }

    private void OnDeviceAvailabilityChanged(bool available)
    {
        string path = client?.DriverPath ?? Current.DriverPath;
        SetState(new X52IntegrationState(
            available ? X52ConnectionStatus.Connected : X52ConnectionStatus.WaitingForDevice,
            path,
            string.Empty));
        if (available) RefreshOutput(force: true);
    }

    private void OnPageActivated() => RefreshOutput(force: true);

    private void OnSoftButtonsChanged(uint buttons)
    {
        if (!SettingsService.Instance.Settings.EnableX52MfdControls) return;
        long now = Environment.TickCount64;
        X52ControlAction? action = softButtonFilter.Process(buttons, now);
        if (action is null)
        {
            Logger.Logger.Debug($"X52 MFD input ignored by debounce/filter: mask=0x{buttons:X8}");
            return;
        }

        EmitControlAction(action.Value, $"mask=0x{buttons:X8}");
    }

    private void EmitControlAction(X52ControlAction action, string source)
    {
        Logger.Logger.Debug($"X52 MFD input accepted: {source}, action={action}");
        ControlRequested?.Invoke(this, new X52ControlEventArgs(action));
    }

    private void RefreshOutput(GameStateSnapshot? suppliedState = null, bool force = false)
    {
        lock (outputSync)
        {
            DirectOutputClient? output = client;
            if (output is null || !output.HasDevice) return;
            AppSettings settings = SettingsService.Instance.Settings;
            GameStateSnapshot game = suppliedState ?? JournalMonitorService.Instance.Current;
            if (settings.EnableX52Mfd)
            {
                string[] lines = X52DisplayFormatter.BuildLines(game, activity);
                if (force || !lines.SequenceEqual(lastLines, StringComparer.Ordinal))
                {
                    output.WriteLines(lines);
                    lastLines = lines;
                }
            }
            else if (force)
            {
                output.WriteLines([string.Empty, string.Empty, string.Empty]);
                lastLines = [];
            }
            if (settings.EnableX52LedState)
            {
                Dictionary<int, bool> leds = X52DisplayFormatter.BuildLedComponents(
                        game,
                        activity,
                        Interlocked.Read(ref animationStep))
                    .ToDictionary(item => item.Key, item => item.Value);
                if (force || !leds.OrderBy(item => item.Key).SequenceEqual(lastLeds.OrderBy(item => item.Key)))
                {
                    output.WriteLedComponents(leds);
                    lastLeds = leds;
                }
            }
            else if (force)
            {
                output.WriteLedComponents(Enumerable.Range(0, 20).ToDictionary(index => index, _ => false));
                lastLeds.Clear();
            }
        }
    }

    private void Disconnect()
    {
        DirectOutputClient? old;
        lock (outputSync)
        {
            old = client;
            client = null;
            lastLines = [];
            lastLeds.Clear();
        }
        if (old is not null)
        {
            old.DeviceAvailabilityChanged -= OnDeviceAvailabilityChanged;
            old.PageActivated -= OnPageActivated;
            old.SoftButtonsChanged -= OnSoftButtonsChanged;
            old.Dispose();
        }
        softButtonFilter.Reset();
    }

    private void SetState(X52IntegrationState value)
    {
        lock (sync) state = value;
        StateChanged?.Invoke(this, new X52StateChangedEventArgs(value));
    }

    public void Dispose()
    {
        if (disposed) return;
        animationTimer?.Dispose();
        animationTimer = null;
        inputTimer?.Dispose();
        inputTimer = null;
        if (started)
        {
            SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
            JournalMonitorService.Instance.StateChanged -= OnJournalStateChanged;
            started = false;
        }
        Disconnect();
        disposed = true;
    }
}
