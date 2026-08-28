using System;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Exploration;
using EDActivityOverlay.Services.Navigation;
using EDActivityOverlay.Services.Dss;
using EDActivityOverlay.Services.Hardware;
using EDActivityOverlay.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly bool overlayMode;
        private readonly IntPtr targetWindow;

        public bool IsOverlayMode => overlayMode;

        private sealed class HotkeyOption
        {
            public string Label { get; set; } = string.Empty;
            public string Value { get; init; } = string.Empty;
        }

        private static readonly List<HotkeyOption> ModifierOptions = new()
        {
            new HotkeyOption { Label = "Ctrl", Value = "Ctrl" },
            new HotkeyOption { Label = "Ctrl + Shift", Value = "Ctrl+Shift" },
            new HotkeyOption { Label = "Ctrl + Alt", Value = "Ctrl+Alt" },
            new HotkeyOption { Label = "Alt + Shift", Value = "Alt+Shift" },
            new HotkeyOption { Label = "Alt", Value = "Alt" },
            new HotkeyOption { Label = "Shift", Value = "Shift" }
        };

        private static readonly List<HotkeyOption> KeyOptions = new()
        {
            new HotkeyOption { Label = "0", Value = "D0" },
            new HotkeyOption { Label = "1", Value = "D1" },
            new HotkeyOption { Label = "2", Value = "D2" },
            new HotkeyOption { Label = "3", Value = "D3" },
            new HotkeyOption { Label = "4", Value = "D4" },
            new HotkeyOption { Label = "5", Value = "D5" },
            new HotkeyOption { Label = "6", Value = "D6" },
            new HotkeyOption { Label = "7", Value = "D7" },
            new HotkeyOption { Label = "8", Value = "D8" },
            new HotkeyOption { Label = "9", Value = "D9" },
            new HotkeyOption { Label = "F1", Value = "F1" },
            new HotkeyOption { Label = "F2", Value = "F2" },
            new HotkeyOption { Label = "F3", Value = "F3" },
            new HotkeyOption { Label = "F4", Value = "F4" },
            new HotkeyOption { Label = "F5", Value = "F5" },
            new HotkeyOption { Label = "F6", Value = "F6" },
            new HotkeyOption { Label = "F7", Value = "F7" },
            new HotkeyOption { Label = "F8", Value = "F8" },
            new HotkeyOption { Label = "F9", Value = "F9" },
            new HotkeyOption { Label = "F10", Value = "F10" },
            new HotkeyOption { Label = "F11", Value = "F11" },
            new HotkeyOption { Label = "F12", Value = "F12" }
        };

        private static readonly List<HotkeyOption> TimeoutOptions = new()
        {
            new HotkeyOption { Label = Loc.Get("Loc_Off"), Value = "0" },
            new HotkeyOption { Label = Loc.Format("Loc_Seconds_Format", 5), Value = "5" },
            new HotkeyOption { Label = Loc.Format("Loc_Seconds_Format", 8), Value = "8" },
            new HotkeyOption { Label = Loc.Format("Loc_Seconds_Format", 10), Value = "10" },
            new HotkeyOption { Label = Loc.Format("Loc_Seconds_Format", 15), Value = "15" }
        };

        private static readonly List<HotkeyOption> PinnedPositionOptions = new()
        {
            new HotkeyOption { Label = Loc.Get("Loc_Middle_left"), Value = "MiddleLeft" },
            new HotkeyOption { Label = Loc.Get("Loc_Middle_right"), Value = "MiddleRight" },
            new HotkeyOption { Label = Loc.Get("Loc_Bottom_center"), Value = "BottomCenter" },
            new HotkeyOption { Label = Loc.Get("Loc_Top_center"), Value = "TopCenter" }
        };

        private static readonly List<HotkeyOption> OverlayChromeStyleOptions = new()
        {
            new HotkeyOption { Label = Loc.Get("Loc_Compact"), Value = Utils.OverlayChromeStyles.Compact },
            new HotkeyOption { Label = Loc.Get("Loc_Minimal"), Value = Utils.OverlayChromeStyles.Minimal }
        };

        private static readonly List<HotkeyOption> ExplorationCacheOptions = new()
        {
            new HotkeyOption { Label = Loc.Format("Loc_Hours_Format", 24), Value = "24" },
            new HotkeyOption { Label = Loc.Format("Loc_Days_Format", 3), Value = "72" },
            new HotkeyOption { Label = Loc.Format("Loc_Days_Format", 7), Value = "168" },
            new HotkeyOption { Label = Loc.Format("Loc_Days_Format", 30), Value = "720" }
        };

        private static readonly List<HotkeyOption> ExplorationSpoilerOptions = new()
        {
            new HotkeyOption { Label = Loc.Get("Loc_Exploration_spoilers_journal_only"), Value = ExplorationSpoilerModes.JournalOnly },
            new HotkeyOption { Label = Loc.Get("Loc_Exploration_spoilers_enrich_scanned"), Value = ExplorationSpoilerModes.EnrichScanned },
            new HotkeyOption { Label = Loc.Get("Loc_Exploration_spoilers_full_catalog"), Value = ExplorationSpoilerModes.FullCatalog }
        };

        private static readonly List<HotkeyOption> ExplorationPoiRatingOptions = new()
        {
            new HotkeyOption { Label = Loc.Get("Loc_POI_rating_any"), Value = "0" },
            new HotkeyOption { Label = "3+", Value = "3" },
            new HotkeyOption { Label = "4+", Value = "4" },
            new HotkeyOption { Label = "5+", Value = "5" },
            new HotkeyOption { Label = "7+", Value = "7" }
        };


        public SettingsWindow() : this(false, IntPtr.Zero)
        {
        }

        public SettingsWindow(bool overlayMode, IntPtr targetWindow)
        {
            this.overlayMode = overlayMode;
            this.targetWindow = targetWindow;
            InitializeComponent();
            ConfigureWindowMode();
            LoadLanguageSettings();
            RefreshLocalizedOptions();
            LoadThemes();
            LoadHotkeySettings();
            LoadExplorationDataSettings();
            LoadExperimentalDssSettings();
            LoadRouteAutomationSettings();
            LoadX52Settings();
            RefreshJournalStatus();
            _ = RefreshStorageUsageAsync();
            ExplorationDataService.Instance.DataChanged += OnExplorationDataChanged;
            X52IntegrationService.Instance.StateChanged += OnX52StateChanged;
            Closed += SettingsWindow_Closed;
        }

        private void ConfigureWindowMode()
        {
            if (!overlayMode)
            {
                return;
            }

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Width = 860;
            Height = 620;
            WindowStartupLocation = WindowStartupLocation.Manual;
            OverlayModeBadge.Visibility = Visibility.Visible;
            Loaded += SettingsWindow_Loaded;
        }

        private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            WindowsAPI.SetupOverlayWindow(this);
            WindowsAPI.SetClickThrough(this, false);
            if (targetWindow != IntPtr.Zero && WindowsAPI.TryGetWindowRectDips(targetWindow, out WindowsAPI.RECT rect))
            {
                Rect workArea = WindowsAPI.GetMonitorWorkArea(targetWindow);
                Width = Math.Min(860, Math.Max(640, rect.Right - rect.Left - 80));
                Height = Math.Min(620, Math.Max(520, rect.Bottom - rect.Top - 80));
                double left = rect.Left + ((rect.Right - rect.Left) - Width) / 2.0;
                double top = rect.Top + ((rect.Bottom - rect.Top) - Height) / 2.0;
                OverlayLayoutHelper.ClampPosition(ref left, ref top, Width, Height, workArea, 12, 12);
                Left = left;
                Top = top;
            }
            WindowsAPI.SetTopmost(this, true);
            Activate();
            WindowsAPI.EnsureCursorVisibleOnWindow(this);
        }

        private void LoadThemes()
        {
            ThemeComboBox.ItemsSource = ThemeManager.Instance.AvailableThemes;
            
            // Select the currently applied theme
            var currentTheme = ThemeManager.Instance.CurrentTheme;
            if (currentTheme != null)
            {
                ThemeComboBox.SelectedItem = currentTheme;
            }
            else if (ThemeComboBox.Items.Count > 0)
            {
                ThemeComboBox.SelectedIndex = 0;
            }
            
            UpdateThemeDetails();
            UpdateColorSwatches();
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateThemeDetails();
            UpdateColorSwatches();
            
            // Apply theme in real-time for preview
            var selectedTheme = ThemeComboBox.SelectedItem as Models.Theme;
            if (selectedTheme != null)
            {
                ThemeManager.Instance.ApplyTheme(selectedTheme);
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // Сначала сохранить ВСЕ значения из UI.
            SaveRouteAutomationSettings();
            SaveHotkeySettings();
            SaveJournalSettings();
            SaveExplorationDataSettings();
            SaveX52Settings();

            if (!SaveExperimentalDssSettings())
            {
                return;
            }

            // Language refresh может перезагрузить controls,
            // поэтому он должен идти после сохранения остальных настроек.
            SaveLanguageSettings();

            var selectedTheme = ThemeComboBox.SelectedItem as Models.Theme;
            if (selectedTheme != null)
            {
                ThemeManager.Instance.ApplyTheme(selectedTheme);
            }

            ApplyStatusText.Text = string.Empty;
        }

        private void LoadLanguageSettings()
        {
            LanguageComboBox.ItemsSource = LocalizationService.Languages;
            string language = LocalizationService.Normalize(SettingsService.Instance.Settings.Language);
            LanguageComboBox.SelectedItem = LocalizationService.Languages.First(option => option.Code == language);
        }

        private void SaveLanguageSettings()
        {
            if (LanguageComboBox.SelectedItem is not LanguageOption option)
            {
                return;
            }

            string currentLanguage =
                LocalizationService.Normalize(SettingsService.Instance.Settings.Language);

            if (string.Equals(
                    currentLanguage,
                    option.Code,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SettingsService.Instance.SetLanguage(option.Code);
            LocalizationService.Instance.ApplyLanguage(option.Code);
            RefreshLocalizedOptions();
        }

        private void RefreshLocalizedOptions()
        {
            string Text(string key) => LocalizationService.Instance.Get(key);
            TimeoutOptions[0].Label = Text("Loc_Off");
            for (int index = 1; index < TimeoutOptions.Count; index++)
            {
                TimeoutOptions[index].Label = LocalizationService.Instance.Format(
                    "Loc_Seconds_Format", TimeoutOptions[index].Value);
            }
            PinnedPositionOptions[0].Label = Text("Loc_Middle_left");
            PinnedPositionOptions[1].Label = Text("Loc_Middle_right");
            PinnedPositionOptions[2].Label = Text("Loc_Bottom_center");
            PinnedPositionOptions[3].Label = Text("Loc_Top_center");
            OverlayChromeStyleOptions[0].Label = Text("Loc_Compact");
            OverlayChromeStyleOptions[1].Label = Text("Loc_Minimal");
            TimeoutComboBox?.Items.Refresh();
            PinnedPositionComboBox?.Items.Refresh();
            OverlayChromeStyleComboBox?.Items.Refresh();
            ExplorationCacheOptions[0].Label = LocalizationService.Instance.Format("Loc_Hours_Format", 24);
            ExplorationCacheOptions[1].Label = LocalizationService.Instance.Format("Loc_Days_Format", 3);
            ExplorationCacheOptions[2].Label = LocalizationService.Instance.Format("Loc_Days_Format", 7);
            ExplorationCacheOptions[3].Label = LocalizationService.Instance.Format("Loc_Days_Format", 30);
            ExplorationCacheComboBox?.Items.Refresh();
            ExplorationSpoilerOptions[0].Label = Text("Loc_Exploration_spoilers_journal_only");
            ExplorationSpoilerOptions[1].Label = Text("Loc_Exploration_spoilers_enrich_scanned");
            ExplorationSpoilerOptions[2].Label = Text("Loc_Exploration_spoilers_full_catalog");
            ExplorationSpoilerComboBox?.Items.Refresh();
            LoadJournalSettings();
            LoadExplorationDataSettings();
            LoadExperimentalDssSettings();
            LoadX52Settings();
            _ = RefreshStorageUsageAsync();
            UpdateThemeDetails();
            UpdateColorSwatches();
        }

        private void LoadJournalSettings()
        {
            var settings = SettingsService.Instance.Settings;
            EnableJournalCheckBox.IsChecked = settings.EnableJournalIntegration;
            JournalDirectoryTextBox.Text = settings.JournalDirectory;
            RefreshJournalStatus();
        }

        private void RefreshJournalStatus()
        {
            var settings = SettingsService.Instance.Settings;
            var state = JournalMonitorService.Instance.Current;
            JournalStatusText.Text = state.JournalAvailable
                ? Loc.Format(
                    "Loc_Journal_connected_format",
                    string.IsNullOrWhiteSpace(state.StarSystem) ? Loc.Get("Loc_Waiting_for_game_data") : state.StarSystem,
                    state.JournalDirectory)
                : Loc.Format(
                    "Loc_Journal_not_found_format",
                    string.IsNullOrWhiteSpace(settings.JournalDirectory) ? Loc.Get("Loc_Journal_auto_search_path") : settings.JournalDirectory);
        }

        private void SaveJournalSettings()
        {
            bool enabled = EnableJournalCheckBox.IsChecked == true;
            string directory = JournalDirectoryTextBox.Text.Trim();
            SettingsService.Instance.SetJournalSettings(enabled, directory);
            if (enabled)
            {
                JournalMonitorService.Instance.Restart(directory);
            }
            else
            {
                JournalMonitorService.Instance.Stop();
            }
        }

        private void LoadExperimentalDssSettings()
        {
            AppSettings settings =
                SettingsService.Instance.Settings;

            EnableExperimentalDssAssistantCheckBox.IsChecked =
                settings.EnableExperimentalDssAssistant;

            DssLogDirectoryTextBox.Text =
                settings.DssResearchLogDirectory;

            UpdateDssLogDirectoryPreview();
        }

        private bool SaveExperimentalDssSettings()
        {
            string configured =
                DssLogDirectoryTextBox.Text.Trim();

            try
            {
                _ =
                    DssResearchPathResolver.Resolve(
                        configured);
            }
            catch
            {
                ApplyStatusText.Text =
                    Loc.Get(
                        "Loc_DSS_LOG_PATH_INVALID");

                return false;
            }

            SettingsService.Instance.SetExperimentalDssSettings(
                EnableExperimentalDssAssistantCheckBox.IsChecked == true,
                configured);

            UpdateDssLogDirectoryPreview();

            return true;
        }

        private void DssLogDirectoryTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            UpdateDssLogDirectoryPreview();
        }

        private void UpdateDssLogDirectoryPreview()
        {
            if (DssLogDirectoryResolvedText is null
                || DssLogDirectoryTextBox is null)
            {
                return;
            }

            try
            {
                string resolved =
                    DssResearchPathResolver.Resolve(
                        DssLogDirectoryTextBox.Text);

                DssLogDirectoryResolvedText.Text =
                    Loc.Format(
                        "Loc_DSS_LOG_RESOLVED_FORMAT",
                        resolved);
            }
            catch
            {
                DssLogDirectoryResolvedText.Text =
                    Loc.Get(
                        "Loc_DSS_LOG_PATH_INVALID");
            }
        }

        private void BrowseDssLogDirectoryButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string initial;

            try
            {
                initial =
                    DssResearchPathResolver.Resolve(
                        DssLogDirectoryTextBox.Text);
            }
            catch
            {
                initial =
                    DssResearchPathResolver.DefaultRoot;
            }

            var dialog =
                new OpenFolderDialog
                {
                    Title =
                        Loc.Get(
                            "Loc_DSS_SELECT_LOG_DIRECTORY"),
                    InitialDirectory =
                        Directory.Exists(
                            initial)
                            ? initial
                            : Environment.GetFolderPath(
                                Environment.SpecialFolder.UserProfile)
                };

            if (dialog.ShowDialog() == true)
            {
                DssLogDirectoryTextBox.Text =
                    dialog.FolderName;
            }
        }

        private void OpenDssLogDirectoryButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                string directory =
                    DssResearchPathResolver.Resolve(
                        DssLogDirectoryTextBox.Text);

                Directory.CreateDirectory(
                    directory);

                Process.Start(
                    new ProcessStartInfo(
                        directory)
                    {
                        UseShellExecute =
                            true
                    });
            }
            catch
            {
                ApplyStatusText.Text =
                    Loc.Get(
                        "Loc_DSS_LOG_PATH_INVALID");
            }
        }

        private void LoadExplorationDataSettings()
        {
            AppSettings settings = SettingsService.Instance.Settings;
            EnableOnlineExplorationDataCheckBox.IsChecked = settings.EnableOnlineExplorationData;
            EnableEdsmFallbackCheckBox.IsChecked = settings.EnableEdsmFallback;
            EnableExplorationPoiDataCheckBox.IsChecked = settings.EnableExplorationPoiData;
            ExplorationCacheComboBox.ItemsSource = ExplorationCacheOptions;
            ExplorationCacheComboBox.SelectedItem = ExplorationCacheOptions.FirstOrDefault(option =>
                option.Value == settings.ExplorationCacheHours.ToString()) ?? ExplorationCacheOptions[2];
            ExplorationSpoilerComboBox.ItemsSource = ExplorationSpoilerOptions;
            ExplorationSpoilerComboBox.SelectedItem = ExplorationSpoilerOptions.First(option =>
                option.Value == ExplorationSpoilerModes.Normalize(settings.ExplorationSpoilerMode));
            ExplorationPoiRatingComboBox.ItemsSource = ExplorationPoiRatingOptions;
            ExplorationPoiRatingComboBox.SelectedItem = ExplorationPoiRatingOptions.FirstOrDefault(option =>
                option.Value == settings.ExplorationPoiMinRating.ToString()) ?? ExplorationPoiRatingOptions[2];

            RefreshExternalDataStatus();
        }

        private void SaveExplorationDataSettings()
        {
            int cacheHours = ExplorationCacheComboBox.SelectedItem is HotkeyOption option
                && int.TryParse(option.Value, out int parsed)
                ? parsed
                : 168;
            SettingsService.Instance.SetExplorationDataSettings(
                EnableOnlineExplorationDataCheckBox.IsChecked == true,
                EnableEdsmFallbackCheckBox.IsChecked == true,
                cacheHours,
                ExplorationSpoilerComboBox.SelectedItem is HotkeyOption spoilers
                    ? spoilers.Value
                    : ExplorationSpoilerModes.EnrichScanned,
                EnableExplorationPoiDataCheckBox.IsChecked == true,
                ExplorationPoiRatingComboBox.SelectedItem is HotkeyOption poiRating
                    && int.TryParse(poiRating.Value, out int parsedRating) ? parsedRating : 4);

            RefreshExternalDataStatus();
        }

        private void LoadRouteAutomationSettings()
        {
            AppSettings settings = SettingsService.Instance.Settings;
            EnableExperimentalRouteAutomationCheckBox.IsChecked =
                settings.EnableExperimentalRouteAutomation;

            string currentSelection =
                EliteBindingsPresetComboBox.SelectedItem is HotkeyOption selected
                    ? selected.Value
                    : settings.EliteBindingsFilePath;

            var files = new List<HotkeyOption>
            {
                new()
                {
                    Label = Loc.Get("Loc_AUTOMATIC_FROM_ELITE"),
                    Value = string.Empty
                }
            };

            files.AddRange(
                EliteBindingsService.ListBindingFiles()
                    .Select(item => new HotkeyOption
                    {
                        Label = item.DisplayName,
                        Value = item.FilePath
                    }));

            string desired =
                !string.IsNullOrWhiteSpace(currentSelection)
                    ? currentSelection
                    : settings.EliteBindingsFilePath;

            if (!string.IsNullOrWhiteSpace(desired)
                && files.All(option =>
                    !string.Equals(
                        option.Value,
                        desired,
                        StringComparison.OrdinalIgnoreCase)))
            {
                files.Add(new HotkeyOption
                {
                    Label = Loc.Format(
                        "Loc_BINDINGS_FILE_MISSING_FORMAT",
                        Path.GetFileName(desired)),
                    Value = desired
                });
            }

            EliteBindingsPresetComboBox.ItemsSource = files;
            EliteBindingsPresetComboBox.SelectedItem =
                files.FirstOrDefault(option =>
                    string.Equals(
                        option.Value,
                        desired,
                        StringComparison.OrdinalIgnoreCase))
                ?? files[0];

            RouteAutomationMapDelayTextBox.Text =
                Math.Clamp(
                    settings.RouteAutomationMapDelayMs,
                    3000,
                    15000).ToString();

            RouteAutomationStepDelayTextBox.Text =
                settings.RouteAutomationStepDelayMs.ToString();

            RefreshRouteBindingsStatus();
        }

        private void RefreshRouteBindingsStatus()
        {
            AppSettings settings = SettingsService.Instance.Settings;

            string selectedFile =
                EliteBindingsPresetComboBox.SelectedItem is HotkeyOption option
                    ? option.Value
                    : settings.EliteBindingsFilePath;

            try
            {
                EliteNavigationBindings bindings =
                    EliteBindingsService.Detect(
                        presetOverride: settings.EliteBindingsPreset,
                        fileOverride: selectedFile);

                RouteAutomationBindingsStatusText.Text =
                    Loc.Format(
                        "Loc_NAVIGATION_BINDINGS_FILE_STATUS",
                        Path.GetFileName(bindings.FilePath),
                        bindings.PresetName,
                        bindings.GalaxyMap.DisplayName,
                        bindings.NextPanel.DisplayName,
                        bindings.Select.DisplayName);
            }
            catch (Exception ex)
            {
                RouteAutomationBindingsStatusText.Text =
                    Loc.Format(
                        "Loc_NAVIGATION_BINDINGS_ERROR",
                        ex.Message);
            }
        }

        private void EliteBindingsPresetComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            RefreshRouteBindingsStatus();
        }

        private void RefreshBindingsFilesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LoadRouteAutomationSettings();
        }

        private void OpenBindingsFolderButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string directory =
                EliteBindingsService.DefaultBindingsDirectory;

            if (!Directory.Exists(directory))
            {
                RouteAutomationBindingsStatusText.Text =
                    Loc.Format(
                        "Loc_NAVIGATION_BINDINGS_ERROR",
                        directory);
                return;
            }

            Process.Start(
                new ProcessStartInfo(directory)
                {
                    UseShellExecute = true
                });
        }

        private void SaveRouteAutomationSettings()
        {
            AppSettings current = SettingsService.Instance.Settings;

            int mapDelay =
                int.TryParse(
                    RouteAutomationMapDelayTextBox.Text,
                    out int parsedMap)
                    ? parsedMap
                    : current.RouteAutomationMapDelayMs;

            int stepDelay =
                int.TryParse(
                    RouteAutomationStepDelayTextBox.Text,
                    out int parsedStep)
                    ? parsedStep
                    : current.RouteAutomationStepDelayMs;

            string selectedFile =
                EliteBindingsPresetComboBox.SelectedItem is HotkeyOption option
                    ? option.Value
                    : string.Empty;

            SettingsService.Instance.SetRouteAutomationSettings(
                EnableExperimentalRouteAutomationCheckBox.IsChecked == true,
                current.EliteBindingsPreset,
                selectedFile,
                mapDelay,
                stepDelay,
                current.RouteAutomationVerificationSeconds);

            LoadRouteAutomationSettings();
        }

        private void RefreshExternalDataStatus()
        {
            ExplorationDataState data = ExplorationDataService.Instance.Current;
            ExternalDataStatusText.Text = data.Status switch
            {
                ExplorationDataStatus.Available when data.System is { } system => Loc.Format(
                    "Loc_Exploration_settings_status_format", system.Source, system.SystemName, system.BodyCount),
                ExplorationDataStatus.Loading => Loc.Get("Loc_Exploration_online_loading"),
                ExplorationDataStatus.Disabled => Loc.Get("Loc_Exploration_online_disabled"),
                ExplorationDataStatus.Unavailable => Loc.Get("Loc_Exploration_online_unavailable"),
                _ => Loc.Get("Loc_Exploration_online_waiting")
            };
        }

        private void RefreshExplorationDataButton_Click(object sender, RoutedEventArgs e)
        {
            SaveExplorationDataSettings();
            ExplorationDataService.Instance.Refresh();
            RefreshExternalDataStatus();
        }

        private async Task RefreshStorageUsageAsync(string? resultMessage = null)
        {
            StorageUsageSnapshot usage = await Task.Run(StorageUsageService.Measure);
            StorageUsageText.Text = Loc.Format("Loc_STORAGE_USAGE_FORMAT",
                FormatBytes(usage.InstallationBytes), FormatBytes(usage.PersistentDataBytes),
                FormatBytes(usage.DatabaseBytes), FormatBytes(usage.CacheBytes));
            if (!string.IsNullOrWhiteSpace(resultMessage))
                StorageUsageText.Text += Environment.NewLine + resultMessage;
            StoragePathText.Text = Loc.Format("Loc_STORAGE_PATH_FORMAT",
                usage.PersistentDataDirectory, usage.CacheDirectory);
        }

        private async void RefreshStorageUsageButton_Click(object sender, RoutedEventArgs e) =>
            await RefreshStorageUsageAsync();

        private async void CleanupStorageButton_Click(object sender, RoutedEventArgs e)
        {
            int hours = SettingsService.Instance.Settings.ExplorationCacheHours;
            int deleted = await Task.Run(() => StorageUsageService.CleanupExpiredCaches(TimeSpan.FromHours(hours)));
            await RefreshStorageUsageAsync(Loc.Format("Loc_CACHE_CLEANED_FORMAT", deleted));
        }

        private static string FormatBytes(long value)
        {
            string[] units = ["B", "KB", "MB", "GB"];
            double size = Math.Max(0, value);
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return $"{size:N1} {units[unit]}";
        }

        private void LoadX52Settings()
        {
            AppSettings settings = SettingsService.Instance.Settings;
            EnableX52SupportCheckBox.IsChecked = settings.EnableX52Support;
            EnableX52MfdCheckBox.IsChecked = settings.EnableX52Mfd;
            EnableX52LedCheckBox.IsChecked = settings.EnableX52LedState;
            EnableX52ControlsCheckBox.IsChecked = settings.EnableX52MfdControls;
            RefreshX52Status();
        }

        private void SaveX52Settings()
        {
            SettingsService.Instance.SetX52Settings(
                EnableX52SupportCheckBox.IsChecked == true,
                EnableX52MfdCheckBox.IsChecked == true,
                EnableX52LedCheckBox.IsChecked == true,
                EnableX52ControlsCheckBox.IsChecked == true);
            RefreshX52Status();
        }

        private void RefreshX52Status()
        {
            X52IntegrationState current = X52IntegrationService.Instance.Current;
            X52StatusText.Text = current.Status switch
            {
                X52ConnectionStatus.Disabled => Loc.Get("Loc_X52_status_disabled"),
                X52ConnectionStatus.DriverMissing => Loc.Get("Loc_X52_status_driver_missing"),
                X52ConnectionStatus.WaitingForDevice => Loc.Format("Loc_X52_status_waiting_format", current.DriverPath),
                X52ConnectionStatus.Connected => Loc.Format("Loc_X52_status_connected_format", current.DriverPath),
                X52ConnectionStatus.Error => Loc.Format("Loc_X52_status_error_format", current.Error),
                _ => current.Status.ToString()
            };
        }

        private void ReconnectX52Button_Click(object sender, RoutedEventArgs e)
        {
            SaveX52Settings();
            X52IntegrationService.Instance.Reconnect();
            RefreshX52Status();
        }

        private void OpenX52CheatsheetButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new X52ControlHelpWindow { Owner = this };
            window.ShowDialog();
        }

        private void OnX52StateChanged(object? sender, X52StateChangedEventArgs e) =>
            Dispatcher.BeginInvoke(new Action(RefreshX52Status));

        private void OnExplorationDataChanged(object? sender, ExplorationDataChangedEventArgs e) =>
            Dispatcher.BeginInvoke(new Action(RefreshExternalDataStatus));

        private void SettingsWindow_Closed(object? sender, EventArgs e)
        {
            ExplorationDataService.Instance.DataChanged -= OnExplorationDataChanged;
            X52IntegrationService.Instance.StateChanged -= OnX52StateChanged;
            Closed -= SettingsWindow_Closed;
        }

        private void BrowseJournalButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = Loc.Get("Loc_Select_the_Elite_Dangerous_Journal_directory"),
                InitialDirectory = string.IsNullOrWhiteSpace(JournalDirectoryTextBox.Text)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    : JournalDirectoryTextBox.Text
            };
            if (dialog.ShowDialog() == true)
            {
                JournalDirectoryTextBox.Text = dialog.FolderName;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!overlayMode || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }
            try { DragMove(); } catch (InvalidOperationException) { }
        }

        private void ImportThemeButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = Loc.Get("Loc_XML_files_xml_xml"),
                Title = Loc.Get("Loc_Import_theme")
            };

            if (openFileDialog.ShowDialog() == true)
            {
                if (ThemeManager.Instance.ImportTheme(openFileDialog.FileName))
                {
                    MessageBox.Show(Loc.Get("Loc_Theme_imported_successfully"), Loc.Get("Loc_Done"), MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadThemes();
                }
                else
                {
                    MessageBox.Show(Loc.Get("Loc_Failed_to_import_theme"), Loc.Get("Loc_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportThemeButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedTheme = ThemeComboBox.SelectedItem as Models.Theme;
            if (selectedTheme == null)
            {
                MessageBox.Show(Loc.Get("Loc_Select_a_theme_to_export"), Loc.Get("Loc_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var saveFileDialog = new SaveFileDialog
            {
                Filter = Loc.Get("Loc_XML_files_xml_xml"),
                Title = Loc.Get("Loc_Export_theme"),
                FileName = selectedTheme.Name + ".xml"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                if (ThemeManager.Instance.ExportTheme(selectedTheme.Name, saveFileDialog.FileName))
                {
                    MessageBox.Show(Loc.Get("Loc_Theme_exported_successfully"), Loc.Get("Loc_Done"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(Loc.Get("Loc_Failed_to_export_theme"), Loc.Get("Loc_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void RefreshThemesButton_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.Instance.LoadAvailableThemes();
            LoadThemes();
            MessageBox.Show(Loc.Get("Loc_Theme_list_refreshed"), Loc.Get("Loc_Done"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UpdateThemeDetails()
        {
            try
            {
                if (ThemeComboBox.SelectedItem is Models.Theme selectedTheme)
                {
                    ThemeNameText.Text = selectedTheme.Name ?? Loc.Get("Loc_Untitled");
                    ThemeDescriptionText.Text = selectedTheme.Description ?? Loc.Get("Loc_No_description_available");
                    ThemeAuthorText.Text = Loc.Format("Loc_Author_format", selectedTheme.Author ?? Loc.Get("Loc_unknown"));
                }
                else
                {
                    ThemeNameText.Text = Loc.Get("Loc_No_theme_selected");
                    ThemeDescriptionText.Text = Loc.Get("Loc_Select_a_theme_to_view_details");
                    ThemeAuthorText.Text = Loc.Get("Loc_Author_unknown");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating theme details: {ex.Message}");
            }
        }

        private void UpdateColorSwatches()
        {
            try
            {
                ColorSwatchGrid.Children.Clear();
                if (ThemeComboBox.SelectedItem is Models.Theme selectedTheme && selectedTheme.Colors != null)
                {
                    int column = 0;
                    foreach (var color in selectedTheme.Colors)
                    {
                        if (column >= ColorSwatchGrid.ColumnDefinitions.Count)
                        {
                            // Only show a limited number of swatches for brevity
                            break;
                        }

                        try
                        {
                            var colorValue = (Color)ColorConverter.ConvertFromString(color.Value);
                            var border = new Border
                            {
                                Background = new SolidColorBrush(colorValue),
                                BorderBrush = new SolidColorBrush(Colors.Black),
                                BorderThickness = new Thickness(1),
                                Width = 40,
                                Height = 40,
                                Margin = new Thickness(5)
                            };

                            var textBlock = new TextBlock
                            {
                                Text = GetColorDisplayName(color.Key),
                                Foreground = new SolidColorBrush(Colors.White),
                                FontSize = 8,
                                FontWeight = FontWeights.Bold,
                                TextAlignment = TextAlignment.Center
                            };

                            var stackPanel = new StackPanel
                            {
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            };
                            stackPanel.Children.Add(border);
                            stackPanel.Children.Add(textBlock);

                            Grid.SetColumn(stackPanel, column);
                            ColorSwatchGrid.Children.Add(stackPanel);

                            column++;
                        }
                        catch (Exception ex)
                        {
                            // Skip invalid colors
                            System.Diagnostics.Debug.WriteLine($"Error parsing color {color.Key}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating color swatches: {ex.Message}");
            }
        }

        private static string GetColorDisplayName(string key) => key switch
        {
            "PrimaryBackgroundColor" => Loc.Get("Loc_Color_primary_background"),
            "SecondaryBackgroundColor" => Loc.Get("Loc_Color_secondary_background"),
            "HighlightBackgroundColor" => Loc.Get("Loc_Color_highlight_background"),
            "BorderColor" => Loc.Get("Loc_Color_border"),
            "ButtonBackgroundColor" => Loc.Get("Loc_Color_button_background"),
            "PrimaryColor" => Loc.Get("Loc_Color_primary_accent"),
            "AccentColor" => Loc.Get("Loc_Color_accent"),
            "SuccessColor" => Loc.Get("Loc_Color_success"),
            "FailureColor" => Loc.Get("Loc_Error"),
            "SecondaryTextColor" => Loc.Get("Loc_Color_secondary_text"),
            "MutedTextColor" => Loc.Get("Loc_Color_muted_text"),
            "PrimaryTextColor" => Loc.Get("Loc_Color_primary_text"),
            _ => key
        };

        private void LoadHotkeySettings()
        {
            HotkeyModifierComboBox.ItemsSource = ModifierOptions;
            HotkeyModifierComboBox.DisplayMemberPath = nameof(HotkeyOption.Label);

            HotkeyKeyComboBox.ItemsSource = KeyOptions;
            HotkeyKeyComboBox.DisplayMemberPath = nameof(HotkeyOption.Label);

            InteractiveModifierComboBox.ItemsSource = ModifierOptions;
            InteractiveModifierComboBox.DisplayMemberPath = nameof(HotkeyOption.Label);

            InteractiveKeyComboBox.ItemsSource = KeyOptions;
            InteractiveKeyComboBox.DisplayMemberPath = nameof(HotkeyOption.Label);

            ConfigureHotkeyPair(TradeModifierComboBox, TradeKeyComboBox);
            ConfigureHotkeyPair(EngineeringModifierComboBox, EngineeringKeyComboBox);
            ConfigureHotkeyPair(ExplorationModifierComboBox, ExplorationKeyComboBox);
            ConfigureHotkeyPair(MiningModifierComboBox, MiningKeyComboBox);

            TimeoutComboBox.ItemsSource = TimeoutOptions;
            TimeoutComboBox.DisplayMemberPath = nameof(HotkeyOption.Label);

            PinnedPositionComboBox.ItemsSource = PinnedPositionOptions;
            PinnedPositionComboBox.DisplayMemberPath = nameof(HotkeyOption.Label);

            ShipStatusPositionComboBox.ItemsSource = PinnedPositionOptions;
            ShipStatusPositionComboBox.DisplayMemberPath = nameof(HotkeyOption.Label);

            OverlayChromeStyleComboBox.ItemsSource = OverlayChromeStyleOptions;
            OverlayChromeStyleComboBox.DisplayMemberPath = nameof(HotkeyOption.Label);

            var settings = SettingsService.Instance.Settings;
            var (modifiers, key) = SettingsService.Instance.GetToggleHotkey();
            var (interactiveModifiers, interactiveKey) = SettingsService.Instance.GetInteractiveHotkey();

            HotkeyModifierComboBox.SelectedItem = ModifierOptions.FirstOrDefault(o => o.Value == modifiers) ?? ModifierOptions[0];
            HotkeyKeyComboBox.SelectedItem = KeyOptions.FirstOrDefault(o => o.Value == key) ?? KeyOptions.FirstOrDefault(o => o.Value == "D5");
            InteractiveModifierComboBox.SelectedItem = ModifierOptions.FirstOrDefault(o => o.Value == interactiveModifiers) ?? ModifierOptions[0];
            InteractiveKeyComboBox.SelectedItem = KeyOptions.FirstOrDefault(o => o.Value == interactiveKey) ?? KeyOptions.FirstOrDefault(o => o.Value == "D6");
            SelectHotkeyPair(TradeModifierComboBox, TradeKeyComboBox, settings.TradeHotkeyModifiers, settings.TradeHotkeyKey, "D1");
            SelectHotkeyPair(EngineeringModifierComboBox, EngineeringKeyComboBox, settings.EngineeringHotkeyModifiers, settings.EngineeringHotkeyKey, "D2");
            SelectHotkeyPair(ExplorationModifierComboBox, ExplorationKeyComboBox, settings.ExplorationHotkeyModifiers, settings.ExplorationHotkeyKey, "D3");
            SelectHotkeyPair(MiningModifierComboBox, MiningKeyComboBox, settings.MiningHotkeyModifiers, settings.MiningHotkeyKey, "D4");

            EnableInteractionModeCheckBox.IsChecked = settings.EnableInteractionMode;
            ReturnOnFocusLossCheckBox.IsChecked = settings.ReturnOnFocusLoss;
            ShowCursorWhenInteractiveCheckBox.IsChecked = settings.ShowCursorWhenInteractive;
            EnableNotificationsCheckBox.IsChecked = settings.EnableOverlayNotifications;
            EnableShipStatusWidgetCheckBox.IsChecked = settings.EnableShipStatusWidget;
            NotificationDurationComboBox.SelectedItem = NotificationDurationComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), settings.NotificationDurationSeconds.ToString(), StringComparison.Ordinal))
                ?? NotificationDurationComboBox.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == "6");
            TimeoutComboBox.SelectedItem = TimeoutOptions.FirstOrDefault(o => o.Value == settings.AutoReturnTimeoutSeconds.ToString()) ?? TimeoutOptions.FirstOrDefault(o => o.Value == "8");
            PinnedPositionComboBox.SelectedItem = PinnedPositionOptions.FirstOrDefault(
                o => o.Value.Equals(settings.PinnedRoutePosition, StringComparison.OrdinalIgnoreCase)) ?? PinnedPositionOptions[0];
            ShipStatusPositionComboBox.SelectedItem = PinnedPositionOptions.FirstOrDefault(
                o => o.Value.Equals(settings.ShipStatusWidgetPosition, StringComparison.OrdinalIgnoreCase)) ?? PinnedPositionOptions[3];
            OverlayChromeStyleComboBox.SelectedItem = OverlayChromeStyleOptions.FirstOrDefault(
                o => o.Value.Equals(Utils.OverlayChromeStyles.Normalize(settings.OverlayChromeStyle), StringComparison.Ordinal))
                ?? OverlayChromeStyleOptions[0];
        }

        private static void ConfigureHotkeyPair(ComboBox modifier, ComboBox key)
        {
            modifier.ItemsSource = ModifierOptions;
            modifier.DisplayMemberPath = nameof(HotkeyOption.Label);
            key.ItemsSource = KeyOptions;
            key.DisplayMemberPath = nameof(HotkeyOption.Label);
        }

        private static void SelectHotkeyPair(ComboBox modifier, ComboBox key, string modifiers, string keyValue, string fallbackKey)
        {
            modifier.SelectedItem = ModifierOptions.FirstOrDefault(option => option.Value == modifiers) ?? ModifierOptions[0];
            key.SelectedItem = KeyOptions.FirstOrDefault(option => option.Value == keyValue)
                ?? KeyOptions.First(option => option.Value == fallbackKey);
        }

        private void SaveHotkeySettings()
        {
            if (HotkeyModifierComboBox.SelectedItem is not HotkeyOption modifierOption ||
                HotkeyKeyComboBox.SelectedItem is not HotkeyOption keyOption)
            {
                return;
            }

            SettingsService.Instance.SetToggleHotkey(modifierOption.Value, keyOption.Value);

            if (InteractiveModifierComboBox.SelectedItem is HotkeyOption interactiveModifierOption
                && InteractiveKeyComboBox.SelectedItem is HotkeyOption interactiveKeyOption)
            {
                SettingsService.Instance.SetInteractiveHotkey(interactiveModifierOption.Value, interactiveKeyOption.Value);
            }

            if (TradeModifierComboBox.SelectedItem is HotkeyOption tradeModifier
                && TradeKeyComboBox.SelectedItem is HotkeyOption tradeKey
                && EngineeringModifierComboBox.SelectedItem is HotkeyOption engineeringModifier
                && EngineeringKeyComboBox.SelectedItem is HotkeyOption engineeringKey
                && ExplorationModifierComboBox.SelectedItem is HotkeyOption explorationModifier
                && ExplorationKeyComboBox.SelectedItem is HotkeyOption explorationKey
                && MiningModifierComboBox.SelectedItem is HotkeyOption miningModifier
                && MiningKeyComboBox.SelectedItem is HotkeyOption miningKey)
            {
                SettingsService.Instance.SetActivityHotkeys(
                    tradeModifier.Value, tradeKey.Value,
                    engineeringModifier.Value, engineeringKey.Value,
                    explorationModifier.Value, explorationKey.Value,
                    miningModifier.Value, miningKey.Value);
            }

            bool enableInteractionMode = EnableInteractionModeCheckBox.IsChecked == true;
            bool returnOnFocusLoss = ReturnOnFocusLossCheckBox.IsChecked == true;
            bool showCursor = ShowCursorWhenInteractiveCheckBox.IsChecked == true;
            int timeout = 8;
            if (TimeoutComboBox.SelectedItem is HotkeyOption timeoutOption
                && int.TryParse(timeoutOption.Value, out int parsedTimeout))
            {
                timeout = parsedTimeout;
            }

            SettingsService.Instance.SetInteractionBehavior(enableInteractionMode, timeout, returnOnFocusLoss, showCursor);
            int notificationDuration = NotificationDurationComboBox.SelectedItem is ComboBoxItem durationItem
                && int.TryParse(durationItem.Tag?.ToString(), out int parsedDuration)
                    ? parsedDuration
                    : 6;
            SettingsService.Instance.SetNotificationSettings(
                EnableNotificationsCheckBox.IsChecked == true,
                notificationDuration);
            SettingsService.Instance.SetShipStatusWidgetSettings(
                EnableShipStatusWidgetCheckBox.IsChecked == true,
                ShipStatusPositionComboBox.SelectedItem is HotkeyOption shipStatusPosition
                    ? shipStatusPosition.Value : "TopCenter");
            if (PinnedPositionComboBox.SelectedItem is HotkeyOption pinnedPosition)
            {
                SettingsService.Instance.SetPinnedRoutePosition(pinnedPosition.Value);
            }
            if (OverlayChromeStyleComboBox.SelectedItem is HotkeyOption chromeStyle)
            {
                SettingsService.Instance.SetOverlayChromeStyle(chromeStyle.Value);
            }
        }
    }
}
