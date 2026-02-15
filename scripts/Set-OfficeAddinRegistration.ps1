# SlideTeX Note: Installs, removes, or validates Office add-in registration for local setup.

param(
    [ValidateSet("Install", "Uninstall", "Validate")]
    [string]$Mode = "Install",
    [string]$ProgId = "SlideTeX",
    [string]$FriendlyName = "SlideTeX 公式插件",
    [string]$Description = "SlideTeX MathJax 公式插件",
    [string]$ManifestPath = "",
    [string]$ReportPath = "artifacts/installer/office-addin-report.json",
    [switch]$RegisterWow6432Node
)

$ErrorActionPreference = "Stop"

function Get-RegistryPaths {
    param(
        [string]$AddinProgId,
        [bool]$IncludeWow6432
    )

    $paths = @(
        "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Office\PowerPoint\Addins\$AddinProgId"
    )

    if ($IncludeWow6432) {
        $paths += "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Office\PowerPoint\Addins\$AddinProgId"
    }

    return $paths
}

function Get-RegistryReport {
    param([string]$RegistryPath)

    $result = [ordered]@{
        Path = $RegistryPath
        Exists = $false
        FriendlyName = $null
        Description = $null
        LoadBehavior = $null
        Manifest = $null
        ManifestFile = $null
        ManifestExists = $false
        IsValid = $false
        Errors = @()
    }

    if (!(Test-Path $RegistryPath)) {
        return $result
    }

    $result.Exists = $true
    $item = Get-ItemProperty -Path $RegistryPath
    $result.FriendlyName = $item.FriendlyName
    $result.Description = $item.Description
    $result.LoadBehavior = $item.LoadBehavior
    $result.Manifest = $item.Manifest

    if ([string]::IsNullOrWhiteSpace($result.FriendlyName)) {
        $result.Errors += "FriendlyName 缺失"
    }
    if ([string]::IsNullOrWhiteSpace($result.Description)) {
        $result.Errors += "Description 缺失"
    }
    if ($result.LoadBehavior -ne 3) {
        $result.Errors += "LoadBehavior 不是 3"
    }
    if ([string]::IsNullOrWhiteSpace($result.Manifest)) {
        $result.Errors += "Manifest 缺失"
    }
    else {
        $manifestFile = $result.Manifest.Split("|")[0]
        $result.ManifestFile = $manifestFile
        $result.ManifestExists = Test-Path $manifestFile
        if (-not $result.ManifestExists) {
            $result.Errors += "Manifest 文件不存在: $manifestFile"
        }
    }

    $result.IsValid = $result.Errors.Count -eq 0
    return $result
}

if ($Mode -eq "Validate") {
    $root = Split-Path -Parent $PSScriptRoot
    $reportFullPath = if ([System.IO.Path]::IsPathRooted($ReportPath)) {
        [System.IO.Path]::GetFullPath($ReportPath)
    }
    else {
        Join-Path $root $ReportPath
    }

    $reportDir = Split-Path -Parent $reportFullPath
    New-Item -ItemType Directory -Path $reportDir -Force | Out-Null

    $registryPaths = Get-RegistryPaths -AddinProgId $ProgId -IncludeWow6432:$true
    $checks = $registryPaths | ForEach-Object { Get-RegistryReport -RegistryPath $_ }
    $existing = @($checks | Where-Object { $_.Exists })
    $valid = @($checks | Where-Object { $_.IsValid })

    $summary = [ordered]@{
        TimestampUtc = (Get-Date).ToUniversalTime().ToString("O")
        ProgId = $ProgId
        ExistingCount = $existing.Count
        ValidCount = $valid.Count
        Checks = $checks
    }

    $summary | ConvertTo-Json -Depth 6 | Set-Content -Path $reportFullPath -Encoding utf8
    Write-Host "Office add-in report: $reportFullPath"

    if ($valid.Count -gt 0) {
        Write-Host "Office add-in registration check passed."
        exit 0
    }

    if ($existing.Count -eq 0) {
        Write-Warning "未找到 SlideTeX Office 加载项注册表项。"
        exit 2
    }

    Write-Warning "找到注册表项，但校验失败。请查看报告详情。"
    exit 1
}

$paths = Get-RegistryPaths -AddinProgId $ProgId -IncludeWow6432:$RegisterWow6432Node

if ($Mode -eq "Install") {
    if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
        throw "Install 模式必须提供 -ManifestPath。"
    }

    if (!(Test-Path $ManifestPath)) {
        throw "ManifestPath 不存在: $ManifestPath"
    }

    $manifestValue = "$ManifestPath|vstolocal"

    foreach ($path in $paths) {
        New-Item -Path $path -Force | Out-Null
        New-ItemProperty -Path $path -Name "FriendlyName" -Value $FriendlyName -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $path -Name "Description" -Value $Description -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $path -Name "LoadBehavior" -Value 3 -PropertyType DWord -Force | Out-Null
        New-ItemProperty -Path $path -Name "Manifest" -Value $manifestValue -PropertyType String -Force | Out-Null
    }

    Write-Host "Office add-in registry installed for $ProgId"
    exit 0
}

foreach ($path in $paths) {
    if (Test-Path $path) {
        Remove-Item -Path $path -Recurse -Force
    }
}

Write-Host "Office add-in registry removed for $ProgId"
exit 0
