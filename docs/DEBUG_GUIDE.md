# Debug Guide

## 1. Build and Baseline Checks
Generate WebUI i18n bundle and build the solution:
```powershell
node ./scripts/generate-webui-i18n-bundle.mjs
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" SlideTeX.sln /m:1
```

Run regression checks:
```powershell
pwsh ./scripts/Test-EquationNumberingTransform.ps1 -Configuration Debug
pwsh ./scripts/Test-EquationNumberingKnownGood.ps1 -Configuration Debug
powershell -ExecutionPolicy Bypass -File scripts/Test-RenderKnownGood.ps1 -Mode verify -Suite smoke
```

## 2. Local TaskPane/WebView2 Debugging (Debug Host)
If WebView2 SDK assemblies are missing during build, set:
```powershell
$env:WEBVIEW2_SDK_DIR="C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\Extensions\Microsoft\Web Live Preview"
```

Start DebugHost:
```powershell
node ./scripts/generate-webui-i18n-bundle.mjs
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" tools\SlideTeX.DebugHost\SlideTeX.DebugHost.csproj /p:Configuration=Debug /m:1
.\tools\SlideTeX.DebugHost\bin\Debug\net48\SlideTeX.DebugHost.exe
```

Manual debug flow:
1. Click the `Initialize WebUI` button in the DebugHost window.
2. Enter formula text in the LaTeX box.
3. Click the `Host Render` button.
4. Inspect callbacks in the right-side log (`notifyRenderSuccess/notifyRenderError`).
5. `auto` mode switches to display when needed (for example `align/align*` or multi-line `\\`).

If WebView2 is still not ready, install WebView2 Runtime.

## 3. PowerPoint COM Smoke Test
```powershell
pwsh ./scripts/Invoke-PowerPointSmoke.ps1
```

- Report output: `artifacts/smoke/smoke-report.json`
- Generated sample: `artifacts/smoke/slidetex-smoke.pptx`

Exit codes:
- `0`: pass
- `1`: fail
- `2`: PowerPoint COM unavailable (Office not installed or inaccessible)

## 4. Post-install Add-in Registration Check
```powershell
pwsh ./scripts/Test-OfficeAddinRegistration.ps1 -ProgId SlideTeX
```

If `ValidCount=0`, verify that the manifest path exists and is accessible.

## 5. Manual Local Registration (Debug)
```powershell
pwsh ./scripts/Set-OfficeAddinRegistration.ps1 `
  -Mode Install `
  -ProgId SlideTeX `
  -ManifestPath "C:\Program Files\SlideTeX\Addin\SlideTeX.VstoAddin.vsto" `
  -RegisterWow6432Node
```

Unregister:
```powershell
pwsh ./scripts/Set-OfficeAddinRegistration.ps1 -Mode Uninstall -ProgId SlideTeX -RegisterWow6432Node
```

## 6. VSTO Shell Build
Use full MSBuild to generate `.vsto/.manifest`:
```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
  src/SlideTeX.VstoAddin/SlideTeX.VstoAddin.csproj `
  /p:Configuration=Debug `
  /m:1
```

## 7. VSTO Diagnostic Logs (Startup/Pane Open Latency)
- Log path: `%LOCALAPPDATA%\SlideTeX\logs\vsto-YYYYMMDD.log`
- Key checkpoints:
  - `ThisAddIn_Startup`
  - `Controller.OpenPane`
  - `EnsureWebViewInitialized`
  - `TaskPaneHostControl.InitializeAsync`
  - `CoreWebView2Environment.CreateAsync`
  - `EnsureCoreWebView2Async`

Tune sequential initialization timeout (default `15000ms`):
```powershell
$env:SLIDETEX_WEBVIEW2_INIT_TIMEOUT_MS="30000"
```
