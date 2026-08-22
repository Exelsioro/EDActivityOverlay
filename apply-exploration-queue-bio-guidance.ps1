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
            $value = [System.Security.SecurityElement]::Escape(
                [string]$Entries[$key])
            $missing += "    <sys:String x:Key=`"$key`">$value</sys:String>"
        }
    }

    if ($missing.Count -eq 0) {
        return
    }

    if (-not $text.Contains('</ResourceDictionary>')) {
        throw "Could not locate ResourceDictionary end in $Path."
    }

    $block = ($missing -join "`n") + "`n"
    Write-Text $Path ($text.Replace(
        '</ResourceDictionary>',
        $block + '</ResourceDictionary>'))
}

$branch = (& git rev-parse --abbrev-ref HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
    throw 'Run this script from the repository root.'
}

Write-Host "Current branch: $branch" -ForegroundColor DarkGray

$xamlPath =
    'ED_Inara_Overlay\Windows\ActivityWorkspaceOverlayWindow.xaml'

$codePath =
    'ED_Inara_Overlay\Windows\ActivityWorkspaceOverlayWindow.xaml.cs'

$visitModelsPath =
    'ED_Inara_Overlay\Models\ExplorationVisitModels.cs'

$progressPath =
    'ED_Inara_Overlay\Models\BodyExplorationProgress.cs'

$predictionPath =
    'ED_Inara_Overlay\Services\Exploration\ExobiologyPredictionService.cs'

$localizationEnPath =
    'ED_Inara_Overlay\Resources\Localization.en-US.xaml'

$localizationRuPath =
    'ED_Inara_Overlay\Resources\Localization.ru-RU.xaml'

$testsPath =
    'Testing\ED_Inara_Overlay.LayoutTests\ExplorationQueueBioGuidanceTests.cs'

foreach ($required in @(
    $xamlPath,
    $codePath,
    $visitModelsPath,
    $progressPath,
    $predictionPath,
    $localizationEnPath,
    $localizationRuPath
)) {
    if (-not (Test-Path $required)) {
        throw "Required file not found: $required. Apply patches 1-3 first."
    }
}

$codeCheck = Read-Text $codePath
if (-not $codeCheck.Contains('RefreshAdaptiveExploration(')) {
    throw 'Adaptive exploration HUD does not appear to be installed.'
}

$backup = 'exploration-queue-bio-guidance-before.patch'
& git diff --binary -- `
    $xamlPath $codePath $localizationEnPath $localizationRuPath $testsPath |
    Set-Content -Path $backup -Encoding utf8

Write-Host "Saved current diff to $backup" -ForegroundColor DarkGray
Write-Host 'Applying exploration queue controls and bio guidance...' -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# 1. Full assistant filters: add visit-state views.
# ---------------------------------------------------------------------------
$code = Read-Text $codePath

if (-not $code.Contains('new("Remaining", "Loc_FILTER_REMAINING")')) {
    $oldFilters = @'
        new("Valuable", "Loc_FILTER_VALUABLE"),
        new("Biological", "Loc_FILTER_BIOLOGICAL"),
        new("Unmapped", "Loc_FILTER_UNMAPPED"),
'@

    $newFilters = @'
        new("Valuable", "Loc_FILTER_VALUABLE"),
        new("Biological", "Loc_FILTER_BIOLOGICAL"),
        new("Remaining", "Loc_FILTER_REMAINING"),
        new("Deferred", "Loc_FILTER_DEFERRED"),
        new("Completed", "Loc_FILTER_COMPLETED"),
        new("Unmapped", "Loc_FILTER_UNMAPPED"),
'@

    Replace-LiteralOnce `
        $codePath `
        $oldFilters `
        $newFilters `
        'catalog filter list'
}

# ---------------------------------------------------------------------------
# 2. Full catalog gets a visit-state column.
# ---------------------------------------------------------------------------
$xaml = Read-Text $xamlPath

if (-not $xaml.Contains('Binding="{Binding VisitState}"')) {
    $oldColumn = @'
                        <DataGridTextColumn Header="{DynamicResource Loc_STATUS}" Binding="{Binding Progress}" Width="1.1*"/>
'@

    $newColumn = @'
                        <DataGridTextColumn Header="{DynamicResource Loc_STATUS}" Binding="{Binding Progress}" Width="1.05*"/>
                        <DataGridTextColumn Header="{DynamicResource Loc_EXPLORATION_VISIT_STATE}" Binding="{Binding VisitState}" Width="0.9*"/>
'@

    Replace-LiteralOnce `
        $xamlPath `
        $oldColumn `
        $newColumn `
        'catalog visit-state column'
}

# ---------------------------------------------------------------------------
# 3. Add Defer/Resume controls to the selected-body side panel.
# ---------------------------------------------------------------------------
$xaml = Read-Text $xamlPath

