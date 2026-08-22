using ED_Inara_Overlay.Models;

namespace ED_Inara_Overlay.Services.Exploration;

internal static class ExplorationVisitPolicy
{
    public static bool IsInteresting(ExplorationCatalogBody body) =>
        body.BodyId >= 0 && body.IsNotable;

    public static ExplorationRequiredObjectives RequiredObjectives(
        ExplorationCatalogBody body)
    {
        ExplorationRequiredObjectives objectives =
            ExplorationRequiredObjectives.FssScan;

        bool isPlanet = body.Type.Equals(
            "Planet",
            StringComparison.OrdinalIgnoreCase);

        bool mapWorthwhile =
            body.IsBiological
            || body.IsValuable
            || body.Terraformable
            || body.Highlights.HasFlag(ExplorationBodyHighlights.EarthLike)
            || body.Highlights.HasFlag(ExplorationBodyHighlights.WaterWorld)
            || body.Highlights.HasFlag(ExplorationBodyHighlights.AmmoniaWorld);

        if (isPlanet && mapWorthwhile)
        {
            objectives |= ExplorationRequiredObjectives.DssMap;
        }

        if (body.IsBiological)
        {
            objectives |= ExplorationRequiredObjectives.Biology;
        }

        return objectives;
    }

    public static int PriorityScore(ExplorationCatalogBody body)
    {
        int score = 0;

        if (body.IsBiological)
        {
            score += 100_000;
        }

        if (body.Highlights.HasFlag(ExplorationBodyHighlights.EarthLike))
        {
            score += 90_000;
        }

        if (body.Highlights.HasFlag(ExplorationBodyHighlights.WaterWorld))
        {
            score += 80_000;
        }

        if (body.Highlights.HasFlag(ExplorationBodyHighlights.AmmoniaWorld))
        {
            score += 75_000;
        }

        if (body.Terraformable)
        {
            score += 60_000;
        }

        if (body.IsValuable)
        {
            score += 50_000;
        }

        if (body.Highlights.HasFlag(ExplorationBodyHighlights.NeutronStar)
            || body.Highlights.HasFlag(ExplorationBodyHighlights.BlackHole))
        {
            score += 40_000;
        }

        score += (int)Math.Clamp(
            body.EstimatedMappingValue / 1_000,
            0,
            30_000);

        if (!body.WasMapped)
        {
            score += 5_000;
        }

        if (!body.WasDiscovered)
        {
            score += 3_000;
        }

        score -= (int)Math.Clamp(
            body.DistanceFromArrivalLs / 10,
            0,
            20_000);

        return score;
    }
}

internal sealed class ExplorationVisitQueueEngine
{
    private readonly HashSet<int> deferredBodyIds = new();

    private string currentSystemKey = string.Empty;
    private string commander = string.Empty;
    private long systemAddress;
    private string systemName = string.Empty;
    private int activeBodyId = -1;

    private Dictionary<int, ExplorationVisitBodyState> entries = new();

    public ExplorationVisitQueueSnapshot Current { get; private set; } =
        ExplorationVisitQueueSnapshot.Empty;

    public ExplorationVisitQueueSnapshot Update(
        GameStateSnapshot state,
        ExplorationSystemCatalog catalog,
        ExplorationSystemHistorySnapshot history)
    {
        string nextSystemKey = SystemKey(
            state.SystemAddress,
            state.StarSystem);

        if (!string.Equals(
            currentSystemKey,
            nextSystemKey,
            StringComparison.OrdinalIgnoreCase))
        {
            ResetVisit(nextSystemKey);
        }

        commander = state.Commander;
        systemAddress = state.SystemAddress;
        systemName = state.StarSystem;

        entries = catalog.Bodies
            .Where(ExplorationVisitPolicy.IsInteresting)
            .Select(body =>
            {
                BodyExplorationProgress progress =
                    BodyExplorationProgressBuilder.Build(
                        state,
                        history,
                        body.BodyId);

                ExplorationRequiredObjectives objectives =
                    ExplorationVisitPolicy.RequiredObjectives(body);

                return new ExplorationVisitBodyState(
                    body,
                    progress,
                    objectives,
                    ExplorationVisitDisposition.Recommended,
                    ExplorationVisitPolicy.PriorityScore(body));
            })
            .ToDictionary(item => item.BodyId);

        deferredBodyIds.RemoveWhere(bodyId =>
            !entries.TryGetValue(bodyId, out ExplorationVisitBodyState? body)
            || body.IsComplete);

        if (activeBodyId >= 0
            && (!entries.TryGetValue(
                    activeBodyId,
                    out ExplorationVisitBodyState? active)
                || active.IsComplete))
        {
            activeBodyId = -1;
        }

        Current = BuildSnapshot();
        return Current;
    }

