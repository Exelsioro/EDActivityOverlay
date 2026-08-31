using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Mining;

internal sealed record MiningLoadoutModuleInput(
    string Slot,
    string Item,
    bool Enabled = true);

internal static class MiningLoadoutAnalyzer
{
    private static readonly MiningModuleKind[] LaserRequired =
    [
        MiningModuleKind.MiningLaser,
        MiningModuleKind.Refinery
    ];

    private static readonly MiningModuleKind[] CoreRequired =
    [
        MiningModuleKind.SeismicChargeLauncher,
        MiningModuleKind.AbrasionBlaster,
        MiningModuleKind.Refinery
    ];

    private static readonly MiningModuleKind[] SubsurfaceRequired =
    [
        MiningModuleKind.SubsurfaceDisplacementMissile,
        MiningModuleKind.Refinery
    ];

    private static readonly MiningModuleKind[] SurfaceRequired =
    [
        MiningModuleKind.AbrasionBlaster,
        MiningModuleKind.Refinery
    ];

    public static MiningLoadoutSnapshot Analyze(
        string? ship,
        bool available,
        IEnumerable<MiningLoadoutModuleInput> rawModules)
    {
        ArgumentNullException.ThrowIfNull(rawModules);

        string normalizedShip = ship?.Trim() ?? string.Empty;
        if (!available)
        {
            return MiningLoadoutSnapshot.Empty with
            {
                Ship = normalizedShip
            };
        }

        MiningLoadoutModuleSnapshot[] modules = rawModules
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Item))
            .Select(ToSnapshot)
            .Where(item =>
                item.Kind != MiningModuleKind.Unknown)
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.Slot, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        bool HasEnabled(MiningModuleKind kind) =>
            modules.Any(item =>
                item.Enabled
                && item.Kind == kind);

        bool hasProspector = modules.Any(item =>
            item.Enabled
            && item.Kind
                is MiningModuleKind.ProspectorController
                or MiningModuleKind.MiningMultiLimpetController);

        bool hasCollector = modules.Any(item =>
            item.Enabled
            && item.Kind
                is MiningModuleKind.CollectorController
                or MiningModuleKind.MiningMultiLimpetController);

        MiningLoadoutModuleSnapshot? bestProspector = modules
            .Where(item =>
                item.Enabled
                && item.Kind
                    is MiningModuleKind.ProspectorController
                    or MiningModuleKind.MiningMultiLimpetController)
            .OrderByDescending(item =>
                RatingRank(item.Rating))
            .FirstOrDefault();

        string bestProspectorRating =
            bestProspector?.Rating ?? string.Empty;
        bool hasAProspector =
            bestProspectorRating.Equals(
                "A",
                StringComparison.OrdinalIgnoreCase);

        bool hasDss =
            HasEnabled(
                MiningModuleKind.DetailedSurfaceScanner);
        bool hasPwa =
            HasEnabled(
                MiningModuleKind.PulseWaveAnalyzer);

        bool HasCapability(MiningModuleKind kind) =>
            kind switch
            {
                MiningModuleKind.ProspectorController =>
                    hasProspector,
                MiningModuleKind.CollectorController =>
                    hasCollector,
                _ => HasEnabled(kind)
            };

        MiningModeReadiness Build(
            MiningLoadoutMode mode,
            IReadOnlyList<MiningModuleKind> required)
        {
            MiningModuleKind[] missing = required
                .Where(kind => !HasCapability(kind))
                .ToArray();

            var advisories =
                new List<MiningLoadoutAdvisory>();

            if (!hasProspector)
            {
                advisories.Add(
                    MiningLoadoutAdvisory.MissingProspector);
            }
            else if (!hasAProspector)
            {
                advisories.Add(
                    MiningLoadoutAdvisory.ProspectorBelowA);
            }

            if (!hasCollector)
            {
                advisories.Add(
                    MiningLoadoutAdvisory.MissingCollector);
            }

            if (!hasDss)
            {
                advisories.Add(
                    MiningLoadoutAdvisory
                        .MissingDetailedSurfaceScanner);
            }

            if (mode != MiningLoadoutMode.Laser
                && !hasPwa)
            {
                advisories.Add(
                    MiningLoadoutAdvisory
                        .MissingPulseWaveAnalyzer);
            }

            MiningReadinessLevel level =
                missing.Length > 0
                    ? MiningReadinessLevel.MissingRequired
                    : advisories.Count == 0
                        ? MiningReadinessLevel.FullKit
                        : MiningReadinessLevel.Usable;

            return new MiningModeReadiness(
                mode,
                level,
                missing,
                advisories);
        }

        return new MiningLoadoutSnapshot(
            true,
            normalizedShip,
            modules,
            hasProspector,
            bestProspectorRating,
            hasAProspector,
            hasCollector,
            hasDss,
            hasPwa,
            Build(
                MiningLoadoutMode.Laser,
                LaserRequired),
            Build(
                MiningLoadoutMode.Core,
                CoreRequired),
            Build(
                MiningLoadoutMode.Subsurface,
                SubsurfaceRequired),
            Build(
                MiningLoadoutMode.Surface,
                SurfaceRequired));
    }

    internal static MiningModuleKind Classify(
        string? item)
    {
        string value = NormalizeItem(item);
        if (string.IsNullOrWhiteSpace(value))
        {
            return MiningModuleKind.Unknown;
        }

        if (value.StartsWith(
                "hpt_mining_subsurfdispmisle_",
                StringComparison.Ordinal))
        {
            return MiningModuleKind
                .SubsurfaceDisplacementMissile;
        }

        if (value.StartsWith(
                "hpt_mining_abrblstr_",
                StringComparison.Ordinal))
        {
            return MiningModuleKind.AbrasionBlaster;
        }

        if (value.StartsWith(
                "hpt_mining_seismchrgwarhd_",
                StringComparison.Ordinal))
        {
            return MiningModuleKind
                .SeismicChargeLauncher;
        }

        if (value.StartsWith(
                "hpt_mrascanner_",
                StringComparison.Ordinal))
        {
            return MiningModuleKind.PulseWaveAnalyzer;
        }

        if (value.StartsWith(
                "hpt_mininglaser_",
                StringComparison.Ordinal)
            || value.StartsWith(
                "hpt_miningtoolv2_",
                StringComparison.Ordinal))
        {
            return MiningModuleKind.MiningLaser;
        }

        if (value.StartsWith(
                "int_multidronecontrol_mining",
                StringComparison.Ordinal))
        {
            return MiningModuleKind
                .MiningMultiLimpetController;
        }

        if (value.StartsWith(
                "int_dronecontrol_prospector_",
                StringComparison.Ordinal))
        {
            return MiningModuleKind
                .ProspectorController;
        }

        if (value.StartsWith(
                "int_dronecontrol_collection_",
                StringComparison.Ordinal))
        {
            return MiningModuleKind
                .CollectorController;
        }

        if (value.StartsWith(
                "int_refinery_",
                StringComparison.Ordinal))
        {
            return MiningModuleKind.Refinery;
        }

        if (value.StartsWith(
                "int_detailedsurfacescanner",
                StringComparison.Ordinal))
        {
            return MiningModuleKind
                .DetailedSurfaceScanner;
        }

        return MiningModuleKind.Unknown;
    }

    internal static string NormalizeItem(
        string? item)
    {
        string value = item?.Trim() ?? string.Empty;
        if (value.StartsWith(
                "$",
                StringComparison.Ordinal))
        {
            value = value[1..];
        }

        if (value.EndsWith(
                ";",
                StringComparison.Ordinal))
        {
            value = value[..^1];
        }

        if (value.EndsWith(
                "_name",
                StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^5];
        }

        return value.ToLowerInvariant();
    }

    private static MiningLoadoutModuleSnapshot ToSnapshot(
        MiningLoadoutModuleInput raw)
    {
        string item = NormalizeItem(raw.Item);
        int size = TokenNumber(
            item,
            "_size");
        int moduleClass = TokenNumber(
            item,
            "_class");

        return new MiningLoadoutModuleSnapshot(
            raw.Slot?.Trim() ?? string.Empty,
            item,
            Classify(item),
            size,
            RatingFromClass(moduleClass),
            raw.Enabled);
    }

    private static int TokenNumber(
        string value,
        string token)
    {
        int index = value.IndexOf(
            token,
            StringComparison.Ordinal);
        if (index < 0)
        {
            return 0;
        }

        index += token.Length;
        int start = index;
        while (index < value.Length
               && char.IsDigit(value[index]))
        {
            index++;
        }

        return index > start
               && int.TryParse(
                   value[start..index],
                   out int number)
            ? number
            : 0;
    }

    private static string RatingFromClass(
        int moduleClass) =>
        moduleClass switch
        {
            5 => "A",
            4 => "B",
            3 => "C",
            2 => "D",
            1 => "E",
            _ => string.Empty
        };

    private static int RatingRank(string rating) =>
        rating.ToUpperInvariant() switch
        {
            "A" => 5,
            "B" => 4,
            "C" => 3,
            "D" => 2,
            "E" => 1,
            _ => 0
        };
}
