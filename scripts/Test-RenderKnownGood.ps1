# SlideTeX Note: Executes render regression tests against known-good snapshots.

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [ValidateSet('verify', 'update-baseline')]
    [string]$Mode = 'verify',
    [ValidateSet('all', 'smoke', 'full')]
    [string]$Suite = 'all',
    [string]$FixturePath = '',
    [string]$CaseId = '',
    [string]$ChromePath = '',
    [string]$ArtifactsDir = ''
)

$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot 'render-known-good.mjs'
if (-not (Test-Path $scriptPath)) {
    throw "Script not found: $scriptPath"
}

$args = @('--mode', $Mode)
$args += @('--suite', $Suite)

if (-not [string]::IsNullOrWhiteSpace($FixturePath)) {
    $args += @('--fixture', $FixturePath)
}

if (-not [string]::IsNullOrWhiteSpace($CaseId)) {
    $args += @('--caseId', $CaseId)
}

if (-not [string]::IsNullOrWhiteSpace($ChromePath)) {
    $args += @('--chromePath', $ChromePath)
}

if (-not [string]::IsNullOrWhiteSpace($ArtifactsDir)) {
    $args += @('--artifactsDir', $ArtifactsDir)
}

Write-Host "Running render known-good tests (mode=$Mode, suite=$Suite, config=$Configuration)..."
& node $scriptPath @args
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}


