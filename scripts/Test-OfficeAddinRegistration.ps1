# SlideTeX Note: Backward-compatible wrapper that delegates validation to Set-OfficeAddinRegistration.

param(
    [string]$ProgId = "SlideTeX",
    [string]$ReportPath = "artifacts/installer/office-addin-report.json"
)

$ErrorActionPreference = "Stop"

$scriptPath = Join-Path $PSScriptRoot "Set-OfficeAddinRegistration.ps1"
if (-not (Test-Path $scriptPath)) {
    throw "Script not found: $scriptPath"
}

pwsh -NoProfile -ExecutionPolicy Bypass -File $scriptPath `
    -Mode Validate `
    -ProgId $ProgId `
    -ReportPath $ReportPath

exit $LASTEXITCODE

