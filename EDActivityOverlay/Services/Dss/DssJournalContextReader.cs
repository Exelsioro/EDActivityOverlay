using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EDActivityOverlay.Services.Dss;

internal sealed record DssModuleSnapshot(
    string Item,
    string LocalizedName,
    double PatchRadius,
    double OriginalPatchRadius,
    string Blueprint,
    int EngineeringLevel)
{
    public static DssModuleSnapshot Empty { get; } =
        new(
            string.Empty,
            string.Empty,
            0,
            0,
            string.Empty,
            0);
}

internal sealed record DssBodyScanSnapshot(
    long SystemAddress,
    int BodyId,
    string BodyName,
    double RadiusMeters)
{
    public static DssBodyScanSnapshot Empty { get; } =
        new(0, -1, string.Empty, 0);
}

internal static class DssJournalContextReader
{
    private static readonly object BodyScanCacheSync = new();

    // Positive-only cache. A miss is not cached permanently because a Scan
    // event can appear later in the current Elite session.
    private static readonly Dictionary<string, DssBodyScanSnapshot>
        BodyScanCache =
            new(StringComparer.OrdinalIgnoreCase);

    public static DssModuleSnapshot ReadLatestDssModule(
        string journalDirectory)
    {
        if (string.IsNullOrWhiteSpace(journalDirectory)
            || !Directory.Exists(journalDirectory))
        {
            return DssModuleSnapshot.Empty;
        }

        try
        {
            foreach (string file in RecentJournalFiles(
                         journalDirectory,
                         4))
            {
                DssModuleSnapshot candidate =
                    ReadLatestDssModuleFromFile(file);
                if (!string.IsNullOrWhiteSpace(candidate.Item))
                {
                    return candidate;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"DSS prototype: failed to recover DSS module from Journal: {ex.Message}");
        }

        return DssModuleSnapshot.Empty;
    }

    public static DssBodyScanSnapshot ResolveBodyScan(
        string journalDirectory,
        long systemAddress,
        int bodyId,
        string? bodyName)
    {
        if (string.IsNullOrWhiteSpace(journalDirectory)
            || !Directory.Exists(journalDirectory))
        {
            return DssBodyScanSnapshot.Empty;
        }

        string cacheKey =
            BuildBodyScanCacheKey(
                journalDirectory,
                systemAddress,
                bodyId,
                bodyName);

        lock (BodyScanCacheSync)
        {
            if (BodyScanCache.TryGetValue(
                    cacheKey,
                    out DssBodyScanSnapshot? cached)
                && cached.RadiusMeters > 0)
            {
                return cached;
            }
        }

        try
        {
            // Previous prototype versions inspected only six latest journal
            // files. Selecting an already-scanned body as Destination does
            // NOT emit a new Scan event, so a body visited more than six Elite
            // sessions ago could never resolve its physical radius.
            //
            // Search newest -> oldest through the complete local journal
            // history and stop at the first matching Scan. This path runs only
            // while resolving DSS body metadata and successful results are
            // cached above.
            string[] files =
                Directory
                    .EnumerateFiles(
                        journalDirectory,
                        "Journal.*.log")
                    .OrderByDescending(
                        File.GetLastWriteTimeUtc)
                    .ToArray();

            int inspectedFiles = 0;

            foreach (string file in files)
            {
                inspectedFiles++;

                DssBodyScanSnapshot candidate =
                    ReadBodyScanFromFile(
                        file,
                        systemAddress,
                        bodyId,
                        bodyName);

                if (candidate.RadiusMeters <= 0)
                {
                    continue;
                }

                lock (BodyScanCacheSync)
                {
                    BodyScanCache[cacheKey] =
                        candidate;
                }

                Logger.Logger.Info(
                    $"DSS prototype: recovered historical body Scan " +
                    $"for '{candidate.BodyName}' (BodyID={candidate.BodyId}, " +
                    $"R={candidate.RadiusMeters:0} m) after inspecting " +
                    $"{inspectedFiles} Journal file(s).");

                return candidate;
            }
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning(
                $"DSS prototype: failed to recover body Scan from Journal: {ex.Message}");
        }

        return DssBodyScanSnapshot.Empty;
    }

    private static string BuildBodyScanCacheKey(
        string journalDirectory,
        long systemAddress,
        int bodyId,
        string? bodyName)
    {
        string normalizedDirectory;

        try
        {
            normalizedDirectory =
                Path.GetFullPath(
                    journalDirectory);
        }
        catch
        {
            normalizedDirectory =
                journalDirectory;
        }

        return
            normalizedDirectory
            + "|"
            + systemAddress.ToString(
                CultureInfo.InvariantCulture)
            + "|"
            + bodyId.ToString(
                CultureInfo.InvariantCulture)
            + "|"
            + (bodyName ?? string.Empty);
    }

    internal static DssModuleSnapshot ParseDssModule(
        JsonElement root)
    {
        if (!root.TryGetProperty(
                "Modules",
                out JsonElement modules)
            || modules.ValueKind != JsonValueKind.Array)
        {
            return DssModuleSnapshot.Empty;
        }

        foreach (JsonElement module in modules.EnumerateArray())
        {
            string item = GetString(module, "Item");
            string localized =
                GetString(module, "Item_Localised");

            string identity = item + " " + localized;
            if (!identity.Contains(
                    "detailedsurfacescanner",
                    StringComparison.OrdinalIgnoreCase)
                && !identity.Contains(
                    "detailed surface scanner",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            double patchRadius = 0;
            double originalPatchRadius = 0;
            string blueprint = string.Empty;
            int engineeringLevel = 0;

            if (module.TryGetProperty(
                    "Engineering",
                    out JsonElement engineering)
                && engineering.ValueKind
                    == JsonValueKind.Object)
            {
                blueprint =
                    GetString(engineering, "BlueprintName");
                engineeringLevel =
                    GetInt(engineering, "Level");

                JsonElement modifiers = default;
                bool hasModifiers =
                    engineering.TryGetProperty(
                        "Modifiers",
                        out modifiers)
                    || engineering.TryGetProperty(
                        "Modifications",
                        out modifiers);

                if (hasModifiers
                    && modifiers.ValueKind
                        == JsonValueKind.Array)
                {
                    foreach (JsonElement modifier
                             in modifiers.EnumerateArray())
                    {
                        if (!GetString(
                                modifier,
                                "Label")
                            .Equals(
                                "DSS_PatchRadius",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        patchRadius =
                            GetDouble(modifier, "Value");
                        originalPatchRadius =
                            GetDouble(
                                modifier,
                                "OriginalValue");
                    }
                }
            }

            return new DssModuleSnapshot(
                item,
                localized,
                patchRadius,
                originalPatchRadius,
                blueprint,
                engineeringLevel);
        }

        return DssModuleSnapshot.Empty;
    }

    private static DssModuleSnapshot ReadLatestDssModuleFromFile(
        string path)
    {
        DssModuleSnapshot result =
            DssModuleSnapshot.Empty;

        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)
                || !line.Contains(
                    "\"Loadout\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(line);
                JsonElement root = document.RootElement;

                if (!GetString(root, "event")
                    .Equals(
                        "Loadout",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DssModuleSnapshot candidate =
                    ParseDssModule(root);
                if (!string.IsNullOrWhiteSpace(candidate.Item))
                {
                    result = candidate;
                }
            }
            catch (JsonException)
            {
            }
        }

        return result;
    }

    private static DssBodyScanSnapshot ReadBodyScanFromFile(
        string path,
        long systemAddress,
        int bodyId,
        string? bodyName)
    {
        DssBodyScanSnapshot result =
            DssBodyScanSnapshot.Empty;

        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)
                || !line.Contains(
                    "\"Scan\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(line);
                JsonElement root = document.RootElement;

                if (!GetString(root, "event")
                    .Equals(
                        "Scan",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                long eventSystemAddress =
                    GetLong(root, "SystemAddress");
                int eventBodyId =
                    GetInt(root, "BodyID");
                string eventBodyName =
                    GetString(root, "BodyName");

                if (systemAddress > 0
                    && eventSystemAddress > 0
                    && eventSystemAddress != systemAddress)
                {
                    continue;
                }

                if (bodyId >= 0)
                {
                    if (eventBodyId != bodyId)
                    {
                        continue;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(bodyName)
                         && !eventBodyName.Equals(
                             bodyName,
                             StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double radius = GetDouble(root, "Radius");
                if (radius <= 0)
                {
                    continue;
                }

                result = new DssBodyScanSnapshot(
                    eventSystemAddress,
                    eventBodyId,
                    eventBodyName,
                    radius);
            }
            catch (JsonException)
            {
            }
        }

        return result;
    }

    private static string[] RecentJournalFiles(
        string directory,
        int count) =>
        Directory
            .EnumerateFiles(directory, "Journal.*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(count)
            .ToArray();

    private static string GetString(
        JsonElement root,
        string name) =>
        root.TryGetProperty(
            name,
            out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int GetInt(
        JsonElement root,
        string name) =>
        root.TryGetProperty(
            name,
            out JsonElement value)
        && value.TryGetInt32(out int result)
            ? result
            : -1;

    private static long GetLong(
        JsonElement root,
        string name) =>
        root.TryGetProperty(
            name,
            out JsonElement value)
        && value.TryGetInt64(out long result)
            ? result
            : 0;

    private static double GetDouble(
        JsonElement root,
        string name)
    {
        if (!root.TryGetProperty(
                name,
                out JsonElement value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out double numeric))
        {
            return numeric;
        }

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed))
        {
            return parsed;
        }

        return 0;
    }
}