if (-not $xaml.Contains('x:Name="DeferSelectedBodyButton"')) {
    $oldButtons = @'
                            <Button x:Name="BookmarkSelectedBodyButton" Margin="0,7,0,0"
                                    Content="{DynamicResource Loc_MARK_NOTABLE_FINDING}"
                                    IsEnabled="False" Click="BookmarkSelectedBodyButton_Click"/>
'@

    $newButtons = @'
                            <Button x:Name="BookmarkSelectedBodyButton" Margin="0,7,0,0"
                                    Content="{DynamicResource Loc_MARK_NOTABLE_FINDING}"
                                    IsEnabled="False" Click="BookmarkSelectedBodyButton_Click"/>
                            <Button x:Name="DeferSelectedBodyButton" Margin="0,7,0,0"
                                    Visibility="Collapsed"
                                    Content="{DynamicResource Loc_EXPLORATION_DEFER_THIS_VISIT}"
                                    Click="DeferSelectedBodyButton_Click"/>
                            <Button x:Name="ResumeSelectedBodyButton" Margin="0,7,0,0"
                                    Visibility="Collapsed"
                                    Content="{DynamicResource Loc_EXPLORATION_RESUME_BODY}"
                                    Click="ResumeSelectedBodyButton_Click"/>
'@

    Replace-LiteralOnce `
        $xamlPath `
        $oldButtons `
        $newButtons `
        'selected-body defer/resume buttons'
}

# ---------------------------------------------------------------------------
# 4. Replace catalog filtering with visit-aware filtering/sorting.
# ---------------------------------------------------------------------------
$applyCatalog = @'
    private void ApplyCatalogFilter()
    {
        if (ExplorationBodiesGrid is null)
        {
            return;
        }

        string search =
            CatalogSearchTextBox?.Text.Trim()
            ?? string.Empty;

        string filter =
            (CatalogFilterComboBox?.SelectedItem
                as CatalogFilterOption)?.Value
            ?? "All";

        GameStateSnapshot state =
            JournalMonitorService.Instance.Current;

        ExplorationVisitQueueSnapshot queue =
            ExplorationVisitStateService.Instance.Current;

        Dictionary<int, ExplorationVisitDisposition> dispositions =
            BuildVisitDispositionMap(
                state,
                queue);

        CatalogRow[] rows = catalog.Bodies
            .Where(body =>
                string.IsNullOrWhiteSpace(search)
                || body.Name.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase)
                || body.Subtype.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase)
                || body.Atmosphere.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase))
            .Where(body =>
            {
                dispositions.TryGetValue(
                    body.BodyId,
                    out ExplorationVisitDisposition disposition);

                bool hasVisitState =
                    dispositions.ContainsKey(body.BodyId);

                return filter switch
                {
                    "Notable" => body.IsNotable,
                    "Valuable" => body.IsValuable,
                    "Biological" => body.IsBiological,
                    "Remaining" =>
                        hasVisitState
                        && disposition
                            is ExplorationVisitDisposition.Active
                            or ExplorationVisitDisposition.Recommended,
                    "Deferred" =>
                        hasVisitState
                        && disposition
                            == ExplorationVisitDisposition.Deferred,
                    "Completed" =>
                        hasVisitState
                        && disposition
                            == ExplorationVisitDisposition.Complete,
                    "Unmapped" =>
                        !body.MappedThisVisit
                        && !body.MappedPreviously,
                    "Landable" => body.Landable,
                    _ => true
                };
            })
            .OrderBy(body =>
                VisitSortOrder(
                    dispositions.TryGetValue(
                        body.BodyId,
                        out ExplorationVisitDisposition disposition)
                        ? disposition
                        : null))
            .ThenByDescending(body => body.IsBiological)
            .ThenByDescending(body => body.IsValuable)
            .ThenByDescending(
                body => body.EstimatedMappingValue)
            .ThenBy(
                body => body.DistanceFromArrivalLs)
            .Select(body =>
                ToCatalogRow(
                    body,
                    dispositions.TryGetValue(
                        body.BodyId,
                        out ExplorationVisitDisposition disposition)
                        ? disposition
                        : null))
            .ToArray();

        ExplorationBodiesGrid.ItemsSource = rows;

        CatalogCountText.Text = Loc.Format(
            "Loc_Exploration_catalog_count_format",
            rows.Length,
            catalog.Bodies.Count);

        if (rows.Length > 0)
        {
            ExplorationBodiesGrid.SelectedIndex = 0;
        }
        else
        {
            ShowSelectedBody(null);
        }
    }

    private static Dictionary<int, ExplorationVisitDisposition>
        BuildVisitDispositionMap(
            GameStateSnapshot state,
            ExplorationVisitQueueSnapshot queue)
    {
        var result =
            new Dictionary<int, ExplorationVisitDisposition>();

        if (!QueueMatchesSystem(queue, state))
        {
            return result;
        }

        if (queue.Active is { } active)
        {
            result[active.BodyId] =
                ExplorationVisitDisposition.Active;
        }

        foreach (ExplorationVisitBodyState item
                 in queue.Recommended)
        {
            result[item.BodyId] =
                ExplorationVisitDisposition.Recommended;
        }

        foreach (ExplorationVisitBodyState item
                 in queue.Deferred)
        {
            result[item.BodyId] =
                ExplorationVisitDisposition.Deferred;
        }

        foreach (ExplorationVisitBodyState item
                 in queue.Completed)
        {
            result[item.BodyId] =
                ExplorationVisitDisposition.Complete;
        }

        return result;
    }

    private static int VisitSortOrder(
        ExplorationVisitDisposition? disposition) =>
        disposition switch
        {
            ExplorationVisitDisposition.Active => 0,
            ExplorationVisitDisposition.Recommended => 1,
            ExplorationVisitDisposition.Deferred => 2,
            ExplorationVisitDisposition.Complete => 3,
            _ => 4
        };

