using System.IO;
using System.Text.Json;
using ED_Inara_Overlay.Models;

namespace ED_Inara_Overlay.Services.Exploration;

internal sealed class ExplorationProgressStore
{
    private readonly object sync = new();
    private readonly string filePath;

    public ExplorationProgressStore(string? filePath = null)
    {
        this.filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ED_Inara_Overlay",
            "exploration-progress.json");
    }

    public IReadOnlyList<OrganicScanProgressSnapshot> Load()
    {
        lock (sync)
        {
            try
            {
                if (!File.Exists(filePath)) return Array.Empty<OrganicScanProgressSnapshot>();
                return JsonSerializer.Deserialize<List<OrganicScanProgressSnapshot>>(File.ReadAllText(filePath))
                       ?? new List<OrganicScanProgressSnapshot>();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Logger.Logger.Warning($"Exploration progress could not be loaded: {ex.Message}");
                return Array.Empty<OrganicScanProgressSnapshot>();
            }
        }
    }

    public void Save(IEnumerable<OrganicScanProgressSnapshot> progress)
    {
        lock (sync)
        {
            try
            {
                string? directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                string temporary = filePath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(progress, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
                File.Move(temporary, filePath, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.Logger.Warning($"Exploration progress could not be saved: {ex.Message}");
            }
        }
    }
}
