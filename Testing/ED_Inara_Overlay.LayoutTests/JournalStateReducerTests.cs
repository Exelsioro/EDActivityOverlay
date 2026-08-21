using ED_Inara_Overlay.Models;
using ED_Inara_Overlay.Services.Journal;
using ED_Inara_Overlay.Services.Exploration;
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class JournalStateReducerTests
{
    [Fact]
    public void ReadsGalaxyMapFocusFromStatus()
    {
        var reducer = new JournalStateReducer();

        reducer.ApplyStatusJson("""{"timestamp":"2026-08-21T12:00:00Z","event":"Status","Flags":0,"Flags2":0,"GuiFocus":6}""");

        Assert.Equal(6, reducer.Current.GuiFocus);
    }

    [Fact]
    public void TracksFuelAndNavRouteCoordinates()
    {
        var reducer = new JournalStateReducer();
        reducer.ApplyJournalLine("""{"event":"Loadout","FuelCapacity":{"Main":32,"Reserve":0.63},"MaxJumpRange":45.5}""");
        reducer.ApplyJournalLine("""{"event":"FSDJump","StarSystem":"A","StarPos":[1.5,-2,3],"FuelLevel":20,"FuelUsed":3,"JumpDist":30}""");
        reducer.ApplyNavRouteJson("""{"Route":[{"StarSystem":"A","StarClass":"L","StarPos":[0,0,0]},{"StarSystem":"B","StarClass":"K","StarPos":[30,0,0]}]}""");

        GameStateSnapshot state = reducer.Current;
        Assert.Equal(32, state.FuelCapacityMain);
        Assert.Equal(20, state.FuelMain);
        Assert.Equal(0.1, state.FuelPerLightYearEstimate, 4);
        Assert.Equal(1.5, state.SystemX);
        Assert.Equal(-2, state.SystemY);
        Assert.Equal(3, state.SystemZ);
        Assert.Equal(30, state.NavRoute[0].DistanceTo(state.NavRoute[1]));
    }
    [Fact]
    public void JournalEventsBuildCurrentTradingState()
    {
        var reducer = new JournalStateReducer();

        reducer.ApplyJournalLine("""{"timestamp":"2026-08-19T10:00:00Z","event":"LoadGame","Commander":"Test Commander","Ship":"type9"}""");
        reducer.ApplyJournalLine("""{"timestamp":"2026-08-19T10:00:01Z","event":"Loadout","Ship":"type9","ShipName":"HAULER","CargoCapacity":720}""");
        reducer.ApplyJournalLine("""{"timestamp":"2026-08-19T10:00:02Z","event":"Location","StarSystem":"Sol","Docked":true,"StationName":"Abraham Lincoln","MarketID":128666762}""");
        reducer.ApplyCargoJson("""{"timestamp":"2026-08-19T10:00:03Z","event":"Cargo","Inventory":[{"Name":"gold","Name_Localised":"Gold","Count":36}]}""");

        var state = reducer.Current;
        Assert.Equal("Test Commander", state.Commander);
        Assert.Equal("Sol", state.StarSystem);
        Assert.Equal("Abraham Lincoln", state.Station);
        Assert.True(state.Docked);
        Assert.Equal(720, state.CargoCapacity);
        Assert.Equal(36, state.CargoUsed);
        Assert.Equal(684, state.FreeCargo);
        Assert.Equal(36, state.Cargo["Gold"]);
    }

    [Fact]
    public void StatusFlagsAndMarketSnapshotAreReduced()
    {
        var reducer = new JournalStateReducer();

        reducer.ApplyStatusJson("""{"timestamp":"2026-08-19T10:00:00Z","event":"Status","Flags":4718593,"Cargo":12.0,"Balance":123456,"LegalState":"Clean","Destination":{"Name":"Achenar"}}""");
        reducer.ApplyMarketJson("""{"timestamp":"2026-08-19T10:00:01Z","event":"Market","StarSystem":"Sol","StationName":"Galileo","MarketID":42,"Items":[{"Name":"gold","Name_Localised":"Gold","BuyPrice":42000,"SellPrice":41000,"Stock":900,"Demand":120}]}""");

        var state = reducer.Current;
        Assert.True(state.Docked);
        Assert.True(state.IsInDanger);
        Assert.Equal(12, state.CargoUsed);
        Assert.Equal(123456, state.Balance);
        Assert.Equal("Achenar", state.Destination);
        Assert.Equal(42000, state.Market["Gold"].BuyPrice);
        Assert.Equal("Sol", state.MarketSystem);
        Assert.Equal("Galileo", state.MarketStation);
        Assert.Empty(state.StarSystem);
    }

    [Fact]
    public void StatusExposesShipSystemsUsedByX52Lighting()
    {
        var reducer = new JournalStateReducer();
        ulong flags = (1UL << 2) | (1UL << 3) | (1UL << 6) | (1UL << 8)
                      | (1UL << 9) | (1UL << 10) | (1UL << 11) | (1UL << 16)
                      | (1UL << 17) | (1UL << 18) | (1UL << 20) | (1UL << 28);

        reducer.ApplyStatusJson($$"""{"timestamp":"2026-08-21T12:00:00Z","event":"Status","Flags":{{flags}},"Flags2":0}""");

        GameStateSnapshot state = reducer.Current;
        Assert.True(state.LandingGearDown);
        Assert.True(state.ShieldsUp);
        Assert.True(state.HardpointsDeployed);
        Assert.True(state.LightsOn);
        Assert.True(state.CargoScoopDeployed);
        Assert.True(state.SilentRunning);
        Assert.True(state.FuelScooping);
        Assert.True(state.FsdMassLocked);
        Assert.True(state.FsdCharging);
        Assert.True(state.FsdCooldown);
        Assert.True(state.OverHeating);
        Assert.True(state.NightVision);
    }

    [Fact]
    public void StatusTracksSurfacePositionAndOdysseyState()
    {
        var reducer = new JournalStateReducer();

        reducer.ApplyStatusJson("""{"timestamp":"2026-08-19T10:00:00Z","event":"Status","Flags":69206018,"Flags2":17,"BodyName":"Test A 1","Latitude":12.5,"Longitude":-42.25,"Heading":90,"Altitude":125.5,"PlanetRadius":1000000,"Gravity":0.32,"Temperature":210}""");

        GameStateSnapshot state = reducer.Current;
        Assert.True(state.Landed);
        Assert.True(state.InSrv);
        Assert.True(state.OnFoot);
        Assert.True(state.OnFootOnPlanet);
        Assert.True(state.HasSurfacePosition);
        Assert.Equal(12.5, state.Latitude);
        Assert.Equal(-42.25, state.Longitude);
        Assert.Equal(90, state.HeadingDegrees);
        Assert.Equal("Test A 1", state.CurrentBody);
    }

    [Fact]
    public void MarketTransactionsUpdateCargoWithoutGoingNegative()
    {
        var reducer = new JournalStateReducer();

        reducer.ApplyJournalLine("""{"timestamp":"2026-08-19T10:00:00Z","event":"MarketBuy","Type":"gold","Type_Localised":"Gold","Count":20,"BuyPrice":42000}""");
        reducer.ApplyJournalLine("""{"timestamp":"2026-08-19T10:00:01Z","event":"MarketSell","Type":"gold","Type_Localised":"Gold","Count":7,"SellPrice":45000}""");

        Assert.Equal(13, reducer.Current.Cargo["Gold"]);

        reducer.ApplyJournalLine("""{"timestamp":"2026-08-19T10:00:02Z","event":"MarketSell","Type":"gold","Type_Localised":"Gold","Count":99,"SellPrice":45000}""");
        Assert.False(reducer.Current.Cargo.ContainsKey("Gold"));
        Assert.Equal(0, reducer.Current.CargoUsed);
    }

    [Fact]
    public void ExplorationAndExobiologyShareOneSystemProgress()
    {
        var reducer = new JournalStateReducer();
        reducer.ApplyJournalLine("""{"event":"FSDJump","StarSystem":"Test Sector"}""");
        reducer.ApplyJournalLine("""{"event":"FSSDiscoveryScan","BodyCount":8}""");
        reducer.ApplyJournalLine("""{"event":"Scan","BodyID":2}""");
        reducer.ApplyJournalLine("""{"event":"Scan","BodyID":2}""");
        reducer.ApplyJournalLine("""{"event":"SAAScanComplete","BodyID":2}""");
        reducer.ApplyJournalLine("""{"event":"SAASignalsFound","Signals":[{"Type":"$SAA_SignalType_Biological;","Count":4}]}""");
        reducer.ApplyJournalLine("""{"event":"ScanOrganic","ScanType":"Log","Species":"$Codex_Ent_Bacterial_Genus_Name;","Species_Localised":"Bacterium"}""");

        var state = reducer.Current;
        Assert.Equal(8, state.SystemBodyCount);
        Assert.Equal(1, state.ScannedBodies);
        Assert.Equal(1, state.MappedBodies);
        Assert.Equal(4, state.BiologicalSignals);
        Assert.Equal("Bacterium", state.LastOrganicSpecies);
        Assert.Equal(1, state.OrganicSampleStage);
    }

    [Fact]
    public void MiningEventsBuildProspectorAndSessionState()
    {
        var reducer = new JournalStateReducer();
        reducer.ApplyJournalLine("""{"event":"ProspectedAsteroid","Materials":[{"Name":"platinum","Name_Localised":"Platinum","Proportion":22.5},{"Name":"painite","Name_Localised":"Painite","Proportion":8.1}],"Content":"High","Remaining":87.5,"MotherlodeMaterial":"alexandrite","MotherlodeMaterial_Localised":"Alexandrite"}""");
        reducer.ApplyJournalLine("""{"event":"MiningRefined","Type":"platinum","Type_Localised":"Platinum"}""");
        reducer.ApplyJournalLine("""{"event":"MiningRefined","Type":"platinum","Type_Localised":"Platinum"}""");
        reducer.ApplyJournalLine("""{"event":"AsteroidCracked"}""");

        var state = reducer.Current;
        Assert.NotNull(state.LastProspectedAsteroid);
        Assert.Equal("Alexandrite", state.LastProspectedAsteroid!.MotherlodeMaterial);
        Assert.Equal("Platinum", state.LastProspectedAsteroid.Materials[0].Name);
        Assert.Equal(2, state.RefinedMiningUnits);
        Assert.Equal(2, state.RefinedMiningCargo["Platinum"]);
        Assert.Equal(1, state.CrackedAsteroids);
    }

    [Fact]
    public void ExplorationTracksNotableBodiesMappingBiologyAndCodex()
    {
        var reducer = new JournalStateReducer();
        reducer.ApplyJournalLine("""{"event":"FSDJump","StarSystem":"Prua Test"}""");
        reducer.ApplyJournalLine("""{"event":"FSSDiscoveryScan","Progress":1.0,"BodyCount":12,"NonBodyCount":3}""");
        reducer.ApplyJournalLine("""{"event":"Scan","BodyID":4,"BodyName":"Prua Test 4","PlanetClass":"Water world","TerraformState":"Terraformable","MassEM":1.25,"DistanceFromArrivalLS":812.5,"WasDiscovered":false,"WasMapped":false}""");
        reducer.ApplyJournalLine("""{"event":"SAAScanComplete","BodyID":4,"BodyName":"Prua Test 4","ProbesUsed":5,"EfficiencyTarget":6}""");
        reducer.ApplyJournalLine("""{"event":"SAASignalsFound","BodyID":4,"BodyName":"Prua Test 4","Signals":[{"Type":"$SAA_SignalType_Biological;","Count":2}],"Genuses":[{"Genus":"$Codex_Ent_Bacterial_Genus_Name;","Genus_Localised":"Bacterium"},{"Genus":"$Codex_Ent_Stratum_Genus_Name;","Genus_Localised":"Stratum"}]}""");
        reducer.ApplyJournalLine("""{"event":"ScanOrganic","ScanType":"Log","Body":4,"Genus_Localised":"Stratum","Species_Localised":"Stratum Tectonicas","Variant_Localised":"Stratum Tectonicas - Green"}""");
        reducer.ApplyJournalLine("""{"event":"ScanOrganic","ScanType":"Sample","Body":4,"Genus_Localised":"Stratum","Species_Localised":"Stratum Tectonicas","Variant_Localised":"Stratum Tectonicas - Green"}""");
        reducer.ApplyJournalLine("""{"event":"ScanOrganic","ScanType":"Analyse","Body":4,"Genus_Localised":"Stratum","Species_Localised":"Stratum Tectonicas","Variant_Localised":"Stratum Tectonicas - Green"}""");
        reducer.ApplyJournalLine("""{"event":"CodexEntry","EntryID":123,"IsNewEntry":true,"Name_Localised":"Stratum Tectonicas"}""");

        var state = reducer.Current;
        Assert.Equal(1, state.FssProgress);
        Assert.Equal(3, state.NonBodySignals);
        Assert.Equal(1, state.MappedBodies);
        Assert.Equal(1, state.EfficientMappings);
        Assert.Equal(2, state.BiologicalSignals);
        Assert.Equal(1, state.BiologicalBodies);
        Assert.Equal(3, state.OrganicSampleStage);
        Assert.Equal(1, state.CompletedOrganicSamples);
        Assert.Equal(1, state.NewCodexEntries);

        ExplorationBodySnapshot body = Assert.Single(state.ExplorationBodies);
        Assert.Equal("Prua Test 4", body.Name);
        Assert.Equal(ExplorationInterest.WaterWorld, body.Interest);
        Assert.True(body.IsMapped);
        Assert.True(body.MappingEfficient);
        Assert.Equal(5, body.LastProbesUsed);
        Assert.Equal(6, body.EfficiencyTarget);
        Assert.Equal("Planet", body.BodyType);
        Assert.Equal("Water world", body.BodyClass);
        Assert.True(body.Terraformable);
        Assert.Equal(1.25, body.EarthMasses);
        Assert.True(body.EstimatedScanValue > 250_000);
        Assert.True(body.EstimatedEfficientMappingValue > body.EstimatedMappingValue);
        Assert.Equal(2, body.BiologicalSignals);
        Assert.Equal(["Bacterium", "Stratum"], body.Genuses);
        Assert.Equal(2, body.BiologyEstimates.Count);
        Assert.Equal(1_000_000 + 1_362_000, body.MinimumBiologyValue);
        Assert.Equal(8_418_000 + 19_010_800, body.MaximumBiologyValue);
    }

    [Fact]
    public void OrganicProgressUsesSpeciesSpecificStagesAndColonyRange()
    {
        var reducer = new JournalStateReducer();
        reducer.ApplyJournalLine("""{"event":"FSDJump","StarSystem":"Prua Test","SystemAddress":12345}""");
        reducer.ApplyStatusJson("""{"event":"Status","Flags":2097152,"BodyName":"Prua Test 4","Latitude":10,"Longitude":20,"PlanetRadius":1000000}""");
        reducer.ApplyJournalLine("""{"event":"ScanOrganic","ScanType":"Log","Body":4,"Genus":"$Codex_Ent_Stratum_Genus_Name;","Genus_Localised":"Stratum","Species":"$Codex_Ent_Stratum_Tectonicas_Name;","Species_Localised":"Stratum Tectonicas"}""");
        reducer.ApplyJournalLine("""{"event":"ScanOrganic","ScanType":"Sample","Body":4,"Genus":"$Codex_Ent_Stratum_Genus_Name;","Genus_Localised":"Stratum","Species":"$Codex_Ent_Stratum_Tectonicas_Name;","Species_Localised":"Stratum Tectonicas"}""");

        OrganicScanProgressSnapshot progress = Assert.Single(reducer.Current.OrganicProgress);
        Assert.Equal(2, progress.Stage);
        Assert.False(progress.Completed);
        Assert.Equal(500, progress.ColonyRangeMeters);
        Assert.Equal(10, progress.LastSampleLatitude);
        Assert.Equal(20, progress.LastSampleLongitude);
    }

    [Fact]
    public void OrganicProgressSurvivesReducerRestart()
    {
        string file = Path.Combine(Path.GetTempPath(), $"ed-overlay-exobio-{Guid.NewGuid():N}.json");
        try
        {
            var first = new JournalStateReducer(new ExplorationProgressStore(file));
            first.ApplyJournalLine("""{"event":"LoadGame","Commander":"Test Commander"}""");
            first.ApplyJournalLine("""{"event":"FSDJump","StarSystem":"Prua Test","SystemAddress":12345}""");
            first.ApplyJournalLine("""{"event":"ScanOrganic","ScanType":"Log","Body":4,"Genus_Localised":"Stratum","Species":"stratumtectonicas","Species_Localised":"Stratum Tectonicas"}""");

            var restarted = new JournalStateReducer(new ExplorationProgressStore(file));
            restarted.ApplyJournalLine("""{"event":"LoadGame","Commander":"Test Commander"}""");
            restarted.ApplyJournalLine("""{"event":"Location","StarSystem":"Prua Test","SystemAddress":12345}""");

            OrganicScanProgressSnapshot progress = Assert.Single(restarted.Current.OrganicProgress);
            Assert.Equal(1, progress.Stage);
            Assert.Equal("Stratum Tectonicas", progress.Species);
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
            if (File.Exists(file + ".tmp")) File.Delete(file + ".tmp");
        }
    }
}
