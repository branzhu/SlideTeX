# SlideTeX Note: Verifies MSI install, upgrade, and uninstall lifecycle behavior.

param(
    [Parameter(Mandatory = $true)]
    [string]$OldMsi,

    [Parameter(Mandatory = $true)]
    [string]$NewMsi,

    [string]$LogDir = "artifacts/msi-lifecycle",
    [switch]$SkipCleanup
)

$ErrorActionPreference = "Stop"

function Invoke-Msi {
    param(
        [string]$Arguments,
        [string]$LogPath
    )

    $p = Start-Process -FilePath "msiexec.exe" -ArgumentList $Arguments -Wait -PassThru
    $code = $p.ExitCode

    return [PSCustomObject]@{
        ExitCode = $code
        LogPath = $LogPath
        Success = ($code -eq 0)
    }
}

function Get-ProductCode {
    param([string]$MsiPath)

    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $installer.GetType().InvokeMember(
        "OpenDatabase",
        [System.Reflection.BindingFlags]::InvokeMethod,
        $null,
        $installer,
        @($MsiPath, 0))
    $view = $db.OpenView("SELECT `Value` FROM `Property` WHERE `Property`='ProductCode'")
    $view.Execute()
    $record = $view.Fetch()
    if ($null -eq $record) {
        throw "Failed to read ProductCode from MSI: $MsiPath"
    }

    return $record.StringData(1)
}

if (!(Test-Path $OldMsi)) {
    throw "OldMsi does not exist: $OldMsi"
}
if (!(Test-Path $NewMsi)) {
    throw "NewMsi does not exist: $NewMsi"
}

New-Item -ItemType Directory -Path $LogDir -Force | Out-Null

$oldFull = (Resolve-Path $OldMsi).Path
$newFull = (Resolve-Path $NewMsi).Path

$oldCode = Get-ProductCode -MsiPath $oldFull
$newCode = Get-ProductCode -MsiPath $newFull

$report = [ordered]@{
    timestamp = (Get-Date).ToString("o")
    oldMsi = $oldFull
    newMsi = $newFull
    oldProductCode = $oldCode
    newProductCode = $newCode
    steps = @()
}

$oldInstallLog = Join-Path $LogDir "01-install-old.log"
$upgradeLog = Join-Path $LogDir "02-upgrade.log"
$uninstallLog = Join-Path $LogDir "03-uninstall.log"

$step1 = Invoke-Msi -Arguments "/i \"$oldFull\" /qn /l*v \"$oldInstallLog\"" -LogPath $oldInstallLog
$report.steps += [ordered]@{ step = "install-old"; result = $step1 }
if (-not $step1.Success) {
    $report | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $LogDir "report.json") -Encoding utf8
    throw "Install old MSI failed, ExitCode=$($step1.ExitCode)"
}

$step2 = Invoke-Msi -Arguments "/i \"$newFull\" /qn /l*v \"$upgradeLog\"" -LogPath $upgradeLog
$report.steps += [ordered]@{ step = "upgrade-to-new"; result = $step2 }
if (-not $step2.Success) {
    $report | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $LogDir "report.json") -Encoding utf8
    throw "Upgrade to new MSI failed, ExitCode=$($step2.ExitCode)"
}

if (-not $SkipCleanup) {
    $step3 = Invoke-Msi -Arguments "/x \"$newCode\" /qn /l*v \"$uninstallLog\"" -LogPath $uninstallLog
    $report.steps += [ordered]@{ step = "uninstall-new"; result = $step3 }
    if (-not $step3.Success) {
        $report | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $LogDir "report.json") -Encoding utf8
        throw "Uninstall new MSI failed, ExitCode=$($step3.ExitCode)"
    }
}

$report | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $LogDir "report.json") -Encoding utf8
Write-Host "MSI lifecycle report: $(Join-Path $LogDir "report.json")"


