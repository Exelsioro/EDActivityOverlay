using EDActivityOverlay.Services;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class StorageUsageTests
{
    [Fact]
    public void StorageMeasurementSeparatesInstallationDatabaseAndCache()
    {
        StorageUsageSnapshot result = StorageUsageService.Measure();

        Assert.True(result.InstallationBytes >= 0);
        Assert.True(result.PersistentDataBytes >= result.DatabaseBytes);
        Assert.True(result.CacheBytes >= 0);
        Assert.EndsWith("EDActivityOverlay", result.PersistentDataDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SettingsExposeStoragePolicyAndCacheMaintenance()
    {
        string repository = FindRepositoryRoot();
        string markup = File.ReadAllText(Path.Combine(
            repository, "EDActivityOverlay", "Windows", "SettingsWindow.xaml"));

        Assert.Contains("x:Name=\"StorageUsageText\"", markup, StringComparison.Ordinal);
        Assert.Contains("Loc_STORAGE_CLEANUP_POLICY", markup, StringComparison.Ordinal);
        Assert.Contains("CleanupStorageButton_Click", markup, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EDActivityOverlay", "EDActivityOverlay.csproj")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
