using System.Collections.ObjectModel;
using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Exploration;

namespace EDActivityOverlay.Services.Journal;

internal sealed class JournalStateReducer
{
    private readonly object sync = new();
    private readonly Dictionary<string, int> cargo = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MarketItemSnapshot> market = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<NavRouteStar> navRoute = new();
    private readonly HashSet<int> scannedBodies = new();
    private readonly HashSet<int> mappedBodies = new();
    private readonly Dictionary<int, int> biologicalSignalsByBody = new();
    private readonly Dictionary<int, ExplorationBodySnapshot> explorationBodies = new();
    private readonly Dictionary<string, OrganicScanProgressSnapshot> organicProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> refinedMiningCargo = new(StringComparer.OrdinalIgnoreCase);
    private readonly ExplorationProgressStore? explorationProgressStore;
    private GameStateSnapshot state = GameStateSnapshot.Empty;
    private int stateBatchDepth;
    private bool stateChangePending;
    private JournalEventOrigin stateBatchOrigin = JournalEventOrigin.Live;

    public JournalStateReducer(ExplorationProgressStore? explorationProgressStore = null)
    {
        this.explorationProgressStore = explorationProgressStore;
        foreach (OrganicScanProgressSnapshot item in explorationProgressStore?.Load()
                 ?? Array.Empty<OrganicScanProgressSnapshot>())
        {
            organicProgress[GetOrganicProgressKey(item)] = item;
        }
    }

    public event EventHandler<GameStateChangedEventArgs>? StateChanged;
    public event EventHandler<JournalEventReceivedEventArgs>? JournalEventReceived;

    public GameStateSnapshot Current
    {
        get
        {
            lock (sync)
            {
                return state;
            }
        }
    }

    internal void BeginStateBatch(
        JournalEventOrigin origin)
    {
        lock (sync)
        {
            stateBatchDepth++;
            if (stateBatchDepth == 1)
            {
                stateBatchOrigin = origin;
                stateChangePending = false;
            }
        }
    }

    internal void EndStateBatch()
    {
        GameStateSnapshot? snapshot = null;
        JournalEventOrigin origin = JournalEventOrigin.Live;

        lock (sync)
        {
            if (stateBatchDepth <= 0)
            {
                throw new InvalidOperationException(
                    "Journal state batch is not active.");
            }

            stateBatchDepth--;

            if (stateBatchDepth == 0
                && stateChangePending)
            {
                snapshot = state;
                origin = stateBatchOrigin;
                stateChangePending = false;
                stateBatchOrigin = JournalEventOrigin.Live;
            }
        }

        if (snapshot is not null)
        {
            StateChanged?.Invoke(
                this,
                new GameStateChangedEventArgs(
                    snapshot,
                    origin));
        }
    }

