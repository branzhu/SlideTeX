# SlideTeX Note: Builds installer outputs and validates packaging prerequisites.

param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$ProductVersion = "1.0.0",
    [string[]]$Cultures = @("zh-CN", "en-US"),
    [string]$BuildOutputDir = "",
    [string]$StagingBuildOutputDir = "",
    [string]$VstoBuildOutputDir = "",
    [string]$AddinProgId = "SlideTeX",
    [string]$LegacyAddinProgId = "SlideTeX.VstoAddin",
    [string]$AddinFriendlyName = "",
    [string]$AddinDescription = "",
    [string]$AddinManifestFileName = "SlideTeX.VstoAddin.vsto",
    [string]$AddinApplicationManifestFileName = "SlideTeX.VstoAddin.dll.manifest",
    [string]$BundleName = "SlideTeX Installer",
    [string]$BundleManufacturer = "SlideTeX Team",
    [string]$BundleOutputName = "",
    [string]$VstoManifestCertificateThumbprint = "",
    [switch]$SkipBuild,
    [switch]$SkipGenerateFragment,
    [switch]$SkipVstoSync,
    [switch]$SkipBundle,
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

# Ensures the WiX bootstrapper extension is available for bundle builds and returns extension dll path.
function Resolve-WixBootstrapperExtensionPath {
    param(
        [string]$ExtensionRef = "WixToolset.BootstrapperApplications.wixext/6.0.2"
    )

    $extensionId = $ExtensionRef.Split('/')[0]
    $extensionSearchRoot = Join-Path $env:USERPROFILE ".wix\extensions\$extensionId"
    $extensionPattern = "$extensionId.dll"

    if (Test-Path $extensionSearchRoot) {
        $existing = Get-ChildItem -Path $extensionSearchRoot -Recurse -File -Filter $extensionPattern -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $existing) {
            return $existing.FullName
        }
    }

    Write-Warning "WiX extension '$extensionId' is not installed. Trying to install it now..."

    & wix extension add -g $ExtensionRef | Out-Host
    if ($LASTEXITCODE -ne 0) {
        & wix extension add $ExtensionRef | Out-Host
    }

    if (Test-Path $extensionSearchRoot) {
        $installed = Get-ChildItem -Path $extensionSearchRoot -Recurse -File -Filter $extensionPattern -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $installed) {
            return $installed.FullName
        }
    }

    return $null
}

# Tries to infer Visual Studio installation root from selected MSBuild path.
function Resolve-VsInstallDirFromMsBuild {
    param(
        [string]$MsBuildExe
    )

    if ([string]::IsNullOrWhiteSpace($MsBuildExe)) {
        return $null
    }

    $current = [System.IO.Path]::GetDirectoryName($MsBuildExe)
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        $msbuildMarker = Join-Path $current "MSBuild\Current\Bin\MSBuild.exe"
        if (Test-Path $msbuildMarker) {
            return $current
        }

        $parent = [System.IO.Directory]::GetParent($current)
        if ($null -eq $parent) {
            break
        }

        $current = $parent.FullName
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
        throw "WebUI i18n generation script was not found: $scriptPath"
    }

    $nodeCommand = Get-Command node -ErrorAction SilentlyContinue
    if ($null -eq $nodeCommand) {
        throw "The node command was not found. Install Node.js to generate the inline WebUI i18n bundle."
    }

    Write-Host "Generating inline i18n bundle for WebUI..."
    & $nodeCommand.Source $scriptPath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to generate the WebUI i18n bundle. ExitCode=$LASTEXITCODE"
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
    throw "The wix CLI was not found. Install WiX Toolset first."
}

$root = Split-Path -Parent $PSScriptRoot
$product = Join-Path $root "src/SlideTeX.Installer/wix/Product.wxs"
$bundle = Join-Path $root "src/SlideTeX.Installer/wix/Bundle.wxs"
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
            throw "Unsupported culture: $culture. Only zh-CN and en-US are currently supported."
        }

        if ($normalizedCultures -notcontains $normalized) {
            $normalizedCultures += $normalized
        }
    }
}

if ($normalizedCultures.Count -eq 0) {
    throw "No valid culture was provided. Specify at least zh-CN or en-US."
}

if (-not $SkipBuild) {
    Invoke-WebUiI18nBundleGeneration -RepoRoot $root

    $msbuildExe = Resolve-MsBuildExe
    if ([string]::IsNullOrWhiteSpace($msbuildExe)) {
        throw "MSBuild.exe was not found. Install Visual Studio Build Tools, or use -SkipBuild and prepare VSTO outputs manually."
    }

    $vsInstallDir = Resolve-VsInstallDirFromMsBuild -MsBuildExe $msbuildExe
    Write-Host "Building SlideTeX.VstoAddin with MSBuild ($Configuration)..."
    $msbuildArgs = @(
        (Join-Path $root "src/SlideTeX.VstoAddin/SlideTeX.VstoAddin.csproj"),
        "/p:Configuration=$Configuration",
        "/p:Platform=AnyCPU",
        "/m:1"
    )

    if (-not [string]::IsNullOrWhiteSpace($vsInstallDir)) {
        $msbuildArgs += "/p:VsInstallDir=$vsInstallDir"
    }

    if (-not [string]::IsNullOrWhiteSpace($VstoManifestCertificateThumbprint)) {
        $msbuildArgs += "/p:ManifestCertificateThumbprint=$VstoManifestCertificateThumbprint"
    }

    & $msbuildExe @msbuildArgs | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "VSTO build failed; packaging cannot continue."
    }
}

