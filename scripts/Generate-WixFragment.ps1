# SlideTeX Note: Generates WiX fragments from staged file outputs.

param(
    [string]$BuildOutputDir = "src/SlideTeX.VstoAddin/bin/Release",
    [string]$OutputFile = "src/SlideTeX.Installer/wix/Fragments/GeneratedFiles.wxs"
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

# Generates deterministic GUID from path seed so component identities stay stable.
function Get-DeterministicGuid {
    param([string]$Seed)

    $md5 = [System.Security.Cryptography.MD5]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Seed)
        $hash = $md5.ComputeHash($bytes)
        return [Guid]::new($hash)
    }
    finally {
        $md5.Dispose()
    }
}

# Builds WiX-safe stable IDs from source path values.
function Get-StableId {
    param(
        [string]$Prefix,
        [string]$Value
    )

    $safe = $Value -replace "[^A-Za-z0-9_]", "_"
    if ([string]::IsNullOrWhiteSpace($safe)) {
        $safe = "root"
    }

    if ($safe.Length -gt 24) {
        $safe = $safe.Substring(0, 24)
    }

    $md5 = [System.Security.Cryptography.MD5]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
        $hash = $md5.ComputeHash($bytes)
        $hashText = ([System.BitConverter]::ToString($hash)).Replace("-", "").Substring(0, 10)
        return "$Prefix`_$safe`_$hashText"
    }
    finally {
        $md5.Dispose()
    }
}

# Normalizes Windows-style relative path representation for WiX source entries.
function Normalize-RelativePath {
    param([string]$Path)

    return $Path.Replace("/", "\").TrimStart('\')
}

$root = Split-Path -Parent $PSScriptRoot
$buildDir = Resolve-PathFromRoot -Root $root -PathValue $BuildOutputDir
$outputPath = Resolve-PathFromRoot -Root $root -PathValue $OutputFile

if (!(Test-Path $buildDir)) {
    throw "BuildOutputDir does not exist: $buildDir"
}

$allFiles = Get-ChildItem -Path $buildDir -Recurse -File |
    Where-Object { $_.Extension -notin @(".pdb", ".xml") }

if ($allFiles.Count -eq 0) {
    throw "No packagable files found: $buildDir"
}

$dirIdMap = @{
    "ADDINFOLDER|" = "ADDINFOLDER"
    "WEBUIFOLDER|" = "WEBUIFOLDER"
}
$dirChildren = @{}
$fileEntries = @()

foreach ($file in $allFiles) {
    $relative = Normalize-RelativePath -Path ([System.IO.Path]::GetRelativePath($buildDir, $file.FullName))

    $baseDirId = "ADDINFOLDER"
    $innerRelative = $relative

    if ($relative.StartsWith("WebUI\", [System.StringComparison]::OrdinalIgnoreCase)) {
        $baseDirId = "WEBUIFOLDER"
        $innerRelative = $relative.Substring(6)
    }

    $innerRelative = Normalize-RelativePath -Path $innerRelative
    $dirPath = [System.IO.Path]::GetDirectoryName($innerRelative)
    if ($null -eq $dirPath) {
        $dirPath = ""
    }

    $dirPath = Normalize-RelativePath -Path $dirPath
    if ($dirPath -eq ".") {
        $dirPath = ""
    }

    $parts = @()
    if (-not [string]::IsNullOrWhiteSpace($dirPath)) {
        $parts = $dirPath.Split([char]'\', [System.StringSplitOptions]::RemoveEmptyEntries)
    }

    $currentPath = ""
    $parentPath = ""

    foreach ($part in $parts) {
        $currentPath = if ([string]::IsNullOrEmpty($currentPath)) { $part } else { "$currentPath\$part" }

        $key = "$baseDirId|$currentPath"
        if (-not $dirIdMap.ContainsKey($key)) {
            $dirIdMap[$key] = Get-StableId -Prefix "DIR" -Value "$baseDirId|$currentPath"
        }

        $parentKey = "$baseDirId|$parentPath"
        if (-not $dirChildren.ContainsKey($parentKey)) {
            $dirChildren[$parentKey] = [System.Collections.Generic.List[object]]::new()
        }

        if (-not ($dirChildren[$parentKey] | Where-Object { $_.Path -eq $currentPath })) {
            $null = $dirChildren[$parentKey].Add([PSCustomObject]@{
                Path = $currentPath
                Name = $part
                Id = $dirIdMap[$key]
            })
        }

        $parentPath = $currentPath
    }

    $fileDirKey = "$baseDirId|$dirPath"
    $fileDirId = $dirIdMap[$fileDirKey]

    $fileEntries += [PSCustomObject]@{
        DirectoryId = $fileDirId
        Relative = $relative
        ComponentId = Get-StableId -Prefix "CMP" -Value $relative
        FileId = Get-StableId -Prefix "FIL" -Value $relative
        Guid = (Get-DeterministicGuid -Seed $relative).ToString("B").ToUpperInvariant()
        Source = '$(var.BuildOutputDir)\' + $relative
    }
}

$fileEntries = $fileEntries | Sort-Object Relative
$parentKeys = $dirChildren.Keys | Sort-Object

$sb = [System.Text.StringBuilder]::new()
$null = $sb.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
$null = $sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
$null = $sb.AppendLine('  <Fragment>')

foreach ($parentKey in $parentKeys) {
    $baseId, $path = $parentKey.Split("|", 2)
    $parentId = $dirIdMap[$parentKey]

    $null = $sb.AppendLine(('    <DirectoryRef Id="{0}">' -f $parentId))
    foreach ($child in ($dirChildren[$parentKey] | Sort-Object Path)) {
        $nameEscaped = [System.Security.SecurityElement]::Escape($child.Name)
        $null = $sb.AppendLine(('      <Directory Id="{0}" Name="{1}" />' -f $child.Id, $nameEscaped))
    }
    $null = $sb.AppendLine('    </DirectoryRef>')
}

$null = $sb.AppendLine('  </Fragment>')
$null = $sb.AppendLine('  <Fragment>')
$null = $sb.AppendLine('    <ComponentGroup Id="SlideTeXProductComponents">')

foreach ($entry in $fileEntries) {
    $null = $sb.AppendLine(('      <Component Id="{0}" Directory="{1}" Guid="{2}">' -f $entry.ComponentId, $entry.DirectoryId, $entry.Guid))
    $null = $sb.AppendLine(('        <File Id="{0}" Source="{1}" KeyPath="yes" />' -f $entry.FileId, $entry.Source))
    $null = $sb.AppendLine('      </Component>')
}

$null = $sb.AppendLine('    </ComponentGroup>')
$null = $sb.AppendLine('  </Fragment>')
$null = $sb.AppendLine('</Wix>')

New-Item -ItemType Directory -Path (Split-Path -Parent $outputPath) -Force | Out-Null
$sb.ToString() | Set-Content -Path $outputPath -Encoding utf8

Write-Host "Generated WiX fragment: $outputPath"
Write-Host "File count: $($fileEntries.Count)"

