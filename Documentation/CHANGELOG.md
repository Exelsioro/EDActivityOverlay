# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Activities and notifications

- Added a full in-game exploration system catalog opened from the compact
  widget, with search, notable/value/biology/mapping/landing filters, body
  details and separate copy-system/copy-body actions.
- Added explicit Journal-only, scanned-body enrichment and full-catalog spoiler
  modes. Community data can no longer be mistaken for personal Scan/DSS state.
- Added background, idempotent import of closed Journal files into the shared
  SQLite companion database. Current-system rows now distinguish current-visit
  scans from the commander's earlier Scan, DSS and organic history.
- Combined Exploration and Exobiology into one journal-backed workspace.
- Added surface telemetry from `Status.json`, including coordinates, heading,
  body radius, gravity and on-foot/SRV state.
- Added persistent species-specific exobiology sampling with live colony-spacing
  distance, offline Canonn Bioforge value ranges and biological target summaries.
- Added scoopability and neutron/white-dwarf warnings for the active navigation route.
- Added independent Spansh current-system enrichment with optional EDSM fallback,
  per-system disk caching, explicit source status and known high-value mapping targets.
- Added a standalone Universal Cartographics value estimator adapted from
  EliteDangerousCore. Journal scans and incomplete provider records now receive
  local scan/mapping estimates without requiring EDDiscovery.
- Added exploration-provider controls to Settings; no EDDiscovery installation or
  full-galaxy database is required.
- Replaced the fourth activity with Mining, including prospector composition, asteroid cores and refinery session counters.
- Added a reusable localized notification pipeline for flight, combat, anti-xeno and mining events.
- Added optional Logitech X52 Pro support through the installed DirectOutput
  driver: three-line MFD status, journal-driven LEDs, activity switching from
  the MFD wheel, connection diagnostics and independent feature toggles.
- Added offline Canonn Bioforge exobiology prediction using atmosphere,
  temperature, pressure, gravity and volcanism, plus a colony-range escape radar.
- Added persistent import and journal-driven advancement for official Spansh
  Road to Riches, travel and neutron-route CSV/JSON exports.
- Added a scrollable full-route view with current/next/completed state,
  per-system copy actions and all Spansh body scan/DSS targets and values.
- Replaced native WPF check boxes with a runtime theme-aware template so text,
  hover, checked and disabled states remain readable in every theme.
- Unified buttons, text inputs, combo items, scroll viewers and scroll bars on
  shared runtime theme resources; native hover chrome can no longer leak into
  scrollbar page areas.
- Added clickable system-name text across exploration route/log summaries,
  pinned trade endpoints and trade cards.
- Added independently persistent folding for the Spansh form and imported route;
  successful imports reveal the route automatically.
- Added disk-usage reporting and safe cache maintenance. Commander history is
  deduplicated and preserved, while reproducible exploration cache files expire.
- Moved DSS probe guidance to a dedicated tab with a larger 460 px aiming diagram.
- Added observed-consumption fuel assessment with scoopable-star and emergency
  reserve warnings for the active `NavRoute.json` route.
- Added cached nearest-POI lookup from EDAstro GEC and Canonn Guardian/Thargoid
  datasets; provider failures are isolated so one source can survive the other.
- Added theme-aware numbered DSS probe layouts for efficiency targets 2–12 and
  retained `ProbesUsed`/`EfficiencyTarget` results per mapped body.
- Made the complete compact exploration body vertically scrollable and added
  an explicit no-route state instead of silently omitting route information.
- Added full-view overview, system catalog, persistent exploration log/findings
  and direct-route tabs, including manual bookmarks for notable bodies.
- Added background reconstruction of estimated unsold Universal Cartographics
  and Vista Genomics values across Journal files with sale-event resets.
- Added direct Spansh Road to Riches validation, background job polling and
  automatic checked-result import without requiring the browser.

### Localization

- Added runtime-selectable Russian and English UI catalogs, persisted in application settings.
- Replaced window and code-behind UI literals with dynamic resources, including Engineering acquisition advice.
- Language changes now refresh open overlays, route cards, the tray menu, and Engineering wishlist calculations without restarting.
- Kept Coriolis recipes in their canonical form and localize display names on demand, preventing cached data from being locked to the startup language.
- Added catalog parity and duplicate-key regression tests.

### Tooling

- Prevented WPF markup compilation from generating random `*_wpftmp.csproj` files that C# Dev Kit cached as failed projects.
- Excluded generated `bin`/`obj` content from project discovery and pinned VS Code to the repository solution.

