// SlideTeX Note: Unified WebView2 communication bridge — eliminates DoEvents reentrancy.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        private bool _isRenderInFlight;
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
            get { return _isRenderInFlight; }
        }

        public event EventHandler StateChanged;
        public event EventHandler<RenderNotificationEventArgs> RenderNotificationReceived;
        public event EventHandler<HostCommandRequestedEventArgs> CommandRequested;
        public event EventHandler<FormulaOcrRequestedEventArgs> FormulaOcrRequested;

        // ── Lifecycle ──────────────────────────────────────────────────

        public async Task InitializeAsync(string pagePath, string uiCultureName)
        {
            if (_state != WebViewPageState.Uninitialized && _state != WebViewPageState.Failed)
            {
                DiagLog.Debug("WebViewBridge.InitializeAsync skipped. state=" + _state);
                return;
            }

            SetState(WebViewPageState.Initializing);
            DiagLog.Info("WebViewBridge.InitializeAsync begin.");

            try
            {
                await _hostControl.InitializeAsync(pagePath, uiCultureName).ConfigureAwait(true);

                if (!_hostControl.IsWebViewReady)
                {
                    SetState(WebViewPageState.Failed);
                    DiagLog.Warn("WebViewBridge.InitializeAsync host not ready after init.");
                    return;
                }

                SetState(WebViewPageState.Navigating);
                DiagLog.Info("WebViewBridge.InitializeAsync → Navigating.");
            }
            catch (Exception ex)
            {
                SetState(WebViewPageState.Failed);
                DiagLog.Error("WebViewBridge.InitializeAsync exception.", ex);
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

            if (_state == WebViewPageState.Ready && !_isRenderInFlight)
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

        // ── Async render with callback ─────────────────────────────────

        public async Task<RenderSuccessPayload> RenderAndWaitAsync(
            string latex,
            RenderOptionsDto options,
            string renderLatex,
            int timeoutMs,
            System.Web.Script.Serialization.JavaScriptSerializer serializer)
        {
            if (_state != WebViewPageState.Ready)
            {
                DiagLog.Warn("WebViewBridge.RenderAndWaitAsync skipped. state=" + _state);
                return null;
            }

            if (_isRenderInFlight)
            {
                DiagLog.Warn("WebViewBridge.RenderAndWaitAsync reentrant call blocked.");
                return null;
            }

            _isRenderInFlight = true;
            try
            {
                var tcs = new TaskCompletionSource<RenderSuccessPayload>();

                EventHandler<RenderNotificationEventArgs> handler = null;
                handler = (s, e) =>
                {
                    DiagLog.Debug("WebViewBridge.RenderAndWaitAsync handler fired. isSuccess=" + e.IsSuccess);
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
                                tcs.TrySetResult(payload);
                            }
                            else
                            {
                                tcs.TrySetResult(null);
                            }
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                    }
                    else
                    {
                        tcs.TrySetException(new Exception(
                            e.ErrorMessage ?? LocalizationManager.Get("error.render_failed_default")));
                    }
                };

                _hostControl.RenderNotificationReceived += handler;

                var renderPayload = serializer.Serialize(new
                {
                    latex = latex,
                    options = options,
                    renderLatex = !string.IsNullOrWhiteSpace(renderLatex) ? renderLatex : null
                });
                var script = "window.slideTex && window.slideTex.renderFromHost(" + renderPayload + ");";
                await _hostControl.ExecuteScriptAsync(script).ConfigureAwait(true);
                DiagLog.Debug("WebViewBridge.RenderAndWaitAsync script sent. Awaiting callback.");

                var timeout = Task.Delay(timeoutMs);
                if (await Task.WhenAny(tcs.Task, timeout).ConfigureAwait(true) == timeout)
                {
                    _hostControl.RenderNotificationReceived -= handler;
                    DiagLog.Warn("WebViewBridge.RenderAndWaitAsync timeout.");
                    throw new TimeoutException(LocalizationManager.Get("error.render_timeout"));
                }

                return await tcs.Task.ConfigureAwait(true);
            }
            finally
            {
                _isRenderInFlight = false;
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

        // ── Private: Queue drain ──────────────────────────────────────

        private void ScheduleDrain()
        {
            if (_isDraining || _scriptQueue.Count == 0 || _state != WebViewPageState.Ready)
            {
                return;
            }

            _hostControl.BeginInvoke(new Action(async () => await DrainQueueAsync()));
        }

        private async Task DrainQueueAsync()
        {
            if (_isDraining)
            {
                return;
            }

            _isDraining = true;
            try
            {
                while (_scriptQueue.Count > 0 && _state == WebViewPageState.Ready && !_isRenderInFlight)
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

    }
}