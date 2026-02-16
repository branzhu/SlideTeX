# SlideTeX Note: Syncs third-party runtime assets (MathJax + pix2text-mfr model files).

param(
    [ValidateSet('mathjax', 'pix2text-mfr', 'all')]
    [string[]]$Component = @('mathjax'),
    [string]$Version = "4.1.0",
    [string]$ArchivePath = "",
    [string]$Pix2TextModelId = "breezedeus/pix2text-mfr-1.5",
    [string]$Pix2TextRevision = "main",
    [string]$Pix2TextModelDir = "",
    [switch]$ForceDownload
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

function Test-ComponentEnabled {
    param(
        [string[]]$AllComponents,
        [string]$Name
    )

    if ($null -eq $AllComponents -or $AllComponents.Count -eq 0) {
        return $false
    }

    if ($AllComponents -contains 'all') {
        return $true
    }

    return $AllComponents -contains $Name
}

function Invoke-DownloadFile {
    param(
        [string]$Uri,
        [string]$OutFile
    )

    $targetDir = Split-Path -Parent $OutFile
    if (-not [string]::IsNullOrWhiteSpace($targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }

    try {
        Invoke-WebRequest -Uri $Uri -OutFile $OutFile
    }
    catch {
        throw "Download failed: $Uri, error: $($_.Exception.Message)"
    }
}

function Sync-MathJax {
    param(
        [string]$MathJaxVersion,
        [string]$MathJaxArchivePath
    )

    $vendorDir = Join-Path $root "src/SlideTeX.WebUI/vendor/mathjax"
    $tmpDir = Join-Path $root ".tmp/mathjax"
    $archive = Join-Path $tmpDir "mathjax-$MathJaxVersion.tgz"
    $extractDir = Join-Path $tmpDir "extract"

    New-Item -ItemType Directory -Path $vendorDir -Force | Out-Null
    New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null

    if ([string]::IsNullOrWhiteSpace($MathJaxArchivePath)) {
        $uri = "https://registry.npmjs.org/mathjax/-/mathjax-$MathJaxVersion.tgz"
        Write-Host "Downloading MathJax $MathJaxVersion from $uri"
        try {
            Invoke-DownloadFile -Uri $uri -OutFile $archive
        }
        catch {
            Write-Warning "Direct download failed; fallback to npm pack."
            $packDir = Join-Path $tmpDir "pack"
            New-Item -ItemType Directory -Path $packDir -Force | Out-Null
            & npm pack "mathjax@$MathJaxVersion" --pack-destination $packDir | Out-Host
            if ($LASTEXITCODE -ne 0) {
                throw "npm pack mathjax@$MathJaxVersion failed."
            }

            $packedArchive = Join-Path $packDir "mathjax-$MathJaxVersion.tgz"
            if (!(Test-Path $packedArchive)) {
                throw "mathjax archive not found after npm pack: $packedArchive"
            }

            Copy-Item -Path $packedArchive -Destination $archive -Force
        }
    }
    else {
        if (!(Test-Path $MathJaxArchivePath)) {
            throw "Specified ArchivePath does not exist: $MathJaxArchivePath"
        }

        Copy-Item -Path $MathJaxArchivePath -Destination $archive -Force
    }

    if (Test-Path $extractDir) {
        Remove-Item -Path $extractDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $extractDir -Force | Out-Null

    tar -xf $archive -C $extractDir

    $pkgDir = Join-Path $extractDir "package"
    if (!(Test-Path $pkgDir)) {
        throw "MathJax package folder not found in archive."
    }

    Get-ChildItem -Path $vendorDir -Exclude README.md | Remove-Item -Recurse -Force
    Copy-Item -Path (Join-Path $pkgDir "*") -Destination $vendorDir -Recurse -Force

    Write-Host "MathJax $MathJaxVersion synced to $vendorDir"

    # Sync the NewCM font package (SVG dynamic fonts used by tex-svg-nofont.js)
    $fontVendorDir = Join-Path $root "src/SlideTeX.WebUI/vendor/mathjax-newcm-font"
    $fontTmpDir = Join-Path $root ".tmp/mathjax-newcm-font"
    $fontArchive = Join-Path $fontTmpDir "mathjax-newcm-font-$MathJaxVersion.tgz"
    $fontExtractDir = Join-Path $fontTmpDir "extract"

    New-Item -ItemType Directory -Path $fontTmpDir -Force | Out-Null

    $fontUri = "https://registry.npmjs.org/@mathjax/mathjax-newcm-font/-/mathjax-newcm-font-$MathJaxVersion.tgz"
    Write-Host "Downloading @mathjax/mathjax-newcm-font $MathJaxVersion from $fontUri"
    try {
        Invoke-DownloadFile -Uri $fontUri -OutFile $fontArchive
    }
    catch {
        Write-Warning "Direct download failed; fallback to npm pack."
        $fontPackDir = Join-Path $fontTmpDir "pack"
        New-Item -ItemType Directory -Path $fontPackDir -Force | Out-Null
        & npm pack "@mathjax/mathjax-newcm-font@$MathJaxVersion" --pack-destination $fontPackDir | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "npm pack @mathjax/mathjax-newcm-font@$MathJaxVersion failed."
        }
        $packedFontArchive = Join-Path $fontPackDir "mathjax-mathjax-newcm-font-$MathJaxVersion.tgz"
        if (!(Test-Path $packedFontArchive)) {
            throw "Font archive not found after npm pack: $packedFontArchive"
        }
        Copy-Item -Path $packedFontArchive -Destination $fontArchive -Force
    }

    if (Test-Path $fontExtractDir) {
        Remove-Item -Path $fontExtractDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $fontExtractDir -Force | Out-Null
    tar -xf $fontArchive -C $fontExtractDir

    $fontPkgDir = Join-Path $fontExtractDir "package"
    if (!(Test-Path $fontPkgDir)) {
        throw "Font package folder not found in archive."
    }

    # Copy svg.js and svg/dynamic/ (only SVG output fonts needed)
    New-Item -ItemType Directory -Path $fontVendorDir -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $fontVendorDir "svg/dynamic") -Force | Out-Null
    Copy-Item -Path (Join-Path $fontPkgDir "svg.js") -Destination $fontVendorDir -Force
    Copy-Item -Path (Join-Path $fontPkgDir "svg/dynamic/*") -Destination (Join-Path $fontVendorDir "svg/dynamic") -Force

    # Build a single concatenated bundle of all dynamic font files so that
    # index.html can load them with one <script> tag.  This is required for
    # WebView2 where MathJax's async font loading hangs.
    $dynamicDir = Join-Path $fontVendorDir "svg/dynamic"
    $bundlePath = Join-Path $fontVendorDir "svg/dynamic-all.js"
    $jsFiles = Get-ChildItem -Path $dynamicDir -Filter "*.js" | Sort-Object Name
    $bundleContent = ($jsFiles | ForEach-Object { Get-Content -Path $_.FullName -Raw }) -join "`n"
    Set-Content -Path $bundlePath -Value $bundleContent -NoNewline
    Write-Host "Built dynamic font bundle: $bundlePath ($($jsFiles.Count) files)"

    Write-Host "@mathjax/mathjax-newcm-font $MathJaxVersion SVG fonts synced to $fontVendorDir"
}

function Sync-Pix2TextMfr {
    param(
        [string]$ModelId,
        [string]$Revision,
        [string]$ModelDir,
        [switch]$Force
    )

    $targetModelDir = $ModelDir
    if ([string]::IsNullOrWhiteSpace($targetModelDir)) {
        $targetModelDir = Join-Path $root "src/SlideTeX.VstoAddin/Assets/OcrModels/pix2text-mfr"
    }
    else {
        if (-not [System.IO.Path]::IsPathRooted($targetModelDir)) {
            $targetModelDir = Join-Path $root $targetModelDir
        }
        $targetModelDir = [System.IO.Path]::GetFullPath($targetModelDir)
    }

    $tmpDir = Join-Path $root ".tmp/pix2text-mfr"
    New-Item -ItemType Directory -Path $targetModelDir -Force | Out-Null
    New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null

    $files = @(
        @{ remote = "onnx/encoder_model.onnx"; local = "encoder_model.onnx" },
        @{ remote = "onnx/decoder_model.onnx"; local = "decoder_model.onnx" },
        @{ remote = "onnx/tokenizer.json"; local = "tokenizer.json" },
        @{ remote = "onnx/generation_config.json"; local = "generation_config.json" },
        @{ remote = "onnx/tokenizer_config.json"; local = "tokenizer_config.json" },
        @{ remote = "onnx/special_tokens_map.json"; local = "special_tokens_map.json" },
        @{ remote = "onnx/preprocessor_config.json"; local = "preprocessor_config.json" }
    )

    foreach ($file in $files) {
        $targetPath = Join-Path $targetModelDir $file.local
        if ((-not $Force.IsPresent) -and (Test-Path $targetPath)) {
            Write-Host "Skip existing: $targetPath"
            continue
        }

        $uri = "https://huggingface.co/$ModelId/resolve/$Revision/$($file.remote)"
        $tmpPath = Join-Path $tmpDir $file.local
        Write-Host "Downloading $uri"
        Invoke-DownloadFile -Uri $uri -OutFile $tmpPath
        Move-Item -Path $tmpPath -Destination $targetPath -Force
    }

    Write-Host "pix2text-mfr assets synced to $targetModelDir"
    Write-Host "Model source: https://huggingface.co/$ModelId/tree/$Revision/onnx"
}

if (Test-ComponentEnabled -AllComponents $Component -Name 'mathjax') {
    Sync-MathJax -MathJaxVersion $Version -MathJaxArchivePath $ArchivePath
}

if (Test-ComponentEnabled -AllComponents $Component -Name 'pix2text-mfr') {
    Sync-Pix2TextMfr -ModelId $Pix2TextModelId -Revision $Pix2TextRevision -ModelDir $Pix2TextModelDir -Force:$ForceDownload
}
