using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Hardware;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class MiningX52CopilotTests
{
    [Fact]
    public void MfdShowsDecisionCargoAndLimpets()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        MiningSessionSnapshot session = BuildSession(
            now,
            prospects:
            [
                new MiningProspectSnapshot(
                    1,
                    now,
                    "High",
                    100,
                    string.Empty,
                    string.Empty,
                    [
                        new MiningProspectMaterialSnapshot(
                            "platinum",
                            "Platinum",
                            32.8)
                    ])
            ],
            cargoUsed: 184,
            cargoCapacity: 256,
            limpets: 61);

        string[] lines = X52MiningCopilotFormatter.BuildLines(
            session,
            new MiningCollectorActivitySnapshot(
                true,
                8,
                6,
                2,
                TimeSpan.FromMinutes(12)),
            "Platinum",
            25,
            now);

        Assert.Equal(3, lines.Length);
        Assert.Contains("MINE", lines[0]);
        Assert.Contains(32.8.ToString("0.#"), lines[0]);
        Assert.Contains("C184/256", lines[1]);
        Assert.Contains("L61", lines[1]);
        Assert.All(
            lines,
            line => Assert.True(
                line.Length
                <= X52DisplayFormatter.MaximumLineLength));
    }

    [Fact]
    public void AdvisoryPrioritizesCriticalLimpets()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        MiningSessionSnapshot session = BuildSession(
            now,
            refinements: Enumerable.Range(1, 20)
                .Select(index =>
                    new MiningRefinementSnapshot(
                        index,
                        now.AddMinutes(-1),
                        "platinum",
                        "Platinum"))
                .ToArray(),
            prospectorsLaunched: 20,
            collectorsLaunched: 20,
            cargoUsed: 120,
            cargoCapacity: 256,
            limpets: 3);

        string[] lines = X52MiningCopilotFormatter.BuildLines(
            session,
            MiningCollectorActivitySnapshot.Empty,
            "Platinum",
            25,
            now);

        Assert.Equal("LIMPETS CRIT", lines[2]);
    }

    [Fact]
    public void AdvisoryShowsCollectorTopUp()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        MiningSessionSnapshot session = BuildSession(
            now,
            cargoUsed: 40,
            cargoCapacity: 256,
            limpets: 80);

        string[] lines = X52MiningCopilotFormatter.BuildLines(
            session,
            new MiningCollectorActivitySnapshot(
                true,
                8,
                5,
                3,
                TimeSpan.FromMinutes(12)),
            "Platinum",
            25,
            now);

        Assert.Contains("COL ~5/8 +3", lines[2]);
    }

    private static MiningSessionSnapshot BuildSession(
        DateTimeOffset now,
        IReadOnlyList<MiningProspectSnapshot>? prospects = null,
        IReadOnlyList<MiningRefinementSnapshot>? refinements = null,
        int prospectorsLaunched = 0,
        int collectorsLaunched = 0,
        int cargoUsed = 0,
        int cargoCapacity = 256,
        int limpets = 0) =>
        new(
            Guid.NewGuid(),
            MiningSessionState.Active,
            now.AddMinutes(-10),
            now,
            null,
            MiningSessionEndReason.None,
            "CMDR",
            1,
            "Test",
            1,
            "Test 1",
            "Test 1 A Ring",
            prospectorsLaunched,
            collectorsLaunched,
            0,
            cargoUsed,
            cargoCapacity,
            limpets,
            prospects ?? Array.Empty<MiningProspectSnapshot>(),
            refinements ?? Array.Empty<MiningRefinementSnapshot>());
}