'@

Replace-RegexOnce `
    $codePath `
    '    private void ApplyCatalogFilter\(\).*?(?=    private static CatalogRow ToCatalogRow\()' `
    $applyCatalog `
    'visit-aware ApplyCatalogFilter'

# ---------------------------------------------------------------------------
# 5. Catalog rows expose visit disposition.
# ---------------------------------------------------------------------------
$toCatalog = @'
    private static CatalogRow ToCatalogRow(
        ExplorationCatalogBody body,
        ExplorationVisitDisposition? disposition) => new(
        body,
        body.Name,
        string.IsNullOrWhiteSpace(body.Subtype)
            ? body.Type
            : body.Subtype,
        BuildHighlightText(body),
        Loc.Format(
            "Loc_Distance_Ls_Value",
            body.DistanceFromArrivalLs),
        body.EstimatedMappingValue > 0
            ? Loc.Format(
                "Loc_Credits_Short_Format",
                body.EstimatedMappingValue)
            : Loc.Get("Loc_VALUE_UNKNOWN"),
        body.MappedThisVisit
            ? Loc.Get(
                body.EfficientlyMappedThisVisit
                    ? "Loc_DSS_EFFICIENT"
                    : "Loc_DSS_MAPPED")
            : body.MappedPreviously
                ? Loc.Get(
                    body.EfficientlyMappedPreviously
                        ? "Loc_HISTORY_DSS_EFFICIENT"
                        : "Loc_HISTORY_DSS_MAPPED")
                : body.ScannedThisVisit
                    ? Loc.Get("Loc_FSS_SCANNED")
                    : body.ScannedPreviously
                        ? Loc.Get("Loc_HISTORY_SCANNED")
                        : Loc.Get("Loc_COMMUNITY_DATA_ONLY"),
        disposition,
        BuildVisitStateLabel(disposition));

    private static string BuildVisitStateLabel(
        ExplorationVisitDisposition? disposition) =>
        disposition switch
        {
            ExplorationVisitDisposition.Active =>
                Loc.Get("Loc_EXPLORATION_STATE_ACTIVE"),
            ExplorationVisitDisposition.Recommended =>
                Loc.Get("Loc_EXPLORATION_STATE_RECOMMENDED"),
            ExplorationVisitDisposition.Deferred =>
                Loc.Get("Loc_EXPLORATION_STATE_DEFERRED"),
            ExplorationVisitDisposition.Complete =>
                Loc.Get("Loc_EXPLORATION_STATE_COMPLETE"),
            _ => "—"
        };

'@

Replace-RegexOnce `
    $codePath `
    '    private static CatalogRow ToCatalogRow\(ExplorationCatalogBody body\).*?(?=    private static string BuildHighlightText\()' `
    $toCatalog `
    'visit-aware ToCatalogRow'

