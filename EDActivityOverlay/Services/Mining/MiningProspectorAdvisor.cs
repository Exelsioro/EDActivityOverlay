using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Trading;

namespace EDActivityOverlay.Services.Mining;

public static class MiningProspectorAdvisor
{
    public static MiningProspectAdvice Evaluate(
        MiningProspectSnapshot? prospect,
        string? targetCommodity,
        double minimumProportion)
    {
        string target = targetCommodity?.Trim() ?? string.Empty;
        double threshold = Math.Clamp(minimumProportion, 0, 100);

        if (prospect is null)
        {
            return new MiningProspectAdvice(
                MiningProspectDecision.NoTarget,
                MiningExtractionMethod.Unknown,
                MiningExtractionMethod.Unknown,
                target,
                string.Empty,
                null,
                false,
                false);
        }

        MiningExtractionMethod recommendedMethod = RecommendMethod(prospect);
        if (string.IsNullOrWhiteSpace(target))
        {
            return new MiningProspectAdvice(
                MiningProspectDecision.NoTarget,
                recommendedMethod,
                MiningExtractionMethod.Unknown,
                string.Empty,
                string.Empty,
                null,
                false,
                false);
        }

        bool motherlodeMatches = Matches(
            target,
            prospect.MotherlodeCommodityId,
            prospect.MotherlodeDisplayName);

        MiningProspectMaterialSnapshot? material = prospect.Materials
            .FirstOrDefault(item => Matches(target, item.CommodityId, item.DisplayName));

        if (motherlodeMatches)
        {
            return new MiningProspectAdvice(
                MiningProspectDecision.Core,
                recommendedMethod,
                MiningExtractionMethod.Core,
                target,
                string.IsNullOrWhiteSpace(prospect.MotherlodeDisplayName)
                    ? prospect.MotherlodeCommodityId
                    : prospect.MotherlodeDisplayName,
                material?.Proportion,
                true,
                true);
        }

        if (material is not null)
        {
            return new MiningProspectAdvice(
                material.Proportion >= threshold
                    ? MiningProspectDecision.Mine
                    : MiningProspectDecision.Skip,
                recommendedMethod,
                MiningExtractionMethod.Laser,
                target,
                string.IsNullOrWhiteSpace(material.DisplayName)
                    ? material.CommodityId
                    : material.DisplayName,
                material.Proportion,
                true,
                false);
        }

        return new MiningProspectAdvice(
            MiningProspectDecision.Skip,
            recommendedMethod,
            MiningExtractionMethod.Unknown,
            target,
            string.Empty,
            null,
            false,
            false);
    }

    public static MiningExtractionMethod RecommendMethod(MiningProspectSnapshot? prospect)
    {
        if (prospect is null)
        {
            return MiningExtractionMethod.Unknown;
        }

        if (prospect.HasMotherlode)
        {
            return MiningExtractionMethod.Core;
        }

        return prospect.Materials.Count > 0
            ? MiningExtractionMethod.Laser
            : MiningExtractionMethod.Unknown;
    }

    private static bool Matches(
        string target,
        string commodityId,
        string displayName)
    {
        string normalizedTarget = CommodityIdentity.Normalize(target);
        if (string.IsNullOrWhiteSpace(normalizedTarget))
        {
            return false;
        }

        return normalizedTarget.Equals(
                   CommodityIdentity.Normalize(commodityId),
                   StringComparison.OrdinalIgnoreCase)
               || normalizedTarget.Equals(
                   CommodityIdentity.Normalize(displayName),
                   StringComparison.OrdinalIgnoreCase)
               || target.Equals(displayName, StringComparison.OrdinalIgnoreCase);
    }
}

public static class MiningTargetAnalytics
{
    public static MiningTargetStatistics Calculate(
        MiningSessionSnapshot session,
        string? targetCommodity,
        double minimumProportion)
    {
        ArgumentNullException.ThrowIfNull(session);

        int prospected = session.Prospects.Count;
        if (prospected == 0 || string.IsNullOrWhiteSpace(targetCommodity))
        {
            return new MiningTargetStatistics(
                prospected,
                0,
                0,
                0,
                0,
                0,
                0,
                0);
        }

        MiningProspectAdvice[] advice = session.Prospects
            .Select(item => MiningProspectorAdvisor.Evaluate(
                item,
                targetCommodity,
                minimumProportion))
            .ToArray();

        int targetBearing = advice.Count(item => item.TargetFound);
        int accepted = advice.Count(item =>
            item.Decision is MiningProspectDecision.Mine or MiningProspectDecision.Core);

        double[] proportions = advice
            .Where(item => item.TargetProportion.HasValue)
            .Select(item => item.TargetProportion!.Value)
            .OrderBy(value => value)
            .ToArray();

        double average = proportions.Length == 0
            ? 0
            : proportions.Average();

        double median = proportions.Length switch
        {
            0 => 0,
            var count when count % 2 == 1 => proportions[count / 2],
            var count => (proportions[count / 2 - 1] + proportions[count / 2]) / 2.0
        };

        return new MiningTargetStatistics(
            prospected,
            targetBearing,
            accepted,
            targetBearing / (double)prospected,
            accepted / (double)prospected,
            average,
            median,
            proportions.Length == 0 ? 0 : proportions[^1]);
    }
}
