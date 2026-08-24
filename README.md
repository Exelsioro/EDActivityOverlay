# ED Activity Overlay

A .NET 8 WPF companion overlay for Elite Dangerous focused on activity-oriented in-game workflows.

The application combines local Player Journal data, Frontier companion JSON files, optional community data, hardware controls, and compact overlays so the player can stay focused on the current activity instead of switching between external tools.

## Requirements

- Windows 10 (1607+) or Windows 11
- Elite Dangerous (`EliteDangerous64.exe`)

## Current Features

- Overlay windows attached to the game window with focus-aware visibility
- Activity workspaces for Trade, Engineering, Exploration + Exobiology, and Mining
- Automatic current system, cargo and ship-state detection from local Frontier data
- Engineering inventory for Horizons and Odyssey materials
- Persistent engineering wishlist with blueprint recipes and live deficit calculation
- Material acquisition guidance for HGE, missions, surface collection and material traders
- Trade route progress tracking from `Docked`, `FSDJump`, `MarketBuy`, and `MarketSell`
- Current market validation through `Market.json`
- In-game navigation progress through `NavRoute.json`
- Results overlay and compact pinned route HUD
- Exploration system catalog with configurable community-data disclosure
- Optional Spansh enrichment with EDSM fallback and local caching
- Offline exobiology prediction and colony-spacing guidance
- Exploration route tools, fuel warnings, EDAstro/Canonn POIs, DSS probe layouts, findings log and estimated unsold data value
- Mining session information from Journal events
- Optional Logitech X52 Pro MFD, LED and activity controls through DirectOutput
- Configurable hotkeys, themes, panel styles and runtime UI language
- Settings and companion data stored under `%APPDATA%\EDActivityOverlay`
- Local/cache data stored under `%LOCALAPPDATA%\EDActivityOverlay` where applicable

## Trade Route Search

Remote trade-route discovery is temporarily disabled while the market-data provider and route-search architecture are being redesigned.

The local trading workflow remains available, including Journal integration, cargo state, market validation, route progress and pinned-route presentation.

## Repository Layout

```text
EDActivityOverlay/
|- EDActivityOverlay/               # Main WPF application
|- Logger/                          # Shared logging library
|- Testing/                         # Tests, harnesses and regression scripts
|- Documentation/                   # Project documentation
|- build.ps1                        # Solution build helper
|- build_installer.ps1              # Publish + installer automation
|- installer.iss                    # Inno Setup script
```

## Build

```powershell
dotnet build .\EDActivityOverlay\EDActivityOverlay.sln
```

or:

```powershell
.\build.ps1
```

## Run

```powershell
dotnet run --project .\EDActivityOverlay\EDActivityOverlay.csproj
```

## Player Journal

Journal integration is enabled by default and resolves the Windows Saved Games known folder. A custom directory can be selected under **Settings > Journal**.

The application opens Journal files read-only. Optional online exploration enrichment sends only the data required by the configured provider.

See [Journal integration](Documentation/JOURNAL_INTEGRATION.md) for supported events and implementation details.

## Engineering Assistant

Material counts are read locally from the Player Journal, `Backpack.json`, and `ShipLocker.json`.

Wishlist and cached commander state are stored in `%APPDATA%\EDActivityOverlay\companion.db`.

The full ship-engineering recipe catalog is downloaded from the public `EDCD/coriolis-data` repository and cached locally. A small built-in starter catalog remains available offline.

See [Engineering Assistant](Documentation/ENGINEERING_ASSISTANT.md) for behavior and limitations.

## Exploration Assistant

Exploration uses the Player Journal as the authoritative source for the commander's personal progress. Community data is used only according to the configured disclosure mode.

See [Exploration Assistant](Documentation/EXPLORATION_ASSISTANT.md) for the current system catalog and disclosure modes.

## Test Utilities

```powershell
dotnet test .\Testing\EDActivityOverlay.Tests\EDActivityOverlay.Tests.csproj
```

Additional test harnesses and regression scripts are available under `Testing/`.

## Installer

```powershell
.\build_installer.ps1
```

Build app only:

```powershell
.\build_installer.ps1 -SkipInstaller
```

Installer output is written to `Installer/`.

## Data Migration

Existing application data from the pre-rebrand installation is migrated automatically to the new `EDActivityOverlay` application-data folders on startup.

## Documentation

See the [Documentation index](Documentation/README.md) for maintained feature, architecture, testing, hardware and attribution documentation.
## License

MIT