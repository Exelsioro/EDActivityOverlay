using System.IO;

namespace EDActivityOverlay.Services.Trading;

/// <summary>
/// Resolves the directory/file used by durable trade execution history.
/// Empty configuration preserves the original %APPDATA%/EDActivityOverlay path.
/// </summary>
public static class TradeHistoryPathResolver
{
    public const string FileName = "trade-history.jsonl";

    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "EDActivityOverlay");

    public static string ResolveDirectory(
        string? configuredDirectory)
    {
        string directory =
            string.IsNullOrWhiteSpace(
                configuredDirectory)
                ? DefaultDirectory
                : Environment.ExpandEnvironmentVariables(
                    configuredDirectory.Trim());

        string resolved =
            Path.GetFullPath(
                directory);

        if (File.Exists(
                resolved))
        {
            throw new IOException(
                "Trade history storage path points to a file, not a directory.");
        }

        return
            resolved;
    }

    public static string ResolveFilePath(
        string? configuredDirectory) =>
        Path.Combine(
            ResolveDirectory(
                configuredDirectory),
            FileName);
}
