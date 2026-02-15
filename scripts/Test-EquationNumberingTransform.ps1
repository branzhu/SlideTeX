# SlideTeX Note: Validates equation-numbering transformation logic with fixture inputs.

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
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

# case 1: align single line auto numbering
$case1Latex = '\begin{align} a &= b \end{align}'
$case1 = Invoke-BuildNumberedLatex -Method $buildMethod -Latex $case1Latex -StartNumber 1
Assert-True ($case1.Consumed -eq 1) 'case1 should consume 1 number'
Assert-True ($case1.Latex.Contains('\begin{align*}')) 'case1 should rewrite to align*'
Assert-True ($case1.Latex.Contains('\tag{1}')) 'case1 should contain \\tag{1}'
$results.Add([pscustomobject]@{ case = 'align_single'; consumed = $case1.Consumed; output = $case1.Latex }) | Out-Null

# case 2: align multi-line auto numbering
$case2Latex = '\begin{align} a &= b \\ c &= d \end{align}'
$case2 = Invoke-BuildNumberedLatex -Method $buildMethod -Latex $case2Latex -StartNumber 3
Assert-True ($case2.Consumed -eq 2) 'case2 should consume 2 numbers'
Assert-True ($case2.Latex.Contains('\tag{3}')) 'case2 should contain \\tag{3}'
Assert-True ($case2.Latex.Contains('\tag{4}')) 'case2 should contain \\tag{4}'
$results.Add([pscustomobject]@{ case = 'align_multi'; consumed = $case2.Consumed; output = $case2.Latex }) | Out-Null

# case 3: align + \notag suppression
$case3Latex = '\begin{align} a &= b \notag \\ c &= d \end{align}'
$case3 = Invoke-BuildNumberedLatex -Method $buildMethod -Latex $case3Latex -StartNumber 10
Assert-True ($case3.Consumed -eq 1) 'case3 should only consume 1 number'
Assert-True (-not $case3.Latex.Contains('\tag{11}')) 'case3 should not generate \\tag{11}'
Assert-True ($case3.Latex.Contains('\tag{10}')) 'case3 should contain \\tag{10}'
$results.Add([pscustomobject]@{ case = 'align_notag'; consumed = $case3.Consumed; output = $case3.Latex }) | Out-Null

# case 4: align + custom \tag
$case4Latex = '\begin{align} a &= b \tag{A} \\ c &= d \end{align}'
$case4 = Invoke-BuildNumberedLatex -Method $buildMethod -Latex $case4Latex -StartNumber 7
Assert-True ($case4.Consumed -eq 1) 'case4 should consume 1 auto number'
Assert-True ($case4.Latex.Contains('\tag{A}')) 'case4 should keep custom \\tag{A}'
Assert-True ($case4.Latex.Contains('\tag{7}')) 'case4 second line should auto number as 7'
$results.Add([pscustomobject]@{ case = 'align_custom_tag'; consumed = $case4.Consumed; output = $case4.Latex }) | Out-Null

# case 5: custom \tag only => no auto numbering
$case5Latex = '\begin{equation} E=mc^2 \tag{X} \end{equation}'
$case5Count = [int]$countMethod.Invoke($null, @($case5Latex))
Assert-True ($case5Count -eq 0) 'case5 auto-number line count should be 0'
$case5 = Invoke-BuildNumberedLatex -Method $buildMethod -Latex $case5Latex -StartNumber 1
Assert-True ($case5.Consumed -eq 0) 'case5 should not consume auto numbering'
Assert-True ($case5.Latex -eq $case5Latex) 'case5 should not rewrite latex'
$results.Add([pscustomobject]@{ case = 'equation_custom_only'; consumed = $case5.Consumed; output = $case5.Latex }) | Out-Null

# case 6: auto-number line counting
$case6Latex = '\begin{align} a &= b \\ c &= d \notag \\ e &= f \end{align}'
$case6Count = [int]$countMethod.Invoke($null, @($case6Latex))
Assert-True ($case6Count -eq 2) 'case6 auto-number line count should be 2'
$results.Add([pscustomobject]@{ case = 'line_count'; autoNumberLineCount = $case6Count }) | Out-Null

# case 7: gather multi-line numbering
$case7Latex = '\begin{gather} a=b \\ c=d \\ e=f \end{gather}'
$case7 = Invoke-BuildNumberedLatex -Method $buildMethod -Latex $case7Latex -StartNumber 20
Assert-True ($case7.Consumed -eq 3) 'case7 should consume 3 numbers'
Assert-True ($case7.Latex.Contains('\begin{gather*}')) 'case7 should rewrite to gather*'
Assert-True ($case7.Latex.Contains('\tag{20}')) 'case7 should contain \\tag{20}'
Assert-True ($case7.Latex.Contains('\tag{22}')) 'case7 should contain \\tag{22}'
$results.Add([pscustomobject]@{ case = 'gather_multi'; consumed = $case7.Consumed; output = $case7.Latex }) | Out-Null

# case 8: multline is not auto-rewritten by current numbering transform logic
$case8Latex = '\begin{multline} a+b+c \\ d+e \\ f+g \end{multline}'
$case8 = Invoke-BuildNumberedLatex -Method $buildMethod -Latex $case8Latex -StartNumber 30
Assert-True ($case8.Consumed -eq 0) 'case8 should consume 0 auto numbers for unsupported multline'
Assert-True ($case8.Latex -eq $case8Latex) 'case8 should keep original multline latex'
$case8Count = [int]$countMethod.Invoke($null, @($case8Latex))
Assert-True ($case8Count -eq 0) 'case8 auto-number line count should be 0 for unsupported multline'
$results.Add([pscustomobject]@{ case = 'multline_unsupported'; consumed = $case8.Consumed; output = $case8.Latex }) | Out-Null

Write-Host "Equation numbering transform tests passed. Cases: $($results.Count)."
$results | ConvertTo-Json -Depth 4

