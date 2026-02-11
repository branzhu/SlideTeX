# SlideTeX Note: Syncs vendored KaTeX assets into the WebUI distribution folder.

param(
    [string]$Version = "0.16.11",
    [string]$ArchivePath = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$vendorDir = Join-Path $root "src/SlideTeX.WebUI/vendor/katex"
$tmpDir = Join-Path $root ".tmp/katex"
$archive = Join-Path $tmpDir "katex-$Version.tgz"
$extractDir = Join-Path $tmpDir "extract"

New-Item -ItemType Directory -Path $vendorDir -Force | Out-Null
New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
    $uri = "https://registry.npmjs.org/katex/-/katex-$Version.tgz"
    Write-Host "Downloading $uri"

    try {
        Invoke-WebRequest -Uri $uri -OutFile $archive
    }
    catch {
        throw "KaTeX 下载失败。可改用 -ArchivePath 指定本地 katex-$Version.tgz。原始错误: $($_.Exception.Message)"
    }
}
else {
    if (!(Test-Path $ArchivePath)) {
        throw "指定的 ArchivePath 不存在: $ArchivePath"
    }

    Copy-Item -Path $ArchivePath -Destination $archive -Force
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

Write-Host "KaTeX $Version synced to $vendorDir"


