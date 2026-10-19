# ED Activity Overlay

A .NET 8 WPF companion overlay for Elite Dangerous focused on activity-oriented in-game workflows.

The application combines local Player Journal data, Frontier companion JSON files, optional community data, hardware controls, and compact overlays so the player can stay focused on the current activity instead of switching between external tools.

## Requirements

- Windows 10 version 2004 (build 19041+) or Windows 11
- Elite Dangerous (`EliteDangerous64.exe`)

## Download and first run

Download the installer or portable ZIP from [GitHub Releases](https://github.com/Exelsioro/EDActivityOverlay/releases).
The installer includes the .NET runtime. Start the application alongside Elite
Dangerous, select the activity you want to see, and use Settings to configure
the Journal path, language, overlay appearance and controls. The application
reads the local game Journal. Exploration enrichment is optional; remote trade
route search requires a network connection to the market-data provider.

The installer is not Authenticode-signed, so Windows may show a reputation
warning. Check the downloaded file against the release's `SHA256SUMS.txt`
before running it.

This is a beta release. The realtime DSS assistant and automatic Galaxy Map
interaction are experimental and can be disabled in Settings. VR mode disables
the DSS assistant because desktop HUD recognition is not calibrated for VR.
Please report a reproducible issue with the application version, Windows and
game resolution, overlay mode, input device, and relevant logs in
[GitHub Issues](https://github.com/Exelsioro/EDActivityOverlay/issues).

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

Trade route discovery uses the Ardent Insight market-data API and supports
one-way, round-trip, continuous and cargo-sale searches. The trading workflow
also combines Journal events and the current `Market.json` snapshot for cargo,
market validation, route progress, history and pinned-route presentation.

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

Build a specific version:

```powershell
.\build_installer.ps1 -Version 1.3.0-beta.4
```

Build app only:

```powershell
.\build_installer.ps1 -SkipInstaller
```

Installer output is written to `Installer/`.

Tagged releases are built by GitHub Actions. A tag such as
`v1.3.0-beta.4` creates a prerelease with a self-contained portable ZIP,
an Inno Setup installer and SHA-256 checksums.

The application checks GitHub Releases on startup. The startup window and
Settings always show the installed version and a separate update status,
including checking, no update, available update and network failure. Available
updates also appear in the tray menu. The beta channel can be enabled in Settings.
Installation is always user-initiated: the installer is downloaded, checked
against the release's `SHA256SUMS.txt`, launched, and then the app exits. The
same Inno Setup AppId updates an existing installation in place.

## Data Migration

Existing application data from the pre-rebrand installation is migrated automatically to the new `EDActivityOverlay` application-data folders on startup.

## Support Development

ED Activity Overlay is free and open source. If it is useful to you, you can
[support continued development](https://exelsioro.github.io/EDActivityOverlay/support/)
through Ko-fi or Boosty. The same page is linked from the startup window and
Settings inside the application.

## Documentation

See the [Documentation index](Documentation/README.md) for maintained feature, architecture, testing, hardware and attribution documentation.
## License

[MIT](LICENSE). Third-party code and data notices are recorded in
[Documentation/THIRD_PARTY.md](Documentation/THIRD_PARTY.md) and are included
in published distributions.
