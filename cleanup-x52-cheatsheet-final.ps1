param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

function Read-Text([string]$Path) {
    if (-not (Test-Path $Path)) {
        throw "Required file not found: ${Path}"
    }

    return ([System.IO.File]::ReadAllText((Resolve-Path $Path).Path)).Replace("`r`n", "`n")
}

function Write-Text([string]$Path, [string]$Text) {
    $full = (Resolve-Path $Path).Path
    $original = [System.IO.File]::ReadAllText($full)

    $newline = if ($original.Contains("`r`n")) { "`r`n" } else { "`n" }

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

$branch = (& git branch --show-current).Trim()
if ($LASTEXITCODE -ne 0) {
    throw 'Could not determine current git branch.'
}

if ($branch -ne 'Exploration-ui-update') {
    throw "Run this patch on Exploration-ui-update. Current branch: ${branch}"
}

$cheatsheetPath = 'Documentation\X52_CONTROL_CHEATSHEET_RU.md'

if (-not (Test-Path $cheatsheetPath)) {
    throw "Required file not found: ${cheatsheetPath}"
}

Write-Host "Current branch: ${branch}" -ForegroundColor DarkGray
Write-Host 'Cleaning X52 cheatsheet...' -ForegroundColor Cyan

$backupPath = 'x52-cheatsheet-cleanup-before.patch'
& git diff --binary -- $cheatsheetPath |
    Set-Content -Path $backupPath -Encoding utf8

$text = Read-Text $cheatsheetPath

# ---------------------------------------------------------------------------
# 1. Remove the stale statement that the application synthesizes Fire A clicks.
#    Keep the useful native mouse-profile explanation.
# ---------------------------------------------------------------------------
$oldParagraph = @'
Для мини-джойстика на РУД в Logitech/Saitek Profiler должны быть назначены
`Mouse X Axis` и `Mouse Y Axis`. Приложение больше не двигает курсор через
цифровой POV 1: именно дискретные шаги POV делали движение медленным и
дёрганым. Fire A используется приложением как клик только в интерактивном
режиме.
'@

$newParagraph = @'
Для мини-джойстика на РУД в Logitech/Saitek Profiler должны быть назначены
`Mouse X Axis` и `Mouse Y Axis`. Приложение больше не двигает курсор через
цифровой POV 1 и не синтезирует клики через Fire A: курсор и левый клик
полностью оставлены штатным `Mouse Pointer` / `Mouse Click` профиля X52.
'@

if ($text.Contains($oldParagraph)) {
    $text = $text.Replace($oldParagraph, $newParagraph)
}
else {
    # Fallback for locally reflowed text: remove only the obsolete sentence.
    $text = [regex]::Replace(
        $text,
        '(?ms)Fire A используется приложением как клик только в интерактивном\s+режиме\.',
        'Приложение не синтезирует клики через Fire A: курсор и левый клик оставлены штатным `Mouse Pointer` / `Mouse Click` профиля X52.'
    )
}

# ---------------------------------------------------------------------------
# 2. Keep one authoritative "Оверлей" section and make sure native Mouse Click
#    is present there.
# ---------------------------------------------------------------------------
$mousePointerRow = '| Mouse Pointer на РУД | Курсор |'
$mouseClickRow = '| Mouse Click на РУД | Левый клик |'

if ($text.Contains($mousePointerRow) -and -not $text.Contains($mouseClickRow)) {
    $text = $text.Replace(
        $mousePointerRow,
        $mousePointerRow + "`n" + $mouseClickRow
    )
}

# Remove the duplicate section that was appended by an earlier repair script.
$duplicatePattern =
    '(?ms)^## Актуальное управление оверлеем\s*$.*\z'

$text = [regex]::Replace(
    $text,
    $duplicatePattern,
    ''
)

$text = $text.TrimEnd() + "`n"
Write-Text $cheatsheetPath $text

# ---------------------------------------------------------------------------
# 3. Validate the actual final document.
# ---------------------------------------------------------------------------
$check = Read-Text $cheatsheetPath

if ($check.Contains('Fire A используется приложением как клик')) {
    throw 'Stale Fire A click-emulation text still remains.'
}

if ($check.Contains('## Актуальное управление оверлеем')) {
    throw 'Duplicate overlay-control section still remains.'
}

if (-not $check.Contains('| Одиночное нажатие правого колеса MFD | Интерактивный фокус |')) {
    throw 'Single-press MFD row is missing from the main overlay section.'
}

if (-not $check.Contains('| Двойное нажатие правого колеса MFD | Скрыть/вернуть все оверлеи |')) {
    throw 'Double-press MFD row is missing from the main overlay section.'
}

if (-not $check.Contains($mousePointerRow)) {
    throw 'Mouse Pointer row is missing.'
}

if (-not $check.Contains($mouseClickRow)) {
    throw 'Mouse Click row is missing.'
}

Write-Host ''
& git diff --check
if ($LASTEXITCODE -ne 0) {
    throw 'git diff --check failed.'
}

Write-Host ''
Write-Host 'Cheatsheet diff:' -ForegroundColor Cyan
& git diff -- $cheatsheetPath

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
Write-Host 'X52 cheatsheet cleanup completed.' -ForegroundColor Green
Write-Host "Backup of prior diff: ${backupPath}"