    public void SetJournalAvailability(string directory, bool available)
    {
        GameStateSnapshot snapshot = Current;
        if (snapshot.JournalAvailable == available
            && string.Equals(snapshot.JournalDirectory, directory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        Update(current => current with
        {
            JournalAvailable = available,
            JournalDirectory = directory
        });
    }

    public void ApplyJournalLine(
        string line,
        JournalEventOrigin origin = JournalEventOrigin.Live)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        string eventName = GetString(root, "event");
        DateTimeOffset timestamp = GetTimestamp(root);

        lock (sync)
        {
            state = ReduceJournalEvent(state, eventName, timestamp, root);
            state = CopyCollections(state);
        }

        JournalEventReceived?.Invoke(
            this,
            new JournalEventReceivedEventArgs(
                eventName,
                timestamp,
                root.Clone(),
                origin));

        RaiseStateChanged(
            origin);
    }

    public void ApplyStatusJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        ulong flags = TryGetUInt64(root, "Flags");
        ulong flags2 = TryGetUInt64(root, "Flags2");
        bool hasSurfacePosition = HasFlag(flags, 21)
                                  && root.TryGetProperty("Latitude", out _)
                                  && root.TryGetProperty("Longitude", out _);
        int cargoUsed = TryGetInt32(root, "Cargo", state.CargoUsed);
        long balance = TryGetInt64(root, "Balance", state.Balance);
        string destinationName = string.Empty;
        long destinationSystemAddress = 0;
        int destinationBodyId = -1;
        if (root.TryGetProperty("Destination", out JsonElement destinationElement)
            && destinationElement.ValueKind == JsonValueKind.Object)
        {
            destinationName = GetString(destinationElement, "Name");
            destinationSystemAddress = TryGetInt64(destinationElement, "System");
            destinationBodyId = TryGetInt32(destinationElement, "Body", -1);
        }
        double fuelMain = state.FuelMain;
        double fuelReservoir = state.FuelReservoir;
        if (root.TryGetProperty("Fuel", out JsonElement fuel) && fuel.ValueKind == JsonValueKind.Object)
        {
            fuelMain = TryGetDouble(fuel, "FuelMain", fuelMain);
            fuelReservoir = TryGetDouble(fuel, "FuelReservoir", fuelReservoir);
        }

        Update(current => current with
        {
            LastEventUtc = MaxTimestamp(current.LastEventUtc, GetTimestamp(root)),
            GuiFocus = TryGetInt32(root, "GuiFocus", current.GuiFocus),
            CargoUsed = cargoUsed,
            Balance = balance,
            Docked = HasFlag(flags, 0),
            LandingGearDown = HasFlag(flags, 2),
            ShieldsUp = HasFlag(flags, 3),
            InSupercruise = HasFlag(flags, 4),
            HardpointsDeployed = HasFlag(flags, 6),
            LightsOn = HasFlag(flags, 8),
            CargoScoopDeployed = HasFlag(flags, 9),
            SilentRunning = HasFlag(flags, 10),
            FuelScooping = HasFlag(flags, 11),
            FsdMassLocked = HasFlag(flags, 16),
            FsdCharging = HasFlag(flags, 17) || HasFlag(flags, 30),
            FsdCooldown = HasFlag(flags, 18),
            LowFuel = HasFlag(flags, 19),
            OverHeating = HasFlag(flags, 20),
            IsInDanger = HasFlag(flags, 22) || HasFlag(flags, 23),
            NightVision = HasFlag(flags, 28),
            Landed = HasFlag(flags, 1),
            InSrv = HasFlag(flags, 26),
            OnFoot = HasFlag(flags2, 0),
            OnFootOnPlanet = HasFlag(flags2, 4),
            GlideMode = HasFlag(flags2, 12),
            HasSurfacePosition = hasSurfacePosition,
            Latitude = hasSurfacePosition ? TryGetNullableDouble(root, "Latitude") : null,
            Longitude = hasSurfacePosition ? TryGetNullableDouble(root, "Longitude") : null,
            AltitudeMeters = TryGetNullableDouble(root, "Altitude"),
            HeadingDegrees = TryGetNullableDouble(root, "Heading"),
            PlanetRadiusMeters = TryGetNullableDouble(root, "PlanetRadius") ?? current.PlanetRadiusMeters,
            SurfaceGravityG = TryGetNullableDouble(root, "Gravity") ?? current.SurfaceGravityG,
            Oxygen = TryGetNullableDouble(root, "Oxygen"),
            Health = TryGetNullableDouble(root, "Health"),
            TemperatureKelvin = TryGetNullableDouble(root, "Temperature"),
            CurrentBody = GetString(root, "BodyName", current.CurrentBody),
            LegalState = GetString(root, "LegalState", current.LegalState),
            Destination = destinationName,
            DestinationName = destinationName,
            DestinationSystemAddress = destinationSystemAddress,
            DestinationBodyId = destinationBodyId,
            FuelMain = fuelMain,
            FuelReservoir = fuelReservoir
        });
    }