# ---------------------------------------------------------------------------
# 6. Selected body: show exact objective progress, missing genera, prediction
#    hints, and explicit limitations about organism coordinates.
# ---------------------------------------------------------------------------
$showSelected = @'
    private void ShowSelectedBody(CatalogRow? row)
    {
        if (row is null)
        {
            SelectedBodyNameText.Text =
                Loc.Get("Loc_Select_a_body");
            SelectedBodyReasonText.Text =
                string.Empty;
            SelectedBodyDetailsText.Text =
                string.Empty;
            DssSelectedBodyText.Text =
                Loc.Get("Loc_Select_a_body");
            DssMappingResultText.Text =
                string.Empty;

            CopySelectedBodyButton.IsEnabled = false;
            BookmarkSelectedBodyButton.IsEnabled = false;

            DeferSelectedBodyButton.Visibility =
                Visibility.Collapsed;
            ResumeSelectedBodyButton.Visibility =
                Visibility.Collapsed;

            return;
        }

        ExplorationCatalogBody body = row.Body;

        SelectedBodyNameText.Text = body.Name;
        DssSelectedBodyText.Text = body.Name;
        SelectedBodyReasonText.Text = row.Highlights;

        ExplorationVisitBodyState? visit =
            FindVisitBodyState(body.BodyId);

        var detailParts = new List<string>();

        string visitDetails =
            BuildSelectedBodyVisitDetails(
                visit);

        if (!string.IsNullOrWhiteSpace(visitDetails))
        {
            detailParts.Add(visitDetails);
        }

        detailParts.AddRange(
        [
            Loc.Format(
                "Loc_BODY_TYPE_DETAIL",
                row.Type),
            Loc.Format(
                "Loc_BODY_DISTANCE_DETAIL",
                body.DistanceFromArrivalLs),
            Loc.Format(
                "Loc_BODY_SCAN_VALUE_DETAIL",
                body.EstimatedScanValue),
            Loc.Format(
                "Loc_BODY_MAPPING_VALUE_DETAIL",
                body.EstimatedMappingValue),
            Loc.Format(
                "Loc_BODY_GRAVITY_DETAIL",
                body.GravityG),
            Loc.Format(
                "Loc_BODY_TEMPERATURE_DETAIL",
                body.SurfaceTemperatureKelvin),
            Loc.Format(
                "Loc_BODY_PRESSURE_DETAIL",
                body.SurfacePressureAtmospheres),
            Loc.Format(
                "Loc_BODY_ATMOSPHERE_DETAIL",
                EmptyAsUnknown(body.Atmosphere)),
            Loc.Format(
                "Loc_BODY_VOLCANISM_DETAIL",
                EmptyAsUnknown(body.Volcanism)),
            Loc.Format(
                "Loc_BODY_BIOLOGY_DETAIL",
                body.BiologicalSignals,
                body.Genuses.Count == 0
                    ? Loc.Get("Loc_VALUE_UNKNOWN")
                    : string.Join(", ", body.Genuses)),
            Loc.Format(
                "Loc_BODY_ORGANICS_HISTORY_DETAIL",
                body.CompletedOrganics),
            Loc.Format(
                "Loc_BODY_SOURCE_DETAIL",
                LocalizeCatalogSource(body.Source))
        ]);

        string bioGuidance =
            BuildSelectedBodyBioGuidance(
                body,
                visit,
                JournalMonitorService.Instance.Current);

        if (!string.IsNullOrWhiteSpace(bioGuidance))
        {
            detailParts.Add(bioGuidance);
        }
        else
        {
            detailParts.Add(
                BuildPredictionDetails(body));
        }

        SelectedBodyDetailsText.Text =
            string.Join(
                Environment.NewLine,
                detailParts.Where(
                    value =>
                        !string.IsNullOrWhiteSpace(value)));

        SetDssTarget(
            body.EfficiencyTarget > 0
                ? body.EfficiencyTarget
                : SettingsService.Instance.Settings
                    .DssEfficiencyTarget);

        DssMappingResultText.Text =
            body.LastProbesUsed > 0
            && body.EfficiencyTarget > 0
                ? Loc.Format(
                    body.LastProbesUsed
                        <= body.EfficiencyTarget
                            ? "Loc_DSS_RESULT_EFFICIENT"
                            : "Loc_DSS_RESULT_OVER_TARGET",
                    body.LastProbesUsed,
                    body.EfficiencyTarget)
                : Loc.Get(
                    "Loc_DSS_NO_RESULT_YET");

        CopySelectedBodyButton.IsEnabled =
            !string.IsNullOrWhiteSpace(body.Name);

        BookmarkSelectedBodyButton.IsEnabled =
            !string.IsNullOrWhiteSpace(body.Name);

        DeferSelectedBodyButton.Visibility =
            visit is not null
            && !visit.IsComplete
            && visit.Disposition
                is ExplorationVisitDisposition.Active
                or ExplorationVisitDisposition.Recommended
                ? Visibility.Visible
                : Visibility.Collapsed;

        ResumeSelectedBodyButton.Visibility =
            visit?.Disposition
                == ExplorationVisitDisposition.Deferred
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private static ExplorationVisitBodyState? FindVisitBodyState(
        int bodyId)
    {
        ExplorationVisitQueueSnapshot queue =
            ExplorationVisitStateService.Instance.Current;

        if (queue.Active?.BodyId == bodyId)
        {
            return queue.Active;
        }

        return queue.Recommended
            .Concat(queue.Deferred)
            .Concat(queue.Completed)
            .FirstOrDefault(
                item => item.BodyId == bodyId);
    }

    private static string BuildSelectedBodyVisitDetails(
        ExplorationVisitBodyState? visit)
    {
        if (visit is null)
        {
            return string.Empty;
        }

        string fss = visit.Progress.FssScanned
            ? "FSS ✓"
            : "FSS ○";

        string dss = !visit.DssRequired
            ? "DSS —"
            : visit.Progress.DssMapped
                ? visit.Progress.DssEfficient
                    ? "DSS ◎"
                    : "DSS ✓"
                : "DSS ○";

        string bio = !visit.BiologyRequired
            ? "BIO —"
            : $"BIO {visit.Progress.CompletedBiologicalSignals}/{visit.Progress.BiologicalSignals}";

        return Loc.Format(
            "Loc_EXPLORATION_SELECTED_PROGRESS_FORMAT",
            BuildVisitStateLabel(visit.Disposition),
            string.Join(
                "  •  ",
                fss,
                dss,
                bio));
    }

    private static string BuildSelectedBodyBioGuidance(
        ExplorationCatalogBody body,
        ExplorationVisitBodyState? visit,
        GameStateSnapshot state)
    {
        if (!body.IsBiological
            || body.BiologicalSignals <= 0)
        {
            return string.Empty;
        }

        var lines = new List<string>
        {
            Loc.Get("Loc_EXPLORATION_BIO_GUIDANCE_HEADER")
        };

        BodyExplorationProgress? progress =
            visit?.Progress;

        if (progress is not null)
        {
            lines.Add(
                Loc.Format(
                    "Loc_EXPLORATION_BIO_BODY_PROGRESS_FORMAT",
                    progress.CompletedBiologicalSignals,
                    progress.BiologicalSignals));

            if (progress.BiologyComplete)
            {
                lines.Add(
                    Loc.Get(
                        "Loc_EXPLORATION_BIO_COMPLETE_GUIDANCE"));

                return string.Join(
                    Environment.NewLine,
                    lines);
            }

            if (progress.MissingGenuses.Count > 0)
            {
                lines.Add(
                    Loc.Format(
                        "Loc_EXPLORATION_MISSING_GENUSES_FORMAT",
                        string.Join(
                            " · ",
                            progress.MissingGenuses)));
            }

            int unnamedRemaining = Math.Max(
                0,
                progress.RemainingBiologicalSignals
                    - progress.MissingGenuses.Count);

            if (unnamedRemaining > 0)
            {
                lines.Add(
                    Loc.Format(
                        "Loc_EXPLORATION_UNKNOWN_GENUSES_FORMAT",
                        unnamedRemaining));
            }
        }

        OrganicScanProgressSnapshot? activeOrganic =
            state.GetActiveOrganicForBody(body.BodyId);

        if (activeOrganic is not null)
        {
            string organism =
                !string.IsNullOrWhiteSpace(
                    activeOrganic.Variant)
                    ? activeOrganic.Variant
                    : activeOrganic.Species;

            lines.Add(
                Loc.Format(
                    "Loc_EXPLORATION_ACTIVE_SAMPLE_FORMAT",
                    organism,
                    activeOrganic.Stage,
                    activeOrganic.ColonyRangeMeters));

            SurfaceNavigationResult? navigation =
                SurfaceNavigationCalculator.Calculate(
                    state.Latitude,
                    state.Longitude,
                    state.HeadingDegrees,
                    state.PlanetRadiusMeters,
                    activeOrganic.LastSampleLatitude,
                    activeOrganic.LastSampleLongitude);

            if (navigation is not null
                && activeOrganic.ColonyRangeMeters > 0)
            {
                double remaining = Math.Max(
                    0,
                    activeOrganic.ColonyRangeMeters
                        - navigation.DistanceMeters);

                lines.Add(
                    navigation.IsFarEnough(
                        activeOrganic.ColonyRangeMeters)
                        ? Loc.Format(
                            "Loc_EXPLORATION_SAMPLE_RANGE_READY_FORMAT",
                            navigation.DistanceMeters)
                        : Loc.Format(
                            "Loc_EXPLORATION_SAMPLE_RANGE_REMAINING_FORMAT",
                            navigation.DistanceMeters,
                            remaining,
                            activeOrganic.ColonyRangeMeters,
                            navigation.EscapeBearingDegrees));
            }
        }

        IReadOnlyList<ExobiologyPrediction> predictions =
            ExobiologyPredictionService.Instance.Predict(
                body,
                12);

        if (progress is { MissingGenuses.Count: > 0 })
        {
            predictions = predictions
                .Where(prediction =>
                    progress.MissingGenuses.Any(
                        genus =>
                            GenusMatches(
                                genus,
                                prediction.Genus)))
                .ToArray();
        }

        ExobiologyPrediction[] likely =
            predictions
                .GroupBy(
                    prediction => prediction.Genus,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group
                        .OrderByDescending(
                            item =>
                                item.RelativeProbability)
                        .ThenByDescending(
                            item =>
                                item.ObservationCount)
                        .First())
                .OrderByDescending(
                    item => item.RelativeProbability)
                .Take(4)
                .ToArray();

        if (likely.Length > 0)
        {
            lines.Add(
                Loc.Get(
                    "Loc_EXPLORATION_LIKELY_SPECIES_HEADER"));

            foreach (ExobiologyPrediction prediction
                     in likely)
            {
                lines.Add(
                    Loc.Format(
                        "Loc_EXPLORATION_LIKELY_SPECIES_LINE_FORMAT",
                        prediction.Genus,
                        prediction.Species,
                        prediction.RelativeProbability * 100,
                        prediction.ColonyRangeMeters,
                        prediction.BaseValue));
            }
        }

        lines.Add(
            Loc.Get(
                "Loc_EXPLORATION_BIO_LOCATION_LIMITATION"));

        return string.Join(
            Environment.NewLine,
            lines);
    }

    private static bool GenusMatches(
        string expected,
        string actual)
    {
        if (string.Equals(
            expected.Trim(),
            actual.Trim(),
            StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return expected.Contains(
                   actual,
                   StringComparison.OrdinalIgnoreCase)
               || actual.Contains(
                   expected,
                   StringComparison.OrdinalIgnoreCase);
    }

'@

Replace-RegexOnce `
    $codePath `
    '    private void ShowSelectedBody\(CatalogRow\? row\).*?(?=    private void SetDssTarget\()' `
    $showSelected `
    'selected body progress and bio guidance'

# ---------------------------------------------------------------------------
# 7. Manual defer/resume button handlers.
# ---------------------------------------------------------------------------
$code = Read-Text $codePath

if (-not $code.Contains('private void DeferSelectedBodyButton_Click(')) {
    $handlers = @'
    private void DeferSelectedBodyButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ExplorationBodiesGrid.SelectedItem
            is not CatalogRow row)
        {
            return;
        }

        if (ExplorationVisitStateService.Instance
            .DeferBody(row.Body.BodyId))
        {
            RefreshContent(
                JournalMonitorService.Instance.Current);
        }
    }

    private void ResumeSelectedBodyButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (ExplorationBodiesGrid.SelectedItem
            is not CatalogRow row)
        {
            return;
        }

        if (ExplorationVisitStateService.Instance
            .ResumeBody(row.Body.BodyId))
        {
            RefreshContent(
                JournalMonitorService.Instance.Current);
        }
    }

'@

    $anchor = @'
    private void SetDssTarget(int target)
'@

    if (-not $code.Contains($anchor)) {
        throw 'Could not locate SetDssTarget insertion anchor.'
    }

    Write-Text $codePath ($code.Replace(
        $anchor,
        $handlers + $anchor))
}

