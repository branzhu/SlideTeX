# SlideTeX

离线 PowerPoint 公式插件 — 渲染、插入、回编辑、OCR 识别，全程无需联网。

语言：[English](README.md) | **简体中文**

## 截图

![SlideTeX WebUI（中文）](docs/images/webui-zh-cn.png)

## 功能

- **LaTeX 任务窗格** — 在 CodeMirror 6 编辑器中编写 LaTeX，支持语法高亮和自动补全，实时预览后一键插入或更新幻灯片上的公式。
- **嵌入式 PNG 输出** — 公式以 PNG 图片形式插入，未安装插件的电脑也能正常显示。
- **回填编辑** — LaTeX 源码和渲染参数保存在 Shape 元数据中，选中公式即可重新编辑并原位更新。
- **MathJax 渲染** — 无需本地安装 LaTeX。渲染链路为 MathJax → SVG → PNG，通过 WebView2 在进程内完成。
- **公式 OCR** — 使用内置 ONNX 模型（pix2text-mfr）识别图片中的公式，将截图或照片转换为可编辑的 LaTeX。
- **自动编号** — `equation`、`align`、`gather` 环境在整个演示文稿中自动编号。
- **完全离线** — 渲染、OCR、UI 资源全部本地化，WebView2 对外部主机的网络请求在运行时被拦截。

## 架构

```
SlideTeX.sln
├── src/
│   ├── SlideTeX.VstoAddin/      # VSTO 插件 (net48) — C# 运行时
│   │   ├── Contracts/            # 宿主 ↔ WebUI 桥接接口
│   │   ├── Hosting/              # WebView2 任务窗格与宿主对象
│   │   ├── Metadata/             # Shape 标签持久化
│   │   ├── PowerPoint/           # Shape 插入、编号、幻灯片服务
│   │   └── Ocr/                  # ONNX Runtime 公式识别
│   ├── SlideTeX.WebUI/           # 任务窗格 UI (HTML/CSS/JS)
│   │   ├── assets/               # CodeMirror 打包、i18n、样式
│   │   └── vendor/               # MathJax、CodeMirror 第三方构建
│   └── SlideTeX.Installer/       # WiX 安装器 (MSI + EXE)
├── tests/                        # 单元、集成、回归、OCR 基线测试
├── scripts/                      # 构建、打包、资源同步脚本
└── docs/                         # 调试、部署、回归说明文档
```

## 环境要求

### 运行

- Windows 10/11
- Microsoft 365 PowerPoint（桌面版）。理论上支持 2016/2019/2021/LTSC，未经完整验证。
- .NET Framework 4.8 Runtime
- WebView2 Runtime

### 构建

- Node.js
- .NET Framework 4.8 Developer Pack
- Visual Studio 2022（或随附 MSBuild）
- WiX Toolset 6.0.2

```powershell
wix extension add -g WixToolset.BootstrapperApplications.wixext/6.0.2
```

## 快速开始

1. 同步第三方资源：

```powershell
# MathJax
pwsh ./scripts/Sync-VendorAssets.ps1 -Version 4.1.0

# OCR 模型（从 Hugging Face 下载）
pwsh ./scripts/Sync-VendorAssets.ps1 -Component pix2text-mfr -Pix2TextModelId "breezedeus/pix2text-mfr-1.5"

# 或一次同步全部
pwsh ./scripts/Sync-VendorAssets.ps1 -Component all
```

2. 编辑 `src/SlideTeX.WebUI/assets/i18n/*.json` 后生成 i18n 内联资源：

```powershell
node ./scripts/generate-webui-i18n-bundle.mjs
```

