# SlideTeX Note: Runs OCR baseline against known LaTeX-image pairs.

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [ValidateSet('all', 'smoke', 'full')]
    [string]$Suite = 'all',
    [string]$FixturePath = '',
    [string]$CaseId = '',
    [string]$ModelDir = '',
    [string]$ArtifactsDir = '',
    [switch]$Strict
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -eq 'Core') {
    $windowsPowerShellPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path $windowsPowerShellPath)) {
        throw "Windows PowerShell not found: $windowsPowerShellPath"
    }

    $forwardArgs = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $PSCommandPath
    )

    foreach ($entry in $PSBoundParameters.GetEnumerator()) {
        $name = [string]$entry.Key
        $value = $entry.Value
        if ($value -is [switch]) {
            if ($value.IsPresent) {
                $forwardArgs += "-$name"
            }
            continue
        }

        $forwardArgs += "-$name"
        $forwardArgs += [string]$value
    }

    & $windowsPowerShellPath @forwardArgs
    exit $LASTEXITCODE
}

function Get-ValueOrDefault {
    param(
        [object]$Value,
        [object]$Fallback
    )

    if ($null -eq $Value) {
        return $Fallback
    }

    return $Value
}

function Resolve-RelativePath {
    param(
        [string]$BasePath,
        [string]$CandidatePath
    )

    if ([string]::IsNullOrWhiteSpace($CandidatePath)) {
        return ''
    }

    if ([System.IO.Path]::IsPathRooted($CandidatePath)) {
        return $CandidatePath
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $CandidatePath))
}

function Resolve-DirectoryInput {
    param(
        [string]$InputPath,
        [string[]]$BasePaths
    )

    if ([string]::IsNullOrWhiteSpace($InputPath)) {
        return [pscustomobject]@{
            ResolvedPath = ''
            Candidates = @()
        }
    }

    $raw = [Environment]::ExpandEnvironmentVariables($InputPath.Trim())
    if ($raw.StartsWith('~')) {
        $home = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
        if (-not [string]::IsNullOrWhiteSpace($home)) {
            if ($raw.Length -eq 1) {
                $raw = $home
            }
            elseif ($raw[1] -eq '\' -or $raw[1] -eq '/') {
                $raw = Join-Path $home $raw.Substring(2)
            }
        }
    }

    $allCandidates = New-Object System.Collections.Generic.List[string]
    if ([System.IO.Path]::IsPathRooted($raw)) {
        $allCandidates.Add([System.IO.Path]::GetFullPath($raw)) | Out-Null
    }
    else {
        foreach ($base in $BasePaths) {
            if ([string]::IsNullOrWhiteSpace($base)) {
                continue
            }
            $candidate = [System.IO.Path]::GetFullPath((Join-Path $base $raw))
            $allCandidates.Add($candidate) | Out-Null
        }
    }

    $dedup = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $ordered = New-Object System.Collections.Generic.List[string]
    foreach ($candidate in $allCandidates) {
        if ($dedup.Add($candidate)) {
            $ordered.Add($candidate) | Out-Null
        }
    }

    foreach ($candidate in $ordered) {
        if (Test-Path -LiteralPath $candidate -PathType Container) {
            return [pscustomobject]@{
                ResolvedPath = $candidate
                Candidates = @($ordered)
            }
        }
    }

    return [pscustomobject]@{
        ResolvedPath = ''
        Candidates = @($ordered)
    }
}

function Get-SuiteList {
    param([object]$SuiteValue)

    if ($null -eq $SuiteValue) {
        return @('full')
    }

    if ($SuiteValue -is [System.Collections.IEnumerable] -and -not ($SuiteValue -is [string])) {
        $list = @()
        foreach ($item in $SuiteValue) {
            $text = [string]$item
            if (-not [string]::IsNullOrWhiteSpace($text)) {
                $list += $text.Trim().ToLowerInvariant()
            }
        }
        if ($list.Count -gt 0) {
            return $list
        }
        return @('full')
    }

    $single = [string]$SuiteValue
    if ([string]::IsNullOrWhiteSpace($single)) {
        return @('full')
    }

    return @($single.Trim().ToLowerInvariant())
}

function Normalize-Latex {
    param([string]$Latex)

    if ([string]::IsNullOrWhiteSpace($Latex)) {
        return ''
    }

    return (($Latex -replace '\s+', '')).Trim()
}

