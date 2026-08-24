using EDActivityOverlay.Models;
using EDActivityOverlay.Services;

namespace EDActivityOverlay.Services.Engineering;

public sealed class MaterialAcquisitionAdvisor
{
    private static readonly HashSet<string> RawMaterials = Set(
        "carbon", "vanadium", "germanium", "cadmium", "niobium", "arsenic", "chromium",
        "molybdenum", "technetium", "iron", "zinc", "yttrium", "phosphorus", "manganese",
        "tungsten", "selenium", "ruthenium", "sulphur", "nickel", "zirconium", "tellurium", "polonium",
        "antimony", "boron", "lead", "mercury");

    private static readonly HashSet<string> EncodedMaterials = Set(
        "atypicaldisruptedwakeechoes", "anomalousfsdtelemetry", "strangewakesolutions",
        "eccentrichyperspacetrajectories", "dataminedwakeexceptions", "distortedshieldcyclerecordings",
        "inconsistentshieldsoakanalysis", "untypicalshieldscans", "aberrantshieldpatternanalysis",
        "peculiarshieldfrequencydata", "specialisedlegacyfirmware", "modifiedconsumerfirmware",
        "crackedindustrialfirmware", "securityfirmwarepatch", "modifiedembeddedfirmware",
        "scrambledemissiondata", "unexpectedemissiondata", "decodedemissiondata",
        "abnormalcompactemissionsdata", "irregularemissiondata", "exceptionalscrambledemissiondata",
        "classifiedscandatabanks", "divergentscandata", "classifiedscandatabanks");

