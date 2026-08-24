namespace EDActivityOverlay.Models;

public sealed record ExobiologyPrediction(
    string Species,
    string Variant,
    string Genus,
    string CatalogIdentifier,
    int ColonyRangeMeters,
    long BaseValue,
    double RelativeProbability,
    int ObservationCount);

