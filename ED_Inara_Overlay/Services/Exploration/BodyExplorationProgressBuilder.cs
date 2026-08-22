using ED_Inara_Overlay.Models;

namespace ED_Inara_Overlay.Services.Exploration;

internal static class BodyExplorationProgressBuilder
{
    private sealed record GenusIdentity(
        string Key,
        string Name);

    public static IReadOnlyList<BodyExplorationProgress> BuildAll(
        GameStateSnapshot state,
        ExplorationSystemHistorySnapshot history)
    {
        int[] ids = state.ExplorationBodies
            .Select(item => item.BodyId)
            .Concat(history.Bodies.Select(item => item.BodyId))
            .Where(id => id >= 0)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        return ids
            .Select(id => Build(state, history, id))
            .ToArray();
    }

    public static BodyExplorationProgress Build(
        GameStateSnapshot state,
        ExplorationSystemHistorySnapshot history,
        int bodyId)
    {
        ExplorationBodySnapshot? live =
            state.ExplorationBodies
                .FirstOrDefault(item => item.BodyId == bodyId);

        ExplorationHistoryBodySnapshot? old =
            history.Bodies
                .FirstOrDefault(item => item.BodyId == bodyId);

        string name =
            !string.IsNullOrWhiteSpace(live?.Name)
                ? live!.Name
                : old?.BodyName ?? string.Empty;

        BodyOrganicProgressStatus[] organics =
            MergeOrganics(
                state.GetOrganicProgressForBody(bodyId),
                old?.Organics
                    ?? Array.Empty<ExplorationHistoryOrganicSnapshot>());

        GenusIdentity[] knownGenuses =
            BuildKnownGenuses(
                live,
                old,
                organics);

        string[] concreteCompleted = organics
            .Where(item => item.Completed)
            .Select(CompletedIdentity)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        HashSet<string> completedGenusIdentities = organics
            .Where(item =>
                item.Completed
                && (!string.IsNullOrWhiteSpace(item.GenusKey)
                    || !string.IsNullOrWhiteSpace(item.Genus)))
            .Select(item =>
                GenusIdentityValue(
                    item.GenusKey,
                    item.Genus))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        GenusIdentity[] missingGenuses = knownGenuses
            .Where(item =>
                !completedGenusIdentities.Contains(
                    GenusIdentityValue(
                        item.Key,
                        item.Name)))
            .ToArray();

        int total = Math.Max(
            live?.BiologicalSignals ?? 0,
            old?.BiologicalSignals ?? 0);

        int historicalCompleted =
            old?.CompletedOrganics ?? 0;

        int completed = Math.Max(
            concreteCompleted.Length,
            historicalCompleted);

        if (total > 0)
        {
            completed = Math.Min(
                completed,
                total);
        }

        bool incompleteLegacyDetail =
            historicalCompleted
                > completedGenusIdentities.Count;

        var result = new BodyExplorationProgress(
            bodyId,
            name,
            live?.IsScanned == true
                || old?.Scanned == true,
            live?.IsMapped == true
                || old?.Mapped == true,
            live?.MappingEfficient == true
                || old?.EfficientlyMapped == true,
            total,
            completed,
            knownGenuses
                .Select(DisplayName)
                .ToArray(),
            missingGenuses
                .Select(DisplayName)
                .ToArray(),
            organics,
            incompleteLegacyDetail)
        {
            KnownGenusKeys = knownGenuses
                .Select(item =>
                    GenusIdentityValue(
                        item.Key,
                        item.Name))
                .ToArray(),

            MissingGenusKeys = missingGenuses
                .Select(item =>
                    GenusIdentityValue(
                        item.Key,
                        item.Name))
                .ToArray()
        };

        return result;
    }

