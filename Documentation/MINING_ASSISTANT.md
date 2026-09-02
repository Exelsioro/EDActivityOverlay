# Mining Assistant

## v1a session core

The Mining assistant uses structured Elite Dangerous journal and companion data. Computer vision is not part of the production Mining architecture.

`MiningSessionService` is a journal consumer registered before `JournalMonitorService` starts. It keeps the current mining session in memory and persists completed live sessions to the existing `%APPDATA%/EDActivityOverlay/companion.db` database.

A session starts on the first of:

- `LaunchDrone` with `Type=Prospector`;
- `ProspectedAsteroid`;
- `MiningRefined`;
- `AsteroidCracked`.

A prospector launch by itself is not considered sufficient mining evidence. If no prospect, refinement, or cracked asteroid follows before a boundary, the empty session is discarded.

Session boundaries are `SupercruiseEntry`, `FSDJump`, `CarrierJump`, `Docked`, `Died`, `Shutdown`, `LoadGame`, or an unexpected system change observed in `Location`.

The accumulator also tracks:

- commander and current system context;
- best-effort body/ring context from `SupercruiseExit`;
- cargo capacity from `Loadout`;
- cargo used and remaining limpets from `Cargo` / `Cargo.json`;
- prospectors and collectors launched;
- every prospect and its material proportions;
- every `MiningRefined` event;
- cracked asteroids.

### Bootstrap semantics

Journal bootstrap replay reconstructs an active session but never writes a completed bootstrap-only session to history. If the reconstructed session remains active and later receives a live boundary, that single completed session is persisted. Session IDs are deterministic from the session start context so replay does not create a different identity.

### Persistence

The following tables are added to `companion.db`:

- `mining_session`;
- `mining_prospect`;
- `mining_prospect_material`;
- `mining_refinement`.

Raw prospect and refinement rows are retained instead of only aggregates so later versions can compute distributions, rolling rates, personal ring performance, and history views without losing source data.

## v1b prospector copilot

The compact Mining workspace reads the v1a session snapshot directly. The commander may set a target commodity and a minimum laser proportion. For each prospect the advisor emits a deterministic decision:

- `CORE` when the selected target matches the reported motherlode;
- `MINE` when the selected target is present in the laser-material list at or above the configured threshold;
- `SKIP` when the target is absent or below the threshold;
- `NO TARGET` when no target commodity is configured.

The HUD also labels the best extraction method that can be justified from structured journal data. A reported motherlode is labelled `CORE`; otherwise a prospector result with material proportions is labelled `LASER`. The Journal does not reliably identify surface or subsurface deposits in a fresh `ProspectedAsteroid` event, so v1b deliberately does not guess those methods and does not use CV.

Target hit rate, acceptance rate, average, median, and best target proportion are calculated from the raw prospect rows retained by v1a. Target and threshold are persisted in normal application settings.

## Current Mining Copilot

The production Mining branch now also includes:

- multi-target selection with an AUTO mode (up to five targets);
- ring-class and reserve context from `Scan`;
- DSS hotspot context from `SAASignalsFound`;
- nearby Ardent sell-price sampling (median reference price, plus average/best data);
- prices beside the structured `ProspectedAsteroid` composition. Material proportion and market price are independent values: the percentage is never multiplied into or used to modify the per-ton market price;
- collector/limpet/loadout and engineering-material assistance;
- full session analytics/history;
- current-cargo and refined-session estimated value using the current nearby market reference price;
- Mine -> Sell handoff: the Mining workspace can switch directly to Trade `Sell current cargo`, run the mixed-cargo buyer search, and pin the chosen sale-only route;
- X52 Mining copilot integration.

### Ring and target semantics

AUTO ranks only commodities compatible with the resolved ring class. When DSS hotspot signals exist, they are shown explicitly and participate in target selection. The compact HUD separately shows:

- ring class / reserve level;
- DSS hotspots;
- best priced resources currently known for that ring;
- active AUTO or manual targets.

Ring class, reserve level, and hotspot commodity IDs are copied into completed mining sessions and persisted with history.

### Economics semantics

Mining economic values are estimates based on current nearby Ardent market observations:

- cargo estimate = priced tons currently in the hold multiplied by the current per-ton reference price;
- session estimate = emitted `MiningRefined` tons multiplied by the current per-ton reference price;
- estimated credits/hour = session estimate divided by elapsed mining-session time after a five-minute warm-up.

Prospector material percentages are not part of these price calculations.

The pinned cargo-sale route separately tracks actual `MarketSell` revenue while the sale route is active. That revenue is deliberately not written back as historical mining-session profit because Elite's journal does not provide reliable provenance for every cargo unit when old and newly mined cargo are mixed.

## Remaining polish before merge

- real-game smoke tests across metallic, metal-rich, rocky and icy rings;
- verify DSS hotspot association on multi-ring bodies;
- verify Mine -> Sell mixed-cargo completion against live `MarketSell`;
- update screenshots/user-facing release notes;
- run the full Trade + Mining + Exploration + X52 regression suite before merging `Mining-module`.
