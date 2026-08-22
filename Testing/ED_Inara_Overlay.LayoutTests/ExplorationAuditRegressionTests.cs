using ED_Inara_Overlay.Models;
using ED_Inara_Overlay.Services.Exploration;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class ExplorationAuditRegressionTests
{
    [Fact]
    public void ManualDeferredBodyCannotBeReactivatedUntilResume()
    {
        var engine = new ExplorationVisitQueueEngine();

        engine.Update(
            State(
                BioBody(
                    4,
                    "Test 4",
                    mapped: true,
                    completed: false)),
            Catalog(
                BioCatalogBody(
                    4,
                    "Test 4")),
            ExplorationSystemHistorySnapshot.Empty);

        Assert.True(engine.ActivateBody(4));
        Assert.True(engine.DeferBody(4));
        Assert.True(engine.IsManuallyDeferred(4));

        Assert.False(engine.ActivateBody(4));
        Assert.Null(engine.Current.Active);
        Assert.Equal(
            4,
            Assert.Single(engine.Current.Deferred).BodyId);

        Assert.True(engine.ResumeBody(4));
        Assert.False(engine.IsManuallyDeferred(4));
        Assert.True(engine.ActivateBody(4));
        Assert.Equal(4, engine.Current.Active?.BodyId);
    }

    [Fact]
    public void SelectingOrdinaryBodyDefersPreviousIncompleteTarget()
    {
        var engine = new ExplorationVisitQueueEngine();

        engine.Update(
            State(
                BioBody(
                    4,
                    "Test 4",
                    mapped: true,
                    completed: false)),
            Catalog(
                BioCatalogBody(
                    4,
                    "Test 4")),
            ExplorationSystemHistorySnapshot.Empty);

        Assert.True(engine.ActivateBody(4));

        // 99 is a real navigation body for the player, but deliberately not
        // present in the interesting-body queue.
        Assert.True(engine.SelectDestinationBody(99));

        Assert.Null(engine.Current.Active);
        Assert.Equal(
            4,
            Assert.Single(engine.Current.Deferred).BodyId);
    }

    [Fact]
    public void SelectingCompletedBodyClosesPreviousActiveWithoutReopeningCompleted()
    {
        var engine = new ExplorationVisitQueueEngine();

        engine.Update(
            State(
                BioBody(
                    4,
                    "Test 4",
                    mapped: true,
                    completed: false),
                ValuableBody(
                    5,
                    "Test 5",
                    mapped: true)),
            Catalog(
                BioCatalogBody(
                    4,
                    "Test 4"),
                ValuableCatalogBody(
                    5,
                    "Test 5")),
            ExplorationSystemHistorySnapshot.Empty);

        Assert.True(engine.ActivateBody(4));
        Assert.True(engine.SelectDestinationBody(5));

        Assert.Null(engine.Current.Active);
        Assert.Equal(
            4,
            Assert.Single(engine.Current.Deferred).BodyId);
        Assert.Equal(
            5,
            Assert.Single(engine.Current.Completed).BodyId);
    }

    [Fact]
    public void RawGenusIdentitySurvivesDifferentDisplayLanguage()
    {
        const string stratumKey =
            "$Codex_Ent_Stratum_Genus_Name;";
        const string bacteriumKey =
            "$Codex_Ent_Bacterial_Genus_Name;";

        var body = new ExplorationBodySnapshot(
            4,
            "Test 4",
            "Rocky body",
            800,
            false,
            false,
            true,
            true,
            2,
            new[]
            {
                "Стратум",
                "Бактерия"
            },
            ExplorationInterest.None)
        {
            IsScanned = true,
            BodyType = "Planet",
            BodyClass = "Rocky body",
            GenusKeys = new[]
            {
                stratumKey,
                bacteriumKey
            }
        };

        var organic = new OrganicScanProgressSnapshot(
            "Cmdr",
            42,
            "Test",
            4,
            "Test 4",
            "Stratum",
            "Stratum Tectonicas",
            "Green",
            3,
            true,
            500,
            null,
            null,
            DateTimeOffset.Parse(
                "2026-08-22T12:00:00Z"))
        {
            GenusKey = stratumKey,
            SpeciesKey =
                "$Codex_Ent_Stratum_Tectonicas_Name;"
        };

        var state = new GameStateSnapshot
        {
            Commander = "Cmdr",
            StarSystem = "Test",
            SystemAddress = 42,
            ExplorationBodies = new[]
            {
                body
            },
            OrganicProgress = new[]
            {
                organic
            }
        };

        BodyExplorationProgress progress =
            BodyExplorationProgressBuilder.Build(
                state,
                ExplorationSystemHistorySnapshot.Empty,
                4);

        Assert.Equal(1, progress.CompletedBiologicalSignals);
        Assert.Equal(
            new[]
            {
                "Бактерия"
            },
            progress.MissingGenuses);

        Assert.Single(progress.MissingGenusKeys);
        Assert.Equal(
            "bacterium",
            progress.MissingGenusKeys[0]);
    }

    [Fact]
    public void SchemaUpgradeInvalidatesBiologyRowsAndImportMarkersOnly()
    {
        string file = Path.Combine(
            Path.GetTempPath(),
            $"ed-overlay-audit-{Guid.NewGuid():N}.db");

        DateTimeOffset timestamp =
            DateTimeOffset.Parse(
                "2026-08-22T12:00:00Z");

        DateTime importedWrite =
            new(
                2026,
                8,
                21,
                12,
                0,
                0,
                DateTimeKind.Utc);

        try
        {
            var initial =
                new ExplorationHistoryRepository(file);

            initial.RecordVisit(
                "Cmdr",
                42,
                "Test",
                timestamp);

            initial.RecordBody(
                "Cmdr",
                42,
                "Test",
                4,
                "Test 4",
                "Rocky body",
                timestamp,
                scanned: true,
                mapped: true,
                biologicalSignals: 2);

            initial.RecordBodyGenuses(
                "Cmdr",
                42,
                "Test",
                4,
                "Test 4",
                new[]
                {
                    (
                        Key: "$Codex_Ent_Stratum_Genus_Name;",
                        Name: "Stratum"
                    )
                },
                timestamp);

            initial.RecordOrganic(
                "Cmdr",
                42,
                "Test",
                4,
                "Test 4",
                "$Codex_Ent_Stratum_Tectonicas_Name;",
                "Stratum Tectonicas",
                true,
                timestamp,
                genusKey:
                    "$Codex_Ent_Stratum_Genus_Name;",
                genusName:
                    "Stratum");

            initial.MarkFileImported(
                @"C:\journals\Journal.01.log",
                123,
                importedWrite,
                10);

            using (var connection =
                   new SqliteConnection(
                       $"Data Source={file}"))
            {
                connection.Open();

                using SqliteCommand command =
                    connection.CreateCommand();

                command.CommandText = """
                    UPDATE exploration_meta
                    SET value = '1'
                    WHERE key = 'schema_version';
                    """;

                Assert.Equal(
                    1,
                    command.ExecuteNonQuery());
            }

            var migrated =
                new ExplorationHistoryRepository(file);

            ExplorationHistoryBodySnapshot body =
                Assert.Single(
                    migrated.LoadSystem(
                            "Cmdr",
                            42,
                            "Test")
                        .Bodies);

            Assert.True(body.Scanned);
            Assert.True(body.Mapped);
            Assert.Empty(body.Genuses);
            Assert.Empty(body.Organics);

            Assert.False(
                migrated.IsFileImported(
                    @"C:\journals\Journal.01.log",
                    123,
                    importedWrite));
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void WorkspaceKeepsNewCompactSizeAndRebindsLocalizedFilters()
    {
        string code = File.ReadAllText(
            FindProjectFile(
                "Windows",
                "ActivityWorkspaceOverlayWindow.xaml.cs"));

        Assert.Contains(
            "private const double CompactWidth = 420;",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "Width = CompactWidth;",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "CatalogFilterComboBox.ItemsSource = null;",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "CatalogFilterComboBox.ItemsSource = CatalogFilters;",
            code,
            StringComparison.Ordinal);
    }

    private static GameStateSnapshot State(
        params ExplorationBodySnapshot[] bodies)
    {
        OrganicScanProgressSnapshot[] organics = bodies
            .Where(body => body.BiologicalSignals > 0)
            .SelectMany(body =>
                body.Genuses
                    .Take(
                        body.Name.EndsWith(
                            "complete",
                            StringComparison.OrdinalIgnoreCase)
                            ? body.BiologicalSignals
                            : 0)
                    .Select((genus, index) =>
                        new OrganicScanProgressSnapshot(
                            "Cmdr",
                            42,
                            "Test",
                            body.BodyId,
                            body.Name,
                            genus,
                            genus + " species",
                            string.Empty,
                            3,
                            true,
                            500,
                            null,
                            null,
                            DateTimeOffset.Parse(
                                "2026-08-22T12:00:00Z"))))
            .ToArray();

        return new GameStateSnapshot
        {
            Commander = "Cmdr",
            StarSystem = "Test",
            SystemAddress = 42,
            ExplorationBodies = bodies,
            OrganicProgress = organics
        };
    }

    private static ExplorationBodySnapshot BioBody(
        int id,
        string name,
        bool mapped,
        bool completed)
    {
        string[] genuses =
        [
            "Stratum",
            "Bacterium"
        ];

        OrganicScanProgressSnapshot[] unused =
            Array.Empty<OrganicScanProgressSnapshot>();

        return new ExplorationBodySnapshot(
            id,
            completed ? name + " complete" : name,
            "Rocky body",
            800,
            false,
            false,
            mapped,
            mapped,
            2,
            genuses,
            ExplorationInterest.None)
        {
            IsScanned = true,
            BodyType = "Planet",
            BodyClass = "Rocky body",
            Landable = true
        };
    }

    private static ExplorationBodySnapshot ValuableBody(
        int id,
        string name,
        bool mapped) =>
        new(
            id,
            name,
            "Water world",
            1_200,
            false,
            false,
            mapped,
            mapped,
            0,
            Array.Empty<string>(),
            ExplorationInterest.WaterWorld)
        {
            IsScanned = true,
            BodyType = "Planet",
            BodyClass = "Water world",
            EstimatedMappingValue = 350_000
        };

    private static ExplorationSystemCatalog Catalog(
        params ExplorationCatalogBody[] bodies) =>
        new(
            "Test",
            bodies.Length,
            ExplorationSpoilerModes.EnrichScanned,
            bodies);

    private static ExplorationCatalogBody BioCatalogBody(
        int id,
        string name) =>
        MakeCatalogBody(
            id,
            name,
            ExplorationBodyHighlights.Biological
            | ExplorationBodyHighlights.Landable,
            mappingValue: 100_000);

    private static ExplorationCatalogBody ValuableCatalogBody(
        int id,
        string name) =>
        MakeCatalogBody(
            id,
            name,
            ExplorationBodyHighlights.WaterWorld
            | ExplorationBodyHighlights.Valuable,
            mappingValue: 350_000);

    private static ExplorationCatalogBody MakeCatalogBody(
        int id,
        string name,
        ExplorationBodyHighlights highlights,
        long mappingValue) =>
        new(
            id,
            name,
            "Planet",
            highlights.HasFlag(
                ExplorationBodyHighlights.WaterWorld)
                ? "Water world"
                : "Rocky body",
            800,
            highlights.HasFlag(
                ExplorationBodyHighlights.Landable),
            0.2,
            250,
            "Thin atmosphere",
            string.Empty,
            highlights.HasFlag(
                ExplorationBodyHighlights.Terraformable),
            100_000,
            mappingValue,
            true,
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            false,
            highlights.HasFlag(
                ExplorationBodyHighlights.Biological)
                ? 2
                : 0,
            highlights.HasFlag(
                ExplorationBodyHighlights.Biological)
                ? new[]
                {
                    "Stratum",
                    "Bacterium"
                }
                : Array.Empty<string>(),
            highlights,
            "Journal");

    private static string FindProjectFile(
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
                    "ED_Inara_Overlay",
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