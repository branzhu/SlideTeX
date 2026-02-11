// SlideTeX Note: Task pane WebView2 host that wires browser events to add-in logic.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using SlideTeX.VstoAddin.Contracts;
using SlideTeX.VstoAddin.Diagnostics;
using SlideTeX.VstoAddin.Localization;

namespace SlideTeX.VstoAddin.Hosting
{
    /// <summary>
    /// Hosts the WebUI inside a task pane and bridges JavaScript callbacks into .NET events.
    /// </summary>
    public sealed class TaskPaneHostControl : UserControl
    {
        private const int DefaultInitializeTimeoutMs = 15000;
        private readonly Label _fallbackLabel;
        private readonly SlideTeXHostObject _hostObject;
        private WebView2 _webView;

        public TaskPaneHostControl()
        {
            _hostObject = new SlideTeXHostObject();
            _hostObject.RenderNotificationReceived += delegate(object sender, RenderNotificationEventArgs args)
            {
                var handler = RenderNotificationReceived;
                if (handler != null)
                {
                    handler(this, args);
                }
            };
            _hostObject.CommandRequested += delegate(object sender, HostCommandRequestedEventArgs args)
            {
                var handler = CommandRequested;
                if (handler != null)
                {
                    handler(this, args);
                }
            };

            Dock = DockStyle.Fill;
            _fallbackLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Text = LocalizationManager.Get("taskpane.webview_not_initialized")
            };

            Controls.Add(_fallbackLabel);
        }

        public event EventHandler<RenderNotificationEventArgs> RenderNotificationReceived;

        public event EventHandler<HostCommandRequestedEventArgs> CommandRequested;

        public string LastInitializationError { get; private set; }

        public bool IsWebViewReady
        {
            get { return _webView != null; }
        }

        /// <summary>
        /// Initializes WebView with default host culture behavior.
        /// </summary>
        public void Initialize(string pagePath)
        {
            Initialize(pagePath, null);
        }

        /// <summary>
        /// Initializes WebView synchronously while pumping UI messages to avoid deadlock.
        /// </summary>
        public void Initialize(string pagePath, string uiCultureName)
        {
            var stopwatch = Stopwatch.StartNew();
            DiagLog.Info("TaskPaneHostControl.Initialize(sync) begin. pagePath=" + pagePath);
            try
            {
                var timeoutMs = ResolveInitializeTimeoutMs();
                DiagLog.Debug("TaskPaneHostControl.Initialize(sync) timeoutMs=" + timeoutMs);

                var task = InitializeAsync(pagePath, uiCultureName);

                // Use message-pumping loop instead of blocking Wait() to avoid deadlock.
                // WebView2's EnsureCoreWebView2Async needs the UI thread message pump to complete.
                var pumpSw = Stopwatch.StartNew();
                while (!task.IsCompleted && pumpSw.ElapsedMilliseconds < timeoutMs)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(10);
                }

                if (!task.IsCompleted)
                {
                    stopwatch.Stop();
                    LastInitializationError = "WebView2 initialization timeout";
                    SetFallbackMessage(LocalizationManager.Get("taskpane.init_timeout"));
                    DiagLog.Warn("TaskPaneHostControl.Initialize(sync) timeout. elapsedMs=" + stopwatch.ElapsedMilliseconds);

                    task.ContinueWith(
                        t =>
                        {
                            if (t.IsFaulted)
                            {
                                var ex = t.Exception != null ? t.Exception.GetBaseException() : null;
                                if (ex != null)
                                {
                                    DiagLog.Error("TaskPaneHostControl.Initialize(sync) timed-out task later faulted.", ex);
                                }
                                else
                                {
                                    DiagLog.Warn("TaskPaneHostControl.Initialize(sync) timed-out task later faulted without exception details.");
                                }
                            }
                            else if (t.IsCanceled)
                            {
                                DiagLog.Warn("TaskPaneHostControl.Initialize(sync) timed-out task later canceled.");
                            }
                            else
                            {
                                DiagLog.Debug("TaskPaneHostControl.Initialize(sync) timed-out task later completed successfully.");
                            }
                        },
                        TaskScheduler.Default);

                    return;
                }

                // Re-throw task exception to keep previous error handling behavior.
                task.GetAwaiter().GetResult();
                stopwatch.Stop();
                DiagLog.Info("TaskPaneHostControl.Initialize(sync) end. ready=" + IsWebViewReady + ", elapsedMs=" + stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                DiagLog.Error("TaskPaneHostControl.Initialize(sync) exception. elapsedMs=" + stopwatch.ElapsedMilliseconds, ex);
                throw;
            }
        }

        /// <summary>
        /// Asynchronously initializes WebView with default culture context.
        /// </summary>
        public async Task InitializeAsync(string pagePath)
        {
            await InitializeAsync(pagePath, null).ConfigureAwait(true);
        }

