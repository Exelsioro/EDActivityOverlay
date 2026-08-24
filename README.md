# Elite Dangerous Inara Overlay

A .NET 8 WPF overlay for Elite Dangerous that displays trade routes sourced from INARA.

## Requirements

- Windows 10 (1607+) or Windows 11
- Elite Dangerous (`EliteDangerous64.exe`)

## Features

- Overlay window attached to the game window
- Focus-aware visibility (hide/show with game focus)
- Trade route search via INARA
- Automatic current system and free cargo detection from the local Player Journal
- Engineering inventory for Horizons and Odyssey materials
- Persistent engineering wishlist with blueprint recipes and live deficit calculation
- Material acquisition guidance for HGE, missions, surface collection and traders
- Live route progress from `Docked`, `FSDJump`, `MarketBuy`, and `MarketSell` events
- Current market validation through `Market.json`
- In-game navigation progress through `NavRoute.json`
- Results overlay with route cards
- Compact pinned route HUD
- Activity bar for Trade, Engineering, combined Exploration + Exobiology, and Mining workspaces
- Optional current-system enrichment from Spansh with EDSM fallback, disk cache, and high-value mapping targets
- Offline exobiology prediction, colony-spacing navigation, fuel warnings, direct/imported Spansh routes, nearby EDAstro/Canonn POIs, DSS probe layouts, a findings log, and estimated unsold data value
- Full exploration body catalog with search, filters, copy actions, and configurable community-data spoiler protection
- Optional Logitech X52 Pro MFD, LED and activity controls through the installed DirectOutput driver
- Selectable side-panel appearance: Compact or Minimal
- Global hotkeys: `Ctrl+5` visibility, `Ctrl+6` interaction, `Ctrl+7` unpin; configurable activity shortcuts default to `Ctrl+1`…`Ctrl+4`
- Theme system with import/export
<<<<<<< HEAD
- Runtime UI language selection (Russian / English) under **Settings > Appearance**
- JSON settings persistence in `%APPDATA%/EDActivityOverlay`
=======
>>>>>>> 5a4b18874f545042cfb0305fd982818d207fcafd

## Repository Layout

```text
EDActivityOverlay/
|- EDActivityOverlay/                # Main WPF app
|- InaraTools/                      # INARA communication and parsing
|- Logger/                          # Shared logging library
|- Testing/                         # Test harnesses and scripts
|- Documentation/                   # Project documentation
|- build_installer.ps1              # Build + installer automation
|- installer.iss                    # Inno Setup script
```

## Build

```bash
dotnet build EDActivityOverlay/EDActivityOverlay.sln
```

## Run

```bash
dotnet run --project EDActivityOverlay/EDActivityOverlay.csproj
```

## Player Journal

Journal integration is enabled by default and resolves the Windows Saved Games known folder. A custom directory can be selected under **Settings > Journal**. The application opens Journal files read-only and does not upload commander data. Optional exploration enrichment sends only the current system name/address to Spansh or EDSM. INARA remains the trade-route provider.

See [Journal integration](Documentation/JOURNAL_INTEGRATION.md) for supported events and implementation details.
See [Exploration Assistant](Documentation/EXPLORATION_ASSISTANT.md) for the current system catalog and disclosure modes.

## Engineering Assistant

Open **Engineering Assistant** from the startup window or the tray menu. Material counts are read locally from the Player Journal, `Backpack.json`, and `ShipLocker.json`. Wishlist and cached commander state are stored in `%APPDATA%/EDActivityOverlay/companion.db`.

Select **ИНЖЕНЕРИЯ** in the compact activity drop-down or press its configured shortcut (`Ctrl+2` by default). The complete Engineering Assistant opens inside the game overlay. Press `Ctrl+6` to enable its controls and the cursor.

When route and Engineering overlays are open together they dock on opposite sides. Interactive mode also exposes Settings, separate station/system copy actions for a pinned route, and copyable farming destinations for missing engineering materials.

The activity bar keeps one primary workspace open at a time, preventing independently self-restoring overlays from competing for the same position. A pinned route remains visible as a secondary HUD card when another activity is selected. Exploration combines FSS/DSS system progress with organic sampling; Mining shows prospector composition and refined cargo for the current session.

Choose the panel shell in **Settings > Appearance > Panel style**. Compact uses readable filled cards; Minimal reduces the panel background and frame weight.

Settings are organized into Appearance, Overlay, Hotkeys, and Journal tabs. Opening Settings from an in-game panel creates a centered interactive overlay window above Elite Dangerous; opening it from the tray or startup window uses a regular desktop window.

The full ship-engineering recipe catalog is downloaded from the public `EDCD/coriolis-data` repository and cached under `%APPDATA%/EDActivityOverlay/catalogs`. A small built-in starter catalog remains available offline.

See [Engineering Assistant](Documentation/ENGINEERING_ASSISTANT.md) for behavior and limitations.

## Test Utilities

```bash
# Build mock target app
dotnet build Testing/MockTargetApp/MockTargetApp.csproj

# Run quick regression script
powershell -ExecutionPolicy Bypass -File Testing/QuickRegressionTest.ps1
```

## Installer

```powershell
# Build app + installer
.\build_installer.ps1

# Build app only
.\build_installer.ps1 -SkipInstaller

# Build installer only (if binaries already built)
.\build_installer.ps1 -SkipBuild
```

Installer output is written to `Installer/`.

## Logging

Runtime logs are written to `...\Elite Dangerous Inara Overlay\logs/`.

## License

MIT
