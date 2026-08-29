using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Exploration;

public static class ExplorationPresentationValueResolver
{
    /// <summary>
    /// Returns the mapping value shown as the body's potential DSS value.
    /// The efficient value is the useful planning value because the assistant
    /// explicitly targets the efficiency bonus. Falls back to the ordinary
    /// mapping estimate when an efficient estimate is unavailable.
    /// </summary>
    public static long ResolveMappingEstimate(
        ExplorationBodySnapshot body) =>
        body.EstimatedEfficientMappingValue > 0
            ? body.EstimatedEfficientMappingValue
            : body.EstimatedMappingValue;

    /// <summary>
    /// Returns the value represented by data actually collected during the
    /// current visit. Mapping estimates are total body payouts, not an
    /// additional amount to add on top of the scan estimate.
    /// </summary>
    public static long ResolveCurrentVisitValue(
        ExplorationBodySnapshot body)
    {
        if (!body.IsMapped)
        {
            return
                body.EstimatedScanValue;
        }

        if (body.MappingEfficient)
        {
            long efficient =
                ResolveMappingEstimate(
                    body);

            if (efficient > 0)
            {
                return efficient;
            }
        }

        return
            body.EstimatedMappingValue > 0
                ? body.EstimatedMappingValue
                : ResolveMappingEstimate(
                    body);
    }
}
