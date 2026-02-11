# SlideTeX Note: Checks equation-numbering output against known-good expectations.

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$FixturePath = ''
)

$ErrorActionPreference = 'Stop'

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw "Assertion failed: $Message"
    }
}

function Invoke-BuildNumberedLatex {
    param(
        [System.Reflection.MethodInfo]$Method,
        [string]$Latex,
        [int]$StartNumber
    )

    $args = @($Latex, $StartNumber, 0)
    $rewritten = $Method.Invoke($null, $args)
    return @{
        Latex = [string]$rewritten
        Consumed = [int]$args[2]
    }
}

function Get-TagListFromLatex {
    param([string]$Latex)

    $tags = @()
    if ([string]::IsNullOrWhiteSpace($Latex)) {
        return $tags
    }

    $pattern = '\\tag\*?\s*\{([^{}]*)\}'
    $matches = [regex]::Matches($Latex, $pattern)
    foreach ($m in $matches) {
        $inner = $m.Groups[1].Value.Trim()
        if ($inner.StartsWith("(") -and $inner.EndsWith(")")) {
            $tags += $inner
        }
        else {
            $tags += "($inner)"
        }
    }

    return $tags
}

function Sequence-Equals {
    param(
        [object[]]$Left,
        [object[]]$Right
    )

    if ($null -eq $Left) { $Left = @() }
    if ($null -eq $Right) { $Right = @() }
    if ($Left.Count -ne $Right.Count) { return $false }

    for ($i = 0; $i -lt $Left.Count; $i++) {
        if ([string]$Left[$i] -ne [string]$Right[$i]) {
            return $false
        }
    }

    return $true
}

if ([string]::IsNullOrWhiteSpace($FixturePath)) {
    $FixturePath = Join-Path $PSScriptRoot '..\tests\equation-numbering\numbering-mathjax-v3.json'
}

if (-not (Test-Path $FixturePath)) {
    throw "Fixture not found: $FixturePath"
}

$fixture = Get-Content -Raw -Path $FixturePath | ConvertFrom-Json
Assert-True ($null -ne $fixture) 'fixture JSON parse failed'
Assert-True ($null -ne $fixture.cases -and $fixture.cases.Count -gt 0) 'fixture contains no cases'

$assemblyPath = Join-Path $PSScriptRoot "..\src\SlideTeX.VstoAddin\bin\$Configuration\SlideTeX.VstoAddin.dll"
if (-not (Test-Path $assemblyPath)) {
    throw "Assembly not found: $assemblyPath. Build SlideTeX.VstoAddin first."
}

$assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)
$controllerType = $assembly.GetType('SlideTeX.VstoAddin.SlideTeXAddinController', $false, $false)
Assert-True ($null -ne $controllerType) 'cannot load SlideTeXAddinController type'

$flags = [System.Reflection.BindingFlags]'NonPublic,Static'
$buildMethod = $controllerType.GetMethod('BuildNumberedLatex', $flags)
$countMethod = $controllerType.GetMethod('GetAutoNumberLineCount', $flags)
Assert-True ($null -ne $buildMethod) 'cannot find BuildNumberedLatex'
Assert-True ($null -ne $countMethod) 'cannot find GetAutoNumberLineCount'

$results = New-Object System.Collections.Generic.List[object]
$failures = New-Object System.Collections.Generic.List[string]

foreach ($case in $fixture.cases) {
    $id = [string]$case.id
    $latex = [string]$case.latex
    $start = if ($null -ne $case.startNumber) { [int]$case.startNumber } else { 1 }

    $buildResult = Invoke-BuildNumberedLatex -Method $buildMethod -Latex $latex -StartNumber $start
    $autoCount = [int]$countMethod.Invoke($null, @($latex))
    $actualTags = @(Get-TagListFromLatex -Latex $buildResult.Latex)
    $expectedTags = @($case.expectedTags | ForEach-Object { [string]$_ })

    $expectedAutoCount = [int]$case.expectedAutoNumberLineCount
    $tagMatch = Sequence-Equals -Left @($actualTags) -Right @($expectedTags)
    $countMatch = ($autoCount -eq $expectedAutoCount)
    $ok = $tagMatch -and $countMatch

    if (-not $ok) {
        $failures.Add($id) | Out-Null
    }

    $results.Add([pscustomobject]@{
        case = $id
        pass = $ok
        expectedTags = @($expectedTags)
        actualTags = @($actualTags)
        expectedAutoNumberLineCount = $expectedAutoCount
        actualAutoNumberLineCount = $autoCount
        consumed = $buildResult.Consumed
        rewrittenLatex = $buildResult.Latex
    }) | Out-Null
}

if ($failures.Count -gt 0) {
    Write-Host "Known-good comparison failed: $($failures.Count) case(s)." -ForegroundColor Red
    Write-Host ("Failed cases: " + ($failures -join ', '))
    $results | ConvertTo-Json -Depth 6
    exit 1
}

Write-Host "Known-good comparison passed. Cases: $($results.Count)."
$results | ConvertTo-Json -Depth 6

