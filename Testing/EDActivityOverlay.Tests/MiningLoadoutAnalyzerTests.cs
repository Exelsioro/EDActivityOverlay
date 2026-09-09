using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Mining;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class MiningLoadoutAnalyzerTests
{
    [Fact]
    public void LaserFullKitRequiresAProspectorAndSupportTools()
    {
        MiningLoadoutSnapshot loadout =
            MiningLoadoutAnalyzer.Analyze(
                "type9",
                true,
                [
                    M(
                        "Hardpoint1",
                        "$Hpt_MiningLaser_Fixed_Medium_Name;"),
                    M(
                        "Optional1",
                        "Int_Refinery_Size4_Class5"),
                    M(
                        "Optional2",
                        "Int_DroneControl_Prospector_Size3_Class5"),
                    M(
                        "Optional3",
                        "Int_DroneControl_Collection_Size5_Class5"),
                    M(
                        "Optional4",
                        "Int_DetailedSurfaceScanner_Tiny")
                ]);

        Assert.Equal(
            MiningReadinessLevel.FullKit,
            loadout.Laser.Level);
        Assert.True(loadout.HasAProspector);
        Assert.Equal(
            "A",
            loadout.BestProspectorRating);
        Assert.Equal(
            MiningReadinessLevel.MissingRequired,
            loadout.Core.Level);
    }

    [Fact]
    public void CoreFullKitRecognizesPulseWaveSeismicAndAbrasion()
    {
        MiningLoadoutSnapshot loadout =
            MiningLoadoutAnalyzer.Analyze(
                "python",
                true,
                FullSupport()
                    .Concat(
                    [
                        M(
                            "Utility1",
                            "Hpt_mrascanner_Size0_Class5"),
                        M(
                            "Hardpoint1",
                            "Hpt_Mining_SeismChrgWarhd_Fixed_Medium"),
                        M(
                            "Hardpoint2",
                            "Hpt_Mining_AbrBlstr_Fixed_Small")
                    ]));

        Assert.Equal(
            MiningReadinessLevel.FullKit,
            loadout.Core.Level);
        Assert.True(
            loadout.HasPulseWaveAnalyzer);
    }

    [Fact]
    public void SubsurfaceAndSurfaceModesUseTheirActualTools()
    {
        MiningLoadoutSnapshot loadout =
            MiningLoadoutAnalyzer.Analyze(
                "python",
                true,
                FullSupport()
                    .Concat(
                    [
                        M(
                            "Utility1",
                            "Hpt_mrascanner_Size0_Class5"),
                        M(
                            "Hardpoint1",
                            "Hpt_Mining_SubSurfDispMisle_Fixed_Medium"),
                        M(
                            "Hardpoint2",
                            "Hpt_Mining_AbrBlstr_Turret_Small")
                    ]));

        Assert.Equal(
            MiningReadinessLevel.FullKit,
            loadout.Subsurface.Level);
        Assert.Equal(
            MiningReadinessLevel.FullKit,
            loadout.Surface.Level);
    }

    [Fact]
    public void MiningMultiControllerCountsAsProspectorAndCollectorButRatingStillMatters()
    {
        MiningLoadoutSnapshot cRated =
            MiningLoadoutAnalyzer.Analyze(
                "python",
                true,
                [
                    M(
                        "Hardpoint1",
                        "Hpt_MiningToolV2_Fixed_Large"),
                    M(
                        "Optional1",
                        "Int_Refinery_Size4_Class5"),
                    M(
                        "Optional2",
                        "Int_MultiDroneControl_Mining_Size3_Class3"),
                    M(
                        "Optional3",
                        "Int_DetailedSurfaceScanner_Tiny")
                ]);

        Assert.True(cRated.HasProspector);
        Assert.True(cRated.HasCollector);
        Assert.Equal("C", cRated.BestProspectorRating);
        Assert.Equal(
            MiningReadinessLevel.Usable,
            cRated.Laser.Level);
        Assert.Contains(
            MiningLoadoutAdvisory.ProspectorBelowA,
            cRated.Laser.Advisories);

        MiningLoadoutSnapshot aRated =
            MiningLoadoutAnalyzer.Analyze(
                "python",
                true,
                [
                    M(
                        "Hardpoint1",
                        "Hpt_MiningToolV2_Fixed_Large"),
                    M(
                        "Optional1",
                        "Int_Refinery_Size4_Class5"),
                    M(
                        "Optional2",
                        "Int_MultiDroneControl_MiningV2_Size5_Class5"),
                    M(
                        "Optional3",
                        "Int_DetailedSurfaceScanner_Tiny")
                ]);

        Assert.Equal(
            MiningReadinessLevel.FullKit,
            aRated.Laser.Level);
    }

    [Fact]
    public void GenericMultiLimpetAndXenoScannerAreNotMiningFalsePositives()
    {
        MiningLoadoutSnapshot loadout =
            MiningLoadoutAnalyzer.Analyze(
                "python",
                true,
                [
                    M(
                        "Optional1",
                        "Int_MultiDroneControl_Operations_Size3_Class5"),
                    M(
                        "Utility1",
                        "Hpt_XenoScanner_Advanced_Tiny")
                ]);

        Assert.False(loadout.HasProspector);
        Assert.False(loadout.HasCollector);
        Assert.False(
            loadout.HasPulseWaveAnalyzer);
        Assert.Empty(loadout.Modules);
    }

    [Fact]
    public void DisabledRequiredModuleDoesNotMakeModeReady()
    {
        MiningLoadoutSnapshot loadout =
            MiningLoadoutAnalyzer.Analyze(
                "type9",
                true,
                FullSupport()
                    .Concat(
                    [
                        M(
                            "Hardpoint1",
                            "Hpt_MiningLaser_Fixed_Medium",
                            false)
                    ]));

        Assert.Equal(
            MiningReadinessLevel.MissingRequired,
            loadout.Laser.Level);
        Assert.Contains(
            MiningModuleKind.MiningLaser,
            loadout.Laser.MissingRequired);
    }

    [Fact]
    public void ServiceTracksLoadoutAndIncrementalOutfittingEvents()
    {
        using var service =
            new MiningLoadoutService();

        service.OnJournalEvent(
            Event(
                "Loadout",
                """
                {
                  "Ship":"type9",
                  "Modules":[
                    {
                      "Slot":"Hardpoint1",
                      "Item":"Hpt_MiningLaser_Fixed_Medium",
                      "On":true
                    },
                    {
                      "Slot":"Optional1",
                      "Item":"Int_Refinery_Size4_Class5",
                      "On":true
                    },
                    {
                      "Slot":"Optional2",
                      "Item":"Int_DroneControl_Prospector_Size3_Class5",
                      "On":true
                    },
                    {
                      "Slot":"Optional3",
                      "Item":"Int_DroneControl_Collection_Size5_Class5",
                      "On":true
                    },
                    {
                      "Slot":"Optional4",
                      "Item":"Int_DetailedSurfaceScanner_Tiny",
                      "On":true
                    }
                  ]
                }
                """));

        Assert.Equal(
            MiningReadinessLevel.FullKit,
            service.Current.Laser.Level);

        service.OnJournalEvent(
            Event(
                "ModuleSell",
                """
                {
                  "Slot":"Optional1",
                  "SellItem":"Int_Refinery_Size4_Class5"
                }
                """));

        Assert.Equal(
            MiningReadinessLevel.MissingRequired,
            service.Current.Laser.Level);

        service.OnJournalEvent(
            Event(
                "ModuleBuy",
                """
                {
                  "Slot":"Optional1",
                  "BuyItem":"Int_Refinery_Size4_Class5"
                }
                """));

        Assert.Equal(
            MiningReadinessLevel.FullKit,
            service.Current.Laser.Level);

        service.OnJournalEvent(
            Event(
                "ShipyardSwap",
                """
                {
                  "Ship":"python"
                }
                """));

        Assert.False(service.Current.Available);
        Assert.Equal(
            "python",
            service.Current.Ship);
    }

    private static IEnumerable<MiningLoadoutModuleInput>
        FullSupport() =>
        [
            M(
                "Optional1",
                "Int_Refinery_Size4_Class5"),
            M(
                "Optional2",
                "Int_DroneControl_Prospector_Size3_Class5"),
            M(
                "Optional3",
                "Int_DroneControl_Collection_Size5_Class5"),
            M(
                "Optional4",
                "Int_DetailedSurfaceScanner_Tiny")
        ];

    private static MiningLoadoutModuleInput M(
        string slot,
        string item,
        bool enabled = true) =>
        new(
            slot,
            item,
            enabled);

    private static JournalEventReceivedEventArgs Event(
        string eventName,
        string json)
    {
        using JsonDocument document =
            JsonDocument.Parse(json);

        return new JournalEventReceivedEventArgs(
            eventName,
            DateTimeOffset.UtcNow,
            document.RootElement.Clone(),
            JournalEventOrigin.Live);
    }
}
