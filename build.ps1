#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Solution = Join-Path $RepoRoot "EDActivityOverlay\EDActivityOverlay.sln"

if (-not (Test-Path -LiteralPath $Solution)) {
    throw "Solution not found: $Solution"
}

dotnet build $Solution -c $Configuration

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}