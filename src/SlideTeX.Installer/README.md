# SlideTeX Installer

此目录提供 WiX MSI 脚手架（已在 WiX `6.0.2` 验证通过）。

## 文件说明
- `wix/Product.wxs`: 安装包主定义（产品、升级策略、目录、Feature）
- `wix/Fragments/Files.wxs`: 主程序与 WebUI 文件组件示例
- `wix/Fragments/GeneratedFiles.wxs`: 由脚本自动生成的完整文件清单
- `wix/Fragments/OfficeRegistration.wxs`: PowerPoint Add-in 注册表项（HKLM + WOW6432Node）
- `wix/Localization/*.wxl`: 安装包本地化资源（当前内置 `zh-CN`、`en-US`）
- `scripts/Build-Installer.ps1`: 构建 MSI 的入口脚本（会将 Addin 输出复制到暂存目录，并同步 VSTO 产物）
- `scripts/generate-webui-i18n-bundle.mjs`: 将 `assets/i18n/*.json` 生成到 `index.html` 内联 i18n bundle
- `scripts/Generate-WixFragment.ps1`: 基于 Addin 输出目录生成 WiX Fragment
- `scripts/Test-MsiLifecycle.ps1`: 安装/升级/卸载链路验证脚本
- `scripts/Set-OfficeAddinRegistration.ps1`: 检查/写入/删除 Office 加载项注册（含 `-Mode Validate` 验证模式）

## 依赖
- WiX Toolset (`wix` CLI，推荐 `6.0.2`)
- Node.js（非 `-SkipBuild` 模式下，`Build-Installer.ps1` 会先执行 WebUI i18n 内联生成）
- 已构建的 Addin 输出目录
- 已构建的 VSTO 输出目录（默认优先 `src/SlideTeX.VstoAddin/bin/<Configuration>`，缺失时回退 `bin/Debug`）

## 约束
- 当前已补充 Add-in 注册表项；默认会校验并打包 `.vsto/.manifest`，仍依赖真实签名策略。

## 常用命令
```powershell
# 默认：先构建 Addin，再自动生成 GeneratedFiles.wxs，并为 zh-CN/en-US 生成 MSI
pwsh ./scripts/Build-Installer.ps1 -Configuration Release -Platform x64

# 指定 MSI 版本号（用于升级回归）
pwsh ./scripts/Build-Installer.ps1 -Configuration Release -Platform x64 -ProductVersion 1.0.1

# 只构建英文安装包（输出文件名不带 culture 后缀）
pwsh ./scripts/Build-Installer.ps1 -Configuration Release -Platform x64 -Cultures en-US

# 生成后立即检查本机 Office 加载项注册状态（要求已安装）
pwsh ./scripts/Build-Installer.ps1 -Configuration Release -Platform x64 -VerifyOfficeRegistration

# 仅更新 WiX 文件清单
pwsh ./scripts/Generate-WixFragment.ps1 -BuildOutputDir src/SlideTeX.Addin/bin/Debug/net8.0-windows

# 指定 VSTO 输出目录（例如 Release 目录）
pwsh ./scripts/Build-Installer.ps1 -Configuration Release -Platform x64 -VstoBuildOutputDir src/SlideTeX.VstoAddin/bin/Release

# 验证旧版->新版升级，再卸载
pwsh ./scripts/Test-MsiLifecycle.ps1 -OldMsi .\artifacts\installer\SlideTeX-1.0.0-Release-x64.msi -NewMsi .\artifacts\installer\SlideTeX-1.0.1-Release-x64.msi

# 单独检查注册表与 Manifest
pwsh ./scripts/Set-OfficeAddinRegistration.ps1 -Mode Validate -ProgId SlideTeX

# 本机手动注册（调试）
pwsh ./scripts/Set-OfficeAddinRegistration.ps1 -Mode Install -ProgId SlideTeX -ManifestPath "C:\Program Files\SlideTeX\Addin\SlideTeX.VstoAddin.vsto" -RegisterWow6432Node
```
