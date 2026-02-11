// SlideTeX Note: Desktop form that hosts the debug task pane and manual test controls.

using System.Web.Script.Serialization;
using SlideTeX.DebugHost.Hosting;

namespace SlideTeX.DebugHost;

/// <summary>
/// Simple desktop harness for manual verification of host-WebUI interaction paths.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly JavaScriptSerializer JsonSerializer = new JavaScriptSerializer();
    private readonly TaskPaneHostControl _hostControl;
    private readonly TextBox _latexBox;
    private readonly TextBox _logBox;
    private readonly Button _initializeButton;
    private readonly Button _renderButton;

    public MainForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "SlideTeX Debug Host";
        Width = 1280;
        Height = 800;

        _hostControl = new TaskPaneHostControl
        {
            Dock = DockStyle.Fill
        };

        _latexBox = new TextBox
        {
            Dock = DockStyle.Top,
            Multiline = true,
            Height = 100,
            Text = "\\frac{a}{b}"
        };

        _initializeButton = new Button
        {
            Text = "初始化 WebUI",
            Dock = DockStyle.Top,
            Height = 32
        };
        _initializeButton.Click += async (_, _) => await InitializeWebUiAsync();

        _renderButton = new Button
        {
            Text = "Host 触发渲染",
            Dock = DockStyle.Top,
            Height = 32
        };
        _renderButton.Click += async (_, _) => await TriggerRenderFromHostAsync();

        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            ReadOnly = true
        };

        var side = new Panel
        {
            Dock = DockStyle.Right,
            Width = 420,
            Padding = new Padding(8)
        };
        side.Controls.Add(_logBox);
        side.Controls.Add(_renderButton);
        side.Controls.Add(_initializeButton);
        side.Controls.Add(_latexBox);

        Controls.Add(_hostControl);
        Controls.Add(side);

        _hostControl.RenderNotificationReceived += (_, args) =>
        {
            AppendLog($"Render: success={args.IsSuccess}, error={args.ErrorMessage}");
            if (!string.IsNullOrWhiteSpace(args.Payload))
            {
                AppendLog(args.Payload!);
            }
        };
        _hostControl.InsertRequested += (_, _) => AppendLog("WebUI 请求插入 requestInsert()");
        _hostControl.UpdateRequested += (_, _) => AppendLog("WebUI 请求更新 requestUpdate()");
        _hostControl.CommandRequested += (_, args) =>
            AppendLog($"Host 命令: {args.Payload.CommandType}, arg={args.Payload.Argument}");
    }

    /// <summary>
    /// Loads the local WebUI page into the debug host and reports readiness state.
    /// </summary>
    private async Task InitializeWebUiAsync()
    {
        try
        {
            var pagePath = Path.Combine(AppContext.BaseDirectory, "WebUI", "index.html");
            if (!File.Exists(pagePath))
            {
                AppendLog($"未找到页面: {pagePath}");
                return;
            }

            await _hostControl.InitializeAsync(pagePath);
            if (_hostControl.IsWebViewReady)
            {
                AppendLog("WebUI 初始化完成。");
            }
            else
            {
                AppendLog($"WebUI 初始化失败: {_hostControl.LastInitializationError ?? "未知错误"}");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"初始化失败: {ex.Message}");
        }
    }

    /// <summary>
    /// Triggers a host-initiated render request using the current textbox formula.
    /// </summary>
    private async Task TriggerRenderFromHostAsync()
    {
        if (!_hostControl.IsWebViewReady)
        {
            AppendLog("WebView2 未就绪，请先初始化。若仍失败请安装 WebView2 Runtime。");
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            latex = _latexBox.Text,
            options = new
            {
                fontPt = 24,
                dpi = 300,
                colorHex = "#000000",
                isTransparent = true,
                displayMode = "auto",
                toleranceMode = "strict",
                strictMode = true
            }
        });

        var script = $"window.slideTex && window.slideTex.renderFromHost({payload});";
        await _hostControl.ExecuteScriptAsync(script);
        AppendLog("已发送 renderFromHost() 请求。");
    }

    /// <summary>
    /// Appends timestamped diagnostics into the right-side log panel.
    /// </summary>
    private void AppendLog(string message)
    {
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}
