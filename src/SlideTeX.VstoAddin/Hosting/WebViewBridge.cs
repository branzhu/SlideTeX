// SlideTeX Note: Unified WebView2 communication bridge — eliminates DoEvents reentrancy.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using SlideTeX.VstoAddin.Contracts;
using SlideTeX.VstoAddin.Diagnostics;
using SlideTeX.VstoAddin.Localization;
using SlideTeX.VstoAddin.Metadata;

namespace SlideTeX.VstoAddin.Hosting
{
    internal enum WebViewPageState
    {
        Uninitialized,
        Initializing,
        Navigating,
        Ready,
        Failed
    }

    internal sealed class WebViewBridge : IDisposable
    {
        private readonly TaskPaneHostControl _hostControl;
        private readonly Queue<string> _scriptQueue = new Queue<string>();
        private WebViewPageState _state = WebViewPageState.Uninitialized;
        private bool _isDraining;
        private bool _isInSyncWait;
        private bool _disposed;

        public WebViewBridge(TaskPaneHostControl hostControl)
        {
            _hostControl = hostControl ?? throw new ArgumentNullException("hostControl");
            _hostControl.PageReady += OnHostPageReady;
            _hostControl.RenderNotificationReceived += OnHostRenderNotification;
            _hostControl.CommandRequested += OnHostCommandRequested;
            _hostControl.FormulaOcrRequested += OnHostFormulaOcrRequested;
        }

        public WebViewPageState State
        {
            get { return _state; }
        }

        public bool IsReady
        {
            get { return _state == WebViewPageState.Ready; }
        }

        public bool IsBusy
        {
            get { return _isInSyncWait; }
        }

        public event EventHandler StateChanged;
        public event EventHandler<RenderNotificationEventArgs> RenderNotificationReceived;
        public event EventHandler<HostCommandRequestedEventArgs> CommandRequested;
        public event EventHandler<FormulaOcrRequestedEventArgs> FormulaOcrRequested;

        // ── Lifecycle ──────────────────────────────────────────────────

        public void Initialize(string pagePath, string uiCultureName)
        {
            if (_state != WebViewPageState.Uninitialized && _state != WebViewPageState.Failed)
            {
                DiagLog.Debug("WebViewBridge.Initialize skipped. state=" + _state);
                return;
            }

            SetState(WebViewPageState.Initializing);
            DiagLog.Info("WebViewBridge.Initialize begin.");

            try
            {
                var task = _hostControl.InitializeAsync(pagePath, uiCultureName);
                var timeoutMs = ResolveInitializeTimeoutMs();
                var sw = Stopwatch.StartNew();

                while (!task.IsCompleted && sw.ElapsedMilliseconds < timeoutMs)
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                }

                if (!task.IsCompleted)
                {
                    SetState(WebViewPageState.Failed);
                    DiagLog.Warn("WebViewBridge.Initialize timeout.");
                    return;
                }

                task.GetAwaiter().GetResult();

                if (!_hostControl.IsWebViewReady)
                {
                    SetState(WebViewPageState.Failed);
                    DiagLog.Warn("WebViewBridge.Initialize host not ready after init.");
                    return;
                }

                // WebView2 control created, Source set → page is navigating
                SetState(WebViewPageState.Navigating);
                DiagLog.Info("WebViewBridge.Initialize → Navigating.");
            }
            catch (Exception ex)
            {
                SetState(WebViewPageState.Failed);
                DiagLog.Error("WebViewBridge.Initialize exception.", ex);
                throw;
            }
        }

        // ── Fire-and-forget script execution ───────────────────────────

