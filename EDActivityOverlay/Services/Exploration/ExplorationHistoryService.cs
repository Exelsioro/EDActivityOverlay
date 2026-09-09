using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;

namespace EDActivityOverlay.Services.Exploration;

public sealed class ExplorationHistoryService : IJournalDataConsumer, IDisposable
{
    private readonly ExplorationHistoryRepository repository = new();
    private readonly ExplorationHistoryAccumulator liveAccumulator;
    private CancellationTokenSource? importCancellation;
    private bool started;
    private bool disposed;
    private bool journalEnabled;
    private string journalDirectory = string.Empty;
    private ExplorationHistoryImportState importState = ExplorationHistoryImportState.Idle;

    public static ExplorationHistoryService Instance { get; } = new();

    public event EventHandler<ExplorationHistoryChangedEventArgs>? HistoryChanged;
    public ExplorationHistoryImportState ImportState => importState;

    private ExplorationHistoryService()
    {
        liveAccumulator = new ExplorationHistoryAccumulator(repository);
    }

    public void Start(string? configuredDirectory = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!started)
        {
            JournalMonitorService.Instance.Events.Register(this);
            SettingsService.Instance.SettingsChanged += OnSettingsChanged;
            started = true;
        }
        journalEnabled = SettingsService.Instance.Settings.EnableJournalIntegration;
        journalDirectory = ResolveDirectory(configuredDirectory);
        liveAccumulator.Reset();
        if (journalEnabled)
        {
            StartImport(journalDirectory);
        }
    }

    public ExplorationSystemHistorySnapshot LoadSystem(GameStateSnapshot game) =>
        repository.LoadSystem(game.Commander, game.SystemAddress, game.StarSystem);

    /// <summary>
    /// Resolves a previously visited system without manufacturing a temporary
    /// game snapshot. This is used for the destination preview shown before a
    /// jump, where only the target name/address is available.
    /// </summary>
    public ExplorationSystemHistorySnapshot LoadSystem(
        string commander,
        long systemAddress,
        string systemName) =>
        repository.LoadSystem(commander, systemAddress, systemName);

    public void OnJournalEvent(JournalEventReceivedEventArgs journalEvent)
    {
        // The importer owns closed journal files; the monitor's bootstrap is
        // the only complete replay of the currently open file. Accepting it
        // restores commander/system context after an overlay restart without
        // double-processing historical files.
        if (!liveAccumulator.Apply(journalEvent.Data)) return;
        RaiseChanged();
    }

    public void OnCompanionFile(CompanionFileReceivedEventArgs companionFile)
    {
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        string directory = ResolveDirectory(e.Settings.JournalDirectory);
        if (journalEnabled == e.Settings.EnableJournalIntegration
            && string.Equals(journalDirectory, directory, StringComparison.OrdinalIgnoreCase)) return;
        journalEnabled = e.Settings.EnableJournalIntegration;
        journalDirectory = directory;
        liveAccumulator.Reset();
        if (journalEnabled) StartImport(directory);
        else CancelImport();
    }

    private void StartImport(string directory)
    {
        CancelImport();
        importCancellation = new CancellationTokenSource();
        CancellationToken token = importCancellation.Token;
        var importer = new ExplorationJournalImporter(repository);
        _ = Task.Run(async () =>
        {
            try
            {
                await importer.ImportAsync(directory, SetImportState, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Logger.Logger.Warning($"Exploration history import failed: {ex.Message}");
                SetImportState(importState with { IsRunning = false, Error = ex.Message });
            }
        }, token);
    }

    private void SetImportState(ExplorationHistoryImportState value)
    {
        importState = value;
        RaiseChanged();
    }

    private void RaiseChanged() => HistoryChanged?.Invoke(this, new ExplorationHistoryChangedEventArgs(importState));

    private void CancelImport()
    {
        importCancellation?.Cancel();
        importCancellation?.Dispose();
        importCancellation = null;
    }

    private static string ResolveDirectory(string? configuredDirectory) =>
        string.IsNullOrWhiteSpace(configuredDirectory)
            ? JournalPathResolver.GetDefaultJournalDirectory()
            : configuredDirectory;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CancelImport();
        if (started)
        {
            JournalMonitorService.Instance.Events.Unregister(this);
            SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
            started = false;
        }
    }
}