### Activity navigation

- Added a persistent Trade / Engineering / Exploration / Exobiology activity bar to the in-game HUD.
- Reduced the activity selector width and made its selected label refresh immediately after a language change.
- Engineering activity now opens a compact deficit/destination widget; the full assistant is reachable only from that widget while in-game.
- Opening the full Engineering Assistant now captures overlay interaction and the cursor automatically; timeout and focus-loss return are suspended until the panel closes, then the previous interaction state is restored.
- Replaced native GroupBox chrome in route filters with the orange application border style.
- Replaced native WPF tabs in Settings with theme-aware headers, hover states, selected indicators, and content borders.
- Converted shared control colors, font sizes, shadows, and list chrome to dynamic theme resources; added compatibility for legacy `ButtonBackground` theme files and automated theme-style coverage tests.
- Applied the theme-aware tab templates to the full Engineering Assistant, removing native white hover and selected states.
- Reworked the main HUD into a narrow vertical control stack and made it the sole in-game entry point for Settings.
- Added explicit copy-system actions and an expandable per-material acquisition guide with every known method, destination, instructions, and external source.
- Exposed copy-system and help actions for every missing material directly in the compact Engineering widget; its scrollable in-widget guide no longer requires opening the full assistant.
- Added concrete per-material Elite Dangerous Wiki links, persistent standalone material tracking, and help/track actions to the full inventory table.
- Replaced the Engineer progress-only table with an offline directory of all 38 ship and Odyssey Engineers, localized discovery/invitation/unlock stages, live Journal status, locations, copy-system actions, and individual Wiki pages.
- Styled Engineering table hover/selection rows with theme resources and moved catalog loading/parsing off the UI thread with duplicate-refresh protection.
- Removed the Engineering shortcut from the startup window and narrowed the vertical main HUD further; Engineering is now entered through activity navigation.
- Activity selection now owns the primary workspace lifecycle, preventing route and Engineering windows from competing for visibility and placement.
- Pinned trade routes remain visible while switching activities.
- Pinned routes now show both origin and destination at all stages and expose separate copy-system and copy-station actions for each endpoint.
- Added journal-backed starter workspaces for Exploration and Exobiology.
- Added selectable Compact and Minimal chrome for side panels. The experimental perspective frame was removed after in-game evaluation.
- Extended Minimal chrome to the main HUD, trade search, route results, and individual trade-route cards, including live updates after Apply.
- Reorganized every user setting into Appearance, Overlay, Hotkeys, and Journal tabs.
- Settings opened from the HUD now use a dedicated centered in-game overlay window.
- Applying a theme no longer shows a blocking system confirmation dialog.

### Added
- **Engineering Assistant foundation**
  - Modular journal event hub for independent gameplay-domain consumers
  - SQLite persistence for commander inventory, engineer progress, and wishlist
  - Horizons, Backpack, and Ship Locker material tracking
  - Coriolis blueprint/experimental recipe catalog with offline cache and starter fallback
  - Wishlist requirement and deficit calculation
  - Material acquisition guidance with specific HGE, mission, scanning, surface, and trader strategies
  - Engineering window available from startup and tray menus
  - Full in-game Engineering Assistant with live wishlist deficits (configurable, `Ctrl+2` by default)
  - Pinned routes now participate in interactive mode, expose an unpin button, and support dragging
  - Configurable pinned-route placement; middle-left is the new HUD-safe default
  - Debounced global hotkeys so a held activity shortcut cannot immediately reopen its overlay
  - Opposite-side docking when route and Engineering overlays are visible together
  - Separate copy-system and copy-station actions on pinned routes
  - Settings access from both compact overlays
  - Concrete farming destinations for targeted raw and encoded materials, with fixed manufactured fallback sites
  - Isolated test-project build directories to remove parallel MSBuild apphost races
- **Global Hotkey System**: Ctrl+5 hotkey support for system-wide overlay control
  - Windows API RegisterHotKey implementation for system-wide hotkey registration
  - Ctrl+5 hotkey triggers the same toggle action as clicking the toggle button
  - Background operation works even when Elite Dangerous is in focus
  - Automatic hotkey registration when overlay starts
  - Graceful fallback handling if hotkey is already in use by another application
  - Thread-safe hotkey event handling with proper UI thread marshaling
  - Comprehensive logging for hotkey registration and activation events
  - Proper cleanup and unregistration of hotkey when application closes

## [Latest] - 2025-07-16

