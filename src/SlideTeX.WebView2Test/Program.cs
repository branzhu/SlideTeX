// SlideTeX Note: WebView2 integration test — validates COM bridge → JS render → callback chain.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using SlideTeX.VstoAddin.Contracts;
using SlideTeX.VstoAddin.Hosting;

namespace SlideTeX.WebView2Test
{
    static class Program
    {
        [STAThread]
        static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var form = new TestForm();
            Application.Run(form);
            return form.ExitCode;
        }
    }

    sealed class TestResult
    {
        public string Name;
        public bool Pass;
        public string Error;
    }

    sealed class TestForm : Form
    {
        const int RENDER_WAIT_MS = 15000;
        const int CMD_WAIT_MS = 5000;
        const int GLOBAL_TIMEOUT_MS = 45000;

        readonly WebView2 webView;
        readonly SlideTeXHostObject hostObject;
        readonly List<TestResult> results = new List<TestResult>();
        readonly Timer globalTimer;

        int exitCode = 1;
        public int ExitCode { get { return exitCode; } }

        bool initialRenderReceived;
        bool hostRenderReceived;
        bool complexRenderReceived;
        bool insertCommandReceived;
        string lastRenderPayload;

        public TestForm()
        {
            Text = "SlideTeX WebView2 Test";
            Size = new Size(800, 600);
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-2000, -2000);

            webView = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(webView);

            hostObject = new SlideTeXHostObject();
            hostObject.RenderNotificationReceived += OnRenderNotification;
            hostObject.CommandRequested += OnCommandRequested;

            globalTimer = new Timer { Interval = GLOBAL_TIMEOUT_MS };
            globalTimer.Tick += (s, e) => { globalTimer.Stop(); Log("Global timeout"); FinishTests(); };

            Load += async (s, e) => await InitializeAsync();
        }

        async Task InitializeAsync()
        {
            try
            {
                Log("Initializing WebView2...");
                var env = await CoreWebView2Environment.CreateAsync();
                await webView.EnsureCoreWebView2Async(env);

                webView.CoreWebView2.AddHostObjectToScript("slidetexHost", hostObject);
                webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

                var indexPath = FindIndexHtml();
                Log("Loading: " + indexPath);
                webView.CoreWebView2.Navigate(new Uri(indexPath).AbsoluteUri);
                globalTimer.Start();
            }
            catch (Exception ex)
            {
                Log("Init error: " + ex.Message);
                results.Add(new TestResult { Name = "init", Pass = false, Error = ex.Message });
                FinishTests();
            }
        }

        async void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                results.Add(new TestResult { Name = "navigation", Pass = false, Error = e.WebErrorStatus.ToString() });
                FinishTests();
                return;
            }

            Log("Page loaded, waiting for initial render...");
            await RunInitialRenderTest();
            await RunHostRenderTest();
            await RunComplexFormulaTest();
            await RunInsertCommandTest();
            FinishTests();
        }

        async Task RunInitialRenderTest()
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(RENDER_WAIT_MS);
            while (!initialRenderReceived && DateTime.UtcNow < deadline)
            {
                await Task.Delay(200);
                Application.DoEvents();
            }

            if (initialRenderReceived && lastRenderPayload != null
                && lastRenderPayload.Contains("\"isSuccess\":true")
                && lastRenderPayload.Contains("\"pngBase64\":"))
            {
                results.Add(new TestResult { Name = "initial-render", Pass = true });
                Log("initial-render: PASS");
            }
            else
            {
                var err = initialRenderReceived ? "Payload missing expected fields" : "Timeout";
                results.Add(new TestResult { Name = "initial-render", Pass = false, Error = err });
                Log("initial-render: FAIL — " + err);
            }
        }

        async Task RunHostRenderTest()
        {
            hostRenderReceived = false;
            lastRenderPayload = null;

            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync(
                    "window.slideTex.renderFromHost({ latex: 'E=mc^2' })");

                var deadline = DateTime.UtcNow.AddMilliseconds(RENDER_WAIT_MS);
                while (!hostRenderReceived && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(200);
                    Application.DoEvents();
                }

                bool pass = hostRenderReceived && lastRenderPayload != null
                    && lastRenderPayload.Contains("\"pngBase64\":");
                var err = hostRenderReceived ? (pass ? null : "Missing pngBase64") : "Timeout";
                results.Add(new TestResult { Name = "host-render", Pass = pass, Error = err });
                Log("host-render: " + (pass ? "PASS" : "FAIL — " + err));
            }
            catch (Exception ex)
            {
                results.Add(new TestResult { Name = "host-render", Pass = false, Error = ex.Message });
                Log("host-render: FAIL — " + ex.Message);
            }
        }

        async Task RunComplexFormulaTest()
        {
            complexRenderReceived = false;
            lastRenderPayload = null;

            try
            {
                var latex = @"{\cal W} \equiv \frac {1} {4 \rho^{2}} \Big [\cosh ( 2 \varphi_{2} ) ( \rho^{6} - 2 ) - ( 3 \rho^{6} + 2 ) \Big], \qquad \rho \equiv e^{\frac {1} {\sqrt {6}} \varphi_{1}}.";
                await webView.CoreWebView2.ExecuteScriptAsync(
                    "window.slideTex.renderFromHost({ latex: '" + latex.Replace("\\", "\\\\") + "' })");

                var deadline = DateTime.UtcNow.AddMilliseconds(RENDER_WAIT_MS);
                while (!complexRenderReceived && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(200);
                    Application.DoEvents();
                }

                bool pass = complexRenderReceived && lastRenderPayload != null
                    && lastRenderPayload.Contains("\"pngBase64\":");
                string err = complexRenderReceived ? (pass ? null : "Missing pngBase64") : "Timeout";

                // Verify SVG structural integrity — the complex formula should
                // produce many glyphs.  Inline line-breaking bug produced only 1.
                if (pass)
                {
                    var svgJson = await webView.CoreWebView2.ExecuteScriptAsync(
                        "JSON.stringify({" +
                        "pathCount: document.querySelectorAll('#previewContent svg path').length," +
                        "useCount: document.querySelectorAll('#previewContent svg use').length," +
                        "hasMerror: !!document.querySelector('#previewContent mjx-merror, #previewContent [data-mjx-error]')" +
                        "})");
                    var svgInfo = svgJson.Trim('"').Replace("\\\"", "\"");
                    Log("complex-formula SVG: " + svgInfo);
                    bool hasMerror = svgInfo.Contains("\"hasMerror\":true");
                    int glyphCount = ExtractJsonInt(svgInfo, "pathCount") + ExtractJsonInt(svgInfo, "useCount");
                    bool svgOk = glyphCount >= 20 && !hasMerror;
                    if (!svgOk)
                    {
                        pass = false;
                        err = "SVG verification failed (glyphs=" + glyphCount + "): " + svgInfo;
                    }
                }

                results.Add(new TestResult { Name = "complex-formula", Pass = pass, Error = err });
                Log("complex-formula: " + (pass ? "PASS" : "FAIL — " + err));
            }
            catch (Exception ex)
            {
                results.Add(new TestResult { Name = "complex-formula", Pass = false, Error = ex.Message });
                Log("complex-formula: FAIL — " + ex.Message);
            }
        }

        async Task RunInsertCommandTest()
        {
            insertCommandReceived = false;

            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync(
                    "document.getElementById('insertBtn')?.click()");

                var deadline = DateTime.UtcNow.AddMilliseconds(CMD_WAIT_MS);
                while (!insertCommandReceived && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(200);
                    Application.DoEvents();
                }

                results.Add(new TestResult {
                    Name = "insert-command",
                    Pass = insertCommandReceived,
                    Error = insertCommandReceived ? null : "Timeout"
                });
                Log("insert-command: " + (insertCommandReceived ? "PASS" : "FAIL — timeout"));
            }
            catch (Exception ex)
            {
                results.Add(new TestResult { Name = "insert-command", Pass = false, Error = ex.Message });
            }
        }

        void OnRenderNotification(object sender, RenderNotificationEventArgs e)
        {
            if (e.IsSuccess)
            {
                lastRenderPayload = e.Payload;
                if (!initialRenderReceived)
                    initialRenderReceived = true;
                else if (!hostRenderReceived)
                    hostRenderReceived = true;
                else
                    complexRenderReceived = true;
            }
        }

        void OnCommandRequested(object sender, HostCommandRequestedEventArgs e)
        {
            if (e.Payload.CommandType == HostCommandType.Insert)
                insertCommandReceived = true;
        }

        void FinishTests()
        {
            globalTimer.Stop();

            int passed = 0, failed = 0;
            var parts = new List<string>();
            foreach (var r in results)
            {
                if (r.Pass) passed++; else failed++;
                var errPart = r.Error != null ? ",\"error\":" + Esc(r.Error) : "";
                parts.Add("{\"name\":" + Esc(r.Name) + ",\"pass\":" + (r.Pass ? "true" : "false") + errPart + "}");
            }

            Console.WriteLine("{\"passed\":" + passed + ",\"failed\":" + failed
                + ",\"results\":[" + string.Join(",", parts) + "]}");

            exitCode = failed > 0 ? 1 : 0;
            Application.Exit();
        }

        static string Esc(string s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
        }

        static string FindIndexHtml()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                var candidate = Path.Combine(dir, "src", "SlideTeX.WebUI", "index.html");
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
                if (dir == null) break;
            }
            throw new FileNotFoundException("Cannot find src/SlideTeX.WebUI/index.html");
        }

        static int ExtractJsonInt(string json, string key)
        {
            var token = "\"" + key + "\":";
            int idx = json.IndexOf(token, StringComparison.Ordinal);
            if (idx < 0) return 0;
            idx += token.Length;
            int end = idx;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-'))
                end++;
            int val;
            int.TryParse(json.Substring(idx, end - idx), out val);
            return val;
        }

        void Log(string msg) { Console.Error.WriteLine("[test] " + msg); }
    }
}