# ---------------------------------------------------------------------------
# 8. Extend CatalogRow with visit state.
# ---------------------------------------------------------------------------
$code = Read-Text $codePath

$oldRow = @'
    private sealed record CatalogRow(
        ExplorationCatalogBody Body,
        string Name,
        string Type,
        string Highlights,
        string Distance,
        string MappingValue,
        string Progress);
'@

$newRow = @'
    private sealed record CatalogRow(
        ExplorationCatalogBody Body,
        string Name,
        string Type,
        string Highlights,
        string Distance,
        string MappingValue,
        string Progress,
        ExplorationVisitDisposition? Disposition,
        string VisitState);
'@

if ($code.Contains($oldRow)) {
    Replace-LiteralOnce `
        $codePath `
        $oldRow `
        $newRow `
        'CatalogRow visit-state fields'
}
elseif (-not $code.Contains('string VisitState);')) {
    throw 'Could not locate CatalogRow record.'
}

# ---------------------------------------------------------------------------
# 9. Full assistant summary includes queue counts.
# ---------------------------------------------------------------------------
$code = Read-Text $codePath

$oldSourceText = @'
        CatalogSourceText.Text = sourceMode + Environment.NewLine + (import.IsRunning
            ? Loc.Format("Loc_Exploration_history_import_progress_format", import.ProcessedFiles, import.TotalFiles)
            : Loc.Format("Loc_Exploration_history_status_format", history.Bodies.Count));
