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

## 2. Local TaskPane/WebView2 Debugging (Browser Mock Host)
Open `src/SlideTeX.WebUI/index.html` directly in a browser. The `mock-host.js` script auto-activates when no WebView2 COM bridge is detected, providing console-logged stubs for all 8 host methods.

Manual debug flow:
1. Open `src/SlideTeX.WebUI/index.html` in Chrome/Edge.
2. Open DevTools console — you should see `[mock-host] Mock host active (browser mode)`.
3. Enter formula text in the LaTeX box; preview renders via MathJax.
4. Click "Insert" — console logs `[mock-host] requestInsert` with the payload.
5. Click "OCR Image" — after 500 ms the mock returns `\frac{a}{b}` via `onFormulaOcrSuccess`.

Note: `mock-host.js` is a no-op when running inside the real VSTO WebView2 host or when the test harness (`test-main-flow.mjs`) injects its own mock first.

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
