param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

function Read-Text([string]$Path) {
    if (-not (Test-Path $Path)) {
        throw "Required file not found: $Path"
    }

    return ([System.IO.File]::ReadAllText((Resolve-Path $Path).Path)).Replace("`r`n", "`n")
}

function Write-Text([string]$Path, [string]$Text) {
    $full = if (Test-Path $Path) {
        (Resolve-Path $Path).Path
    }
    else {
        Join-Path (Get-Location) $Path
    }

    $old = if (Test-Path $Path) {
        [System.IO.File]::ReadAllText($full)
    }
    else {
        ""
    }

    $newline = if ($old.Contains("`r`n")) { "`r`n" } else { "`n" }
    $normalized = $Text.Replace("`r`n", "`n")

    if ($newline -eq "`r`n") {
        $normalized = $normalized.Replace("`n", "`r`n")
    }

    $directory = Split-Path -Parent $full
    if ($directory -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    [System.IO.File]::WriteAllText(
        $full,
        $normalized,
        [System.Text.UTF8Encoding]::new($false)
    )
}

function Replace-LiteralOnce(
    [string]$Path,
    [string]$Old,
    [string]$New,
    [string]$Description
) {
    $text = Read-Text $Path
    $count = ([regex]::Matches($text, [regex]::Escape($Old))).Count

    if ($count -ne 1) {
        throw "Expected exactly one $Description in $Path, found $count."
    }

    Write-Text $Path ($text.Replace($Old, $New))
}

function Replace-RegexOnce(
    [string]$Path,
    [string]$Pattern,
    [string]$Replacement,
    [string]$Description
) {
    $text = Read-Text $Path
    $regex = [regex]::new(
        $Pattern,
        [System.Text.RegularExpressions.RegexOptions]::Singleline
    )

    $matches = $regex.Matches($text)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one $Description in $Path, found $($matches.Count)."
    }

    Write-Text $Path ($regex.Replace($text, $Replacement, 1))
}