    private static GenusIdentity[] BuildKnownGenuses(
        ExplorationBodySnapshot? live,
        ExplorationHistoryBodySnapshot? history,
        IReadOnlyList<BodyOrganicProgressStatus> organics)
    {
        IEnumerable<GenusIdentity> liveGenuses =
            PairLiveGenuses(live);

        IEnumerable<GenusIdentity> historicalGenuses =
            history?.Genuses.Select(item =>
                new GenusIdentity(
                    item.GenusKey,
                    item.GenusName))
            ?? Enumerable.Empty<GenusIdentity>();

        IEnumerable<GenusIdentity> organicGenuses =
            organics.Select(item =>
                new GenusIdentity(
                    item.GenusKey,
                    item.Genus));

        return liveGenuses
            .Concat(historicalGenuses)
            .Concat(organicGenuses)
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Key)
                || !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(
                item =>
                    GenusIdentityValue(
                        item.Key,
                        item.Name),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
                group.First())
            .OrderBy(
                item => DisplayName(item),
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<GenusIdentity> PairLiveGenuses(
        ExplorationBodySnapshot? body)
    {
        if (body is null)
        {
            yield break;
        }

        int count = Math.Max(
            body.Genuses.Count,
            body.GenusKeys.Count);

        for (int index = 0; index < count; index++)
        {
            string key = index < body.GenusKeys.Count
                ? body.GenusKeys[index]
                : string.Empty;

            string name = index < body.Genuses.Count
                ? body.Genuses[index]
                : string.Empty;

            yield return new GenusIdentity(
                key,
                name);
        }
    }

    private static BodyOrganicProgressStatus[] MergeOrganics(
        IReadOnlyList<OrganicScanProgressSnapshot> live,
        IReadOnlyList<ExplorationHistoryOrganicSnapshot> history)
    {
        var rows =
            new Dictionary<string, BodyOrganicProgressStatus>(
                StringComparer.OrdinalIgnoreCase);

        foreach (ExplorationHistoryOrganicSnapshot item in history)
        {
            string key = OrganicIdentity(
                item.SpeciesKey,
                item.SpeciesName,
                item.GenusKey,
                item.GenusName);

            rows[key] = new BodyOrganicProgressStatus(
                item.GenusName,
                item.SpeciesName,
                item.VariantName,
                item.Completed ? 3 : 0,
                item.Completed,
                ExobiologyCatalog.GetColonyRange(
                    item.GenusKey,
                    item.GenusName),
                false,
                item.LastSeenUtc)
            {
                GenusKey = item.GenusKey,
                SpeciesKey = item.SpeciesKey,
                VariantKey = item.VariantKey
            };
        }

        foreach (OrganicScanProgressSnapshot item in live)
        {
            string key = OrganicIdentity(
                item.SpeciesKey,
                item.Species,
                item.GenusKey,
                item.Genus);

            if (rows.TryGetValue(
                key,
                out BodyOrganicProgressStatus? previous))
            {
                rows[key] = previous with
                {
                    Genus = Prefer(
                        item.Genus,
                        previous.Genus),
                    Species = Prefer(
                        item.Species,
                        previous.Species),
                    Variant = Prefer(
                        item.Variant,
                        previous.Variant),
                    Stage = Math.Max(
                        previous.Stage,
                        item.Stage),
                    Completed =
                        previous.Completed
                        || item.Completed,
                    ColonyRangeMeters =
                        item.ColonyRangeMeters > 0
                            ? item.ColonyRangeMeters
                            : previous.ColonyRangeMeters,
                    SeenThisSession = true,
                    UpdatedUtc = item.UpdatedUtc,
                    GenusKey = Prefer(
                        item.GenusKey,
                        previous.GenusKey),
                    SpeciesKey = Prefer(
                        item.SpeciesKey,
                        previous.SpeciesKey),
                    VariantKey = Prefer(
                        item.VariantKey,
                        previous.VariantKey)
                };
            }
            else
            {
                rows[key] =
                    new BodyOrganicProgressStatus(
                        item.Genus,
                        item.Species,
                        item.Variant,
                        item.Stage,
                        item.Completed,
                        item.ColonyRangeMeters,
                        true,
                        item.UpdatedUtc)
                    {
                        GenusKey = item.GenusKey,
                        SpeciesKey = item.SpeciesKey,
                        VariantKey = item.VariantKey
                    };
            }
        }

        return rows.Values
            .OrderBy(item => item.Completed)
            .ThenBy(
                item => item.Genus,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                item => item.Species,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string CompletedIdentity(
        BodyOrganicProgressStatus item)
    {
        string genus = GenusIdentityValue(
            item.GenusKey,
            item.Genus);

        if (!string.IsNullOrWhiteSpace(genus))
        {
            return genus;
        }

        return OrganicIdentity(
            item.SpeciesKey,
            item.Species,
            string.Empty,
            string.Empty);
    }

    private static string GenusIdentityValue(
        string key,
        string name)
    {
        string source =
            !string.IsNullOrWhiteSpace(key)
                ? key
                : name;

        return ExobiologyPredictionService
            .NormalizeGenusIdentity(source);
    }

    private static string OrganicIdentity(
        string speciesKey,
        string speciesName,
        string genusKey,
        string genusName)
    {
        if (!string.IsNullOrWhiteSpace(speciesKey))
        {
            return "species-key:"
                + speciesKey.Trim();
        }

        if (!string.IsNullOrWhiteSpace(speciesName))
        {
            return "species-name:"
                + speciesName.Trim();
        }

        string genus = GenusIdentityValue(
            genusKey,
            genusName);

        return string.IsNullOrWhiteSpace(genus)
            ? "unknown"
            : "genus:" + genus;
    }

    private static string DisplayName(
        GenusIdentity item) =>
        !string.IsNullOrWhiteSpace(item.Name)
            ? item.Name
            : item.Key;

    private static string Prefer(
        string primary,
        string fallback) =>
        string.IsNullOrWhiteSpace(primary)
            ? fallback
            : primary;
}