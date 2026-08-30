param(
    [string]$RepoRoot = "D:\Projects\EDActivityOverlay"
)

$ErrorActionPreference = "Stop"

$SpikeScript =
    Join-Path `
        $RepoRoot `
        "Research\Trading\spike\Trade-v1-Ardent\run-ardent-trade-spike.ps1"

if (!(Test-Path -LiteralPath $SpikeScript))
{
    throw "Trade spike script not found: $SpikeScript"
}

powershell `
    -ExecutionPolicy Bypass `
    -File $SpikeScript `
    -System "Wregoe RJ-I d9-83" `
    -Cargo 750 `
    -SourceRadius 80 `
    -TargetRadius 40 `
    -CommodityCandidates 50

if ($LASTEXITCODE -ne 0)
{
    throw "Trade spike failed with exit code $LASTEXITCODE."
}
