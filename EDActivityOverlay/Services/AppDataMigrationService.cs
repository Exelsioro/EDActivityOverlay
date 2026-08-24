using System;
using System.IO;

namespace EDActivityOverlay.Services;

/// <summary>
/// One-time compatibility migration from the pre-rebrand application folders.
/// This is intentionally the only runtime code that knows the legacy product
/// folder name. It does not communicate with or depend on any external market-data provider.
/// </summary>
internal static class AppDataMigrationService
{
    private const string CurrentFolderName = "EDActivityOverlay";
    private const string LegacyFolderName = "ED_Inara_Overlay";

    public static void MigrateLegacyDirectories()
    {
        MigrateSpecialFolder(Environment.SpecialFolder.ApplicationData);
        MigrateSpecialFolder(Environment.SpecialFolder.LocalApplicationData);
    }

    private static void MigrateSpecialFolder(Environment.SpecialFolder specialFolder)
    {
        string root = Environment.GetFolderPath(specialFolder);

        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        string legacyPath = Path.Combine(root, LegacyFolderName);
        string currentPath = Path.Combine(root, CurrentFolderName);

        if (!Directory.Exists(legacyPath))
        {
            return;
        }

        try
        {
            if (!Directory.Exists(currentPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(currentPath)!);
                Directory.Move(legacyPath, currentPath);
                Logger.Logger.Info(
                    $"Migrated legacy application data directory: {legacyPath} -> {currentPath}");
                return;
            }

            // A new directory may already exist if a development/intermediate
            // build was started after the rename. Merge missing files only;
            // never overwrite data already written by the new application.
            CopyMissingTree(legacyPath, currentPath);

            Logger.Logger.Info(
                $"Merged missing legacy application data from {legacyPath} into {currentPath}");
        }
        catch (Exception ex)
        {
            // Migration must never prevent the application from starting.
            Logger.Logger.Error(
                $"Legacy application data migration failed for {legacyPath}: {ex.Message}");
        }
    }

    private static void CopyMissingTree(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (string directory in Directory.EnumerateDirectories(
                     sourceDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }

        foreach (string sourceFile in Directory.EnumerateFiles(
                     sourceDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, sourceFile);
            string destinationFile = Path.Combine(destinationDirectory, relative);

            if (File.Exists(destinationFile))
            {
                continue;
            }

            string? destinationParent = Path.GetDirectoryName(destinationFile);

            if (!string.IsNullOrWhiteSpace(destinationParent))
            {
                Directory.CreateDirectory(destinationParent);
            }

            File.Copy(sourceFile, destinationFile, overwrite: false);
        }
    }
}