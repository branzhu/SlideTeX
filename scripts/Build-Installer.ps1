# SlideTeX Note: Builds installer outputs and validates packaging prerequisites.

param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$ProductVersion = "1.0.0",
    [string[]]$Cultures = @("zh-CN", "en-US"),
    [string]$BuildOutputDir = "src/SlideTeX.Addin/bin/Release/net8.0-windows",
    [string]$StagingBuildOutputDir = "",
    [string]$VstoBuildOutputDir = "",
    [string]$AddinProgId = "SlideTeX",
    [string]$LegacyAddinProgId = "SlideTeX.VstoAddin",
    [string]$AddinFriendlyName = "",
    [string]$AddinDescription = "",
    [string]$AddinManifestFileName = "SlideTeX.VstoAddin.vsto",
    [string]$AddinApplicationManifestFileName = "SlideTeX.VstoAddin.dll.manifest",
    [switch]$SkipBuild,
    [switch]$SkipGenerateFragment,
    [switch]$SkipVstoSync,
    [switch]$AllowMissingManifest,
    [switch]$VerifyOfficeRegistration
)

$ErrorActionPreference = "Stop"

# Resolves relative paths against repository root and returns normalized absolute path.
function Resolve-PathFromRoot {
    param(
        [string]$Root,
        [string]$PathValue
    )

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $Root $PathValue))
}

# Locates MSBuild executable from commonly installed Visual Studio toolchains.
function Resolve-MsBuildExe {
    $candidates = @(
        (Join-Path ${env:ProgramFiles} "Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"),
        (Join-Path ${env:ProgramFiles} "Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"),
        (Join-Path ${env:ProgramFiles} "Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"),
        (Join-Path ${env:ProgramFiles} "Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"),
        (Join-Path ${env:ProgramFiles} "Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe"),
        (Join-Path ${env:ProgramFiles} "Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

# Regenerates inline i18n bundle consumed by WebUI before packaging.
function Invoke-WebUiI18nBundleGeneration {
    param(
        [string]$RepoRoot
    )

    $scriptPath = Join-Path $RepoRoot "scripts/generate-webui-i18n-bundle.mjs"
    if (!(Test-Path $scriptPath)) {
        throw "未找到 WebUI i18n 生成脚本: $scriptPath"
    }

    $nodeCommand = Get-Command node -ErrorAction SilentlyContinue
    if ($null -eq $nodeCommand) {
        throw "未检测到 node，无法生成 WebUI i18n 内联资源。请安装 Node.js。"
    }

    Write-Host "Generating inline i18n bundle for WebUI..."
    & $nodeCommand.Source $scriptPath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "WebUI i18n bundle 生成失败。ExitCode=$LASTEXITCODE"
    }
}

# Normalizes caller-provided culture aliases to supported installer cultures.
function Normalize-CultureName {
    param([string]$CultureName)

    $raw = [string]$CultureName
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $null
    }

    $trimmed = $raw.Trim()
    if ($trimmed -match '^(?i)zh(?:[-_]?cn)?$') {
        return "zh-CN"
    }

    if ($trimmed -match '^(?i)en(?:[-_]?us)?$') {
        return "en-US"
    }

    return $null
}

# Picks localized default add-in display name when override is not provided.
function Resolve-AddinFriendlyNameForCulture {
    param(
        [string]$Culture,
        [string]$OverrideValue
    )

    if (-not [string]::IsNullOrWhiteSpace($OverrideValue)) {
        return $OverrideValue
    }

    if ($Culture -eq "zh-CN") {
        return "SlideTeX 公式插件"
    }

    return "SlideTeX Formula Add-in"
}

# Picks localized default add-in description when override is not provided.
function Resolve-AddinDescriptionForCulture {
    param(
        [string]$Culture,
        [string]$OverrideValue
    )

    if (-not [string]::IsNullOrWhiteSpace($OverrideValue)) {
        return $OverrideValue
    }

    if ($Culture -eq "zh-CN") {
        return "SlideTeX KaTeX 公式插件"
    }

    return "SlideTeX KaTeX Formula Add-in"
}

if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    throw "未检测到 wix CLI。请先安装 WiX Toolset。"
}

$root = Split-Path -Parent $PSScriptRoot
$product = Join-Path $root "src/SlideTeX.Installer/wix/Product.wxs"
$staticFiles = Join-Path $root "src/SlideTeX.Installer/wix/Fragments/Files.wxs"
$generatedFiles = Join-Path $root "src/SlideTeX.Installer/wix/Fragments/GeneratedFiles.wxs"
$officeRegistration = Join-Path $root "src/SlideTeX.Installer/wix/Fragments/OfficeRegistration.wxs"
$localizationDir = Join-Path $root "src/SlideTeX.Installer/wix/Localization"
$outDir = Join-Path $root "artifacts/installer"

