# SlideTeX Note: Syncs third-party runtime assets (KaTeX + pix2text-mfr model files).

param(
    [ValidateSet('katex', 'pix2text-mfr', 'all')]
    [string[]]$Component = @('katex'),
    [string]$Version = "0.16.11",
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
        throw "下载失败: $Uri，错误: $($_.Exception.Message)"
    }
}

function Sync-KaTeX {
    param(
        [string]$KaTeXVersion,
        [string]$KaTeXArchivePath
    )

    $vendorDir = Join-Path $root "src/SlideTeX.WebUI/vendor/katex"
    $tmpDir = Join-Path $root ".tmp/katex"
    $archive = Join-Path $tmpDir "katex-$KaTeXVersion.tgz"
    $extractDir = Join-Path $tmpDir "extract"

    New-Item -ItemType Directory -Path $vendorDir -Force | Out-Null
    New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null

    if ([string]::IsNullOrWhiteSpace($KaTeXArchivePath)) {
        $uri = "https://registry.npmjs.org/katex/-/katex-$KaTeXVersion.tgz"
        Write-Host "Downloading KaTeX $KaTeXVersion from $uri"
        Invoke-DownloadFile -Uri $uri -OutFile $archive
    }
    else {
        if (!(Test-Path $KaTeXArchivePath)) {
            throw "指定的 ArchivePath 不存在: $KaTeXArchivePath"
        }

        Copy-Item -Path $KaTeXArchivePath -Destination $archive -Force
    }

    if (Test-Path $extractDir) {
        Remove-Item -Path $extractDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $extractDir -Force | Out-Null

    tar -xf $archive -C $extractDir

    $dist = Join-Path $extractDir "package/dist"
    if (!(Test-Path $dist)) {
        throw "KaTeX dist folder not found in archive."
    }

    Get-ChildItem -Path $vendorDir -Exclude README.md | Remove-Item -Recurse -Force
    Copy-Item -Path (Join-Path $dist "katex.min.js") -Destination $vendorDir -Force
    Copy-Item -Path (Join-Path $dist "katex.min.css") -Destination $vendorDir -Force
    Copy-Item -Path (Join-Path $dist "fonts") -Destination (Join-Path $vendorDir "fonts") -Recurse -Force

    Write-Host "KaTeX $KaTeXVersion synced to $vendorDir"
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

if (Test-ComponentEnabled -AllComponents $Component -Name 'katex') {
    Sync-KaTeX -KaTeXVersion $Version -KaTeXArchivePath $ArchivePath
}

if (Test-ComponentEnabled -AllComponents $Component -Name 'pix2text-mfr') {
    Sync-Pix2TextMfr -ModelId $Pix2TextModelId -Revision $Pix2TextRevision -ModelDir $Pix2TextModelDir -Force:$ForceDownload
}

