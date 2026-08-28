using System;
using System.IO;

namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Resolves the root used by DSS research/session diagnostics.
/// Empty configuration preserves the historical LocalAppData location.
/// </summary>
internal static class DssResearchPathResolver
{
    public static string DefaultRoot =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "EDActivityOverlay",
            "Research",
            "DSS");

    public static string Resolve(
        string? configuredDirectory)
    {
        if (string.IsNullOrWhiteSpace(
                configuredDirectory))
        {
            return DefaultRoot;
        }

        string expanded =
            Environment.ExpandEnvironmentVariables(
                configuredDirectory.Trim());

        return Path.GetFullPath(
            expanded);
    }
}
