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

$testPath =
    'Testing\ED_Inara_Overlay.LayoutTests\AdaptiveExplorationHudTests.cs'

if (-not (Test-Path $testPath)) {
    throw "Required file not found: $testPath"
}

& git diff --binary -- $testPath |
    Set-Content `
        -Path 'adaptive-exploration-hud-final-test-before.patch' `
        -Encoding utf8

$text = Read-Text $testPath

if (-not $text.Contains('using System.Xml.Linq;')) {
    $text = $text.Replace(
        'using Xunit;',
        "using System.Xml.Linq;`nusing Xunit;"
    )
}

$oldMethod = @'
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
'@

$newMethod = @'
    [Fact]
    public void AdaptiveExplorationHudIsNotInsideLegacyScrollViewer()
    {
        XDocument document = XDocument.Load(FindWorkspaceXaml());
        XNamespace wpf =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement adaptiveHud = Assert.Single(
            document.Descendants(),
            element =>
                (string?)element.Attribute(x + "Name")
                    == "AdaptiveExplorationPanel");

        XElement legacyScroll = Assert.Single(
            document.Descendants(wpf + "ScrollViewer"),
            element =>
                (string?)element.Attribute(x + "Name")
                    == "LegacyCompactScrollViewer");

        Assert.Empty(
            adaptiveHud.Descendants(wpf + "ScrollViewer"));

        Assert.DoesNotContain(
            legacyScroll.Ancestors(),
            ancestor => ReferenceEquals(ancestor, adaptiveHud));
    }
'@

if ($text.Contains($oldMethod)) {
    $text = $text.Replace($oldMethod, $newMethod)
}
elseif (-not $text.Contains('XDocument document = XDocument.Load(FindWorkspaceXaml());')) {
    throw 'Could not locate the failing AdaptiveExplorationHudIsNotInsideLegacyScrollViewer test.'
}

Write-Text $testPath $text

Write-Host ''
& git diff --check
if ($LASTEXITCODE -ne 0) {
    throw 'git diff --check failed.'
}

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
Write-Host 'Adaptive exploration HUD test fixed.' -ForegroundColor Green
Write-Host 'The production HUD code was not changed.'
