using ED_Inara_Overlay.Models;

namespace ED_Inara_Overlay.Services.Exploration;

internal static class BodyExplorationProgressBuilder
{
    public static IReadOnlyList<BodyExplorationProgress> BuildAll(
        GameStateSnapshot state, ExplorationSystemHistorySnapshot history)
    {
        int[] ids = state.ExplorationBodies.Select(x => x.BodyId)
            .Concat(history.Bodies.Select(x => x.BodyId))
            .Where(id => id >= 0).Distinct().OrderBy(id => id).ToArray();
        return ids.Select(id => Build(state, history, id)).ToArray();
    }

    public static BodyExplorationProgress Build(
        GameStateSnapshot state, ExplorationSystemHistorySnapshot history, int bodyId)
    {
        ExplorationBodySnapshot? live = state.ExplorationBodies.FirstOrDefault(x => x.BodyId == bodyId);
        ExplorationHistoryBodySnapshot? old = history.Bodies.FirstOrDefault(x => x.BodyId == bodyId);
        string name = !string.IsNullOrWhiteSpace(live?.Name) ? live!.Name : old?.BodyName ?? string.Empty;

        BodyOrganicProgressStatus[] organics = MergeOrganics(
            state.GetOrganicProgressForBody(bodyId),
            old?.Organics ?? Array.Empty<ExplorationHistoryOrganicSnapshot>());

        string[] genuses = (live?.Genuses ?? Array.Empty<string>())
            .Concat(old?.Genuses.Select(x => x.GenusName) ?? Enumerable.Empty<string>())
            .Concat(organics.Select(x => x.Genus))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string[] concreteCompleted = organics.Where(x => x.Completed)
            .Select(x => !string.IsNullOrWhiteSpace(x.Genus) ? x.Genus : x.Species)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        int total = Math.Max(live?.BiologicalSignals ?? 0, old?.BiologicalSignals ?? 0);
        int historicalCompleted = old?.CompletedOrganics ?? 0;
        int completed = Math.Max(concreteCompleted.Length, historicalCompleted);
        if (total > 0) completed = Math.Min(completed, total);
        bool incompleteLegacyDetail = historicalCompleted > concreteCompleted.Length;

        string[] completedGenuses = organics.Where(x => x.Completed && !string.IsNullOrWhiteSpace(x.Genus))
            .Select(x => x.Genus).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] missing = genuses
            .Where(g => !completedGenuses.Contains(g, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        return new BodyExplorationProgress(
            bodyId, name,
            live?.IsScanned == true || old?.Scanned == true,
            live?.IsMapped == true || old?.Mapped == true,
            live?.MappingEfficient == true || old?.EfficientlyMapped == true,
            total, completed, genuses, missing, organics, incompleteLegacyDetail);
    }

    private static BodyOrganicProgressStatus[] MergeOrganics(
        IReadOnlyList<OrganicScanProgressSnapshot> live,
        IReadOnlyList<ExplorationHistoryOrganicSnapshot> history)
    {
        var rows = new Dictionary<string, BodyOrganicProgressStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (ExplorationHistoryOrganicSnapshot item in history)
        {
            string key = Identity(item.GenusName, item.SpeciesName, item.SpeciesKey);
            rows[key] = new BodyOrganicProgressStatus(
                item.GenusName, item.SpeciesName, item.VariantName,
                item.Completed ? 3 : 0, item.Completed,
                ExobiologyCatalog.GetColonyRange(item.GenusKey, item.GenusName),
                false, item.LastSeenUtc);
        }
        foreach (OrganicScanProgressSnapshot item in live)
        {
            string key = Identity(item.Genus, item.Species, item.Species);
            if (rows.TryGetValue(key, out BodyOrganicProgressStatus? previous))
            {
                rows[key] = previous with
                {
                    Genus = Prefer(item.Genus, previous.Genus),
                    Species = Prefer(item.Species, previous.Species),
                    Variant = Prefer(item.Variant, previous.Variant),
                    Stage = Math.Max(previous.Stage, item.Stage),
                    Completed = previous.Completed || item.Completed,
                    ColonyRangeMeters = item.ColonyRangeMeters > 0 ? item.ColonyRangeMeters : previous.ColonyRangeMeters,
                    SeenThisSession = true,
                    UpdatedUtc = item.UpdatedUtc
                };
            }
            else
            {
                rows[key] = new BodyOrganicProgressStatus(
                    item.Genus, item.Species, item.Variant, item.Stage, item.Completed,
                    item.ColonyRangeMeters, true, item.UpdatedUtc);
            }
        }
        return rows.Values.OrderBy(x => x.Completed)
            .ThenBy(x => x.Genus, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Species, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string Identity(string genus, string species, string fallback) =>
        !string.IsNullOrWhiteSpace(genus) ? $"genus:{genus}" :
        !string.IsNullOrWhiteSpace(species) ? $"species:{species}" : $"raw:{fallback}";
    private static string Prefer(string primary, string fallback) =>
        string.IsNullOrWhiteSpace(primary) ? fallback : primary;
}