if ([string]::IsNullOrWhiteSpace($BuildOutputDir)) {
    $BuildOutputDir = "src/SlideTeX.VstoAddin/bin/$Configuration"
}

$buildOutputFull = Resolve-PathFromRoot -Root $root -PathValue $BuildOutputDir
if (!(Test-Path $buildOutputFull)) {
    throw "BuildOutputDir does not exist: $buildOutputFull"
}

$stagingBuildOutputRelative = if ([string]::IsNullOrWhiteSpace($StagingBuildOutputDir)) {
    "artifacts/installer/payload/$Configuration-$Platform"
}
else {
    $StagingBuildOutputDir
}

$stagingBuildOutputFull = Resolve-PathFromRoot -Root $root -PathValue $stagingBuildOutputRelative

if ($buildOutputFull.TrimEnd('\') -ieq $stagingBuildOutputFull.TrimEnd('\')) {
    throw "StagingBuildOutputDir must be different from BuildOutputDir."
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
        Write-Warning "No VSTO output directory was found for the selected configuration. Falling back to Debug: $vstoDebugFull"
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
            Write-Warning "VSTO output directory does not exist; skipping sync: $vstoBuildOutputFull"
        }
        else {
            throw "VSTO output directory does not exist: $vstoBuildOutputFull. Generate VSTO manifests first, or continue with -AllowMissingManifest."
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
        Write-Warning "VSTO manifest was not found in the packaging directory: $manifestPath. PowerPoint may fail to load the add-in when manifest files are missing."
    }
    else {
        throw "VSTO manifest was not found in the packaging directory: $manifestPath. Generate and sync `.vsto/.manifest` first, or continue with -AllowMissingManifest."
    }
}

$applicationManifestPath = Join-Path $stagingBuildOutputFull $AddinApplicationManifestFileName
if (!(Test-Path $applicationManifestPath)) {
    if ($AllowMissingManifest) {
        Write-Warning "Application manifest was not found in the packaging directory: $applicationManifestPath. PowerPoint may fail to load the add-in when manifest files are missing."
    }
    else {
        throw "Application manifest was not found in the packaging directory: $applicationManifestPath. Generate and sync `.vsto/.manifest` first, or continue with -AllowMissingManifest."
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

$builtMsiByCulture = @{}
foreach ($culture in $normalizedCultures) {
    $locFile = Join-Path $localizationDir "$culture.wxl"
    if (!(Test-Path $locFile)) {
        throw "Localization resource file was not found: $locFile"
    }

    $friendlyName = Resolve-AddinFriendlyNameForCulture -Culture $culture -OverrideValue $AddinFriendlyName
    $description = Resolve-AddinDescriptionForCulture -Culture $culture -OverrideValue $AddinDescription

    $outputName = "SlideTeX-$ProductVersion-$Configuration-$Platform-$culture.msi"
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
        throw "wix build failed. Culture=$culture, ExitCode=$LASTEXITCODE"
    }

    $builtMsiByCulture[$culture] = $outputMsi
    Write-Host "Installer generated [$culture]: $outputMsi"
}

$shouldBuildBundle = (-not $SkipBundle) -and ($builtMsiByCulture.ContainsKey("zh-CN")) -and ($builtMsiByCulture.ContainsKey("en-US"))
if ($shouldBuildBundle) {
    if (!(Test-Path $bundle)) {
        throw "Bundle definition file was not found: $bundle"
    }

    $bundleExtPath = Resolve-WixBootstrapperExtensionPath
    if ([string]::IsNullOrWhiteSpace($bundleExtPath)) {
        throw "wix bundle build prerequisites are missing. Install extension 'WixToolset.BootstrapperApplications.wixext/6.0.2' first (command: `wix extension add -g WixToolset.BootstrapperApplications.wixext/6.0.2`), or rerun with -SkipBundle to generate MSI files only."
    }

    $bundleOutputFileName = if ([string]::IsNullOrWhiteSpace($BundleOutputName)) {
        "SlideTeX-$ProductVersion-$Configuration-$Platform.exe"
    }
    else {
        $BundleOutputName
    }

    $bundleOutput = Join-Path $outDir $bundleOutputFileName
    Write-Host "Building unified multilingual installer bundle..."
    wix build `
      $bundle `
      -ext $bundleExtPath `
      -d ProductVersion=$ProductVersion `
      -d BundleName="$BundleName" `
      -d BundleManufacturer="$BundleManufacturer" `
      -d MsiEnUsPath="$($builtMsiByCulture["en-US"])" `
      -d MsiZhCnPath="$($builtMsiByCulture["zh-CN"])" `
      -culture en-US `
      -culture zh-CN `
      -arch $Platform `
      -o $bundleOutput

    if ($LASTEXITCODE -ne 0) {
        throw "wix bundle build failed. Ensure WiX extension 'WixToolset.BootstrapperApplications.wixext' is available."
    }

    Write-Host "Unified installer bundle generated: $bundleOutput"
    Write-Host "Language override example: SlideTeX-*.exe SlideTeXInstallerCulture=en-US"
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
