namespace EDActivityOverlay.Utils;

internal static class OverlayRenderModes
{
    public const string Individual = "Individual";
    public const string Composite = "Composite";

    public static string Normalize(string? value) =>
        value?.Trim() switch
        {
            { } candidate when candidate.Equals(Composite, StringComparison.OrdinalIgnoreCase) => Composite,
            { } candidate when candidate.Equals(Individual, StringComparison.OrdinalIgnoreCase) => Individual,
            _ => Individual
        };

    public static bool IsComposite(string? value) =>
        string.Equals(Normalize(value), Composite, StringComparison.Ordinal);
}