function Add-LocalizationEntries(
    [string]$Path,
    [hashtable]$Entries
) {
    $text = Read-Text $Path

    $missing = @()
    foreach ($key in $Entries.Keys) {
        if (-not $text.Contains("x:Key=`"$key`"")) {
            $value = [System.Security.SecurityElement]::Escape([string]$Entries[$key])
            $missing += "    <sys:String x:Key=`"$key`">$value</sys:String>"
        }
    }

    if ($missing.Count -eq 0) {
        return
    }

    $block = ($missing -join "`n") + "`n"
    if (-not $text.Contains('</ResourceDictionary>')) {
        throw "Could not locate ResourceDictionary end in $Path."
    }

    Write-Text $Path ($text.Replace(
        '</ResourceDictionary>',
        $block + '</ResourceDictionary>'))
}

$branch = (& git rev-parse --abbrev-ref HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
    throw 'Run this script from the repository root.'
}

Write-Host "Current branch: $branch" -ForegroundColor DarkGray

$xamlPath = 'ED_Inara_Overlay\Windows\ActivityWorkspaceOverlayWindow.xaml'
$codePath = 'ED_Inara_Overlay\Windows\ActivityWorkspaceOverlayWindow.xaml.cs'
$visitModelsPath = 'ED_Inara_Overlay\Models\ExplorationVisitModels.cs'
$visitServicePath = 'ED_Inara_Overlay\Services\Exploration\ExplorationVisitStateService.cs'
$progressPath = 'ED_Inara_Overlay\Models\BodyExplorationProgress.cs'
$localizationEnPath = 'ED_Inara_Overlay\Resources\Localization.en-US.xaml'
$localizationRuPath = 'ED_Inara_Overlay\Resources\Localization.ru-RU.xaml'
$testsPath = 'Testing\ED_Inara_Overlay.LayoutTests\AdaptiveExplorationHudTests.cs'

foreach ($required in @(
    $xamlPath,
    $codePath,
    $visitModelsPath,
    $visitServicePath,
    $progressPath,
    $localizationEnPath,
    $localizationRuPath
)) {
    if (-not (Test-Path $required)) {
        throw "Required file not found: $required. Apply patches 1 and 2 first."
    }
}

$visitServiceCheck = Read-Text $visitServicePath
if (-not $visitServiceCheck.Contains('public sealed class ExplorationVisitStateService')) {
    throw 'exploration-visit-queue does not appear to be installed.'
}

$backup = 'adaptive-exploration-hud-v3-before.patch'
& git diff --binary -- `
    $xamlPath $codePath $localizationEnPath $localizationRuPath $testsPath |
    Set-Content -Path $backup -Encoding utf8

Write-Host "Saved current diff to $backup" -ForegroundColor DarkGray
Write-Host 'Applying adaptive exploration HUD...' -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# 1. Compact window: slightly wider/taller to fit a fixed, non-scroll HUD.
# ---------------------------------------------------------------------------
$xaml = Read-Text $xamlPath
if ($xaml.Contains('Title="{DynamicResource Loc_Activity_assistant}" Width="400" Height="330"')) {
    $xaml = $xaml.Replace(
        'Title="{DynamicResource Loc_Activity_assistant}" Width="400" Height="330"',
        'Title="{DynamicResource Loc_Activity_assistant}" Width="420" Height="350"')
    Write-Text $xamlPath $xaml
}

# ---------------------------------------------------------------------------
# 2. Replace compact location + scrolling content with:
#    - legacy mining content
#    - fixed adaptive exploration content
#      SYSTEM / BODY TARGET / EXOBIOLOGY
#
# The exploration HUD itself is never inside a ScrollViewer.
# ---------------------------------------------------------------------------
$compactContent = @'
            <Border x:Name="LegacyLocationPanel" Grid.Row="1" Margin="0,10,0,8" Padding="9,6"
                    Background="{DynamicResource HighlightBackgroundBrush}">
                <StackPanel>
                    <TextBlock x:Name="LocationText" Text="{DynamicResource Loc_SYSTEM}"
                               Style="{DynamicResource ClickableTextStyle}"
                               ToolTip="{DynamicResource Loc_CLICK_TO_COPY_SYSTEM}"
                               MouseLeftButtonUp="CopyCurrentSystemText_MouseLeftButtonUp"/>
                    <TextBlock x:Name="FlightStateText" Text="{DynamicResource Loc_Waiting_for_journal}"
                               FontSize="10" Foreground="{DynamicResource SecondaryTextColorBrush}"/>
                    <TextBlock x:Name="ExternalDataText" Margin="0,2,0,0" TextWrapping="Wrap" Visibility="Collapsed"
                               FontSize="9" Foreground="{DynamicResource MutedTextColorBrush}"/>
                </StackPanel>
            </Border>

            <Grid Grid.Row="2">
                <StackPanel x:Name="AdaptiveExplorationPanel" Visibility="Collapsed">
                    <Border Padding="9,7" Margin="0,1,0,7"
                            Background="{DynamicResource HighlightBackgroundBrush}"
                            BorderBrush="{DynamicResource AccentColorBrush}"
                            BorderThickness="2,0,0,0">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <StackPanel>
                                <TextBlock x:Name="CompactModeText"
                                           FontSize="9" FontWeight="SemiBold"
                                           Foreground="{DynamicResource AccentColorBrush}"/>
                                <TextBlock x:Name="CompactContextTitleText"
                                           Margin="0,2,0,0"
                                           FontSize="15" FontWeight="Bold"
                                           TextTrimming="CharacterEllipsis"
                                           Foreground="{DynamicResource PrimaryTextColorBrush}"/>
                            </StackPanel>
                            <TextBlock x:Name="CompactQueueCountText"
                                       Grid.Column="1"
                                       Margin="10,0,0,0"
                                       VerticalAlignment="Center"
                                       TextAlignment="Right"
                                       FontSize="9"
                                       Foreground="{DynamicResource MutedTextColorBrush}"/>
                        </Grid>
                    </Border>

                    <Border x:Name="SystemContextPanel"
                            Padding="9,7" Margin="0,0,0,7"
                            Background="{DynamicResource SecondaryBackgroundColorBrush}"
                            BorderBrush="{DynamicResource BorderColorBrush}"
                            BorderThickness="1,0,0,0">
                        <StackPanel>
                            <ItemsControl x:Name="CompactTargetsItemsControl">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <TextBlock Text="{Binding}"
                                                   Margin="0,0,0,4"
                                                   FontSize="11"
                                                   TextWrapping="Wrap"
                                                   Foreground="{DynamicResource PrimaryTextColorBrush}"/>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                            <TextBlock x:Name="CompactEmptyTargetsText"
                                       Visibility="Collapsed"
                                       TextWrapping="Wrap"
                                       FontSize="11"
                                       Foreground="{DynamicResource MutedTextColorBrush}"/>
                        </StackPanel>
                    </Border>

                    <Border x:Name="BodyContextPanel"
                            Visibility="Collapsed"
                            Padding="9,7" Margin="0,0,0,7"
                            Background="{DynamicResource SecondaryBackgroundColorBrush}"
                            BorderBrush="{DynamicResource AccentColorBrush}"
                            BorderThickness="1,0,0,0">
                        <StackPanel>
                            <TextBlock x:Name="BodyStatusText"
                                       FontWeight="SemiBold"
                                       Foreground="{DynamicResource PrimaryTextColorBrush}"/>
                            <TextBlock x:Name="BodyObjectiveText"
                                       Margin="0,4,0,0"
                                       TextWrapping="Wrap"
                                       Foreground="{DynamicResource SecondaryTextColorBrush}"/>
                            <TextBlock x:Name="BodyMissingText"
                                       Margin="0,3,0,0"
                                       TextWrapping="Wrap"
                                       FontSize="10"
                                       Foreground="{DynamicResource AccentColorBrush}"/>
                            <TextBlock x:Name="BodyMetaText"
                                       Margin="0,4,0,0"
                                       TextWrapping="Wrap"
                                       FontSize="9"
                                       Foreground="{DynamicResource MutedTextColorBrush}"/>
                        </StackPanel>
                    </Border>

                    <StackPanel x:Name="ExobioContextPanel"
                                Visibility="Collapsed">
                        <Border x:Name="SurfaceNavigationPanel"
                                Padding="9,7" Margin="0,0,0,5"
                                Background="{DynamicResource HighlightBackgroundBrush}"
                                BorderBrush="{DynamicResource AccentColorBrush}"
                                BorderThickness="1,0,0,0">
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="58"/>
                                    <ColumnDefinition Width="*"/>
                                </Grid.ColumnDefinitions>
                                <Grid x:Name="SurfaceRadar"
                                      Width="50" Height="50"
                                      VerticalAlignment="Center">
                                    <Ellipse Stroke="{DynamicResource BorderColorBrush}"
                                             StrokeThickness="1"/>
                                    <Ellipse Width="30" Height="30"
                                             Stroke="{DynamicResource MutedTextColorBrush}"
                                             StrokeThickness="1"
                                             StrokeDashArray="2,2"/>
                                    <Ellipse Width="4" Height="4"
                                             Fill="{DynamicResource PrimaryTextColorBrush}"/>
                                    <Path Data="M 25,4 L 20,15 L 25,12 L 30,15 Z"
                                          Fill="{DynamicResource AccentColorBrush}"
                                          RenderTransformOrigin="0.5,0.5">
                                        <Path.RenderTransform>
                                            <RotateTransform x:Name="SurfaceEscapeArrowTransform"
                                                             Angle="0"
                                                             CenterX="25"
                                                             CenterY="25"/>
                                        </Path.RenderTransform>
                                    </Path>
                                </Grid>
                                <StackPanel Grid.Column="1"
                                            VerticalAlignment="Center">
                                    <TextBlock x:Name="SurfaceNavigationText"
                                               TextWrapping="Wrap"
                                               FontWeight="SemiBold"
                                               Foreground="{DynamicResource PrimaryTextColorBrush}"/>
                                </StackPanel>
                            </Grid>
                        </Border>
                        <TextBlock x:Name="ExobioBodyProgressText"
                                   Margin="9,0,9,5"
                                   TextWrapping="Wrap"
                                   FontSize="10"
                                   Foreground="{DynamicResource SecondaryTextColorBrush}"/>
                    </StackPanel>

                    <Border x:Name="CompactRouteAlertPanel"
                            Visibility="Collapsed"
                            Padding="9,6"
                            Background="{DynamicResource SecondaryBackgroundColorBrush}"
                            BorderBrush="{DynamicResource BorderColorBrush}"
                            BorderThickness="1,0,0,0">
                        <TextBlock x:Name="CompactRouteAlertText"
                                   TextWrapping="Wrap"
                                   FontSize="9"
                                   Foreground="{DynamicResource MutedTextColorBrush}"/>
                    </Border>
                </StackPanel>

                <ScrollViewer x:Name="LegacyCompactScrollViewer"
                              VerticalScrollBarVisibility="Auto"
                              HorizontalScrollBarVisibility="Disabled"
                              PanningMode="VerticalOnly">
                    <StackPanel Margin="0,0,5,0">
                        <TextBlock x:Name="PrimaryHintText"
                                   TextWrapping="Wrap"
                                   Margin="0,4,0,9"
                                   Foreground="{DynamicResource PrimaryTextColorBrush}"/>
                        <Border x:Name="FuelAdvicePanel"
                                Padding="9,7" Margin="0,0,0,7"
                                Visibility="Collapsed"
                                Background="{DynamicResource SecondaryBackgroundColorBrush}"
                                BorderBrush="{DynamicResource BorderColorBrush}"
                                BorderThickness="1,0,0,0">
                            <TextBlock x:Name="FuelAdviceText"
                                       TextWrapping="Wrap"
                                       Foreground="{DynamicResource PrimaryTextColorBrush}"/>
                        </Border>
                        <Border x:Name="ExplorationPoiPanel"
                                Padding="9,7" Margin="0,0,0,7"
                                Visibility="Collapsed"
                                Background="{DynamicResource SecondaryBackgroundColorBrush}"
                                BorderBrush="{DynamicResource AccentColorBrush}"
                                BorderThickness="1,0,0,0">
                            <TextBlock x:Name="ExplorationPoiText"
                                       TextWrapping="Wrap"
                                       Foreground="{DynamicResource PrimaryTextColorBrush}"/>
                        </Border>
                        <Border Padding="9,7"
                                Background="{DynamicResource SecondaryBackgroundColorBrush}"
                                BorderBrush="{DynamicResource BorderColorBrush}"
                                BorderThickness="1,0,0,0">
                            <TextBlock x:Name="PlannedFeaturesText"
                                       TextWrapping="Wrap"
                                       FontSize="11"
                                       Foreground="{DynamicResource MutedTextColorBrush}"/>
                        </Border>
                    </StackPanel>
                </ScrollViewer>
            </Grid>
'@

$xaml = Read-Text $xamlPath

$compactStartMarker = '            <Border Grid.Row="1" Margin="0,10,0,8" Padding="9,6"'
$compactEndMarker = '            <Grid Grid.Row="3" Margin="0,8,0,0">'

$startMatches = ([regex]::Matches(
    $xaml,
    [regex]::Escape($compactStartMarker))).Count

$endMatches = ([regex]::Matches(
    $xaml,
    [regex]::Escape($compactEndMarker))).Count

if ($startMatches -ne 1) {
    throw "Expected exactly one compact start marker in $xamlPath, found $startMatches."
}

if ($endMatches -lt 1) {
    throw "Could not locate compact footer marker in $xamlPath."
}

$startIndex = $xaml.IndexOf(
    $compactStartMarker,
    [StringComparison]::Ordinal)

$endIndex = $xaml.IndexOf(
    $compactEndMarker,
    $startIndex,
    [StringComparison]::Ordinal)

if ($startIndex -lt 0 -or $endIndex -le $startIndex) {
    throw "Could not resolve compact exploration content boundaries in $xamlPath."
}

$beforeCompact = $xaml.Substring(0, $startIndex)
$afterCompact = $xaml.Substring($endIndex)

$replacementCompact = $beforeCompact + $compactContent + "`n" + $afterCompact
Write-Text $xamlPath $replacementCompact

# ---------------------------------------------------------------------------
# 3. Replace RefreshContent with adaptive exploration routing while preserving
#    the existing mining path and full exploration assistant.
# ---------------------------------------------------------------------------
$refreshContent = @'
    private void RefreshContent(GameStateSnapshot state)
    {
        bool exploration = activity == ActivityType.Exploration;

        LocationText.Text = string.IsNullOrWhiteSpace(state.StarSystem)
            ? Loc.Get("Loc_SYSTEM")
            : Loc.Format("Loc_System_Format", state.StarSystem.ToUpperInvariant());

        FlightStateText.Text = BuildFlightState(state);

        ExplorationDataState externalData =
            ExplorationDataService.Instance.Current;

        ExternalDataText.Visibility = Visibility.Collapsed;
        ExternalDataText.Text = string.Empty;

        if (exploration)
        {
            TitleText.Text = string.IsNullOrWhiteSpace(state.StarSystem)
                ? Loc.Get("Loc_EXPLORATION")
                : state.StarSystem.ToUpperInvariant();

            ExplorationVisitQueueSnapshot queue =
                ExplorationVisitStateService.Instance.Current;

            ModuleStatusText.Text =
                BuildAdaptiveExplorationHeader(
                    state,
                    externalData,
                    queue);

            LegacyLocationPanel.Visibility = Visibility.Collapsed;
            LegacyCompactScrollViewer.Visibility = Visibility.Collapsed;
            AdaptiveExplorationPanel.Visibility = Visibility.Visible;

            RefreshAdaptiveExploration(
                state,
                externalData,
                queue);

            FooterHintText.Text =
                BuildAdaptiveExplorationFooter(
                    state,
                    queue);

            OpenExplorationAssistantButton.Visibility =
                Visibility.Visible;

            if (string.IsNullOrWhiteSpace(SpanshSourceTextBox.Text)
                && !string.IsNullOrWhiteSpace(state.StarSystem))
            {
                SpanshSourceTextBox.Text = state.StarSystem;
            }

            if (fullExplorationVisible)
            {
                RefreshCatalog(state, externalData);
                FullOverviewText.Text =
                    BuildFullOverview(state, externalData);
                RefreshExplorationLog();
            }

            return;
        }

        TitleText.Text = Loc.Get("Loc_MINING");
        ModuleStatusText.Text = state.IsLive
            ? Loc.Get("Loc_JOURNAL_LIVE_2")
            : Loc.Get("Loc_JOURNAL_ASSISTANT");

        LegacyLocationPanel.Visibility = Visibility.Visible;
        LegacyCompactScrollViewer.Visibility = Visibility.Visible;
        AdaptiveExplorationPanel.Visibility = Visibility.Collapsed;
        SurfaceNavigationPanel.Visibility = Visibility.Collapsed;
        FuelAdvicePanel.Visibility = Visibility.Collapsed;
        ExplorationPoiPanel.Visibility = Visibility.Collapsed;

        ProspectedAsteroidSnapshot? prospect =
            state.LastProspectedAsteroid;

        PrimaryHintText.Text = prospect is null
            ? Loc.Get("Loc_Mining_waiting_for_prospector")
            : Loc.Format(
                prospect.HasMotherlode
                    ? "Loc_Mining_core_prospect_format"
                    : "Loc_Mining_prospect_format",
                prospect.HasMotherlode
                    ? prospect.MotherlodeMaterial
                    : prospect.Content,
                prospect.Remaining);

        string leadingMaterials = prospect is null
            ? Loc.Get("Loc_No_prospect_data")
            : string.Join(
                " · ",
                prospect.Materials
                    .Take(3)
                    .Select(material =>
                        $"{material.Name} {material.Proportion:0.#}%"));

        PlannedFeaturesText.Text = Loc.Format(
            "Loc_Mining_session_format",
            state.RefinedMiningUnits,
            state.CrackedAsteroids,
            leadingMaterials);

        FooterHintText.Text =
            Loc.Get("Loc_Switch_activities_in_the_main_window");

        OpenExplorationAssistantButton.Visibility =
            Visibility.Collapsed;
    }

'@

Replace-RegexOnce `
    $codePath `
    '    private void RefreshContent\(GameStateSnapshot state\).*?(?=    private void RefreshCatalog\()' `
    $refreshContent `
    'RefreshContent method'

# ---------------------------------------------------------------------------
# 4. Subscribe the window directly to visit-queue changes.
# ---------------------------------------------------------------------------
$code = Read-Text $codePath

if (-not $code.Contains('ExplorationVisitStateService.Instance.Changed += OnExplorationVisitStateChanged;')) {
    $old = @'
        ExplorationHistoryService.Instance.HistoryChanged += OnExplorationHistoryChanged;
        ExplorationRouteService.Instance.RouteChanged += OnExplorationRouteChanged;
'@
    $new = @'
        ExplorationHistoryService.Instance.HistoryChanged += OnExplorationHistoryChanged;
        ExplorationVisitStateService.Instance.Changed += OnExplorationVisitStateChanged;
        ExplorationRouteService.Instance.RouteChanged += OnExplorationRouteChanged;
'@
    Replace-LiteralOnce $codePath $old $new 'visit-state subscription'
}

$code = Read-Text $codePath

if (-not $code.Contains('ExplorationVisitStateService.Instance.Changed -= OnExplorationVisitStateChanged;')) {
    $old = @'
        ExplorationHistoryService.Instance.HistoryChanged -= OnExplorationHistoryChanged;
        ExplorationRouteService.Instance.RouteChanged -= OnExplorationRouteChanged;
'@
    $new = @'
        ExplorationHistoryService.Instance.HistoryChanged -= OnExplorationHistoryChanged;
        ExplorationVisitStateService.Instance.Changed -= OnExplorationVisitStateChanged;
        ExplorationRouteService.Instance.RouteChanged -= OnExplorationRouteChanged;
'@
    Replace-LiteralOnce $codePath $old $new 'visit-state unsubscription'
}

$code = Read-Text $codePath

if (-not $code.Contains('private void OnExplorationVisitStateChanged(')) {
    $handler = @'
    private void OnExplorationVisitStateChanged(
        object? sender,
        ExplorationVisitStateChangedEventArgs e) =>
        Dispatcher.BeginInvoke(
            new Action(() =>
                RefreshContent(
                    JournalMonitorService.Instance.Current)));

'@
    $anchor = @'
    private void OnExplorationHistoryChanged(object? sender, ExplorationHistoryChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => RefreshContent(JournalMonitorService.Instance.Current)));

'@
    if (-not $code.Contains($anchor)) {
        throw "Could not locate exploration history event handler."
    }
    Write-Text $codePath ($code.Replace($anchor, $anchor + $handler))
}

