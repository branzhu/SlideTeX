# KaTeX Vendor Directory

此目录用于放置 KaTeX 离线资源，版本固定：`0.16.11`。

预期文件：
- `katex.min.js`
- `katex.min.css`
- `fonts/*`

请运行仓库脚本：

```powershell
pwsh ./scripts/Sync-KaTeX.ps1 -Component katex -Version 0.16.11
```

如果网络环境无法访问 npm，可先手动下载 `katex-0.16.11.tgz`，再执行：

```powershell
pwsh ./scripts/Sync-KaTeX.ps1 -Component katex -Version 0.16.11 -ArchivePath C:\path\katex-0.16.11.tgz
```
