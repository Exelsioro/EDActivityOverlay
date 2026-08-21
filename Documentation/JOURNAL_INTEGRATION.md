# Player Journal integration

The overlay reads local Elite Dangerous files from the Windows Saved Games known folder:

```text
Frontier Developments/Elite Dangerous
```

The location can be overridden under **Settings > Journal**. All files are opened read-only with sharing enabled, so the game remains the owner of the data.

## Data flow

```text
Journal.*.log  ─┐
Status.json     ├─ JournalMonitorService ─ GameStateSnapshot ─ UI
Cargo.json      │                                  └───────── Route progress
Market.json     │
NavRoute.json  ─┘
```

`JournalMonitorService` tails newline-delimited journal files and handles file rollover. Companion JSON files are retried when they are observed during an incomplete game write. UI windows consume immutable snapshots rather than parsing JSON.

Domain consumers subscribe through `JournalEventHub`. The trading reducer remains isolated from engineering inventory, and future exploration/exobiology reducers can consume the same event stream without expanding `GameStateSnapshot`.

## Supported journal state

- Commander and ship: `LoadGame`, `Loadout`
- Position: `Location`, `FSDJump`, `CarrierJump`, `Docked`, `Undocked`
- Cargo: `Cargo`, `MarketBuy`, `MarketSell`, `Cargo.json`
- Destination: `FSDTarget`, `NavRoute.json`, `NavRouteClear`
- Flight context: `Status.json`
- Surface context: latitude, longitude, altitude, heading, body radius, gravity,
  temperature and on-foot/SRV/landed flags from `Status.json`
- Local market validation: `Market.json`
- Horizons materials: `Materials`, `MaterialCollected`, `MaterialDiscarded`, `MaterialTrade`
- Engineering usage: `EngineerCraft`, `Synthesis`, `TechnologyBroker`, `EngineerContribution`
- Engineer access: `EngineerProgress`
- Odyssey inventory: `Backpack`, `BackpackChange`, `ShipLocker`, `Backpack.json`, `ShipLocker.json`
- Odyssey transactions and upgrades: micro-resource buy/sell/trade, suit and weapon upgrades
- Exploration: `FSSDiscoveryScan`, `Scan`, `SAAScanComplete`,
  `FSSBodySignals`, `SAASignalsFound`, `CodexEntry`
- Exobiology: species-specific `ScanOrganic` stages, colony spacing and the
  previous sample coordinates

Incomplete exobiology sampling is persisted locally in
`%LOCALAPPDATA%\ED_Inara_Overlay\exploration-progress.json`, so changing
systems or restarting the overlay does not silently lose the sampling state.

Unknown events are ignored deliberately, allowing newer game versions to add events without breaking the monitor.

## Exploration data enrichment

The local Journal remains authoritative for the commander's scans and current
state. When enabled under **Settings > Journal**, the exploration workspace
supplements the current system from the following public providers:

1. Spansh `GET /api/system/{SystemAddress}` for known bodies and estimated scan
   and mapping values.
2. EDSM `api-system-v1/bodies` and `estimated-value` as an optional fallback.

Only the current system name/address is requested. Successful responses are
cached per system under
`%LOCALAPPDATA%\ED_Inara_Overlay\exploration-system-cache`. Stale cache entries
remain usable when both providers are unavailable. The application does not
download a galaxy dump and has no dependency on an EDDiscovery installation.

## Route provider boundary

INARA continues to provide candidate trade routes. Journal data only supplies the commander's local context and observes route execution. Future EDDN ingestion should be implemented behind a separate market-data provider and must not be coupled to the Journal reader.

## Privacy

Journal data can include the commander name. It is retained in memory and is not transmitted by the Journal module. Optional exploration enrichment sends only the current system name/address to Spansh or EDSM. Any future EDDN contribution feature must be explicitly opt-in and must construct EDDN messages without commander-identifying fields.