function Normalize-LatexForComparison {
    param([string]$Latex)

    if ([string]::IsNullOrWhiteSpace($Latex)) {
        return ''
    }

    $normalized = [string]$Latex
    $normalized = $normalized -replace '\\cfrac', '\\frac'
    $normalized = $normalized -replace '\\left', ''
    $normalized = $normalized -replace '\\right', ''
    $normalized = $normalized -replace '\\mathop\s*\{([^{}]*)\}', '$1'
    $normalized = $normalized -replace '\\mathrm\s*\{([^{}]*)\}', '$1'
    $normalized = $normalized -replace '\\operatorname\s*\{([^{}]*)\}', '$1'
    $normalized = $normalized -replace '\\tag\*?\s*\{([^{}]*)\}', '($1)'
    $normalized = $normalized -replace '\\notag|\\nonumber', ''
    $normalized = $normalized -replace '\\begin\s*\{(?:equation\*?|align\*?|gather\*?|aligned|split)\}', ''
    $normalized = $normalized -replace '\\end\s*\{(?:equation\*?|align\*?|gather\*?|aligned|split)\}', ''
    $normalized = $normalized -replace '&', ''
    $normalized = $normalized -replace '\\,|\\;|\\!|\\quad|\\qquad', ''
    $normalized = $normalized -replace '\\\\', ''
    $normalized = $normalized -replace '\\[()]', ''
    $normalized = $normalized -replace '[{}]', ''
    $normalized = $normalized -replace '\s+', ''

    return $normalized.Trim()
}

function Get-LevenshteinDistance {
    param(
        [string]$Left,
        [string]$Right
    )

    $leftText = [string](Get-ValueOrDefault -Value $Left -Fallback '')
    $rightText = [string](Get-ValueOrDefault -Value $Right -Fallback '')

    $m = $leftText.Length
    $n = $rightText.Length

    if ($m -eq 0) { return $n }
    if ($n -eq 0) { return $m }

    $previous = New-Object int[] ($n + 1)
    $current = New-Object int[] ($n + 1)

    for ($j = 0; $j -le $n; $j++) {
        $previous[$j] = $j
    }

    for ($i = 1; $i -le $m; $i++) {
        $current[0] = $i
        $leftChar = $leftText[$i - 1]
        for ($j = 1; $j -le $n; $j++) {
            $cost = if ($leftChar -ceq $rightText[$j - 1]) { 0 } else { 1 }
            $deletion = $previous[$j] + 1
            $insertion = $current[$j - 1] + 1
            $substitution = $previous[$j - 1] + $cost
            $best = [Math]::Min($deletion, [Math]::Min($insertion, $substitution))
            $current[$j] = $best
        }

        $swap = $previous
        $previous = $current
        $current = $swap
    }

    return $previous[$n]
}

if ([string]::IsNullOrWhiteSpace($FixturePath)) {
    $FixturePath = Join-Path $PSScriptRoot '..\tests\ocr-baseline\ocr-baseline-v1.json'
}

if ([string]::IsNullOrWhiteSpace($ArtifactsDir)) {
    $ArtifactsDir = Join-Path $PSScriptRoot '..\artifacts\ocr-baseline'
}

$fixtureFullPath = [System.IO.Path]::GetFullPath($FixturePath)
if (-not (Test-Path $fixtureFullPath)) {
    throw "Fixture not found: $fixtureFullPath"
}

$artifactFullPath = [System.IO.Path]::GetFullPath($ArtifactsDir)
New-Item -ItemType Directory -Path $artifactFullPath -Force | Out-Null

$fixtureDir = Split-Path $fixtureFullPath -Parent
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture = Get-Content -Raw -Path $fixtureFullPath | ConvertFrom-Json
if ($null -eq $fixture -or $null -eq $fixture.cases -or $fixture.cases.Count -eq 0) {
    throw "Fixture has no cases: $fixtureFullPath"
}

$selectedCases = @()
foreach ($case in $fixture.cases) {
    $suiteList = Get-SuiteList -SuiteValue $case.suite
    $suiteMatch = $Suite -eq 'all' -or $suiteList -contains $Suite.ToLowerInvariant()
    if (-not $suiteMatch) {
        continue
    }

    if (-not [string]::IsNullOrWhiteSpace($CaseId) -and [string]$case.id -ne $CaseId) {
        continue
    }

    $selectedCases += $case
}