'@

$newSourceText = @'
        ExplorationVisitQueueSnapshot visitQueue =
            ExplorationVisitStateService.Instance.Current;

        string queueSummary = QueueMatchesSystem(
            visitQueue,
            state)
            ? Loc.Format(
                "Loc_EXPLORATION_QUEUE_FULL_FORMAT",
                visitQueue.RemainingCount,
                visitQueue.DeferredCount,
                visitQueue.CompletedCount)
            : string.Empty;

        CatalogSourceText.Text =
            sourceMode
            + Environment.NewLine
            + (import.IsRunning
                ? Loc.Format(
                    "Loc_Exploration_history_import_progress_format",
                    import.ProcessedFiles,
                    import.TotalFiles)
                : Loc.Format(
                    "Loc_Exploration_history_status_format",
                    history.Bodies.Count))
            + (string.IsNullOrWhiteSpace(queueSummary)
                ? string.Empty
                : Environment.NewLine + queueSummary);
'@

if ($code.Contains($oldSourceText)) {
    Replace-LiteralOnce `
        $codePath `
        $oldSourceText `
        $newSourceText `
        'catalog queue summary'
}
elseif (-not $code.Contains('Loc_EXPLORATION_QUEUE_FULL_FORMAT')) {
    throw 'Could not locate CatalogSourceText history block.'
}

