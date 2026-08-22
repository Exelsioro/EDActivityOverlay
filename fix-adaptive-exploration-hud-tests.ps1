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
    $full = (Resolve-Path $Path).Path
    $old = [System.IO.File]::ReadAllText($full)
    $newline = if ($old.Contains("`r`n")) { "`r`n" } else { "`n" }

    $normalized = $Text.Replace("`r`n", "`n")
    if ($newline -eq "`r`n") {
        $normalized = $normalized.Replace("`n", "`r`n")
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

$workspaceTests =
    'Testing\ED_Inara_Overlay.LayoutTests\ExplorationWorkspaceUiTests.cs'

$adaptiveTests =
    'Testing\ED_Inara_Overlay.LayoutTests\AdaptiveExplorationHudTests.cs'

$xaml =
    'ED_Inara_Overlay\Windows\ActivityWorkspaceOverlayWindow.xaml'

$code =
    'ED_Inara_Overlay\Windows\ActivityWorkspaceOverlayWindow.xaml.cs'

foreach ($path in @(
    $workspaceTests,
    $adaptiveTests,
    $xaml,
    $code
)) {
    if (-not (Test-Path $path)) {
        throw "Required file not found: $path"
    }
}

& git diff --binary -- $workspaceTests $adaptiveTests |
    Set-Content `
        -Path 'adaptive-exploration-hud-tests-before.patch' `
        -Encoding utf8

Write-Host 'Fixing adaptive exploration HUD tests...' -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# 1. The legacy UI test asserted that the compact exploration workspace itself
#    was a Grid.Row=2 ScrollViewer. That is intentionally no longer true:
#    exploration is now a fixed HUD and only the legacy/mining compact panel
#    remains scrollable.
# ---------------------------------------------------------------------------
$oldScrollAssertion = @'
        Assert.Contains(document.Descendants(wpf + "ScrollViewer"), element =>
            (string?)element.Attribute("Grid.Row") == "2"
            && (string?)element.Attribute("VerticalScrollBarVisibility") == "Auto");
'@

$newScrollAssertion = @'
        XElement legacyScroll = Assert.Single(
            document.Descendants(wpf + "ScrollViewer"),
            element =>
                (string?)element.Attribute(x + "Name")
                    == "LegacyCompactScrollViewer");

        Assert.Equal(
            "Auto",
            (string?)legacyScroll.Attribute(
                "VerticalScrollBarVisibility"));

        XElement adaptiveHud = Assert.Single(
            document.Descendants(),
            element =>
                (string?)element.Attribute(x + "Name")
                    == "AdaptiveExplorationPanel");

        Assert.DoesNotContain(
            adaptiveHud.Descendants(),
            element => element.Name == wpf + "ScrollViewer");
'@

$workspaceText = Read-Text $workspaceTests

if ($workspaceText.Contains($oldScrollAssertion)) {
    Replace-LiteralOnce `
        $workspaceTests `
        $oldScrollAssertion `
        $newScrollAssertion `
        'obsolete compact ScrollViewer assertion'
}
elseif (-not $workspaceText.Contains('LegacyCompactScrollViewer')) {
    throw 'Could not locate the old compact workspace assertion.'
}

# Rename the test to describe the new contract if it still has the old name.
$workspaceText = Read-Text $workspaceTests
$workspaceText = $workspaceText.Replace(
    'public void CompactWorkspaceScrollsAndFullWorkspaceContainsCatalogLogAndRoute()',
    'public void CompactWorkspaceHasAdaptiveHudAndFullWorkspaceContainsCatalogLogAndRoute()'
)
Write-Text $workspaceTests $workspaceText

# ---------------------------------------------------------------------------
# 2. Replace the tests added by patch 3. The original helper incorrectly
#    assumed that the repository root contains ED_Inara_Overlay.sln.
#    Existing tests in this project locate the actual project/XAML file while
#    walking AppContext.BaseDirectory, so use the same robust strategy.
# ---------------------------------------------------------------------------
$adaptive = @'
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class AdaptiveExplorationHudTests
{
    [Fact]
    public void CompactExplorationHudHasThreeAdaptiveContexts()
    {
        string xaml = File.ReadAllText(FindWorkspaceXaml());

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
        string xaml = File.ReadAllText(FindWorkspaceXaml());

        int adaptive = xaml.IndexOf(
            "x:Name=\"AdaptiveExplorationPanel\"",
            StringComparison.Ordinal);

        int legacyScroll = xaml.IndexOf(
            "x:Name=\"LegacyCompactScrollViewer\"",
            StringComparison.Ordinal);

        Assert.True(adaptive >= 0);
        Assert.True(legacyScroll > adaptive);

        string beforeLegacyScroll =
            xaml.Substring(
                adaptive,
                legacyScroll - adaptive);

        Assert.DoesNotContain(
            "<ScrollViewer",
            beforeLegacyScroll,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HudUsesVisitQueueInsteadOfLegacyCompactTargetSelection()
    {
        string code = File.ReadAllText(
            FindProjectFile(
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
        string code = File.ReadAllText(
            FindProjectFile(
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

    private static string FindWorkspaceXaml() =>
        FindProjectFile(
            "Windows",
            "ActivityWorkspaceOverlayWindow.xaml");

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

Write-Text $adaptiveTests $adaptive

# ---------------------------------------------------------------------------
# 3. Sanity checks.
# ---------------------------------------------------------------------------
$workspaceCheck = Read-Text $workspaceTests
$adaptiveCheck = Read-Text $adaptiveTests

if (-not $workspaceCheck.Contains(
        'LegacyCompactScrollViewer')) {
    throw 'Updated workspace UI test was not written.'
}

if ($adaptiveCheck.Contains(
        'ED_Inara_Overlay.sln')) {
    throw 'Broken solution-name root lookup still remains.'
}

foreach ($needle in @(
    'AdaptiveExplorationPanel',
    'SystemContextPanel',
    'BodyContextPanel',
    'ExobioContextPanel'
)) {
    if (-not $adaptiveCheck.Contains($needle)) {
        throw "Adaptive HUD test is missing: $needle"
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
Write-Host 'Adaptive exploration HUD tests fixed.' -ForegroundColor Green
Write-Host 'Production HUD code was not changed by this corrective patch.'