if ($selectedCases.Count -eq 0) {
    throw "No OCR baseline cases selected. suite=$Suite caseId=$CaseId"
}

$assemblyPath = Join-Path $PSScriptRoot "..\src\SlideTeX.VstoAddin\bin\$Configuration\SlideTeX.VstoAddin.dll"
if (-not (Test-Path $assemblyPath)) {
    throw "Assembly not found: $assemblyPath. Build SlideTeX.VstoAddin first."
}

$modelSearchBases = @(
    (Get-Location).Path
    $repoRoot
    $fixtureDir
    $PSScriptRoot
)

$resolvedModelDir = ''
if (-not [string]::IsNullOrWhiteSpace($ModelDir)) {
    $modelResolution = Resolve-DirectoryInput -InputPath $ModelDir -BasePaths $modelSearchBases
    if ([string]::IsNullOrWhiteSpace($modelResolution.ResolvedPath)) {
        $detail = if ($modelResolution.Candidates.Count -gt 0) {
            ($modelResolution.Candidates -join '; ')
        }
        else {
            '(none)'
        }
        throw "OCR model directory not found from ModelDir='$ModelDir'. Candidates: $detail"
    }
    $resolvedModelDir = $modelResolution.ResolvedPath
}
elseif (-not [string]::IsNullOrWhiteSpace($env:SLIDETEX_OCR_MODEL_DIR)) {
    $envResolution = Resolve-DirectoryInput -InputPath $env:SLIDETEX_OCR_MODEL_DIR -BasePaths $modelSearchBases
    if ([string]::IsNullOrWhiteSpace($envResolution.ResolvedPath)) {
        $detail = if ($envResolution.Candidates.Count -gt 0) {
            ($envResolution.Candidates -join '; ')
        }
        else {
            '(none)'
        }
        throw "OCR model directory not found from environment variable SLIDETEX_OCR_MODEL_DIR='$($env:SLIDETEX_OCR_MODEL_DIR)'. Candidates: $detail"
    }
    $resolvedModelDir = $envResolution.ResolvedPath
}
else {
    $resolvedModelDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\SlideTeX.VstoAddin\Assets\OcrModels\pix2text-mfr'))
}

if (-not (Test-Path $resolvedModelDir)) {
    throw "OCR model directory not found: $resolvedModelDir"
}

$requiredModelFiles = @(
    'encoder_model.onnx',
    'tokenizer.json'
)
foreach ($file in $requiredModelFiles) {
    $candidate = Join-Path $resolvedModelDir $file
    if (-not (Test-Path $candidate)) {
        throw "Missing OCR model file: $candidate"
    }
}

$decoderCandidates = @(
    'decoder_model.onnx',
    'decoder_model_merged_quantized.onnx'
)
$decoderFound = $false
foreach ($decoderFile in $decoderCandidates) {
    $decoderCandidate = Join-Path $resolvedModelDir $decoderFile
    if (Test-Path $decoderCandidate) {
        $decoderFound = $true
        break
    }
}
if (-not $decoderFound) {
    throw ("Missing OCR decoder model file. Tried: " + (($decoderCandidates | ForEach-Object { Join-Path $resolvedModelDir $_ }) -join '; '))
}

$env:SLIDETEX_OCR_MODEL_DIR = $resolvedModelDir

$assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)
$ocrType = $assembly.GetType('SlideTeX.VstoAddin.Ocr.FormulaOcrService', $false, $false)
$optionsType = $assembly.GetType('SlideTeX.VstoAddin.Ocr.FormulaOcrOptions', $false, $false)
if ($null -eq $ocrType -or $null -eq $optionsType) {
    throw "Cannot resolve OCR types from assembly: $assemblyPath"
}

$recognizeMethod = $ocrType.GetMethod('Recognize', [System.Reflection.BindingFlags]'Public,Instance')
if ($null -eq $recognizeMethod) {
    throw 'Cannot find FormulaOcrService.Recognize method.'
}

$optionsMaxTokensProp = $optionsType.GetProperty('MaxTokens', [System.Reflection.BindingFlags]'Public,Instance')
$optionsTimeoutProp = $optionsType.GetProperty('TimeoutMs', [System.Reflection.BindingFlags]'Public,Instance')
if ($null -eq $optionsMaxTokensProp -or $null -eq $optionsTimeoutProp) {
    throw 'Cannot resolve FormulaOcrOptions properties.'
}