3. 构建：

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" SlideTeX.sln /m:1
```

4. 在浏览器中预览 WebUI：

直接打开 `src/SlideTeX.WebUI/index.html`，`mock-host.js` 会为所有宿主桥接方法提供 stub，调用信息输出到控制台。

## 构建与测试

### 安装包

```powershell
pwsh ./scripts/Build-Installer.ps1 -Configuration Release -Platform x64
```

同时构建 `zh-CN` 与 `en-US` 时，脚本会生成语言拆分的 MSI 以及一个统一的 `.exe` 安装包（默认按系统语言选择）。安装时可覆盖语言：

```powershell
.\SlideTeX-<version>-Release-x64.exe SlideTeXInstallerCulture=en-US
.\SlideTeX-<version>-Release-x64.exe SlideTeXInstallerCulture=zh-CN
```

覆盖 VSTO 清单签名证书指纹（CI 场景）：

```powershell
pwsh ./scripts/Build-Installer.ps1 -Configuration Release -Platform x64 -VstoManifestCertificateThumbprint "<THUMBPRINT>"
```

### 测试

| 类别 | 命令 |
| --- | --- |
| 单元测试（纯函数） | `node tests/test-app-logic.mjs` / `test-i18n.mjs` / `test-ocr-latex-postprocess.mjs` |
| WebUI 集成测试（Puppeteer） | `node tests/test-main-flow.mjs` |
| WebView2 桥接集成测试 | `node tests/test-webview2-flow.mjs` |
| PowerPoint 烟雾测试 | `pwsh ./tests/Invoke-PowerPointSmoke.ps1` |
| 编号回归 | `pwsh ./tests/Test-EquationNumberingKnownGood.ps1 -Configuration Debug` |
| 渲染 known-good（快速/完整） | `node tests/render-known-good.mjs --mode verify --suite smoke\|full` |
| OCR 基线（快速/完整） | `pwsh ./tests/Test-OcrBaseline.ps1 -Configuration Debug -Suite smoke -ModelDir "C:\models\pix2text-mfr"` |
| MSI 生命周期 | `pwsh ./tests/Test-MsiLifecycle.ps1 -OldMsi <path> -NewMsi <path>` |

## CI/CD

`.github/workflows/ci-build.yml` 在 push/PR 时构建安装产物并上传为 CI artifact。工作流会自动生成临时代码签名证书，并把指纹传入 `Build-Installer.ps1`。

## 文档

- [调试指南](docs/DEBUG_GUIDE.md)
- [管理员部署](docs/ADMIN_DEPLOYMENT.md)
- [回归测试说明](docs/REGRESSION_TESTS.md)

## 当前限制

- 仅支持 Windows PowerPoint 桌面版。
- 公式能力取决于 MathJax 的 TeX 支持范围，不覆盖完整 TeX/LaTeX 生态。
- 仓库不包含生产签名与发布流程。

## 第三方组件

| 组件 | 版本 | 协议 | 用途 |
| --- | --- | --- | --- |
| MathJax | 4.1.0 | Apache-2.0 | 公式渲染（vendored 到 `src/SlideTeX.WebUI/vendor/mathjax`） |
| CodeMirror | 6.0.2 | MIT | 编辑器核心（打包到 `src/SlideTeX.WebUI/assets/js/editor-adapter.js`） |
| @codemirror/legacy-modes | 6.5.2 | MIT | 编辑器语法模式（构建时打包） |
| pix2text-mfr | 1.5 | MIT | 公式 OCR 模型（通过 `scripts/Sync-VendorAssets.ps1` 同步） |
| Microsoft.ML.OnnxRuntime | 1.22.0 | MIT | 公式 OCR 推理（NuGet） |
| pixelmatch | 7.1.0 | ISC | 渲染回归图像 diff（测试） |
| pngjs | 7.0.0 | MIT | PNG 读写（测试） |
| puppeteer-core | 24.31.0 | Apache-2.0 | 浏览器自动化（测试） |
| rollup | 4.57.1 | MIT | CodeMirror 构建打包 |
| @rollup/plugin-node-resolve | 16.0.3 | MIT | Rollup 依赖解析 |

OCR 模型二进制（`src/SlideTeX.VstoAddin/Assets/OcrModels`）默认不提交 git，通过 `scripts/Sync-VendorAssets.ps1` 同步。

## 开源协议

MIT — 详见 [LICENSE](LICENSE)。
