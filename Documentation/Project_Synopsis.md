# ED Activity Overlay — Project Synopsis

## Purpose

ED Activity Overlay is a Windows .NET 8 WPF companion overlay for Elite Dangerous.

The application is activity-oriented rather than information-oriented: it uses local game state to present the information and next actions relevant to what the commander is doing now, without requiring constant switching to external tools.

## Core Architecture

The application is organized around four primary activity workspaces:

- Trade
- Engineering
- Exploration + Exobiology
- Mining

Frontier Player Journal events and companion JSON files provide authoritative local commander/game state. Activity services consume that state and expose focused presentation models to WPF overlays.

External community services are optional enrichment/data providers and must remain behind explicit provider boundaries.

## Local Game Data

The application consumes Frontier data including:

- Player Journal events
- `Market.json`
- `NavRoute.json`
- `Cargo.json`
- `Backpack.json`
- `ShipLocker.json`
- other Frontier companion JSON files where required

The Journal and companion-file readers are read-only.

## Trade

The Trading workspace supports local game-state integration, cargo context, market validation, route-progress tracking, route result presentation and a compact pinned-route HUD.

Remote route discovery is currently disabled while the market-data provider/query architecture is redesigned. Future remote trade discovery must remain provider-neutral and separate from Journal ingestion.

## Engineering

Engineering features include:

- Horizons and Odyssey material inventory
- blueprint catalog and recipes
- persistent wishlist/plan
- live missing-material calculation
- material-acquisition guidance
- engineer status/progress presentation

Material state is assembled from local Frontier data and persisted companion state.

## Exploration and Exobiology

Exploration uses the Player Journal as the authoritative source for personal progress.

Features include:

- current-system/body catalog
- configurable community-data disclosure
- mapping/value guidance
- offline exobiology prediction
- colony-spacing guidance
- route and fuel assistance
- POI enrichment
- DSS probe guidance
- findings/history tracking
- estimated unsold exploration value

Optional community enrichment is isolated behind dedicated provider services.

## Mining

The Mining workspace presents session information derived from local Journal events, including prospector composition and refined cargo state.

## Overlay and Input

The overlay follows the Elite Dangerous window and supports focus-aware visibility and interactive/passive modes.

Input/control features include:

- configurable global/activity hotkeys
- compact activity switching
- pinned secondary HUD presentation
- optional Logitech X52 Pro MFD/LED/activity controls through DirectOutput
- Elite bindings integration for supported navigation automation

## Persistence

Application settings and companion state are stored under the EDActivityOverlay application-data folders.

A compatibility migration moves data from the pre-rebrand folder name on startup.

## External Data Boundaries

External services are treated as replaceable providers rather than application identity or core architecture.

Current/future provider integrations should use:

- self-identifying HTTP User-Agent values;
- bounded retries and caching;
- provider-neutral domain models;
- explicit separation between local Frontier ingestion and remote enrichment;
- no scraping of services that prohibit automated access.

## Technology

- C# / .NET 8
- WPF
- local JSON/Journal ingestion
- SQLite-backed companion state where applicable
- HTTP provider integrations
- Windows process/window integration
- Logitech DirectOutput integration

## Product Direction

The product goal is a contextual in-game activity assistant: use authoritative local game state plus optional provider data to show the commander what matters now and what to do next.