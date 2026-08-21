namespace ED_Inara_Overlay.Services.Exploration;

/// <summary>
/// Estimates Universal Cartographics scan and mapping payouts without requiring
/// EDDiscovery or a network provider.
/// </summary>
/// <remarks>
/// Adapted from EDDiscovery/EliteDangerousCore,
/// EliteDangerous/FrontierData/Enumerations/EstimatedValues.cs (Apache-2.0),
/// copyright 2021-2023 EDDiscovery development team. The implementation was
/// reduced to the post-3.3 formula and string journal identifiers used here.
/// </remarks>
public static class ExplorationValueCalculator
{
    private const double FirstDiscoveryMultiplier = 2.6;
    private const double EfficientMappingMultiplier = 1.25;
    private const double FirstDiscoveredAndMappedMultiplier = 3.699622554;
    private const double FirstMappedMultiplier = 8.0956;
    private const double PreviouslyMappedMultiplier = 3.3333333;

    public static ExplorationValueEstimate Estimate(
        string bodyType,
        string bodyClass,
        bool terraformable,
        double? earthMasses,
        double? solarMasses,
        bool odyssey = true)
    {
        if (IsStar(bodyType, bodyClass))
        {
            double baseValue = StarValue(GetStarCoefficient(bodyClass), PositiveOrDefault(solarMasses));
            return new ExplorationValueEstimate(
                (long)baseValue,
                (long)(baseValue * FirstDiscoveryMultiplier),
                0, 0, 0, 0, 0, 0);
        }

        if (!IsPlanet(bodyType, bodyClass)) return ExplorationValueEstimate.Empty;

        double basePlanetValue = PlanetValue(GetPlanetCoefficient(bodyClass, terraformable), PositiveOrDefault(earthMasses));
        double firstDiscoveredAndMapped = OdysseyBonus(basePlanetValue * FirstDiscoveredAndMappedMultiplier, odyssey);
        double firstMapped = OdysseyBonus(basePlanetValue * FirstMappedMultiplier, odyssey);
        double previouslyMapped = OdysseyBonus(basePlanetValue * PreviouslyMappedMultiplier, odyssey);
        return new ExplorationValueEstimate(
            (long)basePlanetValue,
            (long)(basePlanetValue * FirstDiscoveryMultiplier),
            (long)(firstDiscoveredAndMapped * FirstDiscoveryMultiplier),
            (long)(firstDiscoveredAndMapped * FirstDiscoveryMultiplier * EfficientMappingMultiplier),
            (long)firstMapped,
            (long)(firstMapped * EfficientMappingMultiplier),
            (long)previouslyMapped,
            (long)(previouslyMapped * EfficientMappingMultiplier));
    }

    public static long SelectScanValue(ExplorationValueEstimate estimate, bool wasDiscovered) =>
        wasDiscovered ? estimate.BaseScanValue : estimate.FirstDiscoveryScanValue;

    public static long SelectMappingValue(
        ExplorationValueEstimate estimate,
        bool wasDiscovered,
        bool wasMapped,
        bool efficient)
    {
        if (!wasDiscovered && !wasMapped)
        {
            return efficient
                ? estimate.FirstDiscoveredAndMappedEfficientValue
                : estimate.FirstDiscoveredAndMappedValue;
        }
        if (!wasMapped)
        {
            return efficient ? estimate.FirstMappedEfficientValue : estimate.FirstMappedValue;
        }
        return efficient ? estimate.PreviouslyMappedEfficientValue : estimate.PreviouslyMappedValue;
    }

    private static bool IsStar(string type, string bodyClass) =>
        type.Equals("Star", StringComparison.OrdinalIgnoreCase)
        || IsStarClass(bodyClass);

    private static bool IsPlanet(string type, string bodyClass) =>
        type.Equals("Planet", StringComparison.OrdinalIgnoreCase)
        || bodyClass.Contains("world", StringComparison.OrdinalIgnoreCase)
        || bodyClass.Contains("body", StringComparison.OrdinalIgnoreCase)
        || bodyClass.Contains("gas giant", StringComparison.OrdinalIgnoreCase)
        || bodyClass.Contains("giant with", StringComparison.OrdinalIgnoreCase);

    private static bool IsStarClass(string value)
    {
        string normalized = value.Trim();
        return normalized is "O" or "B" or "A" or "F" or "G" or "K" or "M" or "L" or "T" or "Y" or "N" or "H"
            || normalized.StartsWith("D", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("star", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("black hole", StringComparison.OrdinalIgnoreCase);
    }

    private static double GetStarCoefficient(string bodyClass)
    {
        string normalized = bodyClass.Trim();
        if (normalized.Equals("N", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("H", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("neutron", StringComparison.OrdinalIgnoreCase)) return 22628;
        if (normalized.StartsWith("D", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("white dwarf", StringComparison.OrdinalIgnoreCase)) return 14057;
        if (normalized.Contains("supermassive", StringComparison.OrdinalIgnoreCase)) return 33.5678;
        return 1200;
    }

    private static double GetPlanetCoefficient(string bodyClass, bool terraformable)
    {
        if (bodyClass.Contains("Metal rich", StringComparison.OrdinalIgnoreCase))
            return 21790 + (terraformable ? 65631 : 0);
        if (bodyClass.Contains("Ammonia world", StringComparison.OrdinalIgnoreCase)) return 96932;
        if (bodyClass.Contains("class I gas giant", StringComparison.OrdinalIgnoreCase)) return 1656;
        if (bodyClass.Contains("class II gas giant", StringComparison.OrdinalIgnoreCase)
            || bodyClass.Contains("High metal content", StringComparison.OrdinalIgnoreCase))
            return 9654 + (terraformable ? 100677 : 0);
        if (bodyClass.Contains("Water world", StringComparison.OrdinalIgnoreCase))
            return 64831 + (terraformable ? 116295 : 0);
        if (bodyClass.Contains("Earthlike", StringComparison.OrdinalIgnoreCase)
            || bodyClass.Contains("Earth-like", StringComparison.OrdinalIgnoreCase)) return 64831 + 116295;
        return 300 + (terraformable ? 93328 : 0);
    }

    private static double StarValue(double coefficient, double mass) =>
        coefficient + (mass * coefficient / 66.25);

    private static double PlanetValue(double coefficient, double mass)
    {
        const double massExponentCoefficient = 0.56591828;
        return Math.Max(coefficient + coefficient * Math.Pow(mass, 0.2) * massExponentCoefficient, 500);
    }

    private static double OdysseyBonus(double value, bool odyssey) =>
        value + (odyssey ? Math.Max(value * 0.3, 555) : 0);

    private static double PositiveOrDefault(double? value) => value is > 0 ? value.Value : 1;
}

public sealed record ExplorationValueEstimate(
    long BaseScanValue,
    long FirstDiscoveryScanValue,
    long FirstDiscoveredAndMappedValue,
    long FirstDiscoveredAndMappedEfficientValue,
    long FirstMappedValue,
    long FirstMappedEfficientValue,
    long PreviouslyMappedValue,
    long PreviouslyMappedEfficientValue)
{
    public static ExplorationValueEstimate Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);
}
