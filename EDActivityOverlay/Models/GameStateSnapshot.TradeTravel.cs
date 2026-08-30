namespace EDActivityOverlay.Models;

public sealed partial record GameStateSnapshot
{
    /// <summary>
    /// Hull + fitted modules with empty fuel tank and zero cargo, in tonnes.
    /// Populated from the Journal Loadout event.
    /// </summary>
    public double UnladenMassTonnes { get; init; }
}