$normalizedCultures = @()
foreach ($cultureValue in $Cultures) {
    $segments = @([string]$cultureValue)
    if (-not [string]::IsNullOrWhiteSpace($cultureValue)) {
        $segments = [string]$cultureValue -split '[,;]' | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }

    foreach ($culture in $segments) {
        $normalized = Normalize-CultureName -CultureName $culture
        if ([string]::IsNullOrWhiteSpace($normalized)) {
            throw "不支持的 Culture: $culture。当前仅支持 zh-CN / en-US。"
        }

        if ($normalizedCultures -notcontains $normalized) {
            $normalizedCultures += $normalized
        }
    }
}

if ($normalizedCultures.Count -eq 0) {
    throw "未提供可用的 Culture。请至少指定 zh-CN 或 en-US。"
}

if (-not $SkipBuild) {
    Invoke-WebUiI18nBundleGeneration -RepoRoot $root

    Write-Host "Building SlideTeX.Addin ($Configuration)..."
    dotnet build (Join-Path $root "src/SlideTeX.Addin/SlideTeX.Addin.csproj") -c $Configuration -m:1 | Out-Host

    $msbuildExe = Resolve-MsBuildExe
    if ([string]::IsNullOrWhiteSpace($msbuildExe)) {
        throw "未检测到 MSBuild.exe，无法构建 VSTO 清单。请安装 Visual Studio Build Tools 或使用 -SkipBuild 并手动准备 VSTO 产物。"
    }

    Write-Host "Building SlideTeX.VstoAddin with MSBuild ($Configuration)..."
    & $msbuildExe (Join-Path $root "src/SlideTeX.VstoAddin/SlideTeX.VstoAddin.csproj") /p:Configuration=$Configuration /p:Platform=AnyCPU /m:1 | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "VSTO 构建失败，无法继续打包。"
    }
}

$buildOutputFull = Resolve-PathFromRoot -Root $root -PathValue $BuildOutputDir
if (!(Test-Path $buildOutputFull)) {
    throw "BuildOutputDir 不存在: $buildOutputFull"
}

$stagingBuildOutputRelative = if ([string]::IsNullOrWhiteSpace($StagingBuildOutputDir)) {
    "artifacts/installer/payload/$Configuration-$Platform"
}
else {
    $StagingBuildOutputDir
}

$stagingBuildOutputFull = Resolve-PathFromRoot -Root $root -PathValue $stagingBuildOutputRelative

