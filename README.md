# SlideTeX

Offline PowerPoint add-in for LaTeX equations — render, insert, re-edit, and OCR, all without a network connection.

Language: **English** | [简体中文](README.zh-CN.md)

## Screenshot

<p align="center">
  <img src="docs/images/webui-en-us.png" alt="SlideTeX WebUI (English)" width="360">
</p>

## Features

- **LaTeX task pane** — Write LaTeX in a CodeMirror 6 editor with syntax highlighting and autocomplete, preview in real time, then insert or update equations on the slide.
- **Embedded PNG output** — Equations are inserted as PNG images, so presentations display correctly on any machine, with or without the add-in.
- **Round-trip editing** — LaTeX source and render settings are persisted in shape metadata; select an equation to re-edit and update in place.
- **MathJax rendering** — No local LaTeX installation required. The pipeline is MathJax → SVG → PNG, running entirely in-process via WebView2.
- **Formula OCR** — Recognize formulas from images using a bundled ONNX model (pix2text-mfr), converting screenshots or photos back to editable LaTeX.
- **Automatic equation numbering** — `equation`, `align`, and `gather` environments are numbered automatically across the presentation.
- **Fully offline** — All rendering, OCR, and UI assets are local. WebView2 network requests to external hosts are blocked at runtime.

## Architecture

```
SlideTeX.sln
├── src/
│   ├── SlideTeX.VstoAddin/      # VSTO add-in (net48) — C# runtime
│   │   ├── Contracts/            # Host ↔ WebUI bridge interfaces
│   │   ├── Hosting/              # WebView2 task pane and host object
│   │   ├── Metadata/             # Shape tag persistence
│   │   ├── PowerPoint/           # Shape insertion, numbering, slide services
│   │   └── Ocr/                  # ONNX Runtime formula recognition
│   ├── SlideTeX.WebUI/           # Task pane UI (HTML/CSS/JS)
│   │   ├── assets/               # CodeMirror bundle, i18n, styles
│   │   └── vendor/               # MathJax, CodeMirror vendored builds
│   └── SlideTeX.Installer/       # WiX-based MSI + EXE bundle
├── tests/                        # Unit, integration, regression, OCR baseline
├── scripts/                      # Build, packaging, asset sync
└── docs/                         # Debugging, deployment, regression guides
```

## Requirements

### Runtime

- Windows 10/11
- Microsoft 365 PowerPoint (desktop). PowerPoint 2016/2019/2021/LTSC should work but is not fully validated.
- .NET Framework 4.8 Runtime
- WebView2 Runtime

### Build

- Node.js
- .NET Framework 4.8 Developer Pack
- Visual Studio 2022 (or MSBuild from VS installation)
- WiX Toolset 6.0.2

```powershell
wix extension add -g WixToolset.BootstrapperApplications.wixext/6.0.2
```

## Quick Start

1. Sync third-party assets:

```powershell
# MathJax
pwsh ./scripts/Sync-VendorAssets.ps1 -Version 4.1.0

# OCR model (from Hugging Face)
pwsh ./scripts/Sync-VendorAssets.ps1 -Component pix2text-mfr -Pix2TextModelId "breezedeus/pix2text-mfr-1.5"

# Or sync everything at once
pwsh ./scripts/Sync-VendorAssets.ps1 -Component all
```

2. Generate inline i18n bundle (after editing `src/SlideTeX.WebUI/assets/i18n/*.json`):

```powershell
node ./scripts/generate-webui-i18n-bundle.mjs
```

3. Build:

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" SlideTeX.sln /m:1
```

4. Preview WebUI in browser:

Open `src/SlideTeX.WebUI/index.html` directly — `mock-host.js` stubs all host bridge methods and logs calls to the console.

## Build and Test

### Installer

```powershell
pwsh ./scripts/Build-Installer.ps1 -Configuration Release -Platform x64
```

When both `zh-CN` and `en-US` cultures are built, the script produces language-specific MSI payloads plus a unified `.exe` bundle that selects language from system locale. Override at install time:

```powershell
.\SlideTeX-<version>-Release-x64.exe SlideTeXInstallerCulture=en-US
.\SlideTeX-<version>-Release-x64.exe SlideTeXInstallerCulture=zh-CN
```

Override VSTO manifest certificate thumbprint (for CI):

```powershell
pwsh ./scripts/Build-Installer.ps1 -Configuration Release -Platform x64 -VstoManifestCertificateThumbprint "<THUMBPRINT>"
```

### Tests

| Category | Command |
| --- | --- |
| Unit tests (pure functions) | `node tests/test-app-logic.mjs` / `test-i18n.mjs` / `test-ocr-latex-postprocess.mjs` |
| WebUI integration (Puppeteer) | `node tests/test-main-flow.mjs` |
| WebView2 bridge integration | `node tests/test-webview2-flow.mjs` |
| PowerPoint smoke test | `pwsh ./tests/Invoke-PowerPointSmoke.ps1` |
| Equation numbering regression | `pwsh ./tests/Test-EquationNumberingKnownGood.ps1 -Configuration Debug` |
| Render known-good (smoke/full) | `node tests/render-known-good.mjs --mode verify --suite smoke\|full` |
| OCR baseline (smoke/full) | `pwsh ./tests/Test-OcrBaseline.ps1 -Configuration Debug -Suite smoke -ModelDir "C:\models\pix2text-mfr"` |
| MSI lifecycle | `pwsh ./tests/Test-MsiLifecycle.ps1 -OldMsi <path> -NewMsi <path>` |

## CI/CD

`.github/workflows/ci-build.yml` builds installer artifacts on push/PR and uploads them as CI artifacts. The workflow generates a temporary code-signing certificate and passes its thumbprint to `Build-Installer.ps1`.

## Docs

- [Debug Guide](docs/DEBUG_GUIDE.md)
- [Admin Deployment](docs/ADMIN_DEPLOYMENT.md)
- [Regression Tests](docs/REGRESSION_TESTS.md)

## Limitations

- Windows-only (PowerPoint desktop).
- Formula support is scoped to MathJax's TeX capabilities, not the full TeX/LaTeX ecosystem.
- Production signing and release process is not included.

## Third-Party Components

| Component | Version | License | Usage |
| --- | --- | --- | --- |
| MathJax | 4.1.0 | Apache-2.0 | Equation rendering (vendored in `src/SlideTeX.WebUI/vendor/mathjax`) |
| CodeMirror | 6.0.2 | MIT | Editor core (bundled in `src/SlideTeX.WebUI/assets/js/editor-adapter.js`) |
| @codemirror/legacy-modes | 6.5.2 | MIT | Editor language modes (bundled at build time) |
| pix2text-mfr | 1.5 | MIT | Formula OCR model (synced via `scripts/Sync-VendorAssets.ps1`) |
| Microsoft.ML.OnnxRuntime | 1.22.0 | MIT | Formula OCR inference (NuGet) |
| pixelmatch | 7.1.0 | ISC | Render regression image diff (test) |
| pngjs | 7.0.0 | MIT | PNG read/write (test) |
| puppeteer-core | 24.31.0 | Apache-2.0 | Browser automation (test) |
| rollup | 4.57.1 | MIT | CodeMirror bundle build |
| @rollup/plugin-node-resolve | 16.0.3 | MIT | Rollup dependency resolution |

OCR model binaries (`src/SlideTeX.VstoAddin/Assets/OcrModels`) are git-ignored — sync via `scripts/Sync-VendorAssets.ps1`.

## License

MIT — see [LICENSE](LICENSE).
