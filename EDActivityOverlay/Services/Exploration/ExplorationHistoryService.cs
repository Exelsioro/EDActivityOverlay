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
        if (SettingsService.Instance.Settings.EnableJournalIntegration)
        {
            StartImport(ResolveDirectory(configuredDirectory));
        }
    }

    public ExplorationSystemHistorySnapshot LoadSystem(GameStateSnapshot game) =>
        repository.LoadSystem(game.Commander, game.SystemAddress, game.StarSystem);

    public void OnJournalEvent(JournalEventReceivedEventArgs journalEvent)
    {
        // Historical reconstruction is already owned by the journal importer.
        // Do not replay the current journal a second time through the live path.
        if (journalEvent.Origin == JournalEventOrigin.Bootstrap)
        {
            return;
        }

        if (liveAccumulator.Apply(journalEvent.Data)) RaiseChanged();
    }

    public void OnCompanionFile(CompanionFileReceivedEventArgs companionFile)
    {
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Settings.EnableJournalIntegration) StartImport(ResolveDirectory(e.Settings.JournalDirectory));
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
