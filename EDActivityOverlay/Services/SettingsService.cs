using System;
using System.IO;
using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Services
{
    /// <summary>
    /// Service for managing application settings persistence
    /// </summary>
    public class SettingsService
    {
        private static SettingsService? _instance;
        private readonly string _settingsFilePath;
        private AppSettings _settings;

        public static SettingsService Instance => _instance ??= new SettingsService();

        /// <summary>
        /// Event fired when settings are changed
        /// </summary>
        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

        /// <summary>
        /// Current application settings
        /// </summary>
        public AppSettings Settings => _settings;

        private SettingsService()
        {
            var appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EDActivityOverlay");
            Directory.CreateDirectory(appDataFolder);
            _settingsFilePath = Path.Combine(appDataFolder, "settings.json");
            _settings = LoadSettings();
        }

        /// <summary>
        /// Load settings from file or create default settings
        /// </summary>
        private AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        using JsonDocument legacy = JsonDocument.Parse(json);
                        JsonElement root = legacy.RootElement;
                        if (!root.TryGetProperty(nameof(AppSettings.MiningHotkeyModifiers), out _)
                            && root.TryGetProperty("ExobiologyHotkeyModifiers", out JsonElement oldModifiers))
                        {
                            settings.MiningHotkeyModifiers = oldModifiers.GetString() ?? settings.MiningHotkeyModifiers;
                        }
                        if (!root.TryGetProperty(nameof(AppSettings.MiningHotkeyKey), out _)
                            && root.TryGetProperty("ExobiologyHotkeyKey", out JsonElement oldKey))
                        {
                            settings.MiningHotkeyKey = oldKey.GetString() ?? settings.MiningHotkeyKey;
                        }
                        settings.MiningTargetCommodities ??= new List<string>();
                        if (settings.MiningTargetCommodities.Count == 0
                            && !string.IsNullOrWhiteSpace(settings.MiningTargetCommodity))
                        {
                            settings.MiningTargetCommodities.Add(settings.MiningTargetCommodity.Trim());
                        }
                        if (!root.TryGetProperty(nameof(AppSettings.MiningAutoSelectTargets), out _))
                        {
                            // Preserve legacy explicit target behavior. Fresh/default settings use AUTO.
                            settings.MiningAutoSelectTargets =
                                string.IsNullOrWhiteSpace(settings.MiningTargetCommodity);
                        }
                        settings.OverlayChromeStyle = OverlayChromeStyles.Normalize(settings.OverlayChromeStyle);
                        settings.ExplorationSpoilerMode = ExplorationSpoilerModes.Normalize(settings.ExplorationSpoilerMode);
                        Logger.Logger.Info($"Settings loaded from {_settingsFilePath}");
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Logger.Error($"Error loading settings: {ex.Message}");
            }

            // Return default settings if loading failed
            Logger.Logger.Info("Using default settings");
            return new AppSettings();
        }

        /// <summary>
        /// Save current settings to file
        /// </summary>
        public void SaveSettings()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var json = JsonSerializer.Serialize(_settings, options);
                File.WriteAllText(_settingsFilePath, json);
                
                Logger.Logger.Info($"Settings saved to {_settingsFilePath}");
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(_settings));
            }
            catch (Exception ex)
            {
                Logger.Logger.Error($"Error saving settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Update the selected theme setting
        /// </summary>
        public void SetSelectedTheme(string themeName)
        {
            if (_settings.SelectedTheme != themeName)
            {
                _settings.SelectedTheme = themeName;
                SaveSettings();
                Logger.Logger.Info($"Selected theme updated to: {themeName}");
            }
        }

        /// <summary>
        /// Get the currently selected theme name
        /// </summary>
        public string GetSelectedTheme()
        {
            return _settings.SelectedTheme;
        }

        /// <summary>
        /// Update global overlay toggle hotkey settings.
        /// </summary>
        public void SetToggleHotkey(string modifiers, string key)
        {
            if (_settings.ToggleHotkeyModifiers != modifiers || _settings.ToggleHotkeyKey != key)
            {
                _settings.ToggleHotkeyModifiers = modifiers;
                _settings.ToggleHotkeyKey = key;
                SaveSettings();
                Logger.Logger.Info($"Toggle hotkey updated to: {modifiers}+{key}");
            }
        }

        /// <summary>
        /// Get configured global overlay toggle hotkey.
        /// </summary>
        public (string Modifiers, string Key) GetToggleHotkey()
        {
            return (_settings.ToggleHotkeyModifiers, _settings.ToggleHotkeyKey);
        }

        /// <summary>
        /// Update interactive overlay mode hotkey settings.
        /// </summary>
        public void SetInteractiveHotkey(string modifiers, string key)
        {
            if (_settings.InteractiveHotkeyModifiers != modifiers || _settings.InteractiveHotkeyKey != key)
            {
                _settings.InteractiveHotkeyModifiers = modifiers;
                _settings.InteractiveHotkeyKey = key;
                SaveSettings();
                Logger.Logger.Info($"Interactive hotkey updated to: {modifiers}+{key}");
            }
        }

        /// <summary>
        /// Get configured interactive mode hotkey.
        /// </summary>
        public (string Modifiers, string Key) GetInteractiveHotkey()
        {
            return (_settings.InteractiveHotkeyModifiers, _settings.InteractiveHotkeyKey);
        }

        public void SetActivityHotkeys(
            string tradeModifiers, string tradeKey,
            string engineeringModifiers, string engineeringKey,
            string explorationModifiers, string explorationKey,
            string miningModifiers, string miningKey)
        {
            if (_settings.TradeHotkeyModifiers == tradeModifiers && _settings.TradeHotkeyKey == tradeKey
                && _settings.EngineeringHotkeyModifiers == engineeringModifiers && _settings.EngineeringHotkeyKey == engineeringKey
                && _settings.ExplorationHotkeyModifiers == explorationModifiers && _settings.ExplorationHotkeyKey == explorationKey
                && _settings.MiningHotkeyModifiers == miningModifiers && _settings.MiningHotkeyKey == miningKey)
            {
                return;
            }

            _settings.TradeHotkeyModifiers = tradeModifiers;
            _settings.TradeHotkeyKey = tradeKey;
            _settings.EngineeringHotkeyModifiers = engineeringModifiers;
            _settings.EngineeringHotkeyKey = engineeringKey;
            _settings.ExplorationHotkeyModifiers = explorationModifiers;
            _settings.ExplorationHotkeyKey = explorationKey;
            _settings.MiningHotkeyModifiers = miningModifiers;
            _settings.MiningHotkeyKey = miningKey;
            SaveSettings();
            Logger.Logger.Info("Activity selection hotkeys updated");
        }

        /// <summary>
        /// Update interactive mode behavior settings.
        /// </summary>
        public void SetInteractionBehavior(
            bool enableInteractionMode,
            int autoReturnTimeoutSeconds,
            bool returnOnFocusLoss,
            bool showCursorWhenInteractive)
        {
            if (_settings.EnableInteractionMode == enableInteractionMode
                && _settings.AutoReturnTimeoutSeconds == autoReturnTimeoutSeconds
                && _settings.ReturnOnFocusLoss == returnOnFocusLoss
                && _settings.ShowCursorWhenInteractive == showCursorWhenInteractive)
            {
                return;
            }

            _settings.EnableInteractionMode = enableInteractionMode;
            _settings.AutoReturnTimeoutSeconds = autoReturnTimeoutSeconds;
            _settings.ReturnOnFocusLoss = returnOnFocusLoss;
            _settings.ShowCursorWhenInteractive = showCursorWhenInteractive;
            SaveSettings();
            Logger.Logger.Info(
                $"Interaction settings updated: enabled={enableInteractionMode}, timeout={autoReturnTimeoutSeconds}s, returnOnFocusLoss={returnOnFocusLoss}, showCursor={showCursorWhenInteractive}");
        }

        public void SetNotificationSettings(bool enabled, int durationSeconds)
        {
            durationSeconds = Math.Clamp(durationSeconds, 2, 30);
            if (_settings.EnableOverlayNotifications == enabled
                && _settings.NotificationDurationSeconds == durationSeconds)
            {
                return;
            }

            _settings.EnableOverlayNotifications = enabled;
            _settings.NotificationDurationSeconds = durationSeconds;
            SaveSettings();
            Logger.Logger.Info($"Notification settings updated: enabled={enabled}, duration={durationSeconds}s");
        }

        public void SetShipStatusWidgetSettings(bool enabled, string position)
        {
            position = string.IsNullOrWhiteSpace(position) ? "TopCenter" : position;
            if (_settings.EnableShipStatusWidget == enabled
                && string.Equals(_settings.ShipStatusWidgetPosition, position, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            _settings.EnableShipStatusWidget = enabled;
            _settings.ShipStatusWidgetPosition = position;
            SaveSettings();
            Logger.Logger.Info($"Ship status widget settings updated: enabled={enabled}, position={position}");
        }

        public void SetMainOverlayCollapsed(
            bool collapsed)
        {
            if (_settings.MainOverlayCollapsed
                == collapsed)
            {
                return;
            }

            _settings.MainOverlayCollapsed =
                collapsed;

            SaveSettings();

            Logger.Logger.Info(
                $"Main overlay collapsed state updated: {collapsed}");
        }

        public void SetPinnedRoutePosition(string position)
        {
            if (string.Equals(_settings.PinnedRoutePosition, position, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _settings.PinnedRoutePosition = position;
            SaveSettings();
            Logger.Logger.Info($"Pinned route position updated to: {position}");
        }

        public void SetOverlayChromeStyle(string style)
        {
            string normalized = OverlayChromeStyles.Normalize(style);
            if (string.Equals(_settings.OverlayChromeStyle, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _settings.OverlayChromeStyle = normalized;
            SaveSettings();
            Logger.Logger.Info($"Overlay chrome style updated to: {normalized}");
        }

        public void SetTradeHistoryDirectory(
            string directory)
        {
            directory =
                directory?.Trim()
                ?? string.Empty;

            if (string.Equals(
                    _settings.TradeHistoryDirectory,
                    directory,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _settings.TradeHistoryDirectory =
                directory;

            SaveSettings();

            Logger.Logger.Info(
                $"Trade history directory updated: customDirectory={!string.IsNullOrWhiteSpace(directory)}");
        }

        public void SetMiningCopilotSettings(string targetCommodity, double minimumProportion)
        {
            SetMiningCopilotSettings(
                string.IsNullOrWhiteSpace(targetCommodity)
                    ? Array.Empty<string>()
                    : new[] { targetCommodity },
                false,
                minimumProportion);
        }

        public void SetMiningCopilotSettings(
            IEnumerable<string> targetCommodities,
            bool autoSelectTargets,
            double minimumProportion)
        {
            string[] targets = (targetCommodities ?? Array.Empty<string>())
                .Select(item => item?.Trim() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray();
            minimumProportion = Math.Clamp(minimumProportion, 0, 100);

            bool sameTargets = _settings.MiningTargetCommodities
                .SequenceEqual(targets, StringComparer.OrdinalIgnoreCase);
            if (sameTargets
                && _settings.MiningAutoSelectTargets == autoSelectTargets
                && Math.Abs(_settings.MiningMinimumProportion - minimumProportion) < 0.0001)
            {
                return;
            }

            _settings.MiningTargetCommodities = targets.ToList();
            _settings.MiningTargetCommodity = targets.FirstOrDefault() ?? string.Empty;
            _settings.MiningAutoSelectTargets = autoSelectTargets;
            _settings.MiningMinimumProportion = minimumProportion;
            SaveSettings();
            Logger.Logger.Info(
                $"Mining copilot targets updated: auto={autoSelectTargets}, targets={string.Join(',', targets)}, minimum={minimumProportion:0.#}%");
        }

        public void SetJournalSettings(bool enabled, string directory)
        {
            directory = directory?.Trim() ?? string.Empty;
            if (_settings.EnableJournalIntegration == enabled
                && string.Equals(_settings.JournalDirectory, directory, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _settings.EnableJournalIntegration = enabled;
            _settings.JournalDirectory = directory;
            SaveSettings();
            Logger.Logger.Info($"Journal settings updated: enabled={enabled}, customDirectory={!string.IsNullOrWhiteSpace(directory)}");
        }

        public void SetExplorationDataSettings(
            bool enabled,
            bool edsmFallback,
            int cacheHours,
            string spoilerMode,
            bool poiEnabled,
            int poiMinRating)
        {
            cacheHours = Math.Clamp(cacheHours, 1, 720);
            poiMinRating = Math.Clamp(poiMinRating, 0, 10);
            spoilerMode = ExplorationSpoilerModes.Normalize(spoilerMode);
            if (_settings.EnableOnlineExplorationData == enabled
                && _settings.EnableEdsmFallback == edsmFallback
                && _settings.ExplorationCacheHours == cacheHours
                && _settings.ExplorationSpoilerMode == spoilerMode
                && _settings.EnableExplorationPoiData == poiEnabled
                && _settings.ExplorationPoiMinRating == poiMinRating)
            {
                return;
            }
            _settings.EnableOnlineExplorationData = enabled;
            _settings.EnableEdsmFallback = edsmFallback;
            _settings.ExplorationCacheHours = cacheHours;
            _settings.ExplorationSpoilerMode = spoilerMode;
            _settings.EnableExplorationPoiData = poiEnabled;
            _settings.ExplorationPoiMinRating = poiMinRating;
            SaveSettings();
            Logger.Logger.Info($"Exploration data settings updated: enabled={enabled}, edsmFallback={edsmFallback}, cacheHours={cacheHours}, spoilers={spoilerMode}, poi={poiEnabled}, poiRating={poiMinRating}");
        }

        public void SetExperimentalDssSettings(
            bool enabled,
            string researchLogDirectory)
        {
            researchLogDirectory =
                researchLogDirectory?.Trim()
                ?? string.Empty;

            if (_settings.EnableExperimentalDssAssistant == enabled
                && string.Equals(
                    _settings.DssResearchLogDirectory,
                    researchLogDirectory,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _settings.EnableExperimentalDssAssistant =
                enabled;

            _settings.DssResearchLogDirectory =
                researchLogDirectory;

            SaveSettings();

            Logger.Logger.Info(
                $"Experimental DSS settings updated: enabled={enabled}, " +
                $"customLogDirectory={!string.IsNullOrWhiteSpace(researchLogDirectory)}");
        }

        public void SetDssGuidanceSettings(int efficiencyTarget)
        {
            efficiencyTarget = Math.Clamp(efficiencyTarget, 2, 12);
            if (_settings.DssEfficiencyTarget == efficiencyTarget)
            {
                return;
            }
            _settings.DssEfficiencyTarget = efficiencyTarget;
            SaveSettings();
            Logger.Logger.Info($"DSS guidance settings updated: target={efficiencyTarget}");
        }

        public void SetExplorationRoutePanelState(bool formCollapsed, bool routeCollapsed)
        {
            if (_settings.ExplorationRouteFormCollapsed == formCollapsed
                && _settings.ExplorationRouteListCollapsed == routeCollapsed)
            {
                return;
            }
            _settings.ExplorationRouteFormCollapsed = formCollapsed;
            _settings.ExplorationRouteListCollapsed = routeCollapsed;
            SaveSettings();
        }

        public void SetRouteAutomationSettings(
            bool experimentalEnabled,
            string bindingsPreset,
            string bindingsFilePath,
            int mapDelayMs,
            int stepDelayMs,
            int verificationSeconds)
        {
            bindingsPreset = bindingsPreset?.Trim() ?? string.Empty;
            bindingsFilePath = bindingsFilePath?.Trim() ?? string.Empty;
            mapDelayMs = Math.Clamp(mapDelayMs, 3000, 15000);
            stepDelayMs = Math.Clamp(stepDelayMs, 100, 2000);
            verificationSeconds = Math.Clamp(verificationSeconds, 5, 30);
            if (_settings.EnableExperimentalRouteAutomation == experimentalEnabled
                && string.Equals(_settings.EliteBindingsPreset, bindingsPreset, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_settings.EliteBindingsFilePath, bindingsFilePath, StringComparison.OrdinalIgnoreCase)
                && _settings.RouteAutomationMapDelayMs == mapDelayMs
                && _settings.RouteAutomationStepDelayMs == stepDelayMs
                && _settings.RouteAutomationVerificationSeconds == verificationSeconds)
            {
                return;
            }
            _settings.EnableExperimentalRouteAutomation = experimentalEnabled;
            _settings.EliteBindingsPreset = bindingsPreset;
            _settings.EliteBindingsFilePath = bindingsFilePath;
            _settings.RouteAutomationMapDelayMs = mapDelayMs;
            _settings.RouteAutomationStepDelayMs = stepDelayMs;
            _settings.RouteAutomationVerificationSeconds = verificationSeconds;
            SaveSettings();
            Logger.Logger.Info($"Route automation settings updated: experimental={experimentalEnabled}, preset={bindingsPreset}, file={bindingsFilePath}, mapDelay={mapDelayMs}, stepDelay={stepDelayMs}, verify={verificationSeconds}");
        }

        public void SetX52Settings(bool enabled, bool mfd, bool leds, bool mfdControls)
        {
            if (_settings.EnableX52Support == enabled
                && _settings.EnableX52Mfd == mfd
                && _settings.EnableX52LedState == leds
                && _settings.EnableX52MfdControls == mfdControls)
            {
                return;
            }
            _settings.EnableX52Support = enabled;
            _settings.EnableX52Mfd = mfd;
            _settings.EnableX52LedState = leds;
            _settings.EnableX52MfdControls = mfdControls;
            SaveSettings();
            Logger.Logger.Info($"X52 settings updated: enabled={enabled}, mfd={mfd}, leds={leds}, controls={mfdControls}");
        }

        public void SetExperimentalX52MiningCopilot(bool enabled)
        {
            if (_settings.EnableExperimentalX52MiningCopilot == enabled)
            {
                return;
            }

            _settings.EnableExperimentalX52MiningCopilot = enabled;
            SaveSettings();
            Logger.Logger.Info(
                $"Experimental X52 Mining Copilot updated: enabled={enabled}");
        }

        public void SetLanguage(string language)
        {
            string normalized = LocalizationService.Normalize(language);
            if (string.Equals(_settings.Language, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _settings.Language = normalized;
            SaveSettings();
        }

        /// <summary>
        /// Reset settings to default values
        /// </summary>
        public void ResetToDefaults()
        {
            _settings = new AppSettings();
            SaveSettings();
            Logger.Logger.Info("Settings reset to defaults");
        }
    }

    /// <summary>
    /// Application settings model
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// UI culture loaded from Resources/Localization.&lt;culture&gt;.xaml.
        /// </summary>
        public string Language { get; set; } = "ru-RU";

        /// <summary>
        /// Currently selected theme name
        /// </summary>
        public string SelectedTheme { get; set; } = "Default Orange";

        /// <summary>
        /// Application version when settings were last saved
        /// </summary>
        public string Version { get; set; } = "1.0.0";

        /// <summary>
        /// Timestamp when settings were last saved
        /// </summary>
        public DateTime LastSaved { get; set; } = DateTime.Now;

        /// <summary>
        /// Global overlay toggle hotkey modifiers (for example: Ctrl, Ctrl+Shift).
        /// </summary>
        public string ToggleHotkeyModifiers { get; set; } = "Ctrl";

        /// <summary>
        /// Global overlay toggle hotkey key value (WPF Key enum string, for example: D5, F1).
        /// </summary>
        public string ToggleHotkeyKey { get; set; } = "D5";

        /// <summary>
        /// Enables/disables entering interactive mode for overlay windows.
        /// </summary>
        public bool EnableInteractionMode { get; set; } = true;

        /// <summary>
        /// Auto-return timeout from interactive mode in seconds. 0 disables timeout.
        /// </summary>
        public int AutoReturnTimeoutSeconds { get; set; } = 8;

        /// <summary>
        /// Return to passive mode when interactive overlay focus is lost.
        /// </summary>
        public bool ReturnOnFocusLoss { get; set; } = true;

        /// <summary>
        /// Force cursor visibility when interactive mode is enabled.
        /// </summary>
        public bool ShowCursorWhenInteractive { get; set; } = true;

        /// <summary>
        /// Interactive mode hotkey modifiers.
        /// </summary>
        public string InteractiveHotkeyModifiers { get; set; } = "Ctrl";

        /// <summary>
        /// Interactive mode hotkey key value.
        /// </summary>
        public string InteractiveHotkeyKey { get; set; } = "D6";

        public string TradeHotkeyModifiers { get; set; } = "Ctrl";
        public string TradeHotkeyKey { get; set; } = "D1";
        public string EngineeringHotkeyModifiers { get; set; } = "Ctrl";
        public string EngineeringHotkeyKey { get; set; } = "D2";
        public string ExplorationHotkeyModifiers { get; set; } = "Ctrl";
        public string ExplorationHotkeyKey { get; set; } = "D3";
        public string MiningHotkeyModifiers { get; set; } = "Ctrl";
        public string MiningHotkeyKey { get; set; } = "D4";

        /// <summary>
        /// Legacy primary Mining target. Kept for settings/X52 compatibility; the compact HUD uses MiningTargetCommodities.
        /// </summary>
        public string MiningTargetCommodity { get; set; } = string.Empty;

        /// <summary>Manual Mining targets. Up to five commodities are evaluated independently against the same percentage threshold.</summary>
        public List<string> MiningTargetCommodities { get; set; } = new();

        /// <summary>Automatically selects up to five ring-compatible targets from current Ardent sell prices.</summary>
        public bool MiningAutoSelectTargets { get; set; } = true;

        /// <summary>Minimum asteroid composition accepted by the Mining prospector advisor. Market price never changes this percentage.</summary>
        public double MiningMinimumProportion { get; set; } = 25;

        /// <summary>Displays non-interactive journal notifications over the game.</summary>
        public bool EnableOverlayNotifications { get; set; } = true;

        /// <summary>Lifetime of an overlay notification in seconds.</summary>
        public int NotificationDurationSeconds { get; set; } = 6;

        /// <summary>Remembers whether the small main overlay controller is collapsed.</summary>
        public bool MainOverlayCollapsed { get; set; }

        /// <summary>Shows persistent route context and active ship advisories.</summary>
        public bool EnableShipStatusWidget { get; set; } = true;

        /// <summary>Placement of the shared ship status widget.</summary>
        public string ShipStatusWidgetPosition { get; set; } = "TopCenter";

        /// <summary>
        /// Placement of the compact pinned route relative to the game window.
        /// </summary>
        public string PinnedRoutePosition { get; set; } = "MiddleLeft";

        /// <summary>
        /// Visual shell used by compact in-game panels: Compact or Minimal.
        /// </summary>
        public string OverlayChromeStyle { get; set; } = OverlayChromeStyles.Compact;

        /// <summary>
        /// Reads the local Elite Dangerous Player Journal and companion files.
        /// </summary>
        public bool EnableJournalIntegration { get; set; } = true;

        /// <summary>
        /// Optional custom Journal directory. Empty uses the Windows Saved Games folder.
        /// </summary>
        public string JournalDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Optional directory for durable trade-history JSONL.
        /// Empty preserves %APPDATA%/EDActivityOverlay.
        /// </summary>
        public string TradeHistoryDirectory { get; set; } = string.Empty;

        /// <summary>Enriches the current system from public community APIs.</summary>
        public bool EnableOnlineExplorationData { get; set; } = true;

        /// <summary>Uses EDSM when Spansh has no data or is unavailable.</summary>
        public bool EnableEdsmFallback { get; set; } = true;

        /// <summary>Lifetime of a successful current-system response in the local cache.</summary>
        public int ExplorationCacheHours { get; set; } = 168;

        /// <summary>Controls whether community data may reveal bodies not personally scanned.</summary>
        public string ExplorationSpoilerMode { get; set; } = ExplorationSpoilerModes.EnrichScanned;

        /// <summary>Shows nearby curated Galactic Exploration Catalog locations.</summary>
        public bool EnableExplorationPoiData { get; set; } = true;

        /// <summary>Minimum EDAstro GEC explorer rating accepted for nearby POIs.</summary>
        public int ExplorationPoiMinRating { get; set; } = 4;

        /// <summary>
        /// Enables the experimental real-time DSS assistant. Opt-in because
        /// Windows Graphics Capture and live CV can increase system load.
        /// </summary>
        public bool EnableExperimentalDssAssistant { get; set; }

        /// <summary>
        /// Optional root for DSS research/session logs.
        /// Empty preserves %LOCALAPPDATA%/EDActivityOverlay/Research/DSS.
        /// </summary>
        public string DssResearchLogDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Legacy pre-production manual efficiency target. Retained only for
        /// settings-file compatibility; production DSS never uses it as authority.
        /// </summary>
        public int DssEfficiencyTarget { get; set; } = 6;

        /// <summary>Remembers whether the Spansh input form is folded in the full workspace.</summary>
        public bool ExplorationRouteFormCollapsed { get; set; }

        /// <summary>Remembers whether the imported route list is folded in the full workspace.</summary>
        public bool ExplorationRouteListCollapsed { get; set; }

        /// <summary>Allows the app to select a Galaxy Map result and hold UI Select to plot it.</summary>
        public bool EnableExperimentalRouteAutomation { get; set; }

        /// <summary>Legacy controls preset override. Empty follows StartPreset.4.start.</summary>
        public string EliteBindingsPreset { get; set; } = string.Empty;

        /// <summary>
        /// Exact Elite Dangerous .binds file used by Galaxy Map automation.
        /// Empty keeps the legacy automatic preset detection.
        /// </summary>
        public string EliteBindingsFilePath { get; set; } = string.Empty;

        /// <summary>Wait after opening Galaxy Map before navigating its UI.</summary>
        public int RouteAutomationMapDelayMs { get; set; } = 6000;

        /// <summary>Wait between Galaxy Map UI input steps.</summary>
        public int RouteAutomationStepDelayMs { get; set; } = 350;

        /// <summary>Maximum wait for the requested destination to appear in NavRoute.json.</summary>
        public int RouteAutomationVerificationSeconds { get; set; } = 15;

        /// <summary>Enables optional Logitech X52 Pro DirectOutput integration.</summary>
        public bool EnableX52Support { get; set; }

        /// <summary>Shows journal and activity state on the X52 Pro MFD.</summary>
        public bool EnableX52Mfd { get; set; } = true;

        /// <summary>Reflects ship state on the X52 Pro LEDs.</summary>
        public bool EnableX52LedState { get; set; } = true;

        /// <summary>Uses the MFD wheel to switch and toggle activity widgets.</summary>
        public bool EnableX52MfdControls { get; set; } = true;

        /// <summary>Shows Mining-specific MFD and LED copilot cues. Experimental and opt-in.</summary>
        public bool EnableExperimentalX52MiningCopilot { get; set; }
    }

    /// <summary>
    /// Event args for settings changed event
    /// </summary>
    public class SettingsChangedEventArgs : EventArgs
    {
        public AppSettings Settings { get; }

        public SettingsChangedEventArgs(AppSettings settings)
        {
            Settings = settings;
        }
    }
}