$results = New-Object System.Collections.Generic.List[object]
$failures = New-Object System.Collections.Generic.List[string]
$defaults = $fixture.defaults
$defaultOcrOptions = if ($null -ne $defaults) { $defaults.ocrOptions } else { $null }
$defaultPass = if ($null -ne $defaults) { $defaults.passCriteria } else { $null }
$defaultMaxTokens = [int](Get-ValueOrDefault -Value ($defaultOcrOptions.maxTokens) -Fallback 256)
$defaultTimeoutMs = [int](Get-ValueOrDefault -Value ($defaultOcrOptions.timeoutMs) -Fallback 20000)
$defaultRequireExact = [bool](Get-ValueOrDefault -Value ($defaultPass.requireExact) -Fallback $false)
$defaultMaxCer = [double](Get-ValueOrDefault -Value ($defaultPass.maxCer) -Fallback 0.35)
$defaultMinPassRatio = [double](Get-ValueOrDefault -Value ($defaultPass.minPassRatio) -Fallback 0.65)

$service = [System.Activator]::CreateInstance($ocrType, $true)
try {
    foreach ($case in $selectedCases) {
        $id = [string]$case.id
        $imagePath = Resolve-RelativePath -BasePath $fixtureDir -CandidatePath ([string]$case.imagePath)
        $expectedLatex = [string]$case.latex

        $caseOcrOptions = if ($null -ne $case.ocrOptions) { $case.ocrOptions } else { $null }
        $casePassCriteria = if ($null -ne $case.passCriteria) { $case.passCriteria } else { $null }

        $maxTokens = [int](Get-ValueOrDefault -Value ($caseOcrOptions.maxTokens) -Fallback $defaultMaxTokens)
        $timeoutMs = [int](Get-ValueOrDefault -Value ($caseOcrOptions.timeoutMs) -Fallback $defaultTimeoutMs)
        $requireExact = [bool](Get-ValueOrDefault -Value ($casePassCriteria.requireExact) -Fallback $defaultRequireExact)
        $maxCer = [double](Get-ValueOrDefault -Value ($casePassCriteria.maxCer) -Fallback $defaultMaxCer)

        if (-not (Test-Path $imagePath)) {
            $failures.Add($id) | Out-Null
            $results.Add([pscustomobject]@{
                caseId = $id
                pass = $false
                error = "Image file not found: $imagePath"
            }) | Out-Null
            continue
        }

        $optionsObj = [System.Activator]::CreateInstance($optionsType, $true)
        $optionsMaxTokensProp.SetValue($optionsObj, [int]$maxTokens, $null)
        $optionsTimeoutProp.SetValue($optionsObj, [int]$timeoutMs, $null)

        $actualLatex = ''
        $engine = ''
        $elapsedMs = 0
        $runtimeError = ''

        try {
            $imageBase64 = [System.Convert]::ToBase64String([System.IO.File]::ReadAllBytes($imagePath))
            $resultObj = $recognizeMethod.Invoke($service, @($imageBase64, $optionsObj))
            if ($null -ne $resultObj) {
                $resultType = $resultObj.GetType()
                $actualLatex = [string](Get-ValueOrDefault -Value ($resultType.GetProperty('Latex').GetValue($resultObj, $null)) -Fallback '')
                $engine = [string](Get-ValueOrDefault -Value ($resultType.GetProperty('Engine').GetValue($resultObj, $null)) -Fallback '')
                $elapsedMs = [long](Get-ValueOrDefault -Value ($resultType.GetProperty('ElapsedMs').GetValue($resultObj, $null)) -Fallback 0)
            }
        }
        catch [System.Reflection.TargetInvocationException] {
            $inner = $_.Exception.InnerException
            $runtimeError = if ($null -ne $inner) { $inner.Message } else { $_.Exception.Message }
        }
        catch {
            $runtimeError = $_.Exception.Message
        }

        if (-not [string]::IsNullOrWhiteSpace($runtimeError)) {
            $failures.Add($id) | Out-Null
            $results.Add([pscustomobject]@{
                caseId = $id
                pass = $false
                error = $runtimeError
                imagePath = $imagePath
            }) | Out-Null
            continue
        }

        $expectedNorm = Normalize-Latex -Latex $expectedLatex
        $actualNorm = Normalize-Latex -Latex $actualLatex
        $strictExactMatch = ($expectedNorm -ceq $actualNorm)
        $strictDistance = Get-LevenshteinDistance -Left $expectedNorm -Right $actualNorm
        $strictCer = [double]$strictDistance / [Math]::Max(1, $expectedNorm.Length)
        $strictCerRounded = [Math]::Round($strictCer, 6)

        $expectedComparable = Normalize-LatexForComparison -Latex $expectedLatex
        $actualComparable = Normalize-LatexForComparison -Latex $actualLatex
        $comparableExactMatch = ($expectedComparable -ceq $actualComparable)
        $comparableDistance = Get-LevenshteinDistance -Left $expectedComparable -Right $actualComparable
        $comparableCer = [double]$comparableDistance / [Math]::Max(1, $expectedComparable.Length)
        $comparableCerRounded = [Math]::Round($comparableCer, 6)

        $casePass = if ($requireExact) {
            $comparableExactMatch
        }
        else {
            $comparableExactMatch -or ($comparableCer -le $maxCer)
        }

        if (-not $casePass) {
            $failures.Add($id) | Out-Null
        }

        $results.Add([pscustomobject]@{
            caseId = $id
            pass = $casePass
            imagePath = $imagePath
            expectedLatex = $expectedLatex
            actualLatex = $actualLatex
            expectedNormalized = $expectedNorm
            actualNormalized = $actualNorm
            expectedComparable = $expectedComparable
            actualComparable = $actualComparable
            exactMatch = $comparableExactMatch
            cer = $comparableCerRounded
            levenshteinDistance = $comparableDistance
            strictExactMatch = $strictExactMatch
            strictCer = $strictCerRounded
            strictLevenshteinDistance = $strictDistance
            maxCer = $maxCer
            requireExact = $requireExact
            maxTokens = $maxTokens
            timeoutMs = $timeoutMs
            elapsedMs = $elapsedMs
            engine = $engine
        }) | Out-Null
    }
}
finally {
    if ($null -ne $service -and $service -is [System.IDisposable]) {
        ([System.IDisposable]$service).Dispose()
    }
}