# ---------------------------------------------------------------------------
# 5. Adaptive HUD rendering helpers.
# ---------------------------------------------------------------------------
$helpers = @'
    private void RefreshAdaptiveExploration(
        GameStateSnapshot state,
        ExplorationDataState externalData,
        ExplorationVisitQueueSnapshot queue)
    {
        bool queueMatchesSystem =
            QueueMatchesSystem(queue, state);

        ExplorationVisitBodyState? active =
            queueMatchesSystem
                ? queue.Active
                : null;

        OrganicScanProgressSnapshot? activeOrganic =
            active is null
                ? null
                : state.GetActiveOrganicForBody(active.BodyId);

        SystemContextPanel.Visibility =
            active is null
                ? Visibility.Visible
                : Visibility.Collapsed;

        BodyContextPanel.Visibility =
            active is not null && activeOrganic is null
                ? Visibility.Visible
                : Visibility.Collapsed;

        ExobioContextPanel.Visibility =
            active is not null && activeOrganic is not null
                ? Visibility.Visible
                : Visibility.Collapsed;

        CompactQueueCountText.Text =
            queueMatchesSystem
                ? Loc.Format(
                    "Loc_EXPLORATION_QUEUE_FORMAT",
                    queue.RemainingCount,
                    queue.DeferredCount,
                    queue.CompletedCount)
                : string.Empty;

        if (active is null)
        {
            CompactModeText.Text =
                Loc.Get("Loc_EXPLORATION_MODE_SYSTEM");

            CompactContextTitleText.Text =
                Loc.Get("Loc_EXPLORATION_TARGETS_HEADER");

            ExplorationVisitBodyState[] targets =
                queueMatchesSystem
                    ? queue.Recommended
                        .Take(3)
                        .ToArray()
                    : Array.Empty<ExplorationVisitBodyState>();

            CompactTargetsItemsControl.ItemsSource =
                targets
                    .Select(BuildAdaptiveTargetLine)
                    .ToArray();

            CompactEmptyTargetsText.Visibility =
                targets.Length == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            CompactEmptyTargetsText.Text =
                queueMatchesSystem
                    && queue.DeferredCount > 0
                    && queue.Recommended.Count == 0
                        ? Loc.Format(
                            "Loc_EXPLORATION_DEFERRED_ONLY_FORMAT",
                            queue.DeferredCount)
                        : state.FssProgress >= 0.999
                            ? Loc.Get(
                                "Loc_EXPLORATION_SYSTEM_COMPLETE_COMPACT")
                            : Loc.Get(
                                "Loc_EXPLORATION_NO_TARGETS");
        }
        else if (activeOrganic is null)
        {
            CompactModeText.Text =
                Loc.Get("Loc_EXPLORATION_MODE_BODY");

            CompactContextTitleText.Text =
                active.BodyName;

            BodyStatusText.Text =
                BuildAdaptiveBodyStatus(active);

            BodyObjectiveText.Text =
                BuildAdaptiveBodyObjectives(active);

            BodyMissingText.Text =
                BuildAdaptiveMissingBiology(active);

            BodyMissingText.Visibility =
                string.IsNullOrWhiteSpace(BodyMissingText.Text)
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            BodyMetaText.Text =
                BuildAdaptiveBodyMeta(active);
        }
        else
        {
            CompactModeText.Text =
                Loc.Get("Loc_EXPLORATION_MODE_EXOBIO");

            CompactContextTitleText.Text =
                !string.IsNullOrWhiteSpace(activeOrganic.Variant)
                    ? activeOrganic.Variant
                    : !string.IsNullOrWhiteSpace(activeOrganic.Species)
                        ? activeOrganic.Species
                        : active.BodyName;

            SurfaceNavigationText.Text =
                BuildSurfaceNavigation(state);

            SurfaceNavigationPanel.Visibility =
                Visibility.Visible;

            ExobioBodyProgressText.Text =
                BuildAdaptiveExobioProgress(
                    active,
                    activeOrganic);
        }

        string routeAlert =
            BuildCompactRouteOrAlert(
                state,
                queueMatchesSystem ? queue : null);

        CompactRouteAlertText.Text = routeAlert;
        CompactRouteAlertPanel.Visibility =
            string.IsNullOrWhiteSpace(routeAlert)
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    private static bool QueueMatchesSystem(
        ExplorationVisitQueueSnapshot queue,
        GameStateSnapshot state)
    {
        if (state.SystemAddress != 0
            && queue.SystemAddress != 0)
        {
            return state.SystemAddress == queue.SystemAddress;
        }

        return string.Equals(
            queue.SystemName,
            state.StarSystem,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildAdaptiveExplorationHeader(
        GameStateSnapshot state,
        ExplorationDataState externalData,
        ExplorationVisitQueueSnapshot queue)
    {
        if (!state.JournalAvailable)
        {
            return Loc.Get(
                "Loc_Waiting_for_Elite_Dangerous_journal");
        }

        int knownBodyCount = Math.Max(
            state.SystemBodyCount,
            externalData.System is { } system
            && string.Equals(
                system.SystemName,
                state.StarSystem,
                StringComparison.OrdinalIgnoreCase)
                ? system.BodyCount
                : 0);

        int resolvedBodyCount = knownBodyCount == 0
            ? state.ScannedBodies
            : Math.Clamp(
                (int)Math.Round(
                    knownBodyCount * state.FssProgress),
                0,
                knownBodyCount);

        string bodyProgress = knownBodyCount > 0
            ? $"{resolvedBodyCount}/{knownBodyCount}"
            : state.ScannedBodies.ToString();

        string result = Loc.Format(
            "Loc_EXPLORATION_HEADER_FORMAT",
            Math.Round(state.FssProgress * 100),
            bodyProgress,
            state.MappedBodies,
            state.BiologicalSignals);

        long localValue =
            state.ExplorationBodies
                .Sum(body => body.EstimatedScanValue)
            + state.ExplorationBodies
                .Where(body => body.IsMapped)
                .Sum(body =>
                    body.MappingEfficient
                        ? body.EstimatedEfficientMappingValue
                        : body.EstimatedMappingValue);

        if (localValue > 0)
        {
            result += "  •  "
                + Loc.Format(
                    "Loc_Credits_Short_Format",
                    localValue);
        }

        return result;
    }

    private static string BuildAdaptiveTargetLine(
        ExplorationVisitBodyState item)
    {
        var parts = new List<string>
        {
            item.BodyName
        };

        if (!item.Progress.FssScanned)
        {
            parts.Add("FSS ○");
        }

        if (item.DssRequired)
        {
            parts.Add(
                item.Progress.DssMapped
                    ? "DSS ✓"
                    : "DSS ○");
        }

        if (item.BiologyRequired)
        {
            parts.Add(
                $"BIO {item.Progress.CompletedBiologicalSignals}/{item.Progress.BiologicalSignals}");
        }

        if (item.Body.DistanceFromArrivalLs > 0)
        {
            parts.Add(
                Loc.Format(
                    "Loc_Distance_Ls_Value",
                    item.Body.DistanceFromArrivalLs));
        }

        long value = item.Body.EstimatedMappingValue;
        if (value > 0)
        {
            parts.Add(
                Loc.Format(
                    "Loc_Credits_Short_Format",
                    value));
        }

        return string.Join("  •  ", parts);
    }

    private static string BuildAdaptiveBodyStatus(
        ExplorationVisitBodyState active)
    {
        string fss = active.Progress.FssScanned
            ? "FSS ✓"
            : "FSS ○";

        string dss = !active.DssRequired
            ? "DSS —"
            : active.Progress.DssMapped
                ? active.Progress.DssEfficient
                    ? "DSS ◎"
                    : "DSS ✓"
                : "DSS ○";

        string bio = !active.BiologyRequired
            ? "BIO —"
            : $"BIO {active.Progress.CompletedBiologicalSignals}/{active.Progress.BiologicalSignals}";

        return string.Join(
            "  •  ",
            fss,
            dss,
            bio);
    }

    private static string BuildAdaptiveBodyObjectives(
        ExplorationVisitBodyState active)
    {
        var pending = new List<string>();

        if (active.FssRequired
            && !active.Progress.FssScanned)
        {
            pending.Add("FSS");
        }

        if (active.DssRequired
            && !active.Progress.DssMapped)
        {
            pending.Add("DSS");
        }

        if (active.BiologyRequired
            && !active.Progress.BiologyComplete)
        {
            pending.Add(
                $"BIO {active.Progress.CompletedBiologicalSignals}/{active.Progress.BiologicalSignals}");
        }

        return pending.Count == 0
            ? Loc.Get(
                "Loc_EXPLORATION_ALL_OBJECTIVES_DONE")
            : Loc.Format(
                "Loc_EXPLORATION_PENDING_FORMAT",
                string.Join(" + ", pending));
    }

    private static string BuildAdaptiveMissingBiology(
        ExplorationVisitBodyState active)
    {
        if (!active.BiologyRequired
            || active.Progress.BiologyComplete)
        {
            return string.Empty;
        }

        string known = active.Progress.MissingGenuses.Count > 0
            ? string.Join(
                " · ",
                active.Progress.MissingGenuses)
            : string.Empty;

        int unknownCount = Math.Max(
            0,
            active.Progress.RemainingBiologicalSignals
                - active.Progress.MissingGenuses.Count);

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(known))
        {
            parts.Add(
                Loc.Format(
                    "Loc_EXPLORATION_MISSING_GENUSES_FORMAT",
                    known));
        }

        if (unknownCount > 0)
        {
            parts.Add(
                Loc.Format(
                    "Loc_EXPLORATION_UNKNOWN_GENUSES_FORMAT",
                    unknownCount));
        }

        if (active.Progress.HistoricalBiologyDetailIncomplete)
        {
            parts.Add(
                Loc.Get(
                    "Loc_EXPLORATION_HISTORY_BIO_DETAIL_INCOMPLETE"));
        }

        return string.Join(
            Environment.NewLine,
            parts);
    }

    private static string BuildAdaptiveBodyMeta(
        ExplorationVisitBodyState active)
    {
        var parts = new List<string>();

        string type = string.IsNullOrWhiteSpace(
            active.Body.Subtype)
                ? active.Body.Type
                : active.Body.Subtype;

        if (!string.IsNullOrWhiteSpace(type))
        {
            parts.Add(type);
        }

        if (active.Body.DistanceFromArrivalLs > 0)
        {
            parts.Add(
                Loc.Format(
                    "Loc_Distance_Ls_Value",
                    active.Body.DistanceFromArrivalLs));
        }

        if (active.Body.Landable
            && active.Body.GravityG > 0)
        {
            parts.Add(
                $"{active.Body.GravityG:0.00} g");
        }

        if (active.Body.EstimatedMappingValue > 0)
        {
            parts.Add(
                Loc.Format(
                    "Loc_Credits_Short_Format",
                    active.Body.EstimatedMappingValue));
        }

        return string.Join(
            "  •  ",
            parts);
    }

    private static string BuildAdaptiveExobioProgress(
        ExplorationVisitBodyState active,
        OrganicScanProgressSnapshot organic)
    {
        string stage = $"{organic.Stage}/3";

        string bodyProgress =
            $"BIO {active.Progress.CompletedBiologicalSignals}/{active.Progress.BiologicalSignals}";

        string missing =
            BuildAdaptiveMissingBiology(active);

        string result = Loc.Format(
            "Loc_EXPLORATION_EXOBIO_PROGRESS_FORMAT",
            stage,
            bodyProgress);

        return string.IsNullOrWhiteSpace(missing)
            ? result
            : result
              + Environment.NewLine
              + missing;
    }

    private static string BuildCompactRouteOrAlert(
        GameStateSnapshot state,
        ExplorationVisitQueueSnapshot? queue)
    {
        FuelRouteAssessment fuel =
            FuelRouteAdvisor.Evaluate(state);

        if (fuel.Severity
            is FuelRouteSeverity.Critical
            or FuelRouteSeverity.Caution)
        {
            return BuildFuelAdvice(fuel);
        }

        ExplorationRoutePlan route =
            ExplorationRouteService.Instance.Current;

        if (route.NextStop is { } next)
        {
            return Loc.Format(
                "Loc_EXPLORATION_ROUTE_NEXT_HUD_FORMAT",
                next.System,
                Math.Min(
                    route.Stops.Count,
                    route.CurrentIndex + 2),
                route.Stops.Count);
        }

        if (queue is { DeferredCount: > 0 })
        {
            return Loc.Format(
                "Loc_EXPLORATION_DEFERRED_HUD_FORMAT",
                queue.DeferredCount);
        }

        return string.Empty;
    }

    private static string BuildAdaptiveExplorationFooter(
        GameStateSnapshot state,
        ExplorationVisitQueueSnapshot queue)
    {
        if (QueueMatchesSystem(queue, state)
            && queue.DeferredCount > 0)
        {
            return Loc.Format(
                "Loc_EXPLORATION_FOOTER_QUEUE_FORMAT",
                queue.DeferredCount,
                queue.CompletedCount);
        }

        return BuildExplorationFooter(state);
    }

'@

$code = Read-Text $codePath
if (-not $code.Contains('private void RefreshAdaptiveExploration(')) {
    $anchor = @'
    private sealed record CatalogRow(
        ExplorationCatalogBody Body,
        string Name,
        string Type,
        string Highlights,
        string Distance,
        string MappingValue,
        string Progress);

'@

    if (-not $code.Contains($anchor)) {
        throw "Could not locate CatalogRow insertion anchor."
    }

    Write-Text $codePath ($code.Replace(
        $anchor,
        $anchor + $helpers))
}

# ---------------------------------------------------------------------------
# 6. Localization.
# ---------------------------------------------------------------------------
$en = @{
    'Loc_EXPLORATION_MODE_SYSTEM' = 'SYSTEM'
    'Loc_EXPLORATION_MODE_BODY' = 'BODY TARGET'
    'Loc_EXPLORATION_MODE_EXOBIO' = 'EXOBIOLOGY'
    'Loc_EXPLORATION_TARGETS_HEADER' = 'NEXT TARGETS'
    'Loc_EXPLORATION_QUEUE_FORMAT' = '{0} remaining • {1} deferred • {2} complete'
    'Loc_EXPLORATION_HEADER_FORMAT' = 'FSS {0}% • {1} bodies • DSS {2} • BIO {3}'
    'Loc_EXPLORATION_NO_TARGETS' = 'No recommended bodies yet.'
    'Loc_EXPLORATION_SYSTEM_COMPLETE_COMPACT' = 'No unfinished recommended bodies.'
    'Loc_EXPLORATION_DEFERRED_ONLY_FORMAT' = '{0} deferred • open the full assistant to resume'
    'Loc_EXPLORATION_PENDING_FORMAT' = 'NEXT: {0}'
    'Loc_EXPLORATION_ALL_OBJECTIVES_DONE' = 'Objectives complete'
    'Loc_EXPLORATION_MISSING_GENUSES_FORMAT' = 'Missing: {0}'
    'Loc_EXPLORATION_UNKNOWN_GENUSES_FORMAT' = 'Unknown after DSS/history: {0}'
    'Loc_EXPLORATION_HISTORY_BIO_DETAIL_INCOMPLETE' = 'Some older completed bio scans have no genus detail.'
    'Loc_EXPLORATION_EXOBIO_PROGRESS_FORMAT' = 'sample {0} • {1}'
    'Loc_EXPLORATION_ROUTE_NEXT_HUD_FORMAT' = 'ROUTE → {0} • stop {1}/{2}'
    'Loc_EXPLORATION_DEFERRED_HUD_FORMAT' = '{0} deferred • available in the full assistant'
    'Loc_EXPLORATION_FOOTER_QUEUE_FORMAT' = 'Deferred {0} • complete {1}'
}

$ru = @{
    'Loc_EXPLORATION_MODE_SYSTEM' = 'СИСТЕМА'
    'Loc_EXPLORATION_MODE_BODY' = 'ЦЕЛЕВОЕ ТЕЛО'
    'Loc_EXPLORATION_MODE_EXOBIO' = 'ЭКЗОБИОЛОГИЯ'
    'Loc_EXPLORATION_TARGETS_HEADER' = 'СЛЕДУЮЩИЕ ЦЕЛИ'
    'Loc_EXPLORATION_QUEUE_FORMAT' = 'осталось {0} • отложено {1} • завершено {2}'
    'Loc_EXPLORATION_HEADER_FORMAT' = 'FSS {0}% • тела {1} • DSS {2} • BIO {3}'
    'Loc_EXPLORATION_NO_TARGETS' = 'Рекомендуемых тел пока нет.'
    'Loc_EXPLORATION_SYSTEM_COMPLETE_COMPACT' = 'Незавершённых рекомендуемых тел нет.'
    'Loc_EXPLORATION_DEFERRED_ONLY_FORMAT' = 'отложено {0} • вернуть можно в полном ассистенте'
    'Loc_EXPLORATION_PENDING_FORMAT' = 'ДАЛЕЕ: {0}'
    'Loc_EXPLORATION_ALL_OBJECTIVES_DONE' = 'Цели исследования выполнены'
    'Loc_EXPLORATION_MISSING_GENUSES_FORMAT' = 'Не найдено: {0}'
    'Loc_EXPLORATION_UNKNOWN_GENUSES_FORMAT' = 'Не определено по DSS/истории: {0}'
    'Loc_EXPLORATION_HISTORY_BIO_DETAIL_INCOMPLETE' = 'Для части старых завершённых биосканов genus не сохранён.'
    'Loc_EXPLORATION_EXOBIO_PROGRESS_FORMAT' = 'образец {0} • {1}'
    'Loc_EXPLORATION_ROUTE_NEXT_HUD_FORMAT' = 'МАРШРУТ → {0} • точка {1}/{2}'
    'Loc_EXPLORATION_DEFERRED_HUD_FORMAT' = 'отложено {0} • доступно в полном ассистенте'
    'Loc_EXPLORATION_FOOTER_QUEUE_FORMAT' = 'Отложено {0} • завершено {1}'
}

Add-LocalizationEntries $localizationEnPath $en
Add-LocalizationEntries $localizationRuPath $ru

# ---------------------------------------------------------------------------
# 7. Regression/static tests.
# ---------------------------------------------------------------------------
$tests = @'
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class AdaptiveExplorationHudTests
{
    [Fact]
    public void CompactExplorationHudHasThreeAdaptiveContexts()
    {
        string repository = FindRepositoryRoot();
        string xaml = File.ReadAllText(
            Path.Combine(
                repository,
                "ED_Inara_Overlay",
                "Windows",
                "ActivityWorkspaceOverlayWindow.xaml"));

        Assert.Contains(
            "x:Name=\"AdaptiveExplorationPanel\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"SystemContextPanel\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"BodyContextPanel\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"ExobioContextPanel\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"CompactTargetsItemsControl\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AdaptiveExplorationHudIsNotInsideLegacyScrollViewer()
    {
        string repository = FindRepositoryRoot();
        string xaml = File.ReadAllText(
            Path.Combine(
                repository,
                "ED_Inara_Overlay",
                "Windows",
                "ActivityWorkspaceOverlayWindow.xaml"));

        int adaptive = xaml.IndexOf(
            "x:Name=\"AdaptiveExplorationPanel\"",
            StringComparison.Ordinal);

        int legacyScroll = xaml.IndexOf(
            "x:Name=\"LegacyCompactScrollViewer\"",
            StringComparison.Ordinal);

        Assert.True(adaptive >= 0);
        Assert.True(legacyScroll > adaptive);

        string beforeLegacyScroll =
            xaml.Substring(adaptive, legacyScroll - adaptive);

        Assert.DoesNotContain(
            "<ScrollViewer",
            beforeLegacyScroll,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HudUsesVisitQueueInsteadOfLegacyCompactTargetSelection()
    {
        string repository = FindRepositoryRoot();
        string code = File.ReadAllText(
            Path.Combine(
                repository,
                "ED_Inara_Overlay",
                "Windows",
                "ActivityWorkspaceOverlayWindow.xaml.cs"));

        Assert.Contains(
            "ExplorationVisitStateService.Instance.Current",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "queue.Recommended",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            ".Take(3)",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "queue.Active",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "state.GetActiveOrganicForBody(active.BodyId)",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HudRefreshesWhenVisitStateChanges()
    {
        string repository = FindRepositoryRoot();
        string code = File.ReadAllText(
            Path.Combine(
                repository,
                "ED_Inara_Overlay",
                "Windows",
                "ActivityWorkspaceOverlayWindow.xaml.cs"));

        Assert.Contains(
            "ExplorationVisitStateService.Instance.Changed += OnExplorationVisitStateChanged;",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "ExplorationVisitStateService.Instance.Changed -= OnExplorationVisitStateChanged;",
            code,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory =
            new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                Path.Combine(
                    directory.FullName,
                    "ED_Inara_Overlay.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Repository root was not found.");
    }
}
'@

Write-Text $testsPath $tests

# ---------------------------------------------------------------------------
# 8. Sanity checks.
# ---------------------------------------------------------------------------
$xamlCheck = Read-Text $xamlPath
$codeCheck = Read-Text $codePath

foreach ($needle in @(
    'x:Name="AdaptiveExplorationPanel"',
    'x:Name="SystemContextPanel"',
    'x:Name="BodyContextPanel"',
    'x:Name="ExobioContextPanel"',
    'x:Name="CompactTargetsItemsControl"',
    'x:Name="LegacyCompactScrollViewer"'
)) {
    if (-not $xamlCheck.Contains($needle)) {
        throw "Missing adaptive HUD XAML: $needle"
    }
}

foreach ($needle in @(
    'RefreshAdaptiveExploration(',
    'ExplorationVisitStateService.Instance.Current',
    'GetActiveOrganicForBody(active.BodyId)',
    'BuildAdaptiveTargetLine',
    'BuildCompactRouteOrAlert'
)) {
    if (-not $codeCheck.Contains($needle)) {
        throw "Missing adaptive HUD code: $needle"
    }
}

Write-Host ''
& git diff --check
if ($LASTEXITCODE -ne 0) {
    throw 'git diff --check failed.'
}

Write-Host ''
& git diff --stat

if (-not $SkipBuild) {
    Write-Host ''
    Write-Host 'Building application...' -ForegroundColor Cyan

    & dotnet build '.\ED_Inara_Overlay\ED_Inara_Overlay.csproj' -c Debug
    if ($LASTEXITCODE -ne 0) {
        throw 'Application build failed.'
    }

    Write-Host ''
    Write-Host 'Running regression tests...' -ForegroundColor Cyan

    & dotnet test '.\Testing\ED_Inara_Overlay.LayoutTests\ED_Inara_Overlay.LayoutTests.csproj' -c Debug
    if ($LASTEXITCODE -ne 0) {
        throw 'Regression tests failed.'
    }
}

Write-Host ''
Write-Host 'Adaptive exploration HUD applied.' -ForegroundColor Green
Write-Host ''
Write-Host 'Compact exploration modes:'
Write-Host '  SYSTEM      -> top 3 unfinished Recommended bodies'
Write-Host '  BODY TARGET -> FSS / DSS / BIO progress + missing genera'
Write-Host '  EXOBIOLOGY  -> active sample + surface separation radar'
Write-Host ''
Write-Host 'Complete and Deferred bodies never appear in the compact target list.'
Write-Host 'Fuel caution/critical overrides route info in the bottom alert row.'
Write-Host 'Mining keeps the previous compact layout.'
Write-Host ''
Write-Host "Backup of previous local diff: $backup"