    private static Dictionary<string, AcquisitionOption[]> BuildSpecific() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["pharmaceuticalisolators"] = Hge(
            Loc.Get("Loc_Advice_Search_systems_where_the_controlling_faction_is_in_Outbreak")),
        ["imperialshielding"] = Hge(
            Loc.Get("Loc_Advice_Search_Imperial_systems_surplus_grade_5_material_can_be_traded")),
        ["coredynamicscomposites"] = Hge(
            Loc.Get("Loc_Advice_Search_Federal_systems")),
        ["proprietarycomposites"] = Hge(
            Loc.Get("Loc_Advice_Search_suitable_Independent_or_Alliance_faction_systems")),
        ["improvisedcomponents"] = Hge(
            Loc.Get("Loc_Advice_Search_systems_in_Civil_Unrest")),
        ["militarygradealloys"] = Hge(
            Loc.Get("Loc_Advice_Search_systems_in_War_or_Civil_War")),
        ["militarysupercapacitors"] = Hge(
            Loc.Get("Loc_Advice_Search_systems_in_War_or_Civil_War")),
        ["protoheatradiators"] = Hge(
            Loc.Get("Loc_Advice_Search_systems_in_Boom")),
        ["protoradiolicalloys"] = Hge(
            Loc.Get("Loc_Advice_Search_systems_in_Boom")),
        ["protolightalloys"] = Hge(
            Loc.Get("Loc_Advice_Search_systems_in_Boom")),
        ["biotechconductors"] = MissionReward(
            Loc.Get("Loc_Advice_Choose_missions_rewarding_Biotech_Conductors_or_trade_surplus_manufactured_")),
        ["exquisitefocuscrystals"] = MissionReward(
            Loc.Get("Loc_Advice_Look_for_rewards_from_allied_factions_passenger_and_delivery_missions_often")),
        ["modifiedembeddedfirmware"] = MissionReward(
            Loc.Get("Loc_Advice_Obtain_it_as_a_mission_reward_or_trade_for_it_at_an_encoded_material_trader")),
        ["dataminedwakeexceptions"] =
        [
            new AcquisitionOption(Loc.Get("Loc_Advice_High_energy_wake_scanning"), Loc.Get("Loc_Advice_Fit_a_wake_scanner_and_scan_FSD_wakes_near_busy_stations_or_distribution_ce"), Priority: 10),
            Site(Loc.Get("Loc_Advice_Data_collection"), Loc.Get("Loc_Advice_Scan_the_four_beacons_at_Jameson_s_crash_site_relog_and_trade_the_collected"), "HIP 12099", "1 B — Jameson Crash Site", 15),
            new AcquisitionOption(Loc.Get("Loc_Advice_Encoded_material_trader"), Loc.Get("Loc_Advice_Trade_surplus_data_Collecting_high_grade_data_and_trading_it_is_usually_fas"), Priority: 20)
        ],
        ["selenium"] =
        [
            Site(Loc.Get("Loc_Advice_Selenium_from_brain_trees"), Loc.Get("Loc_Advice_Map_the_body_with_the_DSS_and_collect_material_at_brain_tree_sites_in_an_SR"), "HR 3230", "3 A A", 10),
            new AcquisitionOption(Loc.Get("Loc_Advice_Surface_search"), Loc.Get("Loc_Advice_Use_the_DSS_on_landable_bodies_containing_Selenium_then_visit_geological_or"), Priority: 20),
            Trader(Loc.Get("Loc_Advice_Raw_material_trader"), 30)
        ],
        ["tellurium"] = RawRare("Tellurium", "HIP 36601", "C 3 B"),
        ["polonium"] = RawRare("Polonium", "HIP 36601", "C 1 A"),
        ["yttrium"] = RawRare("Yttrium", "Outotz LS-K d8-3", "B 5 A"),
        ["technetium"] = RawRare("Technetium", "HIP 36601", "C 5 A"),
        ["ruthenium"] = RawRare("Ruthenium", "HIP 36601", "C 1 D"),
        ["antimony"] = RawRare("Antimony", "Outotz LS-K d8-3", "B 5 C")
    };

    public EngineeringMaterialCategory InferCategory(
        string materialId,
        IReadOnlyDictionary<string, MaterialInventoryEntry> inventory)
    {
        if (inventory.TryGetValue(materialId, out MaterialInventoryEntry? item))
        {
            return item.Category;
        }
        if (RawMaterials.Contains(materialId))
        {
            return EngineeringMaterialCategory.Raw;
        }
        if (EncodedMaterials.Contains(materialId))
        {
            return EngineeringMaterialCategory.Encoded;
        }
        return EngineeringMaterialCategory.Manufactured;
    }

    public MaterialAcquisitionAdvice Create(MaterialRequirement requirement)
    {
        IReadOnlyList<AcquisitionOption> options = BuildSpecific().TryGetValue(requirement.MaterialId, out AcquisitionOption[]? specific)
            ? specific
            : Generic(requirement.Category);
        return new MaterialAcquisitionAdvice(
            requirement.MaterialId,
            requirement.Name,
            requirement.Category,
            requirement.Missing,
            options);
    }

    private static IReadOnlyList<AcquisitionOption> Generic(EngineeringMaterialCategory category) => category switch
    {
        EngineeringMaterialCategory.Raw =>
        [
            Site(Loc.Get("Loc_Advice_Rare_raw_material_collection"), Loc.Get("Loc_Advice_Visit_bodies_with_crystalline_shards_and_trade_collected_grade_4_raw_materi"), "HIP 36601", "C 1 A / C 1 D / C 3 B / C 5 A", 5),
            new AcquisitionOption(Loc.Get("Loc_Advice_Surface_search"), Loc.Get("Loc_Advice_Map_a_landable_body_containing_the_material_and_collect_it_from_geological_"), Priority: 10),
            Trader(Loc.Get("Loc_Advice_Raw_material_trader"), 20)
        ],
        EngineeringMaterialCategory.Encoded =>
        [
            Site(Loc.Get("Loc_Advice_Data_collection"), Loc.Get("Loc_Advice_Scan_beacons_at_Jameson_s_crash_site_and_trade_the_high_grade_data"), "HIP 12099", "1 B — Jameson Crash Site", 5),
            new AcquisitionOption(Loc.Get("Loc_Advice_Ship_wake_and_data_point_scanning"), Loc.Get("Loc_Advice_Choose_the_scan_type_matching_the_data_group_Busy_stations_and_signal_sourc"), Priority: 10),
            Trader(Loc.Get("Loc_Advice_Encoded_material_trader"), 20)
        ],
        EngineeringMaterialCategory.Manufactured =>
        [
            new AcquisitionOption(Loc.Get("Loc_Advice_Signal_sources_and_ship_wreckage"), Loc.Get("Loc_Advice_Use_the_FSS_or_navigation_beacon_find_high_threat_signal_sources_and_collec"), Priority: 10),
            new AcquisitionOption(Loc.Get("Loc_Advice_Mission_rewards"), Loc.Get("Loc_Advice_Check_material_rewards_before_accepting_high_reputation_factions_offer_rare"), Priority: 20),
            Site(Loc.Get("Loc_Advice_Guaranteed_source"), Loc.Get("Loc_Advice_Collect_materials_at_Dav_s_Hope_and_trade_them_if_High_Grade_Emissions_are_"), "Hyades Sector DR-V c2-23", "A 5 — Dav's Hope", 25),
            Trader(Loc.Get("Loc_Advice_Manufactured_material_trader"), 30)
        ],
        EngineeringMaterialCategory.Item or EngineeringMaterialCategory.Component or EngineeringMaterialCategory.Data =>
        [
            new AcquisitionOption(Loc.Get("Loc_Advice_Odyssey_settlements"), Loc.Get("Loc_Advice_Find_settlements_whose_economy_and_building_type_match_the_item_Missions_ca"), Priority: 10),
            new AcquisitionOption(Loc.Get("Loc_Advice_Mission_rewards"), Loc.Get("Loc_Advice_Check_mission_rewards_for_the_required_Odyssey_resource_before_accepting"), Priority: 20),
            new AcquisitionOption(Loc.Get("Loc_Advice_Fleet_carrier_bartender"), Loc.Get("Loc_Advice_Buy_or_trade_the_resource_from_a_bartender_if_this_item_type_is_supported"), Priority: 30)
        ],
        _ =>
        [new AcquisitionOption(Loc.Get("Loc_Advice_Source_not_identified"), Loc.Get("Loc_Advice_The_Journal_recognized_this_item_but_the_local_knowledge_base_has_no_acquis"))]
    };

    private static AcquisitionOption[] Hge(string instructions) =>
    [
        new AcquisitionOption(Loc.Get("Loc_Advice_High_Grade_Emissions"), instructions + Loc.Get("Loc_Advice_After_entering_the_system_scan_the_navigation_beacon_or_use_the_FSS"), Priority: 10),
        Trader(Loc.Get("Loc_Advice_Manufactured_material_trader"), 20),
        Site(Loc.Get("Loc_Advice_Alternative_permanent_source"), Loc.Get("Loc_Advice_Dav_s_Hope_provides_a_repeatable_source_of_low_and_mid_grade_manufactured_m"), "Hyades Sector DR-V c2-23", "A 5 — Dav's Hope", 30)
    ];

    private static AcquisitionOption[] MissionReward(string instructions) =>
    [
        new AcquisitionOption(Loc.Get("Loc_Advice_Mission_rewards"), instructions, Priority: 10),
        Trader(Loc.Get("Loc_Advice_Material_trader"), 20)
    ];

    private static AcquisitionOption[] RawRare(string name, string system, string body) =>
    [
        Site(
            Loc.Get("Loc_Advice_Targeted_surface_collection"),
            Loc.Format("Loc_Advice_Targeted_surface_collection_instructions", EngineeringLocalization.MaterialName(name, name)),
            system,
            body,
            10),
        Trader(Loc.Get("Loc_Advice_Raw_material_trader"), 20)
    ];

    private static AcquisitionOption Site(
        string title,
        string instructions,
        string system,
        string location,
        int priority) =>
        new(title, instructions, Priority: priority, SystemName: system, LocationName: location);

    private static AcquisitionOption Trader(string title, int priority) =>
        new(title, Loc.Get("Loc_Advice_Trade_surplus_materials_from_the_same_category_Check_the_rate_before_tradin"), Priority: priority);

    private static HashSet<string> Set(params string[] items) => new(items, StringComparer.OrdinalIgnoreCase);
}
