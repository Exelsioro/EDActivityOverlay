using ED_Inara_Overlay.Models;
using ED_Inara_Overlay.Services.Exploration;
using ED_Inara_Overlay.Services.Journal;
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class ExplorationProgressCoreTests
{
    [Fact]
    public void StatusDestinationPreservesBodyIdentityAndClearsWhenRemoved()
    {
        var reducer = new JournalStateReducer();
        reducer.ApplyStatusJson("""{"timestamp":"2026-08-22T12:00:00Z","event":"Status","Flags":0,"Flags2":0,"Destination":{"System":123456789,"Body":7,"Name":"HIP 12345 A 7 a"}}""");
        Assert.Equal("HIP 12345 A 7 a", reducer.Current.DestinationName);
        Assert.Equal(123456789, reducer.Current.DestinationSystemAddress);
        Assert.Equal(7, reducer.Current.DestinationBodyId);

        reducer.ApplyStatusJson("""{"timestamp":"2026-08-22T12:00:01Z","event":"Status","Flags":0,"Flags2":0}""");
        Assert.Equal(string.Empty, reducer.Current.DestinationName);
        Assert.Equal(0, reducer.Current.DestinationSystemAddress);
        Assert.Equal(-1, reducer.Current.DestinationBodyId);
    }

    [Fact]
    public void PerBodyBiologyDoesNotMixOtherBodies()
    {
        var reducer = new JournalStateReducer();
        reducer.ApplyJournalLine("""{"event":"FSDJump","StarSystem":"Test","SystemAddress":42}""");
        reducer.ApplyJournalLine("""{"event":"SAASignalsFound","BodyID":4,"BodyName":"Test 4","Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2}],"Genuses":[{"Genus":"$Codex_Ent_Stratum_Genus_Name;","Genus_Localised":"Stratum"},{"Genus":"$Codex_Ent_Bacterial_Genus_Name;","Genus_Localised":"Bacterium"}]}""");
        reducer.ApplyJournalLine("""{"event":"ScanOrganic","ScanType":"Analyse","Body":4,"Genus":"$Codex_Ent_Stratum_Genus_Name;","Genus_Localised":"Stratum","Species":"$Codex_Ent_Stratum_Tectonicas_Name;","Species_Localised":"Stratum Tectonicas"}""");
        reducer.ApplyJournalLine("""{"event":"ScanOrganic","ScanType":"Analyse","Body":5,"Genus":"$Codex_Ent_Bacterial_Genus_Name;","Genus_Localised":"Bacterium","Species":"$Codex_Ent_Bacterial_01_Name;","Species_Localised":"Bacterium Cerbrus"}""");
        Assert.Equal(1, reducer.Current.GetCompletedBiologicalSignalsForBody(4));
        Assert.Equal(1, reducer.Current.GetRemainingBiologicalSignalsForBody(4));
    }

    [Fact]
    public void HistoryStoresDssGenusesAndOrganicMetadata()
    {
        string file = Path.Combine(Path.GetTempPath(), $"ed-overlay-history-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new ExplorationHistoryRepository(file);
            DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T12:00:00Z");
            repository.RecordVisit("Cmdr", 42, "Test", now);
            repository.RecordBody("Cmdr",42,"Test",4,"Test 4","Rocky body",now,scanned:true,mapped:true,biologicalSignals:2);
            repository.RecordBodyGenuses("Cmdr",42,"Test",4,"Test 4",new[]
            {
                (Key: "$Codex_Ent_Stratum_Genus_Name;", Name: "Stratum"),
                (Key: "$Codex_Ent_Bacterial_Genus_Name;", Name: "Bacterium")
            },now);
            repository.RecordOrganic("Cmdr",42,"Test",4,"Test 4",
                "$Codex_Ent_Stratum_Tectonicas_Name;","Stratum Tectonicas",true,now,
                genusKey:"$Codex_Ent_Stratum_Genus_Name;",genusName:"Stratum",
                variantKey:"$Codex_Ent_Stratum_Tectonicas_Green_Name;",variantName:"Stratum Tectonicas - Green");

            ExplorationHistoryBodySnapshot body = Assert.Single(repository.LoadSystem("Cmdr",42,"Test").Bodies);
            Assert.Equal(2, body.Genuses.Count);
            Assert.Equal(1, body.CompletedOrganics);
            ExplorationHistoryOrganicSnapshot organic = Assert.Single(body.Organics);
            Assert.Equal("Stratum", organic.GenusName);
            Assert.Equal("Stratum Tectonicas", organic.SpeciesName);
            Assert.Equal("Stratum Tectonicas - Green", organic.VariantName);
            Assert.True(organic.Completed);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void BuilderReportsExactMissingGenusForCurrentData()
    {
        var body = new ExplorationBodySnapshot(
            4,"Test 4","Rocky body",800,false,false,true,true,2,
            new[] { "Stratum", "Bacterium" },ExplorationInterest.None) { IsScanned = true };
        var organic = new OrganicScanProgressSnapshot(
            "Cmdr",42,"Test",4,"Test 4","Stratum","Stratum Tectonicas","Green",
            3,true,500,10,20,DateTimeOffset.Parse("2026-08-22T12:00:00Z"));
        var state = new GameStateSnapshot
        {
            Commander="Cmdr", StarSystem="Test", SystemAddress=42,
            ExplorationBodies=new[] { body }, OrganicProgress=new[] { organic }
        };

        BodyExplorationProgress progress = BodyExplorationProgressBuilder.Build(
            state, ExplorationSystemHistorySnapshot.Empty, 4);
        Assert.True(progress.FssScanned);
        Assert.True(progress.DssMapped);
        Assert.True(progress.DssEfficient);
        Assert.Equal(2, progress.BiologicalSignals);
        Assert.Equal(1, progress.CompletedBiologicalSignals);
        Assert.Equal(1, progress.RemainingBiologicalSignals);
        Assert.Equal(new[] { "Bacterium" }, progress.MissingGenuses);
        Assert.False(progress.HistoricalBiologyDetailIncomplete);
    }
}