        public void PostScript(string script)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                return;
            }

            if (_state == WebViewPageState.Ready && !_isInSyncWait)
            {
                _scriptQueue.Enqueue(script);
                ScheduleDrain();
            }
            else if (_state == WebViewPageState.Navigating || _state == WebViewPageState.Initializing)
            {
                _scriptQueue.Enqueue(script);
                DiagLog.Debug("WebViewBridge.PostScript queued (page not ready). queueSize=" + _scriptQueue.Count);
            }
            else
            {
                DiagLog.Debug("WebViewBridge.PostScript dropped. state=" + _state);
            }
        }

        // ── Synchronous render (only DoEvents path) ───────────────────

        public RenderSuccessPayload RenderAndWait(
            string latex,
            RenderOptionsDto options,
            string renderLatex,
            int timeoutMs,
            System.Web.Script.Serialization.JavaScriptSerializer serializer)
        {
            if (_state != WebViewPageState.Ready)
            {
                DiagLog.Warn("WebViewBridge.RenderAndWait skipped. state=" + _state);
                return null;
            }

            if (_isInSyncWait)
            {
                DiagLog.Warn("WebViewBridge.RenderAndWait reentrant call blocked.");
                return null;
            }

            _isInSyncWait = true;
            try
            {
                return RenderAndWaitCore(latex, options, renderLatex, timeoutMs, serializer);
            }
            finally
            {
                _isInSyncWait = false;
                ScheduleDrain();
            }
        }

        // ── Dispose ───────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _hostControl.PageReady -= OnHostPageReady;
            _hostControl.RenderNotificationReceived -= OnHostRenderNotification;
            _hostControl.CommandRequested -= OnHostCommandRequested;
            _hostControl.FormulaOcrRequested -= OnHostFormulaOcrRequested;
            _scriptQueue.Clear();
            DiagLog.Debug("WebViewBridge.Dispose done.");
        }

        // ── Private: RenderAndWaitCore ────────────────────────────────

        private RenderSuccessPayload RenderAndWaitCore(
            string latex,
            RenderOptionsDto options,
            string renderLatex,
            int timeoutMs,
            System.Web.Script.Serialization.JavaScriptSerializer serializer)
        {
            var tcs = new ManualResetEvent(false);
            RenderSuccessPayload result = null;
            Exception renderError = null;

            EventHandler<RenderNotificationEventArgs> handler = null;
            handler = (s, e) =>
            {
                DiagLog.Debug("WebViewBridge.RenderAndWaitCore handler fired. isSuccess=" + e.IsSuccess);
                _hostControl.RenderNotificationReceived -= handler;
                if (e.IsSuccess && !string.IsNullOrWhiteSpace(e.Payload))
                {
                    try
                    {
                        var payload = serializer.Deserialize<RenderSuccessPayload>(e.Payload);
                        if (payload != null && payload.IsSuccess)
                        {
                            if (payload.Options == null)
                            {
                                payload.Options = new RenderOptionsDto();
                            }
                            result = payload;
                        }
                    }
                    catch (Exception ex)
                    {
                        renderError = ex;
                    }
                }
                else
                {
                    renderError = new Exception(e.ErrorMessage ?? LocalizationManager.Get("error.render_failed_default"));
                }
                tcs.Set();
            };

            _hostControl.RenderNotificationReceived += handler;

            var renderPayload = serializer.Serialize(new
            {
                latex = latex,
                options = options,
                renderLatex = !string.IsNullOrWhiteSpace(renderLatex) ? renderLatex : null
            });
            var script = "window.slideTex && window.slideTex.renderFromHost(" + renderPayload + ");";
            _hostControl.ExecuteScript(script);
            DiagLog.Debug("WebViewBridge.RenderAndWaitCore script sent. Entering wait loop.");

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            int loopCount = 0;
            while (!tcs.WaitOne(0) && DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                Thread.Sleep(10);
                loopCount++;
                if (loopCount % 100 == 0)
                {
                    DiagLog.Debug("WebViewBridge.RenderAndWaitCore still waiting. loopCount=" + loopCount);
                }
            }

            if (!tcs.WaitOne(0))
            {
                _hostControl.RenderNotificationReceived -= handler;
                DiagLog.Warn("WebViewBridge.RenderAndWaitCore timeout.");
                throw new TimeoutException(LocalizationManager.Get("error.render_timeout"));
            }

            if (renderError != null)
            {
                DiagLog.Warn("WebViewBridge.RenderAndWaitCore failed: " + renderError.Message);
                throw renderError;
            }

            return result;
        }

        // ── Private: Queue drain ──────────────────────────────────────

        private void ScheduleDrain()
        {
            if (_isDraining || _scriptQueue.Count == 0 || _state != WebViewPageState.Ready)
            {
                return;
            }

            _hostControl.BeginInvoke(new Action(async () => await DrainQueueAsync()));
        }

        private async System.Threading.Tasks.Task DrainQueueAsync()
        {
            if (_isDraining)
            {
                return;
            }

            _isDraining = true;
            try
            {
                while (_scriptQueue.Count > 0 && _state == WebViewPageState.Ready && !_isInSyncWait)
                {
                    var script = _scriptQueue.Dequeue();
                    try
                    {
                        await _hostControl.ExecuteScriptAsync(script).ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        DiagLog.Warn("WebViewBridge.DrainQueueAsync script failed: " + ex.Message);
                    }
                }
            }
            finally
            {
                _isDraining = false;
            }
        }

        // ── Private: Event forwarding ─────────────────────────────────

        private void OnHostPageReady(object sender, EventArgs e)
        {
            if (_state == WebViewPageState.Navigating || _state == WebViewPageState.Initializing)
            {
                SetState(WebViewPageState.Ready);
                DiagLog.Info("WebViewBridge.OnHostPageReady → Ready.");
                ScheduleDrain();
            }
        }

        private void OnHostRenderNotification(object sender, RenderNotificationEventArgs e)
        {
            var handler = RenderNotificationReceived;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        private void OnHostCommandRequested(object sender, HostCommandRequestedEventArgs e)
        {
            var handler = CommandRequested;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        private void OnHostFormulaOcrRequested(object sender, FormulaOcrRequestedEventArgs e)
        {
            var handler = FormulaOcrRequested;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        // ── Private: State management ─────────────────────────────────

        private void SetState(WebViewPageState newState)
        {
            if (_state == newState)
            {
                return;
            }

            var old = _state;
            _state = newState;
            DiagLog.Debug("WebViewBridge state: " + old + " → " + newState);

            var handler = StateChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private static int ResolveInitializeTimeoutMs()
        {
            var raw = Environment.GetEnvironmentVariable("SLIDETEX_WEBVIEW2_INIT_TIMEOUT_MS");
            int parsed;
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out parsed) && parsed >= 1000 && parsed <= 120000)
            {
                return parsed;
            }

            return 15000;
        }
    }
}