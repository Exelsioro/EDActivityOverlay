using System;
using System.IO;
using System.Globalization;
using System.Xml.Linq;

namespace EDActivityOverlay.Services.Dss;

internal sealed record EliteGraphicsSettingsSnapshot(
    double VerticalFovDegrees,
    double HumanoidFovDegrees,
    string FilePath,
    DateTime LastWriteUtc)
{
    public static EliteGraphicsSettingsSnapshot Default { get; } =
        new(56.817001, 56.249001, string.Empty, DateTime.MinValue);
}

internal static class EliteGraphicsSettingsReader
{
    public static string DefaultSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Frontier Developments",
        "Elite Dangerous",
        "Options",
        "Graphics",
        "Settings.xml");

    public static EliteGraphicsSettingsSnapshot Read(string? path = null)
    {
        string candidate = string.IsNullOrWhiteSpace(path)
            ? DefaultSettingsPath
            : path;

        try
        {
            if (!File.Exists(candidate))
            {
                Logger.Logger.Warning(
                    $"DSS prototype: Elite graphics settings were not found: {candidate}. " +
                    $"Using fallback FOV {EliteGraphicsSettingsSnapshot.Default.VerticalFovDegrees:0.###}°.");
                return EliteGraphicsSettingsSnapshot.Default with { FilePath = candidate };
            }

            XDocument document = XDocument.Load(candidate);
            XElement? root = document.Root;
            double fov = ParseDouble(root?.Element("FOV")?.Value)
                         ?? EliteGraphicsSettingsSnapshot.Default.VerticalFovDegrees;
            double humanoidFov = ParseDouble(root?.Element("HumanoidFOV")?.Value)
                                 ?? EliteGraphicsSettingsSnapshot.Default.HumanoidFovDegrees;

            return new EliteGraphicsSettingsSnapshot(
                fov,
                humanoidFov,
                candidate,
                File.GetLastWriteTimeUtc(candidate));
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"DSS prototype: failed to read Elite graphics settings: {ex.Message}. " +
                $"Using fallback FOV {EliteGraphicsSettingsSnapshot.Default.VerticalFovDegrees:0.###}°.");
            return EliteGraphicsSettingsSnapshot.Default with { FilePath = candidate };
        }
    }

    private static double? ParseDouble(string? value) =>
        double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double result)
            ? result
            : null;
}
