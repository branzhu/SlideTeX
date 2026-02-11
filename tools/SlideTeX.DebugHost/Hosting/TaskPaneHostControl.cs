// SlideTeX Note: WebView2 container that bridges debug host commands and web UI events.

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Windows.Forms;
using SlideTeX.DebugHost.Contracts;

namespace SlideTeX.DebugHost.Hosting;

/// <summary>
/// DebugHost wrapper around WebView2 used to test host-bridge behavior outside Office.
/// </summary>
public sealed class TaskPaneHostControl : UserControl
{
    private readonly Label _fallbackLabel;
    private readonly SlideTeXHostObject _hostObject;
    private WebView2? _webView;

    public TaskPaneHostControl()
    {
        _hostObject = new SlideTeXHostObject();
        _hostObject.RenderNotificationReceived += (_, e) => RenderNotificationReceived?.Invoke(this, e);
        _hostObject.InsertRequested += (_, _) => InsertRequested?.Invoke(this, EventArgs.Empty);
        _hostObject.UpdateRequested += (_, _) => UpdateRequested?.Invoke(this, EventArgs.Empty);
        _hostObject.CommandRequested += (_, e) => CommandRequested?.Invoke(this, e);

        Dock = DockStyle.Fill;

        _fallbackLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            Text = "WebView2 未初始化。"
        };

        Controls.Add(_fallbackLabel);
    }

    public event EventHandler<RenderNotificationEventArgs>? RenderNotificationReceived;
    public event EventHandler? InsertRequested;
    public event EventHandler? UpdateRequested;
    public event EventHandler<HostCommandRequestedEventArgs>? CommandRequested;
    public string? LastInitializationError { get; private set; }

    public bool IsWebViewReady => _webView is not null;

    /// <summary>
    /// Initializes WebView, registers host object bridge, and navigates to local WebUI page.
    /// </summary>
    public async Task InitializeAsync(string pagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pagePath))
        {
            throw new ArgumentException("pagePath is required.", nameof(pagePath));
        }
        LastInitializationError = null;

        if (!File.Exists(pagePath))
        {
            SetFallbackMessage($"未找到 WebUI 页面: {pagePath}");
            LastInitializationError = "WebUI page not found";
            return;
        }

        try
        {
            _webView = new WebView2
            {
                Dock = DockStyle.Fill
            };

            Controls.Clear();
            Controls.Add(_webView);

            cancellationToken.ThrowIfCancellationRequested();
            var userDataFolder = ResolveUserDataFolder();
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder,
                options: null);

            await _webView.EnsureCoreWebView2Async(environment);

            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;

            _webView.CoreWebView2.AddHostObjectToScript("slidetexHost", _hostObject);

            var uri = new Uri(Path.GetFullPath(pagePath));
            _webView.Source = uri;
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            _webView = null;
            SetFallbackMessage("未检测到 WebView2 Runtime。请安装后重启宿主程序。");
            LastInitializationError = ex.Message;
        }
        catch (UnauthorizedAccessException ex)
        {
            _webView = null;
            SetFallbackMessage("WebView2 初始化失败: 用户数据目录无写权限。请检查当前账户对 LocalAppData 的访问权限。");
            LastInitializationError = ex.Message;
        }
        catch (Exception ex)
        {
            _webView = null;
            if (ex.HResult == unchecked((int)0x80070005))
            {
                SetFallbackMessage("WebView2 初始化失败: 拒绝访问（0x80070005）。请检查当前账户对 LocalAppData 的访问权限。");
            }
            else
            {
                SetFallbackMessage($"WebView2 初始化失败: {ex.Message}");
            }
            LastInitializationError = ex.Message;
        }
    }

    /// <summary>
    /// Shows a fallback label when runtime initialization fails.
    /// </summary>
    private void SetFallbackMessage(string message)
    {
        Controls.Clear();
        _fallbackLabel.Text = message;
        Controls.Add(_fallbackLabel);
    }

    /// <summary>
    /// Executes JavaScript on the active page if the WebView runtime is available.
    /// </summary>
    public async Task ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        if (_webView is null)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private static string ResolveUserDataFolder()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        return Path.Combine(localAppData, "SlideTeX", "WebView2", "DebugHost");
    }
}
