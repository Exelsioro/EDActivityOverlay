using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Exploration;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class ExplorationPostMergeAuditTests
{
    [Fact]
    public void CatalogBuilderPreservesHistoricalBiology()
    {
        string code = File.ReadAllText(
            FindProjectFile(
                "Services",
                "Exploration",
                "ExplorationSystemCatalogBuilder.cs"));

        Assert.Contains(
            "history?.BiologicalSignals ?? 0",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "ResolveGenusNames(",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "biologicalSignals,\n            genuses,\n            highlights,",
            code.Replace("\r\n", "\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyProgressRowsWithoutCanonicalSpeciesKeyAreRemoved()
    {
        string file = Path.Combine(
            Path.GetTempPath(),
            $"ed-overlay-progress-{Guid.NewGuid():N}.json");

        DateTimeOffset timestamp =
            DateTimeOffset.Parse(
                "2026-08-22T12:00:00Z");

        try
        {
            var legacy =
                new OrganicScanProgressSnapshot(
                    "Cmdr",
                    42,
                    "Test",
                    4,
                    "Test 4",
                    "Стратум",
                    "Стратум Тектоникас",
                    string.Empty,
                    2,
                    false,
                    500,
                    null,
                    null,
                    timestamp);

            var canonical =
                new OrganicScanProgressSnapshot(
                    "Cmdr",
                    42,
                    "Test",
                    4,
                    "Test 4",
                    "Stratum",
                    "Stratum Tectonicas",
                    string.Empty,
                    3,
                    true,
                    500,
                    null,
                    null,
                    timestamp)
                {
                    GenusKey =
                        "$Codex_Ent_Stratum_Genus_Name;",
                    SpeciesKey =
                        "$Codex_Ent_Stratum_Tectonicas_Name;"
                };

            File.WriteAllText(
                file,
                JsonSerializer.Serialize(
                    new[]
                    {
                        legacy,
                        canonical
                    }));

            var store =
                new ExplorationProgressStore(file);

            OrganicScanProgressSnapshot loaded =
                Assert.Single(store.Load());

            Assert.Equal(
                canonical.SpeciesKey,
                loaded.SpeciesKey);

            List<OrganicScanProgressSnapshot>? persisted =
                JsonSerializer.Deserialize<
                    List<OrganicScanProgressSnapshot>>(
                    File.ReadAllText(file));

            Assert.NotNull(persisted);

            OrganicScanProgressSnapshot persistedRow =
                Assert.Single(persisted!);

            Assert.Equal(
                canonical.SpeciesKey,
                persistedRow.SpeciesKey);
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void VisitStateServiceRejectsStaleSameSystemRefresh()
    {
        string code = File.ReadAllText(
            FindProjectFile(
                "Services",
                "Exploration",
                "ExplorationVisitStateService.cs"));

        Assert.Contains(
            "private int refreshGeneration;",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "generation = ++refreshGeneration;",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "generation != refreshGeneration",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void X52HelpMatchesCurrentInputBehavior()
    {
        string english = File.ReadAllText(
            FindProjectFile(
                "Resources",
                "Localization.en-US.xaml"));

        string russian = File.ReadAllText(
            FindProjectFile(
                "Resources",
                "Localization.ru-RU.xaml"));

        string cheatsheet = File.ReadAllText(
            FindRepositoryFile(
                "Documentation",
                "X52_CONTROL_CHEATSHEET_RU.md"));

        Assert.Contains(
            "Single press: toggle interactive focus.",
            english,
            StringComparison.Ordinal);

        Assert.Contains(
            "Double press: hide/restore all overlays.",
            english,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "Hold for 0.7 s: interactive mode.",
            english,
            StringComparison.Ordinal);

        Assert.Contains(
            "Одиночное нажатие: включить/выключить интерактивный фокус.",
            russian,
            StringComparison.Ordinal);

        Assert.Contains(
            "Двойное нажатие: скрыть/вернуть все оверлеи.",
            russian,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "Fire A используется приложением как клик",
            cheatsheet,
            StringComparison.Ordinal);

        Assert.Contains(
            "Mouse Pointer",
            cheatsheet,
            StringComparison.Ordinal);

        Assert.Contains(
            "Mouse Click",
            cheatsheet,
            StringComparison.Ordinal);
    }

    private static string FindProjectFile(
        params string[] relative) =>
        FindRepositoryFile(
            [
                "EDActivityOverlay",
                .. relative
            ]);

    private static string FindRepositoryFile(
        params string[] relative)
    {
        for (
            DirectoryInfo? directory =
                new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            string candidate = Path.Combine(
                [
                    directory.FullName,
                    .. relative
                ]);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            string.Join(
                Path.DirectorySeparatorChar,
                relative));
    }
}