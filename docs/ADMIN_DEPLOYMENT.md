# Admin Deployment Guide

## 1. Prerequisites
- Windows 10/11
- Microsoft 365 PowerPoint (desktop)
- WebView2 Runtime (Evergreen recommended)
- VSTO Runtime

## 2. Silent Install
```powershell
.\SlideTeX-1.0.0-Release-x64.exe /quiet /norestart /log C:\Temp\SlideTeX-install.log
```

Force installer language (optional):
```powershell
.\SlideTeX-1.0.0-Release-x64.exe SlideTeXInstallerCulture=en-US /quiet /norestart
.\SlideTeX-1.0.0-Release-x64.exe SlideTeXInstallerCulture=zh-CN /quiet /norestart
```

Install a specific language MSI directly (optional):
```powershell
msiexec /i SlideTeX-1.0.0-Release-x64-en-US.msi /qn /l*v C:\Temp\SlideTeX-install.log
```

Recommended MSI build command:
```powershell
pwsh ./scripts/Build-Installer.ps1 -Configuration Release -Platform x64
```

Notes:
- The script calls `MSBuild.exe` to build `SlideTeX.VstoAddin`.
- It generates/signs `.vsto/.manifest` by default.
- If the signing certificate is unavailable, packaging fails immediately.

Build with an explicit product version (for upgrade validation):
```powershell
pwsh ./scripts/Build-Installer.ps1 -Configuration Release -Platform x64 -ProductVersion 1.0.1
```

Override legacy ProgId cleanup (to avoid duplicate SlideTeX ribbon entries):
```powershell
pwsh ./scripts/Build-Installer.ps1 `
  -Configuration Release `
  -Platform x64 `
  -LegacyAddinProgId SlideTeX.VstoAddin
```

## 3. Silent Uninstall
```powershell
msiexec /x {PRODUCT-CODE} /qn /l*v C:\Temp\SlideTeX-uninstall.log
```

## 4. Upgrade Strategy
- Use MSI Major Upgrade.
- Keep `UpgradeCode` unchanged and increment `ProductVersion`.
- Existing PPT files are not impacted by upgrade because equations are embedded as PNG.

## 5. Rollback Recommendations
- Keep at least the latest two stable MSI packages.
- Configure automatic rollback in your software distribution platform.

## 5.1 Upgrade Path Validation Script
```powershell
pwsh ./scripts/Test-MsiLifecycle.ps1 `
  -OldMsi .\artifacts\installer\SlideTeX-1.0.0-Release-x64-en-US.msi `
  -NewMsi .\artifacts\installer\SlideTeX-1.0.1-Release-x64-en-US.msi
```

## 6. Troubleshooting
- Missing WebView2: verify `edgewebview2` installation.
- No PowerPoint entry point: check whether the add-in is disabled in Office.
- Render failures: verify KaTeX resources are present under `vendor/katex`.
- Duplicate SlideTeX ribbon items: usually caused by legacy ProgId (`SlideTeX.VstoAddin`) leftovers. New MSI upgrades should remove them automatically. Manual cleanup keys:
  - `HKLM\SOFTWARE\Microsoft\Office\PowerPoint\Addins\SlideTeX.VstoAddin`
  - `HKLM\SOFTWARE\WOW6432Node\Microsoft\Office\PowerPoint\Addins\SlideTeX.VstoAddin`

## 7. Office Add-in Registration Check
Run after installation:
```powershell
pwsh ./scripts/Test-OfficeAddinRegistration.ps1 -ProgId SlideTeX
```

- Report output: `artifacts/installer/office-addin-report.json`
- If registration is missing, check:
  - `HKLM\SOFTWARE\Microsoft\Office\PowerPoint\Addins\SlideTeX`
  - `HKLM\SOFTWARE\WOW6432Node\Microsoft\Office\PowerPoint\Addins\SlideTeX`

## 7.1 Manual Registration Repair (Admin)
```powershell
pwsh ./scripts/Set-OfficeAddinRegistration.ps1 `
  -Mode Install `
  -ProgId SlideTeX `
  -ManifestPath "C:\Program Files\SlideTeX\Addin\SlideTeX.VstoAddin.vsto" `
  -RegisterWow6432Node
```