$totalCount = $results.Count
$passCount = @($results | Where-Object { $_.pass }).Count
$exactCount = @($results | Where-Object { $_.exactMatch }).Count
$passRatio = if ($totalCount -gt 0) { [double]$passCount / $totalCount } else { 0.0 }
$avgCer = 0.0
$cerCases = @($results | Where-Object { $_.PSObject.Properties.Name -contains 'cer' })
if ($cerCases.Count -gt 0) {
    $avgCer = [Math]::Round((($cerCases | Measure-Object -Property cer -Average).Average), 6)
}

$overallPass = $passRatio -ge $defaultMinPassRatio
if ($Strict.IsPresent) {
    $overallPass = $overallPass -and ($passCount -eq $totalCount)
}

$failureArray = $failures.ToArray()
$resultArray = $results.ToArray()

$report = [pscustomobject]@{
    meta = [pscustomobject]@{
        fixture = $fixtureFullPath
        suite = $Suite
        caseId = $CaseId
        configuration = $Configuration
        assemblyPath = [System.IO.Path]::GetFullPath($assemblyPath)
        modelDir = $resolvedModelDir
        strict = $Strict.IsPresent
        generatedAt = [DateTime]::UtcNow.ToString('o')
    }
    summary = [pscustomobject]@{
        totalCount = $totalCount
        passCount = $passCount
        failCount = $totalCount - $passCount
        exactMatchCount = $exactCount
        passRatio = [Math]::Round($passRatio, 6)
        minPassRatio = $defaultMinPassRatio
        averageCer = $avgCer
        overallPass = $overallPass
    }
    failures = $failureArray
    results = $resultArray
}

$reportPath = Join-Path $artifactFullPath 'report.json'
$report | ConvertTo-Json -Depth 12 | Set-Content -Path $reportPath -Encoding utf8

if ($overallPass) {
    Write-Host "OCR baseline passed. Cases=$totalCount Pass=$passCount Exact=$exactCount PassRatio=$([Math]::Round($passRatio, 4))."
    Write-Host "Report: $reportPath"
    exit 0
}

Write-Host "OCR baseline failed. Cases=$totalCount Pass=$passCount Fail=$($totalCount - $passCount)." -ForegroundColor Red
Write-Host ("Failed cases: " + (($failures | Select-Object -Unique) -join ', '))
Write-Host "Report: $reportPath"
exit 1
