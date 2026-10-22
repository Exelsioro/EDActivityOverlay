namespace EDActivityOverlay.Models;

/// <summary>Journal/Spansh codes and the EDSM label; never infer a positive state from a substring.</summary>
internal static class TerraformingStatus
{
    public static bool IsPositive(string? value) => value?.Trim().ToUpperInvariant() is
        "TERRAFORMABLE" or "TERRAFORMING" or "TERRAFORMED" or "CANDIDATE FOR TERRAFORMING";
}
