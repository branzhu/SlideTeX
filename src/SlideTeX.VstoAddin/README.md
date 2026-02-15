# SlideTeX.VstoAddin

`SlideTeX.VstoAddin` 已基于 Visual Studio VSTO PowerPoint 模板落地为 `.NET Framework 4.8` 项目骨架。

## 当前内容
- `ThisAddIn.cs` / `ThisAddIn.Designer.cs` / `ThisAddIn.Designer.xml`
- `SlideTeXRibbon.cs`（Ribbon 入口：打开面板/插入公式/编辑选中）
- `SlideTeXAddinController.cs`（TaskPane 生命周期、插入/更新/编辑选中流程编排）
- `Hosting/*`（TaskPane 宿主与 HostObject）
- `PowerPoint/*`（插入上下文、Shape 服务、Tag 访问器）
- `Metadata/*`（元数据 DTO、Tag 存取、内容哈希）
- `Properties` 下基础配置（AssemblyInfo / Resources / Settings）
- 项目可在仓库内通过 `dotnet build` 编译出 `SlideTeX.VstoAddin.dll`

## 重要说明
- 项目采用统一策略：默认生成 VSTO Manifest（`.vsto/.manifest`），不再按构建运行时区分“跳过模式”。
- 发布链路建议使用 Full MSBuild（Visual Studio/MSBuild.exe）执行构建与签名。
- 默认未绑定 PowerPoint PIA 文件时，项目会使用 `InteropStubs/PowerPointInteropStubs.cs` 作为编译兜底。
- 真机部署前建议设置真实 PIA：
  - 环境变量 `SLIDETEX_POWERPOINT_INTEROP_DLL=<Microsoft.Office.Interop.PowerPoint.dll 路径>`

## 与安装器约定
- `scripts/Build-Installer.ps1` 默认按 `SlideTeX.VstoAddin.vsto` 写入注册表 Manifest。
- 安装构建时会将 VSTO 输出目录产物（含 `.vsto/.manifest`）同步到 MSI 打包暂存目录。
- 安装构建支持 `LegacyAddinProgId` 参数（默认 `SlideTeX.VstoAddin`），用于升级时清理旧注册键。

## 公式 OCR 模型约定
- Host 侧 OCR 采用 ONNX Runtime（NuGet: `Microsoft.ML.OnnxRuntime`）。
- 默认模型目录：`OcrModels/pix2text-mfr`（运行目录相对路径）。
- 可用环境变量覆盖模型目录：`SLIDETEX_OCR_MODEL_DIR`。
- 需至少包含：
  - `encoder_model.onnx`
  - `decoder_model.onnx`（兼容旧文件名 `decoder_model_merged_quantized.onnx`）
  - `tokenizer.json`
  - `generation_config.json`
  - `MODEL_MANIFEST.json`（可选，未提供时采用内置默认配置）
- 模型文件建议通过脚本同步（默认不提交 git）：
  - `pwsh ./scripts/Sync-MathJax.ps1 -Component pix2text-mfr -Pix2TextModelId "breezedeus/pix2text-mfr-1.5"`
