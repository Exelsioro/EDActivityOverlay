using System.IO;
using System.Text.Json;
using ED_Inara_Overlay.Models;
using ED_Inara_Overlay.Services.Journal;

namespace ED_Inara_Overlay.Services.Exploration;

public sealed class ExplorationRouteService : IDisposable
{
    private readonly string statePath;
    private ExplorationRoutePlan current = ExplorationRoutePlan.Empty;
    private bool started;

    public static ExplorationRouteService Instance { get; } = new();
    public ExplorationRoutePlan Current => current;
    public event EventHandler<ExplorationRouteChangedEventArgs>? RouteChanged;

    private ExplorationRouteService()
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ED_Inara_Overlay");
        Directory.CreateDirectory(directory);
        statePath = Path.Combine(directory, "exploration-route.json");
    }

    public void Start()
    {
        if (started) return;
        started = true;
        LoadSaved();
        JournalMonitorService.Instance.StateChanged += OnStateChanged;
        AdvanceTo(JournalMonitorService.Instance.Current.StarSystem);
    }

    public ExplorationRoutePlan Import(string path)
    {
        current = SpanshRouteFileParser.Parse(path);
        AdvanceTo(JournalMonitorService.Instance.Current.StarSystem, saveEvenIfUnchanged: true);
        RaiseChanged();
        return current;
    }

    public ExplorationRoutePlan SetPlan(ExplorationRoutePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Stops.Count == 0) throw new InvalidDataException("The route contains no systems.");
        current = plan;
        AdvanceTo(JournalMonitorService.Instance.Current.StarSystem, saveEvenIfUnchanged: true);
        RaiseChanged();
        return current;
    }

    public void Clear()
    {
        current = ExplorationRoutePlan.Empty;
        Save();
        RaiseChanged();
    }

    private void OnStateChanged(object? sender, GameStateChangedEventArgs e) => AdvanceTo(e.State.StarSystem);

    private void AdvanceTo(string system, bool saveEvenIfUnchanged = false)
    {
        if (string.IsNullOrWhiteSpace(system) || current.Stops.Count == 0)
        {
            if (saveEvenIfUnchanged) Save();
            return;
        }
        int index = current.Stops.ToList().FindIndex(stop => stop.System.Equals(system, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index == current.CurrentIndex)
        {
            if (saveEvenIfUnchanged) Save();
            return;
        }
        current = current with { CurrentIndex = index };
        Save();
        RaiseChanged();
    }

    private void LoadSaved()
    {
        try
        {
            if (!File.Exists(statePath)) return;
            current = JsonSerializer.Deserialize<ExplorationRoutePlan>(File.ReadAllText(statePath))
                      ?? ExplorationRoutePlan.Empty;
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning($"Exploration route state could not be loaded: {ex.Message}");
            current = ExplorationRoutePlan.Empty;
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(statePath, JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning($"Exploration route state could not be saved: {ex.Message}");
        }
    }

    private void RaiseChanged() => RouteChanged?.Invoke(this, new ExplorationRouteChangedEventArgs(current));

    public void Dispose()
    {
        if (!started) return;
        JournalMonitorService.Instance.StateChanged -= OnStateChanged;
        started = false;
    }
}
