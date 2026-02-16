# SlideTeX Note: Runs a smoke test flow against PowerPoint automation integration.

param(
    [string]$OutputDir = "artifacts/smoke",
    [switch]$KeepPowerPointVisible
)

$ErrorActionPreference = "Stop"

function New-SamplePng {
    param([string]$Path)

    # 2x2 transparent PNG
    $base64 = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFElEQVR4nGP4z8DQwMDAwMDEAAUAGx0CBdSUsTUAAAAASUVORK5CYII="
    [IO.File]::WriteAllBytes($Path, [Convert]::FromBase64String($base64))
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$reportPath = Join-Path $OutputDir "smoke-report.json"
$pptPath = Join-Path $OutputDir "slidetex-smoke.pptx"
$tempPng = Join-Path $OutputDir "smoke.png"

$result = [ordered]@{
    timestamp = (Get-Date).ToString("o")
    machine = $env:COMPUTERNAME
    officeAvailable = $false
    insertPictureOk = $false
    tagRoundTripOk = $false
    savedOk = $false
    details = @()
}

$pp = $null
$presentation = $null

try {
    $pp = New-Object -ComObject PowerPoint.Application
    $result.officeAvailable = $true

    if ($KeepPowerPointVisible) {
        $pp.Visible = $true
    }

    $presentation = $pp.Presentations.Add()
    $slide = $presentation.Slides.Add(1, 12) # ppLayoutBlank

    New-SamplePng -Path $tempPng
    $shape = $slide.Shapes.AddPicture($tempPng, 0, -1, 100, 100, 64, 64)
    $result.insertPictureOk = $true

    $shape.Tags.Add("SLIDETEX_META_VERSION", "1")
    $shape.Tags.Add("SLIDETEX_LATEX", "\\frac{a}{b}")
    $shape.Tags.Add("SLIDETEX_RENDER_OPTIONS", '{"dpi":300}')

    $metaVersion = $shape.Tags.Item("SLIDETEX_META_VERSION")
    $latex = $shape.Tags.Item("SLIDETEX_LATEX")

    if ($metaVersion -eq "1" -and $latex -eq "\\frac{a}{b}") {
        $result.tagRoundTripOk = $true
    }
    else {
        $result.details += "Tag readback mismatch."
    }

    $presentation.SaveAs($pptPath)
    $result.savedOk = Test-Path $pptPath

    if (-not $result.savedOk) {
        $result.details += "Presentation was not saved."
    }
}
catch {
    $result.details += $_.Exception.Message
}
finally {
    if ($presentation -ne $null) {
        try { $presentation.Close() } catch {}
    }

    if ($pp -ne $null -and -not $KeepPowerPointVisible) {
        try { $pp.Quit() } catch {}
    }

    if ($shape -ne $null) {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shape)
    }
    if ($slide -ne $null) {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($slide)
    }
    if ($presentation -ne $null) {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($presentation)
    }
    if ($pp -ne $null) {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($pp)
    }

    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}

$result | ConvertTo-Json -Depth 5 | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Smoke report: $reportPath"

if (-not $result.officeAvailable) {
    Write-Warning "PowerPoint COM not available."
    exit 2
}

if ($result.insertPictureOk -and $result.tagRoundTripOk -and $result.savedOk) {
    Write-Host "Smoke test passed."
    exit 0
}

Write-Error "Smoke test failed. See $reportPath"
exit 1


