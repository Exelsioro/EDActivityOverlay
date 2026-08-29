using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Exploration;

public static class ExplorationSystemCatalogBuilder
{
    private const long ValuableMappingThreshold = 100_000;

    public static ExplorationSystemCatalog Build(
        GameStateSnapshot journal,
        ExplorationDataState externalData,
        string? spoilerMode,
        ExplorationSystemHistorySnapshot? history = null)
    {
        string mode = ExplorationSpoilerModes.Normalize(spoilerMode);
        ExplorationSystemDataSnapshot? external = externalData.Status == ExplorationDataStatus.Available
            && externalData.System is { } system
            && string.Equals(system.SystemName, journal.StarSystem, StringComparison.OrdinalIgnoreCase)
                ? system
                : null;

        var rows = new List<ExplorationCatalogBody>();
        foreach (ExplorationBodySnapshot local in journal.ExplorationBodies)
        {
            ExternalExplorationBodySnapshot? matching = external?.Bodies.FirstOrDefault(body => Matches(local, body));
            ExplorationHistoryBodySnapshot? historical = history?.Bodies.FirstOrDefault(body => Matches(local, body));
            bool mayEnrich = mode == ExplorationSpoilerModes.FullCatalog
                             || mode == ExplorationSpoilerModes.EnrichScanned && local.IsScanned;
            rows.Add(Merge(local, mayEnrich ? matching : null, historical, external?.Source));
        }

        if (mode == ExplorationSpoilerModes.FullCatalog && external is not null)
        {
            foreach (ExternalExplorationBodySnapshot body in external.Bodies)
            {
                if (journal.ExplorationBodies.Any(local => Matches(local, body))) continue;
                ExplorationHistoryBodySnapshot? historical = history?.Bodies.FirstOrDefault(item => Matches(item, body));
                rows.Add(FromExternal(body, historical, external.Source));
            }
        }

        ExplorationCatalogBody[] ordered = rows
            .GroupBy(Identity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(body => body.BodyId < 0 ? int.MaxValue : body.BodyId)
            .ThenBy(body => body.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        int knownBodies = mode == ExplorationSpoilerModes.FullCatalog && external is not null
            ? Math.Max(journal.SystemBodyCount, external.BodyCount)
            : Math.Max(journal.SystemBodyCount, ordered.Length);
        return new ExplorationSystemCatalog(journal.StarSystem, knownBodies, mode, ordered);
    }

    private static bool Matches(ExplorationBodySnapshot local, ExternalExplorationBodySnapshot external) =>
        local.BodyId >= 0 && external.BodyId >= 0 && local.BodyId == external.BodyId
        || !string.IsNullOrWhiteSpace(local.Name)
        && local.Name.Equals(external.Name, StringComparison.OrdinalIgnoreCase);

    private static ExplorationCatalogBody Merge(
        ExplorationBodySnapshot local,
        ExternalExplorationBodySnapshot? external,
        ExplorationHistoryBodySnapshot? history,
        string? externalSource)
    {
        string type = Prefer(local.BodyType, external?.Type);
        string subtype = Prefer(local.BodyClass, external?.Subtype, local.Description);
        bool terraformable = local.Terraformable
                             || ContainsTerraform(external?.TerraformingState);
        long mappingValue =
            ExplorationPresentationValueResolver.ResolveMappingEstimate(
                local);

        if (mappingValue <= 0)
        {
            mappingValue =
                external?.EstimatedMappingValue
                ?? 0;
        }
        int biologicalSignals = Math.Max(
            local.BiologicalSignals,
            history?.BiologicalSignals ?? 0);
        IReadOnlyList<string> genuses =
            ResolveGenusNames(
                local.Genuses,
                history);

        ExplorationBodyHighlights highlights = BuildHighlights(
            subtype,
            terraformable,
            local.Interest,
            mappingValue,
            biologicalSignals,
            local.Landable || external?.Landable == true);
        return new ExplorationCatalogBody(
            local.BodyId,
            Prefer(local.Name, external?.Name),
            type,
            subtype,
            local.DistanceFromArrivalLs > 0 ? local.DistanceFromArrivalLs : external?.DistanceFromArrivalLs ?? 0,
            local.Landable || external?.Landable == true,
            local.GravityG > 0 ? local.GravityG : external?.GravityG ?? 0,
            local.SurfaceTemperatureKelvin > 0 ? local.SurfaceTemperatureKelvin : external?.SurfaceTemperatureKelvin ?? 0,
            Prefer(local.Atmosphere, external?.Atmosphere),
            Prefer(local.Volcanism, external?.Volcanism),
            terraformable,
            local.EstimatedScanValue > 0 ? local.EstimatedScanValue : external?.EstimatedScanValue ?? 0,
            mappingValue,
            local.IsScanned,
            local.IsMapped,
            local.MappingEfficient,
            history?.Scanned == true,
            history?.Mapped == true,
            history?.EfficientlyMapped == true,
            history?.CompletedOrganics ?? 0,
            local.WasDiscovered,
            local.WasMapped,
            biologicalSignals,
            genuses,
            highlights,
            external is null || string.IsNullOrWhiteSpace(externalSource)
                ? "Journal"
                : $"Journal + {externalSource}")
        {
            SurfacePressureAtmospheres = local.SurfacePressureAtmospheres > 0
                ? local.SurfacePressureAtmospheres
                : external?.SurfacePressureAtmospheres ?? 0,
            LastProbesUsed = local.LastProbesUsed,
            EfficiencyTarget = local.EfficiencyTarget
        };
    }

    private static ExplorationCatalogBody FromExternal(
        ExternalExplorationBodySnapshot body,
        ExplorationHistoryBodySnapshot? history,
        string source)
    {
        bool terraformable = ContainsTerraform(body.TerraformingState);
        int biologicalSignals =
            history?.BiologicalSignals ?? 0;
        IReadOnlyList<string> genuses =
            ResolveGenusNames(
                Array.Empty<string>(),
                history);

        ExplorationBodyHighlights highlights = BuildHighlights(
            body.Subtype,
            terraformable,
            ExplorationInterest.None,
            body.EstimatedMappingValue,
            biologicalSignals,
            body.Landable);
        return new ExplorationCatalogBody(
            body.BodyId,
            body.Name,
            body.Type,
            body.Subtype,
            body.DistanceFromArrivalLs,
            body.Landable,
            body.GravityG,
            body.SurfaceTemperatureKelvin,
            body.Atmosphere,
            body.Volcanism,
            terraformable,
            body.EstimatedScanValue,
            body.EstimatedMappingValue,
            false,
            false,
            false,
            history?.Scanned == true,
            history?.Mapped == true,
            history?.EfficientlyMapped == true,
            history?.CompletedOrganics ?? 0,
            false,
            false,
            biologicalSignals,
            genuses,
            highlights,
            source)
        {
            SurfacePressureAtmospheres = body.SurfacePressureAtmospheres
        };
    }

    private static IReadOnlyList<string> ResolveGenusNames(
        IReadOnlyList<string> liveGenuses,
        ExplorationHistoryBodySnapshot? history)
    {
        if (liveGenuses.Count > 0)
        {
            return liveGenuses;
        }

        return history?.Genuses
            .Select(item =>
                !string.IsNullOrWhiteSpace(item.GenusName)
                    ? item.GenusName
                    : item.GenusKey)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? Array.Empty<string>();
    }

    private static ExplorationBodyHighlights BuildHighlights(
        string subtype,
        bool terraformable,
        ExplorationInterest localInterest,
        long mappingValue,
        int biologicalSignals,
        bool landable)
    {
        ExplorationBodyHighlights result = ExplorationBodyHighlights.None;
        if (mappingValue >= ValuableMappingThreshold) result |= ExplorationBodyHighlights.Valuable;
        if (biologicalSignals > 0) result |= ExplorationBodyHighlights.Biological;
        if (terraformable || localInterest == ExplorationInterest.Terraformable) result |= ExplorationBodyHighlights.Terraformable;
        if (localInterest == ExplorationInterest.EarthLike || Contains(subtype, "Earthlike", "Earth-like")) result |= ExplorationBodyHighlights.EarthLike;
        if (localInterest == ExplorationInterest.WaterWorld || Contains(subtype, "Water world")) result |= ExplorationBodyHighlights.WaterWorld;
        if (localInterest == ExplorationInterest.AmmoniaWorld || Contains(subtype, "Ammonia world")) result |= ExplorationBodyHighlights.AmmoniaWorld;
        if (localInterest == ExplorationInterest.NeutronStar || Contains(subtype, "Neutron")) result |= ExplorationBodyHighlights.NeutronStar;
        if (localInterest == ExplorationInterest.BlackHole || Contains(subtype, "Black hole", "BlackHole")) result |= ExplorationBodyHighlights.BlackHole;
        if (landable) result |= ExplorationBodyHighlights.Landable;
        return result;
    }

    private static string Identity(ExplorationCatalogBody body) =>
        body.BodyId >= 0 ? $"id:{body.BodyId}" : $"name:{body.Name}";

    private static bool ContainsTerraform(string? value) =>
        value?.Contains("Terraform", StringComparison.OrdinalIgnoreCase) == true;

    private static bool Contains(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static string Prefer(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static bool Matches(ExplorationBodySnapshot local, ExplorationHistoryBodySnapshot history) =>
        local.BodyId >= 0 && history.BodyId >= 0 && local.BodyId == history.BodyId
        || !string.IsNullOrWhiteSpace(local.Name)
        && local.Name.Equals(history.BodyName, StringComparison.OrdinalIgnoreCase);

    private static bool Matches(ExplorationHistoryBodySnapshot history, ExternalExplorationBodySnapshot external) =>
        history.BodyId >= 0 && external.BodyId >= 0 && history.BodyId == external.BodyId
        || !string.IsNullOrWhiteSpace(history.BodyName)
        && history.BodyName.Equals(external.Name, StringComparison.OrdinalIgnoreCase);
}
