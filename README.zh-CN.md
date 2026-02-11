# SlideTeX

一个PowerPoint公式插件，将LaTeX公式渲染为 PNG 图片插入PowerPoint。

`状态`：原型 / 持续开发中（非生产可用）

语言： [English](README.md) | **简体中文**

## 功能概览

- 在PowerPoint任务窗格中输入 LaTeX、预览、插入、更新。
- 公式以 PNG 形式嵌入到 PPT，在未安装插件的电脑上也可以显示。
- 通过Shape元数据保存公式信息，支持回填编辑与更新。
- 采用 KaTeX 作为渲染核心，速度快，不需要本地安装 LaTeX；同时只支持 KaTeX 支持的 LaTeX 子集。
- 采用CodeMirror 6作为编辑器，支持语法高亮和补全。

## 截图

![SlideTeX WebUI（中文）](docs/images/webui-zh-cn.png)

## 仓库结构

- `src/SlideTeX.VstoAddin`: VSTO 插件运行时代码。
- `src/SlideTeX.WebUI`: 任务窗格 HTML/CSS/JS 与静态资源。
- `src/SlideTeX.Installer`: WiX 安装器项目。
- `tools/SlideTeX.DebugHost`: 本地调试宿主（用于 WebUI/Host 桥接调试）。
- `scripts`: 构建、打包、烟雾测试、回归脚本。
- `tests/render-regression`: 渲染视觉回归基线与用例。
- `tests/equation-numbering`: 编号回归 known-good 用例。
- `docs`: 调试、部署、回归说明文档。

## 环境要求

### 运行
- Windows 10/11
- Microsoft 365 PowerPoint（桌面版）。理论上支持 2016/2019/2021/LTSC，未经测试。
- .NET Framework `4.8` Runtime
- WebView2 Runtime

### 构建
- Node.js
- .NET Framework `4.8` Developer Pack
- Visual Studio 2022（或随附 MSBuild）
- WiX Toolset `6.0.2`

## 快速开始

1. 同步 KaTeX 资源：

```powershell
pwsh ./scripts/Sync-KaTeX.ps1 -Version 0.16.11
```

2. 当 `src/SlideTeX.WebUI/assets/i18n/*.json` 变更后，生成 i18n 内联资源：

```powershell
node ./scripts/generate-webui-i18n-bundle.mjs
```

3. 构建解决方案：

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" SlideTeX.sln /m:1
```

4. 启动调试宿主：

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" tools\SlideTeX.DebugHost\SlideTeX.DebugHost.csproj /p:Configuration=Debug /m:1
.\tools\SlideTeX.DebugHost\bin\Debug\net48\SlideTeX.DebugHost.exe
```

## 常用构建与测试命令

- 构建安装包：

```powershell
pwsh ./scripts/Build-Installer.ps1 -Configuration Release -Platform x64
```

- PowerPoint 烟雾测试：

```powershell
pwsh ./scripts/Invoke-PowerPointSmoke.ps1
```

- 编号转换回归：

```powershell
pwsh ./scripts/Test-EquationNumberingTransform.ps1 -Configuration Debug
```

- 编号 known-good 回归：

```powershell
pwsh ./scripts/Test-EquationNumberingKnownGood.ps1 -Configuration Debug
```

- 渲染 known-good（快速/完整）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-RenderKnownGood.ps1 -Mode verify -Suite smoke
powershell -ExecutionPolicy Bypass -File scripts/Test-RenderKnownGood.ps1 -Mode verify -Suite full
```

## 部署与文档

- 调试指南：`docs/DEBUG_GUIDE.md`
- 管理员部署：`docs/ADMIN_DEPLOYMENT.md`
- 回归测试说明：`docs/REGRESSION_TESTS.md`

## 当前限制

- 仅支持 Windows PowerPoint。
- 仅保证 KaTeX 子集，不覆盖完整 TeX/LaTeX 生态。
- 仓库不包含完整生产证书签名链路。

## 第三方组件说明

以下为项目当前使用到的主要第三方组件（含版本与协议）：

| 组件 | 版本 | 协议 | 用途 |
| --- | --- | --- | --- |
| KaTeX | `0.16.11` | MIT | 公式渲染（运行时，vendored 到 `src/SlideTeX.WebUI/vendor/katex`） |
| html2canvas | `1.4.1` | MIT | DOM 转图像（运行时，vendored 到 `src/SlideTeX.WebUI/vendor/html2canvas.min.js`） |
| CodeMirror | `6.0.2` | MIT | 编辑器核心（运行时，打包到 `src/SlideTeX.WebUI/assets/js/editor-adapter.js`） |
| @codemirror/legacy-modes | `6.5.2` | MIT | 编辑器语法模式（构建时打包） |
| pixelmatch | `7.1.0` | ISC | 渲染回归图像 diff（测试） |
| pngjs | `7.0.0` | MIT | PNG 读写（测试） |
| puppeteer-core | `24.31.0` | Apache-2.0 | 浏览器自动化（渲染回归测试） |
| rollup | `4.57.1` | MIT | CodeMirror 构建打包（开发时） |
| @rollup/plugin-node-resolve | `16.0.3` | MIT | Rollup 依赖解析（开发时） |

说明：
- 以上版本来自仓库固定脚本/配置（`package.json`、`src/SlideTeX.WebUI/vendor/codemirror/VERSIONS.md`）。
- 以上协议信息来自对应上游包元数据；完整法律文本请以各组件官方仓库与发行包为准。

## 开源协议

项目采用 MIT 协议，详见 `LICENSE`。
