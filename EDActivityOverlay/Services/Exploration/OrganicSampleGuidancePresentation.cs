namespace EDActivityOverlay.Services.Exploration;

/// <summary>Separates the next action from its supporting measurement; no organism-name duplication.</summary>
internal sealed record OrganicSampleGuidancePresentation(
    string HeadlineKey, object?[] HeadlineArguments,
    string DetailKey, object?[] DetailArguments)
{
    internal static OrganicSampleGuidancePresentation Create(OrganicSampleSeparation separation)
    {
        if (separation.RequiredSpacingMeters <= 0)
            return new("Loc_EXOBIO_SPACING_UNKNOWN_SHORT", [], "Loc_EXOBIO_SPACING_UNKNOWN", []);
        if (!separation.HasCompleteHistory)
            return new("Loc_EXOBIO_HISTORY_UNKNOWN_SHORT", [], "Loc_EXOBIO_SAMPLE_HISTORY_INCOMPLETE_FORMAT",
                [separation.KnownSampleCount, separation.RequiredSampleCount]);
        if (separation.NearestSample is not { } nearest)
            return new("Loc_EXOBIO_POSITION_UNKNOWN_SHORT", [], "Loc_EXOBIO_POSITION_UNAVAILABLE", []);
        if (separation.IsReady)
            return new("Loc_EXOBIO_SPACING_READY_SHORT", [], "Loc_EXOBIO_SPACING_DETAILS_FORMAT",
                [nearest.DistanceMeters, separation.RequiredSpacingMeters]);
        double remaining = Math.Max(0, separation.EscapeDistanceMeters
            ?? separation.RequiredSpacingMeters - nearest.DistanceMeters);
        return new("Loc_EXOBIO_MOVE_MORE_SHORT_FORMAT", [remaining], "Loc_EXOBIO_SPACING_DETAILS_FORMAT",
            [nearest.DistanceMeters, separation.RequiredSpacingMeters]);
    }
}