if ($buildOutputFull.TrimEnd('\') -ieq $stagingBuildOutputFull.TrimEnd('\')) {
    throw "StagingBuildOutputDir 不能与 BuildOutputDir 相同。"
}

if ([string]::IsNullOrWhiteSpace($VstoBuildOutputDir)) {
    $vstoConfigRelative = "src/SlideTeX.VstoAddin/bin/$Configuration"
    $vstoDebugRelative = "src/SlideTeX.VstoAddin/bin/Debug"
    $vstoConfigFull = Resolve-PathFromRoot -Root $root -PathValue $vstoConfigRelative
    $vstoDebugFull = Resolve-PathFromRoot -Root $root -PathValue $vstoDebugRelative

    if (Test-Path $vstoConfigFull) {
        $VstoBuildOutputDir = $vstoConfigRelative
    }
    elseif (Test-Path $vstoDebugFull) {
        $VstoBuildOutputDir = $vstoDebugRelative
        Write-Warning "未找到配置匹配的 VSTO 输出目录，已回退到 Debug: $vstoDebugFull"
    }
    else {
        $VstoBuildOutputDir = $vstoConfigRelative
    }
}

if (Test-Path $stagingBuildOutputFull) {
    Remove-Item -Path $stagingBuildOutputFull -Recurse -Force
}

New-Item -ItemType Directory -Path $stagingBuildOutputFull -Force | Out-Null

$addInPayloadPattern = Join-Path $buildOutputFull "*"
if (Test-Path $addInPayloadPattern) {
    Copy-Item -Path $addInPayloadPattern -Destination $stagingBuildOutputFull -Recurse -Force
}

if (-not $SkipVstoSync) {
    $vstoBuildOutputFull = Resolve-PathFromRoot -Root $root -PathValue $VstoBuildOutputDir
    if (!(Test-Path $vstoBuildOutputFull)) {
        if ($AllowMissingManifest) {
            Write-Warning "VSTO 输出目录不存在，跳过同步: $vstoBuildOutputFull"
        }
        else {
            throw "VSTO 输出目录不存在: $vstoBuildOutputFull。请先生成 VSTO 清单，或使用 -AllowMissingManifest 继续。"
        }
    }
    else {
        $vstoFiles = Get-ChildItem -Path $vstoBuildOutputFull -File |
            Where-Object { $_.Extension -notin @(".pdb", ".xml") }

        foreach ($vstoFile in $vstoFiles) {
            $destination = Join-Path $stagingBuildOutputFull $vstoFile.Name
            Copy-Item -Path $vstoFile.FullName -Destination $destination -Force
        }
    }
}

$manifestPath = Join-Path $stagingBuildOutputFull $AddinManifestFileName
if (!(Test-Path $manifestPath)) {
    if ($AllowMissingManifest) {
        Write-Warning "未在打包目录找到 VSTO 清单: $manifestPath。PowerPoint 可能因 Manifest 缺失而无法加载加载项。"
    }
    else {
        throw "未在打包目录找到 VSTO 清单: $manifestPath。请先生成并同步 `.vsto/.manifest`，或使用 -AllowMissingManifest 继续。"
    }
}

$applicationManifestPath = Join-Path $stagingBuildOutputFull $AddinApplicationManifestFileName
if (!(Test-Path $applicationManifestPath)) {
    if ($AllowMissingManifest) {
        Write-Warning "未在打包目录找到应用清单: $applicationManifestPath。PowerPoint 可能因 Manifest 缺失而无法加载加载项。"
    }
    else {
        throw "未在打包目录找到应用清单: $applicationManifestPath。请先生成并同步 `.vsto/.manifest`，或使用 -AllowMissingManifest 继续。"
    }
}

$files = $staticFiles
if (Test-Path $generatedFiles) {
    $files = $generatedFiles
}
if (-not $SkipGenerateFragment) {
    Write-Host "Generating WiX fragment from staging output..."
    pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "scripts/Generate-WixFragment.ps1") `
      -BuildOutputDir $stagingBuildOutputFull `
      -OutputFile "src/SlideTeX.Installer/wix/Fragments/GeneratedFiles.wxs" | Out-Host

    if (Test-Path $generatedFiles) {
        $files = $generatedFiles
    }
}

New-Item -ItemType Directory -Path $outDir -Force | Out-Null

foreach ($culture in $normalizedCultures) {
    $locFile = Join-Path $localizationDir "$culture.wxl"
    if (!(Test-Path $locFile)) {
        throw "未找到本地化资源文件: $locFile"
    }

    $friendlyName = Resolve-AddinFriendlyNameForCulture -Culture $culture -OverrideValue $AddinFriendlyName
    $description = Resolve-AddinDescriptionForCulture -Culture $culture -OverrideValue $AddinDescription

    $outputName = if ($normalizedCultures.Count -eq 1) {
        "SlideTeX-$ProductVersion-$Configuration-$Platform.msi"
    }
    else {
        "SlideTeX-$ProductVersion-$Configuration-$Platform-$culture.msi"
    }
    $outputMsi = Join-Path $outDir $outputName

    Write-Host "Building installer for culture: $culture"
    wix build `
      $product `
      $files `
      $officeRegistration `
      -d BuildOutputDir="$stagingBuildOutputFull" `
      -d ProductVersion=$ProductVersion `
      -d AddinProgId=$AddinProgId `
      -d LegacyAddinProgId=$LegacyAddinProgId `
      -d AddinFriendlyName="$friendlyName" `
      -d AddinDescription="$description" `
      -d AddinManifestFileName=$AddinManifestFileName `
      -loc $locFile `
      -culture $culture `
      -arch $Platform `
      -o $outputMsi

    if ($LASTEXITCODE -ne 0) {
        throw "wix build 失败。Culture=$culture, ExitCode=$LASTEXITCODE"
    }

    Write-Host "Installer generated [$culture]: $outputMsi"
}

if ($VerifyOfficeRegistration) {
    Write-Host "Verifying Office add-in registry..."
    pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "scripts/Set-OfficeAddinRegistration.ps1") `
      -Mode Validate `
      -ProgId $AddinProgId | Out-Host

    if ($LASTEXITCODE -ne 0) {
        throw "Office add-in registry verification failed. ExitCode=$LASTEXITCODE"
    }
}