        /// <summary>
        /// Asynchronously initializes WebView runtime, host object bridge, and initial URI.
        /// </summary>
        public async Task InitializeAsync(string pagePath, string uiCultureName)
        {
            var stopwatch = Stopwatch.StartNew();
            DiagLog.Info("TaskPaneHostControl.InitializeAsync begin. pagePath=" + pagePath);
            if (string.IsNullOrWhiteSpace(pagePath))
            {
                throw new ArgumentException("pagePath is required.", "pagePath");
            }

            LastInitializationError = null;

            if (!File.Exists(pagePath))
            {
                SetFallbackMessage(LocalizationManager.Format("taskpane.page_not_found", pagePath));
                LastInitializationError = "WebUI page not found";
                stopwatch.Stop();
                DiagLog.Warn("TaskPaneHostControl.InitializeAsync page not found. elapsedMs=" + stopwatch.ElapsedMilliseconds);
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
                DiagLog.Debug("TaskPaneHostControl.InitializeAsync WebView control created and attached.");

                var userDataFolder = ResolveUserDataFolder();
                Directory.CreateDirectory(userDataFolder);
                DiagLog.Debug("TaskPaneHostControl.InitializeAsync userDataFolder ready: " + userDataFolder);

                var createEnvSw = Stopwatch.StartNew();
                var environment = await CoreWebView2Environment.CreateAsync(
                    null,
                    userDataFolder,
                    null).ConfigureAwait(true);
                createEnvSw.Stop();
                DiagLog.Debug("TaskPaneHostControl.InitializeAsync CreateAsync done. elapsedMs=" + createEnvSw.ElapsedMilliseconds);

                var ensureSw = Stopwatch.StartNew();
                await _webView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
                ensureSw.Stop();
                DiagLog.Debug("TaskPaneHostControl.InitializeAsync EnsureCoreWebView2Async done. elapsedMs=" + ensureSw.ElapsedMilliseconds);

                _webView.CoreWebView2.AddHostObjectToScript("slidetexHost", _hostObject);
                DiagLog.Debug("TaskPaneHostControl.InitializeAsync AddHostObjectToScript done.");
                await InjectHostContextAsync(uiCultureName).ConfigureAwait(true);
                _webView.Source = BuildWebUiSourceUri(pagePath);
                stopwatch.Stop();
                DiagLog.Info("TaskPaneHostControl.InitializeAsync end success. source=" + _webView.Source + ", elapsedMs=" + stopwatch.ElapsedMilliseconds);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                _webView = null;
                SetFallbackMessage(LocalizationManager.Get("taskpane.runtime_not_found"));
                LastInitializationError = ex.Message;
                stopwatch.Stop();
                DiagLog.Error("TaskPaneHostControl.InitializeAsync runtime not found. elapsedMs=" + stopwatch.ElapsedMilliseconds, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                _webView = null;
                SetFallbackMessage(LocalizationManager.Get("taskpane.init_no_write_permission"));
                LastInitializationError = ex.Message;
                stopwatch.Stop();
                DiagLog.Error("TaskPaneHostControl.InitializeAsync unauthorized access. elapsedMs=" + stopwatch.ElapsedMilliseconds, ex);
            }
            catch (Exception ex)
            {
                _webView = null;
                if (ex.HResult == unchecked((int)0x80070005))
                {
                    SetFallbackMessage(LocalizationManager.Get("taskpane.init_access_denied"));
                }
                else
                {
                    SetFallbackMessage(LocalizationManager.Format("taskpane.init_failed", ex.Message));
                }
                LastInitializationError = ex.Message;
                stopwatch.Stop();
                DiagLog.Error("TaskPaneHostControl.InitializeAsync general exception. elapsedMs=" + stopwatch.ElapsedMilliseconds, ex);
            }
        }

        /// <summary>
        /// Executes JavaScript synchronously via async bridge while keeping message pump alive.
        /// </summary>
        public void ExecuteScript(string script)
        {
            var task = ExecuteScriptAsync(script);

            // Use message-pumping loop instead of blocking GetAwaiter().GetResult()
            // to avoid deadlock. WebView2 needs the UI thread message pump to complete.
            while (!task.IsCompleted)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(5);
            }

            // Propagate exceptions if any.
            if (task.IsFaulted && task.Exception != null)
            {
                DiagLog.Error("ExecuteScript faulted.", task.Exception.GetBaseException());
            }
        }

        /// <summary>
        /// Executes JavaScript against the active WebView instance when it is ready.
        /// </summary>
        public async Task ExecuteScriptAsync(string script)
        {
            if (_webView == null || _webView.CoreWebView2 == null)
            {
                DiagLog.Debug("ExecuteScriptAsync skipped: WebView2 is not ready.");
                return;
            }

            await _webView.CoreWebView2.ExecuteScriptAsync(script).ConfigureAwait(true);
        }

        /// <summary>
        /// Sets a user-visible fallback message when WebView initialization cannot continue.
        /// </summary>
        private void SetFallbackMessage(string message)
        {
            DiagLog.Warn("TaskPaneHostControl.SetFallbackMessage: " + message);
            Controls.Clear();
            _fallbackLabel.Text = message;
            Controls.Add(_fallbackLabel);
        }

        private static string ResolveUserDataFolder()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Path.GetTempPath();
            }

            return Path.Combine(localAppData, "SlideTeX", "WebView2", "PowerPoint");
        }

        /// <summary>
        /// Injects host-side context (such as UI culture) before page scripts execute.
        /// </summary>
        private async Task InjectHostContextAsync(string uiCultureName)
        {
            if (_webView == null || _webView.CoreWebView2 == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(uiCultureName))
            {
                return;
            }

            var escapedCulture = EscapeJavaScriptString(uiCultureName);
            var script = "window.slideTexContext = window.slideTexContext || {}; window.slideTexContext.uiCulture = '" + escapedCulture + "';";
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script).ConfigureAwait(true);
            DiagLog.Debug("TaskPaneHostControl.InitializeAsync host context injected. uiCulture=" + uiCultureName);
        }

        private static Uri BuildWebUiSourceUri(string pagePath)
        {
            return new Uri(Path.GetFullPath(pagePath));
        }

        private static string EscapeJavaScriptString(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("'", "\\'");
        }

        private static int ResolveInitializeTimeoutMs()
        {
            var raw = Environment.GetEnvironmentVariable("SLIDETEX_WEBVIEW2_INIT_TIMEOUT_MS");
            int parsed;
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out parsed) && parsed >= 1000 && parsed <= 120000)
            {
                return parsed;
            }

            return DefaultInitializeTimeoutMs;
        }
    }
}


