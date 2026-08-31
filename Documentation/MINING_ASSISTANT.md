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

## Next steps

- v1c: full analytics/history workspace, t/h, RPM, distributions and ETA-full;
- v1d: mining loadout analyzer;
- v2: ring/location search through a provider boundary, Ardent sell integration, mine-to-sell opportunity ranking.