    public bool CanActivateBody(int bodyId) =>
        entries.TryGetValue(bodyId, out ExplorationVisitBodyState? body)
        && !body.IsComplete;

    public bool ActivateBody(int bodyId)
    {
        if (!CanActivateBody(bodyId))
        {
            return false;
        }

        if (activeBodyId == bodyId)
        {
            deferredBodyIds.Remove(bodyId);
            Current = BuildSnapshot();
            return false;
        }

        if (activeBodyId >= 0
            && entries.TryGetValue(
                activeBodyId,
                out ExplorationVisitBodyState? previous)
            && !previous.IsComplete)
        {
            deferredBodyIds.Add(activeBodyId);
        }

        deferredBodyIds.Remove(bodyId);
        activeBodyId = bodyId;

        Current = BuildSnapshot();
        return true;
    }

    public bool DeferBody(int bodyId)
    {
        if (!entries.TryGetValue(
                bodyId,
                out ExplorationVisitBodyState? body)
            || body.IsComplete)
        {
            return false;
        }

        bool changed = deferredBodyIds.Add(bodyId);

        if (activeBodyId == bodyId)
        {
            activeBodyId = -1;
            changed = true;
        }

        if (changed)
        {
            Current = BuildSnapshot();
        }

        return changed;
    }

    public bool ResumeBody(int bodyId)
    {
        if (!entries.TryGetValue(
                bodyId,
                out ExplorationVisitBodyState? body)
            || body.IsComplete)
        {
            return false;
        }

        bool changed = deferredBodyIds.Remove(bodyId);

        if (changed)
        {
            Current = BuildSnapshot();
        }

        return changed;
    }

    private ExplorationVisitQueueSnapshot BuildSnapshot()
    {
        ExplorationVisitBodyState? active = null;
        var recommended = new List<ExplorationVisitBodyState>();
        var deferred = new List<ExplorationVisitBodyState>();
        var completed = new List<ExplorationVisitBodyState>();

        foreach (ExplorationVisitBodyState item in entries.Values)
        {
            ExplorationVisitDisposition disposition;

            if (item.IsComplete)
            {
                disposition = ExplorationVisitDisposition.Complete;
            }
            else if (item.BodyId == activeBodyId)
            {
                disposition = ExplorationVisitDisposition.Active;
            }
            else if (deferredBodyIds.Contains(item.BodyId))
            {
                disposition = ExplorationVisitDisposition.Deferred;
            }
            else
            {
                disposition = ExplorationVisitDisposition.Recommended;
            }

            ExplorationVisitBodyState resolved =
                item with { Disposition = disposition };

            switch (disposition)
            {
                case ExplorationVisitDisposition.Active:
                    active = resolved;
                    break;

                case ExplorationVisitDisposition.Recommended:
                    recommended.Add(resolved);
                    break;

                case ExplorationVisitDisposition.Deferred:
                    deferred.Add(resolved);
                    break;

                case ExplorationVisitDisposition.Complete:
                    completed.Add(resolved);
                    break;
            }
        }

        static IOrderedEnumerable<ExplorationVisitBodyState> Rank(
            IEnumerable<ExplorationVisitBodyState> source) =>
            source
                .OrderByDescending(item => item.PriorityScore)
                .ThenBy(item => item.Body.DistanceFromArrivalLs)
                .ThenBy(item => item.BodyId);

        return new ExplorationVisitQueueSnapshot(
            commander,
            systemAddress,
            systemName,
            active,
            Rank(recommended).ToArray(),
            Rank(deferred).ToArray(),
            completed
                .OrderBy(item => item.BodyId)
                .ToArray());
    }

    private void ResetVisit(string nextSystemKey)
    {
        currentSystemKey = nextSystemKey;
        activeBodyId = -1;
        deferredBodyIds.Clear();
        entries.Clear();
        Current = ExplorationVisitQueueSnapshot.Empty;
    }

    private static string SystemKey(
        long address,
        string name) =>
        address != 0
            ? $"id:{address}"
            : string.IsNullOrWhiteSpace(name)
                ? string.Empty
                : $"name:{name.Trim().ToUpperInvariant()}";
}