# ---------------------------------------------------------------------------
# 10. Localization.
# ---------------------------------------------------------------------------
$en = @{
    'Loc_FILTER_REMAINING' = 'Remaining'
    'Loc_FILTER_DEFERRED' = 'Deferred'
    'Loc_FILTER_COMPLETED' = 'Completed'
    'Loc_EXPLORATION_VISIT_STATE' = 'QUEUE'
    'Loc_EXPLORATION_STATE_ACTIVE' = 'ACTIVE'
    'Loc_EXPLORATION_STATE_RECOMMENDED' = 'RECOMMENDED'
    'Loc_EXPLORATION_STATE_DEFERRED' = 'DEFERRED'
    'Loc_EXPLORATION_STATE_COMPLETE' = 'COMPLETE'
    'Loc_EXPLORATION_DEFER_THIS_VISIT' = 'DEFER FOR THIS VISIT'
    'Loc_EXPLORATION_RESUME_BODY' = 'RETURN TO QUEUE'
    'Loc_EXPLORATION_SELECTED_PROGRESS_FORMAT' = 'Visit state: {0}  •  {1}'
    'Loc_EXPLORATION_QUEUE_FULL_FORMAT' = 'Queue: {0} remaining • {1} deferred • {2} complete'
    'Loc_EXPLORATION_BIO_GUIDANCE_HEADER' = 'BIO GUIDANCE'
    'Loc_EXPLORATION_BIO_BODY_PROGRESS_FORMAT' = 'Completed signals: {0}/{1}'
    'Loc_EXPLORATION_BIO_COMPLETE_GUIDANCE' = 'All known biological signals on this body are complete.'
    'Loc_EXPLORATION_ACTIVE_SAMPLE_FORMAT' = 'Current sample: {0} • stage {1}/3 • required separation {2} m'
    'Loc_EXPLORATION_SAMPLE_RANGE_READY_FORMAT' = 'Distance from last sample: {0:0} m • separation requirement satisfied.'
    'Loc_EXPLORATION_SAMPLE_RANGE_REMAINING_FORMAT' = 'Distance from last sample: {0:0} m • {1:0} m remaining of {2} m • move away around bearing {3:0}°.'
    'Loc_EXPLORATION_LIKELY_SPECIES_HEADER' = 'Likely species from known body conditions:'
    'Loc_EXPLORATION_LIKELY_SPECIES_LINE_FORMAT' = '{0}: {1} • relative confidence {2:0}% • colony range {3} m • base {4:N0} CR'
    'Loc_EXPLORATION_BIO_LOCATION_LIMITATION' = 'Elite does not expose coordinates of organisms that have not been found. Species suggestions are statistical; after the first sample the overlay can guide minimum sample separation, not the location of the next organism.'
}

$ru = @{
    'Loc_FILTER_REMAINING' = 'Осталось'
    'Loc_FILTER_DEFERRED' = 'Отложено'
    'Loc_FILTER_COMPLETED' = 'Завершено'
    'Loc_EXPLORATION_VISIT_STATE' = 'ОЧЕРЕДЬ'
    'Loc_EXPLORATION_STATE_ACTIVE' = 'АКТИВНО'
    'Loc_EXPLORATION_STATE_RECOMMENDED' = 'РЕКОМЕНДОВАНО'
    'Loc_EXPLORATION_STATE_DEFERRED' = 'ОТЛОЖЕНО'
    'Loc_EXPLORATION_STATE_COMPLETE' = 'ЗАВЕРШЕНО'
    'Loc_EXPLORATION_DEFER_THIS_VISIT' = 'ОТЛОЖИТЬ В ЭТОМ ВИЗИТЕ'
    'Loc_EXPLORATION_RESUME_BODY' = 'ВЕРНУТЬ В ОЧЕРЕДЬ'
    'Loc_EXPLORATION_SELECTED_PROGRESS_FORMAT' = 'Статус визита: {0}  •  {1}'
    'Loc_EXPLORATION_QUEUE_FULL_FORMAT' = 'Очередь: осталось {0} • отложено {1} • завершено {2}'
    'Loc_EXPLORATION_BIO_GUIDANCE_HEADER' = 'ПОДСКАЗКИ ПО ЭКЗОБИОЛОГИИ'
    'Loc_EXPLORATION_BIO_BODY_PROGRESS_FORMAT' = 'Завершено сигналов: {0}/{1}'
    'Loc_EXPLORATION_BIO_COMPLETE_GUIDANCE' = 'Все известные биологические сигналы на этом теле завершены.'
    'Loc_EXPLORATION_ACTIVE_SAMPLE_FORMAT' = 'Текущий образец: {0} • этап {1}/3 • необходимая дистанция {2} м'
    'Loc_EXPLORATION_SAMPLE_RANGE_READY_FORMAT' = 'От последнего образца: {0:0} м • требуемая дистанция набрана.'
    'Loc_EXPLORATION_SAMPLE_RANGE_REMAINING_FORMAT' = 'От последнего образца: {0:0} м • осталось {1:0} м из {2} м • удаляйтесь примерно по азимуту {3:0}°.'
    'Loc_EXPLORATION_LIKELY_SPECIES_HEADER' = 'Вероятные виды по известным условиям тела:'
    'Loc_EXPLORATION_LIKELY_SPECIES_LINE_FORMAT' = '{0}: {1} • относительная вероятность {2:0}% • дистанция колонии {3} м • база {4:N0} CR'
    'Loc_EXPLORATION_BIO_LOCATION_LIMITATION' = 'Elite не сообщает координаты ещё не найденных организмов. Прогноз видов статистический; после первого образца оверлей может подсказать минимальную дистанцию между образцами, но не местоположение следующего организма.'
}