    public void ApplyCargoJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        lock (sync)
        {
            ReplaceCargo(root);
            state = state with
            {
                CargoUsed = cargo.Values.Sum(),
                LastEventUtc = MaxTimestamp(state.LastEventUtc, GetTimestamp(root))
            };
            state = CopyCollections(state);
        }
        RaiseStateChanged();
    }

    public void ApplyNavRouteJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        lock (sync)
        {
            navRoute.Clear();
            if (root.TryGetProperty("Route", out JsonElement route) && route.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in route.EnumerateArray())
                {
                    double?[] position = ReadStarPosition(item);
                    navRoute.Add(new NavRouteStar(
                        GetString(item, "StarSystem"),
                        GetString(item, "StarClass"),
                        position[0], position[1], position[2]));
                }
            }

            NavRouteStar? currentRouteStar =
                navRoute.FirstOrDefault(
                    item =>
                        item.System.Equals(
                            state.StarSystem,
                            StringComparison.OrdinalIgnoreCase));

            state = CopyCollections(state with
            {
                LastEventUtc =
                    MaxTimestamp(
                        state.LastEventUtc,
                        GetTimestamp(root)),
                CurrentStarClass =
                    !string.IsNullOrWhiteSpace(
                        currentRouteStar?.StarClass)
                        ? currentRouteStar.StarClass
                        : state.CurrentStarClass
            });
        }
        RaiseStateChanged();
    }
    public void ApplyMarketJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        lock (sync)
        {
            market.Clear();
            if (root.TryGetProperty("Items", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in items.EnumerateArray())
                {
                    string name = GetLocalizedName(item, "Name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    market[name] = new MarketItemSnapshot(
                        name,
                        TryGetInt32(item, "BuyPrice"),
                        TryGetInt32(item, "SellPrice"),
                        TryGetInt32(item, "Stock"),
                        TryGetInt32(item, "Demand"));
                }
            }

            DateTimeOffset timestamp = GetTimestamp(root);
            state = CopyCollections(state with
            {
                MarketSystem = GetString(root, "StarSystem"),
                MarketStation = GetString(root, "StationName"),
                MarketUpdatedUtc = timestamp,
                LastEventUtc = MaxTimestamp(state.LastEventUtc, timestamp)
            });
        }
        RaiseStateChanged();
    }

    private GameStateSnapshot ReduceJournalEvent(
        GameStateSnapshot current,
        string eventName,
        DateTimeOffset timestamp,
        JsonElement root)
    {
        current = current with { LastEventUtc = MaxTimestamp(current.LastEventUtc, timestamp) };
        switch (eventName.ToLowerInvariant())
        {
            case "loadgame":
                refinedMiningCargo.Clear();
                return current with
                {
                    Commander = GetString(root, "Commander", current.Commander),
                    Ship = GetString(root, "Ship", current.Ship),
                    ShipName = GetString(root, "ShipName", current.ShipName),
                    FuelMain = TryGetDouble(root, "FuelLevel", current.FuelMain),
                    FuelCapacityMain = TryGetDouble(root, "FuelCapacity", current.FuelCapacityMain),
                    CrackedAsteroids = 0,
                    LastProspectedAsteroid = null
                };
            case "loadout":
                double mainCapacity = current.FuelCapacityMain;
                double reserveCapacity = current.FuelCapacityReservoir;
                if (root.TryGetProperty("FuelCapacity", out JsonElement capacity)
                    && capacity.ValueKind == JsonValueKind.Object)
                {
                    mainCapacity = TryGetDouble(capacity, "Main", mainCapacity);
                    reserveCapacity = TryGetDouble(capacity, "Reserve", reserveCapacity);
                }
                return current with
                {
                    Ship = GetString(root, "Ship", current.Ship),
                    ShipName = GetString(root, "ShipName", current.ShipName),
                    CargoCapacity = TryGetInt32(root, "CargoCapacity", current.CargoCapacity),
                    UnladenMassTonnes = TryGetDouble(root, "UnladenMass", current.UnladenMassTonnes),
                    FuelCapacityMain = mainCapacity,
                    FuelCapacityReservoir = reserveCapacity,
                    MaxJumpRangeLy = TryGetDouble(root, "MaxJumpRange", current.MaxJumpRangeLy)
                };
            case "location":
            case "fsdjump":
            case "carrierjump":
                string currentStarSystem =
                    GetString(
                        root,
                        "StarSystem",
                        current.StarSystem);

                string currentStarClass =
                    GetString(
                        root,
                        "StarClass");

                if (string.IsNullOrWhiteSpace(
                        currentStarClass))
                {
                    currentStarClass =
                        navRoute.FirstOrDefault(
                            item =>
                                item.System.Equals(
                                    currentStarSystem,
                                    StringComparison.OrdinalIgnoreCase))
                            ?.StarClass
                        ?? string.Empty;
                }

                double jumpFuelUsed = eventName.Equals("FSDJump", StringComparison.OrdinalIgnoreCase)
                    ? TryGetDouble(root, "FuelUsed") : 0;
                double jumpDistance = eventName.Equals("FSDJump", StringComparison.OrdinalIgnoreCase)
                    ? TryGetDouble(root, "JumpDist") : 0;
                double measuredRate = jumpFuelUsed > 0 && jumpDistance > 0 ? jumpFuelUsed / jumpDistance : 0;
                double fuelRate = measuredRate <= 0 ? current.FuelPerLightYearEstimate
                    : current.FuelPerLightYearEstimate <= 0 ? measuredRate
                    : current.FuelPerLightYearEstimate * 0.7 + measuredRate * 0.3;
                bool docked = GetBoolean(root, "Docked", false);
                double?[] systemPosition = ReadStarPosition(root);
                scannedBodies.Clear();
                mappedBodies.Clear();
                biologicalSignalsByBody.Clear();
                explorationBodies.Clear();
                return current with
                {
                    StarSystem = currentStarSystem,
                    CurrentStarClass = currentStarClass,
                    SystemAddress = TryGetInt64(root, "SystemAddress", current.SystemAddress),
                    SystemX = systemPosition[0] ?? current.SystemX,
                    SystemY = systemPosition[1] ?? current.SystemY,
                    SystemZ = systemPosition[2] ?? current.SystemZ,
                    Station = docked ? GetString(root, "StationName", current.Station) : string.Empty,
                    MarketId = docked ? TryGetNullableInt64(root, "MarketID") : null,
                    Docked = docked,
                    CurrentBody = GetString(root, "Body", string.Empty),
                    HasSurfacePosition = false,
                    Latitude = null,
                    Longitude = null,
                    Destination = string.Empty,
                    DestinationName = string.Empty,
                    DestinationSystemAddress = 0,
                    DestinationBodyId = -1,
                    SystemBodyCount = 0,
                    FssProgress = 0,
                    NonBodySignals = 0,
                    ScannedBodies = 0,
                    MappedBodies = 0,
                    EfficientMappings = 0,
                    BiologicalSignals = 0,
                    BiologicalBodies = 0,
                    LastOrganicSpecies = string.Empty,
                    LastOrganicGenus = string.Empty,
                    LastOrganicVariant = string.Empty,
                    LastOrganicScanType = string.Empty,
                    LastOrganicBodyId = -1,
                    OrganicSampleStage = 0,
                    CompletedOrganicSamples = 0,
                    NewCodexEntries = 0,
                    FuelMain = TryGetDouble(root, "FuelLevel", current.FuelMain),
                    LastJumpFuelUsed = jumpFuelUsed > 0 ? jumpFuelUsed : current.LastJumpFuelUsed,
                    LastJumpDistanceLy = jumpDistance > 0 ? jumpDistance : current.LastJumpDistanceLy,
                    FuelPerLightYearEstimate = fuelRate
                };            case "docked":
                return current with
                {
                    StarSystem = GetString(root, "StarSystem", current.StarSystem),
                    Station = GetString(root, "StationName", current.Station),
                    MarketId = TryGetNullableInt64(root, "MarketID") ?? current.MarketId,
                    Docked = true
                };
            case "undocked":
                return current with { Station = string.Empty, Docked = false };
            case "fsdtarget":
                string fsdTargetName = GetString(root, "Name", current.Destination);
                return current with
                {
                    Destination = fsdTargetName,
                    DestinationName = fsdTargetName,
                    DestinationSystemAddress = TryGetInt64(root, "SystemAddress", current.DestinationSystemAddress),
                    DestinationBodyId = -1
                };
            case "navrouteclear":
                navRoute.Clear();
                return current with
                {
                    Destination = string.Empty,
                    DestinationName = string.Empty,
                    DestinationSystemAddress = 0,
                    DestinationBodyId = -1
                };
            case "fuelscoop":
                return current with { FuelMain = TryGetDouble(root, "Total", current.FuelMain) };
            case "refuelall":
                double refuelAmount = TryGetDouble(root, "Amount");
                return current with
                {
                    FuelMain = current.FuelCapacityMain > 0
                        ? Math.Min(current.FuelCapacityMain, current.FuelMain + refuelAmount)
                        : current.FuelMain + refuelAmount
                };
            case "reservoirreplenished":
                return current with
                {
                    FuelMain = TryGetDouble(root, "FuelMain", current.FuelMain),
                    FuelReservoir = TryGetDouble(root, "FuelReservoir", current.FuelReservoir)
                };
            case "cargo":
                ReplaceCargo(root);
                return current with { CargoUsed = cargo.Values.Sum() };
            case "marketbuy":
                ApplyCargoDelta(root, +1);
                return current with { CargoUsed = cargo.Values.Sum() };
            case "marketsell":
                ApplyCargoDelta(root, -1);
                return current with { CargoUsed = cargo.Values.Sum() };
            case "fssdiscoveryscan":
                return current with
                {
                    SystemBodyCount = TryGetInt32(root, "BodyCount", current.SystemBodyCount),
                    FssProgress = Math.Clamp(TryGetDouble(root, "Progress", current.FssProgress), 0, 1),
                    NonBodySignals = TryGetInt32(root, "NonBodyCount", current.NonBodySignals)
                };
            case "scan":
                AddBodyId(root, scannedBodies);
                UpsertScannedBody(root);
                return current with { ScannedBodies = scannedBodies.Count };
            case "saascancomplete":
                bool firstMapping = AddBodyId(root, mappedBodies);
                bool efficient = IsEfficientMapping(root);
                UpsertMappedBody(root, efficient);
                return current with
                {
                    MappedBodies = mappedBodies.Count,
                    EfficientMappings = current.EfficientMappings + (firstMapping && efficient ? 1 : 0)
                };
            case "fssbodysignals":
            case "saasignalsfound":
                int biologicalCount = ReadBiologicalSignalCount(root);
                int signalBodyId = TryGetInt32(root, "BodyID", -1);
                if (signalBodyId >= 0)
                {
                    biologicalSignalsByBody[signalBodyId] = biologicalCount;
                }
                UpsertBiologicalBody(root, biologicalCount);
                return current with
                {
                    BiologicalSignals = signalBodyId >= 0
                        ? biologicalSignalsByBody.Values.Sum()
                        : biologicalCount,
                    BiologicalBodies = biologicalSignalsByBody.Count(pair => pair.Value > 0)
                };
            case "scanorganic":
                string scanType = GetString(root, "ScanType");
                string species = GetLocalizedName(root, "Species");
                string genus = GetLocalizedName(root, "Genus");
                string variant = GetLocalizedName(root, "Variant");
                string genusIdentifier = GetString(root, "Genus");
                string speciesIdentifier = GetString(root, "Species", species);
                string variantIdentifier = GetString(root, "Variant");
                int organicBodyId = TryGetInt32(root, "Body", current.LastOrganicBodyId);
                string progressKey = GetOrganicProgressKey(
                    current.Commander, current.SystemAddress, current.StarSystem, organicBodyId, speciesIdentifier);
                organicProgress.TryGetValue(progressKey, out OrganicScanProgressSnapshot? previousProgress);

                if (previousProgress is null)
                {
                    KeyValuePair<string, OrganicScanProgressSnapshot> legacy =
                        organicProgress.FirstOrDefault(pair =>
                            pair.Value.BodyId == organicBodyId
                            && IsCurrentSystem(pair.Value, current)
                            && string.Equals(
                                pair.Value.Species,
                                species,
                                StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrWhiteSpace(legacy.Key))
                    {
                        previousProgress = legacy.Value;
                        organicProgress.Remove(legacy.Key);
                    }
                }

                int sampleStage = scanType.Equals("Analyse", StringComparison.OrdinalIgnoreCase)
                    ? 3
                    : scanType.Equals("Sample", StringComparison.OrdinalIgnoreCase)
                        ? Math.Clamp((previousProgress?.Stage ?? 1) + 1, 2, 3)
                        : 1;
                int colonyRange = ExobiologyCatalog.GetColonyRange(genusIdentifier, genus);
                string bodyName = explorationBodies.TryGetValue(organicBodyId, out ExplorationBodySnapshot? organicBody)
                    ? organicBody.Name
                    : current.CurrentBody;
                organicProgress[progressKey] = new OrganicScanProgressSnapshot(
                    current.Commander,
                    current.SystemAddress,
                    current.StarSystem,
                    organicBodyId,
                    bodyName,
                    genus,
                    species,
                    variant,
                    sampleStage,
                    scanType.Equals("Analyse", StringComparison.OrdinalIgnoreCase),
                    colonyRange,
                    current.Latitude,
                    current.Longitude,
                    timestamp)
                {
                    GenusKey = genusIdentifier,
                    SpeciesKey = speciesIdentifier,
                    VariantKey = variantIdentifier
                };
                explorationProgressStore?.Save(organicProgress.Values);
                return current with
                {
                    LastOrganicSpecies = species,
                    LastOrganicGenus = genus,
                    LastOrganicVariant = variant,
                    LastOrganicScanType = scanType,
                    LastOrganicBodyId = organicBodyId,
                    OrganicSampleStage = sampleStage,
                    CompletedOrganicSamples = organicProgress.Values.Count(item =>
                        IsCurrentSystem(item, current) && item.Completed)
                };
            case "codexentry":
                return current with
                {
                    NewCodexEntries = current.NewCodexEntries + (GetBoolean(root, "IsNewEntry", false) ? 1 : 0)
                };
            case "prospectedasteroid":
                return current with { LastProspectedAsteroid = ReadProspectedAsteroid(root) };
            case "asteroidcracked":
                return current with { CrackedAsteroids = current.CrackedAsteroids + 1 };
            case "miningrefined":
                string refinedType = GetLocalizedName(root, "Type");
                if (!string.IsNullOrWhiteSpace(refinedType))
                {
                    refinedMiningCargo.TryGetValue(refinedType, out int refinedCount);
                    refinedMiningCargo[refinedType] = refinedCount + 1;
                }
                return current;
            case "died":
                cargo.Clear();
                return current with { CargoUsed = 0, IsInDanger = false };
            default:
                return current;
        }
    }

    private void ReplaceCargo(JsonElement root)
    {
        cargo.Clear();
        if (!root.TryGetProperty("Inventory", out JsonElement inventory) || inventory.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement item in inventory.EnumerateArray())
        {
            string name = GetLocalizedName(item, "Name");
            int count = TryGetInt32(item, "Count");
            if (!string.IsNullOrWhiteSpace(name) && count > 0)
            {
                cargo[name] = count;
            }
        }
    }

    private void ApplyCargoDelta(JsonElement root, int direction)
    {
        string name = GetLocalizedName(root, "Type");
        int count = TryGetInt32(root, "Count");
        if (string.IsNullOrWhiteSpace(name) || count <= 0)
        {
            return;
        }

        cargo.TryGetValue(name, out int currentCount);
        int nextCount = Math.Max(0, currentCount + (count * direction));
        if (nextCount == 0)
        {
            cargo.Remove(name);
        }
        else
        {
            cargo[name] = nextCount;
        }
    }

    private void Update(Func<GameStateSnapshot, GameStateSnapshot> update)
    {
        lock (sync)
        {
            state = CopyCollections(update(state));
        }
        RaiseStateChanged();
    }

    private GameStateSnapshot CopyCollections(GameStateSnapshot current)
    {
        OrganicScanProgressSnapshot[] progress = GetCurrentOrganicProgress(current);
        return current with
        {
            Cargo = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(cargo, StringComparer.OrdinalIgnoreCase)),
            Market = new ReadOnlyDictionary<string, MarketItemSnapshot>(new Dictionary<string, MarketItemSnapshot>(market, StringComparer.OrdinalIgnoreCase)),
            NavRoute = navRoute.ToArray(),
            RefinedMiningCargo = new ReadOnlyDictionary<string, int>(
                new Dictionary<string, int>(refinedMiningCargo, StringComparer.OrdinalIgnoreCase)),
            ExplorationBodies = explorationBodies.Values.OrderBy(body => body.BodyId).ToArray(),
            OrganicProgress = progress,
            CompletedOrganicSamples = progress.Count(item => item.Completed)
        };
    }

    private static bool AddBodyId(JsonElement root, HashSet<int> bodies)
    {
        int id = TryGetInt32(root, "BodyID", -1);
        return id >= 0 && bodies.Add(id);
    }

    private static double?[] ReadStarPosition(JsonElement item)
    {
        var result = new double?[3];
        if (!item.TryGetProperty("StarPos", out JsonElement position)
            || position.ValueKind != JsonValueKind.Array) return result;
        int index = 0;
        foreach (JsonElement coordinate in position.EnumerateArray())
        {
            if (index >= result.Length) break;
            result[index++] = coordinate.TryGetDouble(out double value) ? value : null;
        }
        return result;
    }

    private void UpsertScannedBody(JsonElement root)
    {
        int id = TryGetInt32(root, "BodyID", -1);
        if (id < 0) return;
        ExplorationBodySnapshot previous = GetExplorationBody(id, root);
        string planetClass = GetLocalizedName(root, "PlanetClass");
        string rawPlanetClass = GetString(root, "PlanetClass");
        string starType = GetString(root, "StarType");
        string description = !string.IsNullOrWhiteSpace(planetClass) ? planetClass : starType;
        string bodyType = !string.IsNullOrWhiteSpace(starType) ? "Star" : "Planet";
        string bodyClass = !string.IsNullOrWhiteSpace(rawPlanetClass) ? rawPlanetClass : starType;
        string terraformState = GetString(root, "TerraformState");
        bool terraformable = terraformState.Contains("Terraform", StringComparison.OrdinalIgnoreCase);
        double earthMasses = TryGetDouble(root, "MassEM", previous.EarthMasses);
        double solarMasses = TryGetDouble(root, "StellarMass", previous.SolarMasses);
        bool wasDiscovered = GetBoolean(root, "WasDiscovered", previous.WasDiscovered);
        bool wasMapped = GetBoolean(root, "WasMapped", previous.WasMapped);
        ExplorationValueEstimate values = ExplorationValueCalculator.Estimate(
            bodyType, bodyClass, terraformable, earthMasses, solarMasses);
        ExplorationInterest interest = DetermineInterest(
            rawPlanetClass, terraformState, starType);
        explorationBodies[id] = previous with
        {
            IsScanned = true,
            Name = GetString(root, "BodyName", previous.Name),
            Description = string.IsNullOrWhiteSpace(description) ? previous.Description : description,
            DistanceFromArrivalLs = TryGetDouble(root, "DistanceFromArrivalLS", previous.DistanceFromArrivalLs),
            WasDiscovered = wasDiscovered,
            WasMapped = wasMapped,
            Interest = interest == ExplorationInterest.None ? previous.Interest : interest,
            Landable = GetBoolean(root, "Landable", previous.Landable),
            GravityG = TryGetDouble(root, "SurfaceGravity", previous.GravityG * 9.80665) / 9.80665,
            SurfaceTemperatureKelvin = TryGetDouble(root, "SurfaceTemperature", previous.SurfaceTemperatureKelvin),
            SurfacePressureAtmospheres = TryGetDouble(root, "SurfacePressure", previous.SurfacePressureAtmospheres * 101325) / 101325,
            RadiusMeters = TryGetDouble(root, "Radius", previous.RadiusMeters),
            Atmosphere = GetLocalizedNameOrFallback(root, "Atmosphere", previous.Atmosphere),
            Volcanism = GetLocalizedNameOrFallback(root, "Volcanism", previous.Volcanism),
            BodyType = bodyType,
            BodyClass = bodyClass,
            Terraformable = terraformable,
            EarthMasses = earthMasses,
            SolarMasses = solarMasses,
            EstimatedScanValue = ExplorationValueCalculator.SelectScanValue(values, wasDiscovered),
            EstimatedMappingValue = ExplorationValueCalculator.SelectMappingValue(values, wasDiscovered, wasMapped, false),
            EstimatedEfficientMappingValue = ExplorationValueCalculator.SelectMappingValue(values, wasDiscovered, wasMapped, true)
        };
    }

    private void UpsertMappedBody(JsonElement root, bool efficient)
    {
        int id = TryGetInt32(root, "BodyID", -1);
        if (id < 0) return;
        ExplorationBodySnapshot previous = GetExplorationBody(id, root);
        explorationBodies[id] = previous with
        {
            Name = GetString(root, "BodyName", previous.Name),
            IsMapped = true,
            MappingEfficient = efficient,
            LastProbesUsed = TryGetInt32(root, "ProbesUsed", previous.LastProbesUsed),
            EfficiencyTarget = TryGetInt32(root, "EfficiencyTarget", previous.EfficiencyTarget)
        };
    }

    private void UpsertBiologicalBody(JsonElement root, int biologicalCount)
    {
        int id = TryGetInt32(root, "BodyID", -1);
        if (id < 0) return;

        ExplorationBodySnapshot previous =
            GetExplorationBody(id, root);

        IReadOnlyList<(string Key, string Name)> genusEntries =
            ReadGenusEntries(root);

        string[] genusNames = genusEntries
            .Select(item => item.Name)
            .ToArray();

        string[] genusKeys = genusEntries
            .Select(item => item.Key)
            .ToArray();

        IReadOnlyList<BiologyEstimateSnapshot> estimates =
            ReadBiologyEstimates(root);

        explorationBodies[id] = previous with
        {
            Name = GetString(root, "BodyName", previous.Name),
            BiologicalSignals = biologicalCount,
            Genuses = genusEntries.Count == 0
                ? previous.Genuses
                : genusNames,
            GenusKeys = genusEntries.Count == 0
                ? previous.GenusKeys
                : genusKeys,
            BiologyEstimates = estimates.Count == 0
                ? previous.BiologyEstimates
                : estimates
        };
    }

    private static IReadOnlyList<BiologyEstimateSnapshot> ReadBiologyEstimates(JsonElement root)
    {
        if (!root.TryGetProperty("Genuses", out JsonElement source) || source.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<BiologyEstimateSnapshot>();
        }
        return source.EnumerateArray()
            .Select(item => ExobiologyCatalog.Estimate(
                GetString(item, "Genus"),
                GetLocalizedName(item, "Genus")))
            .Where(item => !string.IsNullOrWhiteSpace(item.Genus))
            .GroupBy(item => item.Genus, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private OrganicScanProgressSnapshot[] GetCurrentOrganicProgress(GameStateSnapshot current) => organicProgress.Values
        .Where(item => IsCurrentSystem(item, current))
        .OrderByDescending(item => item.UpdatedUtc)
        .ToArray();

    private static bool IsCurrentSystem(OrganicScanProgressSnapshot item, GameStateSnapshot current) =>
        current.SystemAddress != 0 && item.SystemAddress == current.SystemAddress
        || current.SystemAddress == 0
        && item.SystemAddress == 0
        && string.Equals(item.SystemName, current.StarSystem, StringComparison.OrdinalIgnoreCase);

    private static string GetOrganicProgressKey(OrganicScanProgressSnapshot item) => GetOrganicProgressKey(
        item.Commander,
        item.SystemAddress,
        item.SystemName,
        item.BodyId,
        string.IsNullOrWhiteSpace(item.SpeciesKey)
            ? item.Species
            : item.SpeciesKey);

    private static string GetOrganicProgressKey(
        string commander, long systemAddress, string systemName, int bodyId, string species) =>
        $"{commander}|{(systemAddress != 0 ? systemAddress.ToString() : systemName)}|{bodyId}|{species}";

    private ExplorationBodySnapshot GetExplorationBody(int id, JsonElement root) =>
        explorationBodies.TryGetValue(id, out ExplorationBodySnapshot? body)
            ? body
            : new ExplorationBodySnapshot(
                id, GetString(root, "BodyName"), string.Empty, 0,
                false, false, false, false, 0, Array.Empty<string>(), ExplorationInterest.None);

    private static bool IsEfficientMapping(JsonElement root)
    {
        int target = TryGetInt32(root, "EfficiencyTarget");
        int used = TryGetInt32(root, "ProbesUsed");
        return target > 0 && used > 0 && used <= target;
    }

    private static IReadOnlyList<(string Key, string Name)> ReadGenusEntries(
        JsonElement root)
    {
        if (!root.TryGetProperty(
                "Genuses",
                out JsonElement source)
            || source.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<(string Key, string Name)>();
        }

        return source.EnumerateArray()
            .Select(item =>
                (
                    Key: GetString(item, "Genus"),
                    Name: GetLocalizedName(item, "Genus")
                ))
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Key)
                || !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(
                item =>
                    string.IsNullOrWhiteSpace(item.Key)
                        ? item.Name
                        : item.Key,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static ExplorationInterest DetermineInterest(string planetClass, string terraformState, string starType)
    {
        if (planetClass.Contains("Earthlike", StringComparison.OrdinalIgnoreCase)
            || planetClass.Contains("Earth-like", StringComparison.OrdinalIgnoreCase)) return ExplorationInterest.EarthLike;
        if (planetClass.Contains("Water world", StringComparison.OrdinalIgnoreCase)) return ExplorationInterest.WaterWorld;
        if (planetClass.Contains("Ammonia world", StringComparison.OrdinalIgnoreCase)) return ExplorationInterest.AmmoniaWorld;
        if (!string.IsNullOrWhiteSpace(terraformState)
            && terraformState.Contains("Terraform", StringComparison.OrdinalIgnoreCase)) return ExplorationInterest.Terraformable;
        if (starType.Equals("N", StringComparison.OrdinalIgnoreCase)) return ExplorationInterest.NeutronStar;
        if (starType.Equals("H", StringComparison.OrdinalIgnoreCase)
            || starType.Contains("BlackHole", StringComparison.OrdinalIgnoreCase)) return ExplorationInterest.BlackHole;
        return ExplorationInterest.None;
    }

    private static int ReadBiologicalSignalCount(JsonElement root)
    {
        if (!root.TryGetProperty("Signals", out JsonElement signals) || signals.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }
        foreach (JsonElement signal in signals.EnumerateArray())
        {
            string type = GetString(signal, "Type");
            string localized = GetString(signal, "Type_Localised");
            if (type.Contains("Biological", StringComparison.OrdinalIgnoreCase)
                || localized.Contains("Biological", StringComparison.OrdinalIgnoreCase)
                || localized.Contains("Биолог", StringComparison.OrdinalIgnoreCase))
            {
                return TryGetInt32(signal, "Count");
            }
        }
        return 0;
    }

    private static ProspectedAsteroidSnapshot ReadProspectedAsteroid(JsonElement root)
    {
        List<ProspectedMaterialSnapshot> materials = new();
        if (root.TryGetProperty("Materials", out JsonElement source) && source.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement material in source.EnumerateArray())
            {
                materials.Add(new ProspectedMaterialSnapshot(
                    GetLocalizedName(material, "Name"),
                    TryGetDouble(material, "Proportion")));
            }
        }
        return new ProspectedAsteroidSnapshot(
            GetLocalizedName(root, "Content"),
            TryGetDouble(root, "Remaining"),
            GetLocalizedName(root, "MotherlodeMaterial"),
            materials.OrderByDescending(material => material.Proportion).ToArray());
    }

    private void RaiseStateChanged(
        JournalEventOrigin origin = JournalEventOrigin.Live)
    {
        GameStateSnapshot snapshot;

        lock (sync)
        {
            if (stateBatchDepth > 0)
            {
                stateChangePending = true;
                return;
            }

            snapshot = state;
        }

        StateChanged?.Invoke(
            this,
            new GameStateChangedEventArgs(
                snapshot,
                origin));
    }

    private static bool HasFlag(ulong flags, int bit) => (flags & (1UL << bit)) != 0;

    private static DateTimeOffset GetTimestamp(JsonElement element)
    {
        string value = GetString(element, "timestamp");
        return DateTimeOffset.TryParse(value, out DateTimeOffset result) ? result : DateTimeOffset.UtcNow;
    }

    private static DateTimeOffset MaxTimestamp(DateTimeOffset? current, DateTimeOffset candidate) =>
        current is null || candidate > current ? candidate : current.Value;

    private static string GetLocalizedName(JsonElement element, string property)
    {
        string localized = GetString(element, property + "_Localised");
        return string.IsNullOrWhiteSpace(localized) ? NormalizeInternalName(GetString(element, property)) : localized;
    }

    private static string GetLocalizedNameOrFallback(JsonElement element, string property, string fallback)
    {
        string value = GetLocalizedName(element, property);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string NormalizeInternalName(string value) => value.Trim().Trim('$').Replace("_name;", string.Empty, StringComparison.OrdinalIgnoreCase);

    private static string GetString(JsonElement element, string property, string fallback = "") =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static bool GetBoolean(JsonElement element, string property, bool fallback) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static int TryGetInt32(JsonElement element, string property, int fallback = 0)
    {
        if (!element.TryGetProperty(property, out JsonElement value))
        {
            return fallback;
        }
        if (value.TryGetInt32(out int integer))
        {
            return integer;
        }
        return value.TryGetDouble(out double number) ? (int)Math.Round(number) : fallback;
    }

    private static long TryGetInt64(JsonElement element, string property, long fallback = 0) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetInt64(out long result) ? result : fallback;

    private static long? TryGetNullableInt64(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetInt64(out long result) ? result : null;

    private static ulong TryGetUInt64(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetUInt64(out ulong result) ? result : 0;

    private static double TryGetDouble(JsonElement element, string property, double fallback = 0) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetDouble(out double result) ? result : fallback;

    private static double? TryGetNullableDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetDouble(out double result) ? result : null;
}