### Added
- **Ko-fi Integration**: Built-in support link for project development
  - Custom coffee cup icon with Ko-fi brand colors (#FF5E5E)
  - Clickable Ko-fi link (Ko-fi.com/exelsior) in waiting window
  - Proper event handling to open link in browser
  - User action logging for Ko-fi link clicks
  - Visual appeal with heart emoji and professional styling
- **Application Icon**: Custom app icon for enhanced user experience
  - New app.ico file in Resources folder
  - Icon appears in taskbar, Alt+Tab, and system UI
  - Professional branding for the application
- **Manual Overlay Control**: Enhanced user control over overlay startup
  - Prevents automatic overlay startup when target process is detected
  - Added "Start Overlay" button for explicit user control
  - Enhanced UI feedback with green text when target is available
  - Status message: "Target application found! Click 'Start Overlay' to proceed."
  - Continuous monitoring without auto-starting overlay
  - Graceful return to waiting state if target process closes

### Enhanced
- **Waiting Window**: Improved user experience and control
  - Enhanced target process detection with visual feedback
  - Animated status messages with progress indicators
  - Better state management for target process availability
  - Improved button layout and visual hierarchy
  - Professional styling consistent with application theme
- **User Experience**: Better control and feedback mechanisms
  - User retains full control over overlay initialization timing
  - Clear visual indication when target process is available
  - Smooth transitions between waiting and ready states
  - Enhanced logging for user actions and system events

### Technical Improvements
- **Event Handling**: Improved event management for user interactions
  - Better separation of user-initiated vs system-initiated actions
  - Enhanced logging for debugging and user behavior analysis
  - Proper cleanup of event handlers and resources
- **State Management**: Better tracking of application and target states
  - Improved target process monitoring logic
  - Better handling of edge cases and state transitions
  - Enhanced reliability of overlay startup process

## [Latest] - 2025-07-15

### Added
- **Theme Persistence System**: Comprehensive settings management for theme preferences
  - `SettingsService` - JSON-based settings storage and management
  - Automatic theme saving when applied through settings window
  - Theme restoration on application startup
  - Settings stored in `%APPDATA%/EDActivityOverlay/settings.json`
- **Enhanced Theme Management**: Improved theme system with better state management
  - Current theme tracking and restoration
  - Fallback to default theme when saved theme is unavailable
  - Proper theme initialization in Settings window
  - Better error handling and logging for theme operations
- **Improved User Experience**: Theme selection now persists across application restarts
  - No need to reselect theme every time the app starts
  - Seamless theme experience with automatic preference saving
  - Settings window now properly shows currently selected theme

### Enhanced
- **ThemeManager**: Updated with persistence support and better state management
  - Integration with SettingsService for automatic theme saving
  - LoadSavedTheme method for startup theme restoration
  - Better handling of theme loading and application states
- **App.xaml.cs**: Simplified theme initialization using persistent settings
  - Automatic loading of saved theme preferences
  - Fallback to default theme when no saved preference exists
- **SettingsWindow**: Enhanced to show current theme selection
  - Current theme is pre-selected when opening settings
  - Real-time theme preview with automatic saving
  - Better theme state management

### Technical Improvements
- **Settings Architecture**: Robust JSON-based configuration system
  - Singleton pattern for settings management
  - Automatic file creation and directory management
  - Comprehensive error handling and logging
  - Version tracking and timestamp recording
- **Theme State Management**: Improved theme state tracking
  - Better synchronization between theme manager and settings
  - Proper handling of theme availability and fallbacks
  - Enhanced logging for theme operations
- **Code Quality**: Improved code organization and documentation
  - Better separation of concerns between theme management and persistence
  - Enhanced error handling and user feedback
  - Consistent coding patterns and best practices

## [2.0.1] - 2025-07-12

### Fixed
- **Nullable Reference Types**: Fixed MainWindow constructor parameter to properly handle nullable Process parameter
- **Build Configuration**: Added `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>` to all project files to prevent warnings from blocking builds
- **Multiple Entry Points**: Resolved conflicting Main methods in test projects causing compilation errors
- **XAML Compilation**: Fixed missing XAML files and InitializeComponent errors in test projects
- **Project Dependencies**: Cleaned up test project file inclusions to prevent conflicts

### Added
- **Comprehensive Testing Suite**: Complete test harness ecosystem for thorough application testing
  - `TestHarness.bat` - Automated batch script for complete testing workflow
  - `OverlayTestHarness` - Interactive WPF application for manual testing with automation
  - `MinimalTestHarness` - Simple console-based test harness
  - `MockTargetApp` - Dedicated mock application for testing overlay behavior
- **Testing Documentation**: Comprehensive documentation for all testing procedures and troubleshooting
- **Build Status Verification**: All projects now build successfully with zero compilation errors

### Changed
- **Documentation Structure**: Moved Documentation folder to solution level for better organization
- **Build Output**: Solution now builds with 0 errors and 13 warnings (all non-critical nullable reference type warnings)
- **Project Organization**: Improved test project structure and file organization

### Technical Improvements
- **Error-Free Compilation**: Achieved zero compilation errors across all projects
- **Warning Management**: Configured appropriate warning levels while maintaining code quality
- **Test Infrastructure**: Robust testing infrastructure for continuous verification
- **Documentation Updates**: Updated build guides and testing documentation with current status

## [2.0.0] - 2025-07-12

### Project Restructure
- **Repository Unification**: Consolidated all components into a single unified repository
  - `EDActivityOverlay/` - Main WPF application
  - `InaraTools/` - INARA API communication library
  - `Logger/` - Centralized logging infrastructure
  - `Testing/` - Test harness and mock applications
  - `Documentation/` - Project documentation

### Enhanced Features
- **Advanced UI Components**: Enhanced TradeRouteCard with Elite Dangerous styling
- **Clipboard Integration**: Clickable system names with copy-to-clipboard functionality
- **Improved User Experience**: Professional UI styling with hover effects and visual feedback
- **Enhanced Logging**: Comprehensive logging across all components with file output

### Fixed Issues
- **Application Shutdown**: Proper cleanup and shutdown handling to prevent process hanging
- **Auto-Close Feature**: Automatic overlay closure when target application exits
- **Overlay Behavior**: Improved focus detection and window positioning
- **Spinner Animation**: Fixed loading animations and state transitions
- **Waiting Window**: Enhanced waiting window behavior and visibility management
- **UI Compilation**: Fixed XAML compilation issues and resource dependencies
- **Window Positioning**: Accurate trade route window positioning relative to target
- **State Machine**: Robust visibility state management for reliable overlay behavior

### Technical Improvements
- **Build System**: Unified solution file managing all projects
- **Dependency Management**: Consistent dependency versions across components
- **Error Handling**: Comprehensive exception handling and user feedback
- **Resource Management**: Proper disposal patterns and memory leak prevention
- **Code Organization**: Clear separation of concerns and modular design

### Documentation
- **Consolidated Documentation**: Removed redundant fix-specific documentation
- **Updated README**: Comprehensive project overview and setup instructions
- **Contributing Guide**: Detailed development workflow for unified repository
- **Build Guide**: Step-by-step build instructions with troubleshooting
- **Project Synopsis**: Complete technical architecture overview

## [1.0.0] - 2024-07-12

### Added
- Initial release of Elite Dangerous Inara Overlay
- WPF-based overlay system with automatic target detection
- State-machine based visibility management (Waiting  ForceShow  Auto)
- Timer-based retry mechanism for target process detection
- Comprehensive test harness for regression testing
- Trade route overlay functionality
- Results overlay window
- Pinned route overlay
- Proper window positioning relative to target application
- Focus-based visibility management
- Automatic cleanup when target application closes

### Technical Implementation
- **State Machine**: Three-state system for overlay visibility management
  - `Waiting`: Initial state when no target is detected
  - `ForceShow`: Transition state that ensures visibility after target detection
  - `Auto`: Normal operational state with focus-based visibility
- **Retry Mechanism**: Non-blocking timer-based system with configurable retry count
- **Process Detection**: Two-level detection system (process + window handle)
- **Resource Management**: Proper cleanup of timers and windows on application exit

### Test Coverage
- Automated regression test suite (Basic, Quick, Simple, Comprehensive)
- Test harness for manual verification
- Mock applications for development testing
- Coverage for both test scenarios and real Elite Dangerous integration


# 2026-08-21 — Galaxy Map route handoff

- Added a semi-automatic route action: copy the next imported waypoint, focus Elite, open Galaxy Map, select navigation search and paste the system; the player retains the final confirmation.
- Added opt-in experimental automatic confirmation with focus-loss aborts and destination verification through `NavRoute.json`.
- Active Galaxy Map, panel navigation and UI-select keys are detected from `StartPreset.4.start` and the matching `.binds` file, including Unicode preset names.
- Added theme-aware settings for the experimental mode and conservative UI timing delays. Experimental automation is disabled by default.