Add-LocalizationEntries $localizationEnPath $en
Add-LocalizationEntries $localizationRuPath $ru

# ---------------------------------------------------------------------------
# 11. Regression/static tests.
# ---------------------------------------------------------------------------
$tests = @'
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class ExplorationQueueBioGuidanceTests
{
    [Fact]
    public void FullAssistantExposesQueueFiltersAndManualControls()
    {
        string code = File.ReadAllText(
            FindProjectFile(
                "Windows",
                "ActivityWorkspaceOverlayWindow.xaml.cs"));

        string xaml = File.ReadAllText(
            FindProjectFile(
                "Windows",
                "ActivityWorkspaceOverlayWindow.xaml"));

        Assert.Contains(
            "new(\"Remaining\", \"Loc_FILTER_REMAINING\")",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "new(\"Deferred\", \"Loc_FILTER_DEFERRED\")",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "new(\"Completed\", \"Loc_FILTER_COMPLETED\")",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"DeferSelectedBodyButton\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "x:Name=\"ResumeSelectedBodyButton\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "DeferBody(row.Body.BodyId)",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "ResumeBody(row.Body.BodyId)",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedBodyGuidanceUsesExactProgressAndPredictions()
    {
        string code = File.ReadAllText(
            FindProjectFile(
                "Windows",
                "ActivityWorkspaceOverlayWindow.xaml.cs"));

        Assert.Contains(
            "progress.MissingGenuses",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "state.GetActiveOrganicForBody(body.BodyId)",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "SurfaceNavigationCalculator.Calculate(",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "ExobiologyPredictionService.Instance.Predict(",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "Loc_EXPLORATION_BIO_LOCATION_LIMITATION",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void QueueStateIsVisibleInCatalogRows()
    {
        string code = File.ReadAllText(
            FindProjectFile(
                "Windows",
                "ActivityWorkspaceOverlayWindow.xaml.cs"));

        string xaml = File.ReadAllText(
            FindProjectFile(
                "Windows",
                "ActivityWorkspaceOverlayWindow.xaml"));

        Assert.Contains(
            "string VisitState);",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "BuildVisitDispositionMap(",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "Binding=\"{Binding VisitState}\"",
            xaml,
            StringComparison.Ordinal);
    }

    private static string FindProjectFile(
        params string[] relative)
    {
        for (
            DirectoryInfo? directory =
                new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            string candidate = Path.Combine(
                [
                    directory.FullName,
                    "ED_Inara_Overlay",
                    .. relative
                ]);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            string.Join(
                Path.DirectorySeparatorChar,
                relative));
    }
}
'@

Write-Text $testsPath $tests

# ---------------------------------------------------------------------------
# 12. Sanity checks.
# ---------------------------------------------------------------------------
$finalCode = Read-Text $codePath
$finalXaml = Read-Text $xamlPath

foreach ($needle in @(
    'Loc_FILTER_REMAINING',
    'BuildVisitDispositionMap(',
    'BuildSelectedBodyBioGuidance(',
    'DeferSelectedBodyButton_Click(',
    'ResumeSelectedBodyButton_Click(',
    'Loc_EXPLORATION_BIO_LOCATION_LIMITATION'
)) {
    if (-not $finalCode.Contains($needle)) {
        throw "Missing patch-4 code: $needle"
    }
}

foreach ($needle in @(
    'x:Name="DeferSelectedBodyButton"',
    'x:Name="ResumeSelectedBodyButton"',
    'Binding="{Binding VisitState}"'
)) {
    if (-not $finalXaml.Contains($needle)) {
        throw "Missing patch-4 XAML: $needle"
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

    & dotnet build `
        '.\ED_Inara_Overlay\ED_Inara_Overlay.csproj' `
        -c Debug

    if ($LASTEXITCODE -ne 0) {
        throw 'Application build failed.'
    }

    Write-Host ''
    Write-Host 'Running regression tests...' -ForegroundColor Cyan

    & dotnet test `
        '.\Testing\ED_Inara_Overlay.LayoutTests\ED_Inara_Overlay.LayoutTests.csproj' `
        -c Debug

    if ($LASTEXITCODE -ne 0) {
        throw 'Regression tests failed.'
    }
}

Write-Host ''
Write-Host 'Exploration queue + bio guidance applied.' -ForegroundColor Green
Write-Host ''
Write-Host 'Full assistant now adds:'
Write-Host '  - Remaining / Deferred / Completed filters'
Write-Host '  - queue-state column'
Write-Host '  - manual Defer / Resume controls'
Write-Host '  - exact FSS / DSS / BIO progress for selected interesting bodies'
Write-Host '  - missing genus list'
Write-Host '  - likely species predictions for unfinished genera'
Write-Host '  - active sample colony-range guidance'
Write-Host '  - explicit limitation: no coordinates for undiscovered organisms'
Write-Host ''
Write-Host "Backup of previous local diff: $backup"
