// SlideTeX Note: Central coordinator for task pane lifecycle, render requests, and shape updates.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Office = Microsoft.Office.Core;
using SlideTeX.VstoAddin.Contracts;
using SlideTeX.VstoAddin.Diagnostics;
using SlideTeX.VstoAddin.Hosting;
using SlideTeX.VstoAddin.Localization;
using SlideTeX.VstoAddin.Metadata;
using SlideTeX.VstoAddin.Ocr;
using SlideTeX.VstoAddin.PowerPoint;

namespace SlideTeX.VstoAddin
{
    /// <summary>
    /// Coordinates the end-to-end equation authoring flow between Ribbon actions,
    /// WebView rendering callbacks, and PowerPoint shape persistence.
    /// </summary>
    internal sealed class SlideTeXAddinController : IDisposable
    {
        private static readonly Regex NumberingEnvBeginRegex = new Regex(
            @"\\begin\{(equation|align|gather)(\*)?\}", RegexOptions.Compiled);
        private static readonly Regex NumberingSuppressionRegex = new Regex(
            @"\\(?:nonumber|notag)\b", RegexOptions.Compiled);
        private static readonly Regex LineBreakRegex = new Regex(
            @"(?<!\\)\\\\(?![a-zA-Z])", RegexOptions.Compiled);

        private readonly ThisAddIn _addIn;
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private readonly PowerPointEquationShapeService _equationShapeService = new PowerPointEquationShapeService();
        private readonly ShapeTagMetadataStore _metadataStore = new ShapeTagMetadataStore();
        private readonly FormulaOcrService _formulaOcrService = new FormulaOcrService();

        private TaskPaneHostControl _taskPaneControl;
        private Microsoft.Office.Tools.CustomTaskPane _taskPane;
        private RenderSuccessPayload _lastRender;
        private string _webUiPagePath;
        private bool _webViewInitialized;
        private Timer _selectionTimer;
        private string _lastAutoEditShapeKey;
        private bool _isBusyRendering;

        public SlideTeXAddinController(ThisAddIn addIn)
        {
            _addIn = addIn ?? throw new ArgumentNullException("addIn");
            LocalizationManager.Initialize(addIn);
        }

        /// <summary>
        /// Gets the PowerPoint Application object using late-binding if the typed property is null.
        /// This handles the case where the project is built without the PowerPoint Interop PIA.
        /// </summary>
        private dynamic GetApplication()
        {
            // Try the typed Application property first (direct field access)
            dynamic app = _addIn.Application;
            if (app != null)
            {
                DiagLog.Debug("GetApplication: Using typed Application property.");
                return app;
            }

            // Fallback 1: try to get Host property via reflection from AddInBase
            try
            {
                var baseType = _addIn.GetType().BaseType;
                while (baseType != null)
                {
                    var hostProperty = baseType.GetProperty("Host",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);

                    if (hostProperty != null)
                    {
                        app = hostProperty.GetValue(_addIn, null);
                        if (app != null)
                        {
                            DiagLog.Debug("GetApplication: Retrieved via Host property from " + baseType.Name);
                            return app;
                        }
                    }
                    baseType = baseType.BaseType;
                }
                DiagLog.Debug("GetApplication: Host property not found in type hierarchy.");
            }
            catch (Exception ex)
            {
                DiagLog.Debug("GetApplication: Host property reflection failed. " + ex.Message);
            }

            // Fallback 2: use Marshal.GetActiveObject to get running PowerPoint instance
            try
            {
                app = System.Runtime.InteropServices.Marshal.GetActiveObject("PowerPoint.Application");
                if (app != null)
                {
                    DiagLog.Debug("GetApplication: Retrieved via Marshal.GetActiveObject.");
                    return app;
                }
            }
            catch (Exception ex)
            {
                DiagLog.Debug("GetApplication: Marshal.GetActiveObject failed. " + ex.Message);
            }

            DiagLog.Warn("GetApplication: All methods failed, returning null.");
            return null;
        }

        /// <summary>
        /// Creates task pane infrastructure, event wiring, and WebUI path resolution.
        /// </summary>
        public void Initialize()
        {
            DiagLog.Info("Controller.Initialize begin.");
            _taskPaneControl = new TaskPaneHostControl();
            _taskPaneControl.RenderNotificationReceived += OnRenderNotificationReceived;
            _taskPaneControl.CommandRequested += OnCommandRequested;
            _taskPaneControl.FormulaOcrRequested += OnFormulaOcrRequested;

            _taskPane = _addIn.CustomTaskPanes.Add(_taskPaneControl, LocalizationManager.Get("app.taskpane_title"));
            _taskPane.DockPosition = Office.MsoCTPDockPosition.msoCTPDockPositionRight;
            _taskPane.Width = 700;
            _taskPane.Visible = false;
            _taskPane.VisibleChanged += OnTaskPaneVisibleChanged;
            DiagLog.Debug("Controller.Initialize task pane created.");

            _selectionTimer = new Timer();
            _selectionTimer.Interval = 300;
            _selectionTimer.Tick += OnSelectionTimerTick;

            _webUiPagePath = ResolveWebUiPath();
            if (_webUiPagePath == null)
            {
                DiagLog.Warn("Controller.Initialize web UI page path not found.");
                ShowWarning(LocalizationManager.Get("controller.webui_unavailable"));
                return;
            }

            DiagLog.Info("Controller.Initialize end. webUiPagePath=" + _webUiPagePath);
        }

        /// <summary>
        /// Ensures the task pane is initialized and visible, then attempts auto-edit sync.
        /// </summary>
        public void OpenPane()
        {
            DiagLog.Info("Controller.OpenPane requested.");
            if (_taskPane == null)
            {
                DiagLog.Warn("Controller.OpenPane skipped because taskPane is null.");
                return;
            }

            EnsureWebViewInitialized();
            _taskPane.Visible = true;
            DiagLog.Debug("Controller.OpenPane completed. taskPane.Visible=true.");

            TryAutoEditSelected();
        }

        /// <summary>
        /// Inserts the current render result as a new shape on the active slide.
        /// </summary>
        public void InsertCurrentFormula()
        {
            DiagLog.Info("InsertCurrentFormula begin.");
            if (!EnsureLastRender())
            {
                return;
            }

            var slide = GetActiveSlide();
            if (slide == null)
            {
                ShowWarning(LocalizationManager.Get("warning.no_active_slide"));
                return;
            }

            try
            {
                var renderToInsert = _lastRender;
                var originalLatex = _lastRender.Latex;
                int autoNumberLineCount = GetAutoNumberLineCount(originalLatex);
                bool autoNumbered = autoNumberLineCount > 0;
                DiagLog.Debug("InsertCurrentFormula autoNumbered=" + autoNumbered
                    + " autoNumberLineCount=" + autoNumberLineCount
                    + " latexLength=" + (originalLatex == null ? 0 : originalLatex.Length));

                if (autoNumbered)
                {
                    DiagLog.Debug("InsertCurrentFormula scanning existing shapes.");
                    var existingShapes = ScanAutoNumberedShapes();
                    int nextNumber = 1;
                    foreach (var info in existingShapes)
                    {
                        nextNumber += Math.Max(1, info.AutoNumberLineCount);
                    }

                    int consumedCount;
                    string numberedLatex = BuildNumberedLatex(originalLatex, nextNumber, out consumedCount);
                    DiagLog.Debug("InsertCurrentFormula nextNumber=" + nextNumber + " consumedCount=" + consumedCount);
                    var numberedRender = RenderAndWait(originalLatex, _lastRender.Options, numberedLatex);
                    DiagLog.Debug("InsertCurrentFormula numbered render returned. isNull=" + (numberedRender == null));
                    if (numberedRender != null)
                    {
                        renderToInsert = numberedRender;
                        autoNumberLineCount = consumedCount;
                    }
                }

                DiagLog.Debug("InsertCurrentFormula inserting shape.");
                var context = BuildInsertContext(slide, renderToInsert);
                var shape = _equationShapeService.Insert(renderToInsert, context);
                var meta = BuildMeta(renderToInsert, autoNumbered, autoNumberLineCount);
                meta.LatexSource = originalLatex;
                _metadataStore.Write(new PowerPointShapeTagAccessor(shape), meta);
                DiagLog.Info("InsertCurrentFormula succeeded.");
            }
            catch (Exception ex)
            {
                DiagLog.Error("InsertCurrentFormula failed.", ex);
                ShowWarning(LocalizationManager.Format("warning.insert_failed", ex.Message));
            }
        }

        /// <summary>
        /// Opens the pane and loads metadata for the currently selected SlideTeX shape.
        /// </summary>
        public void EditSelectedFormula()
        {
            var shape = GetSelectedShape();
            if (shape == null)
            {
                ShowWarning(LocalizationManager.Get("warning.select_slidetex_shape"));
                return;
            }

            ShapeMetaV1 meta;
            if (!_metadataStore.TryRead(new PowerPointShapeTagAccessor(shape), out meta) || meta == null)
            {
                ShowWarning(LocalizationManager.Get("warning.no_metadata_edit"));
                return;
            }

            DiagLog.Debug("EditSelectedFormula metadata found.");
            _lastAutoEditShapeKey = null;
            OpenPane();
        }

        /// <summary>
        /// Re-renders and replaces the selected SlideTeX shape while preserving visual scale.
        /// </summary>
        public void UpdateSelectedFormula()
        {
            if (!EnsureLastRender())
            {
                return;
            }

            var shape = GetSelectedShape();
            if (shape == null)
            {
                ShowWarning(LocalizationManager.Get("warning.select_slidetex_shape"));
                return;
            }

            ShapeMetaV1 meta;
            if (!_metadataStore.TryRead(new PowerPointShapeTagAccessor(shape), out meta))
            {
                ShowWarning(LocalizationManager.Get("warning.no_metadata_update"));
                return;
            }

            try
            {
                var newOptions = _lastRender.Options ?? new RenderOptionsDto();
                float newWidth = PowerPointEquationShapeService.PixelsToPoints(_lastRender.PixelWidth, newOptions.Dpi);
                float newHeight = PowerPointEquationShapeService.PixelsToPoints(_lastRender.PixelHeight, newOptions.Dpi);

                if (meta != null && meta.PixelWidth > 0 && meta.PixelHeight > 0)
                {
                    var oldNaturalWidth = PowerPointEquationShapeService.PixelsToPoints(meta.PixelWidth, meta.RenderOptions.Dpi);
                    var oldNaturalHeight = PowerPointEquationShapeService.PixelsToPoints(meta.PixelHeight, meta.RenderOptions.Dpi);

                    if (oldNaturalWidth > 0f && oldNaturalHeight > 0f)
                    {
                        var scaleX = (float)shape.Width / oldNaturalWidth;
                        var scaleY = (float)shape.Height / oldNaturalHeight;
                        newWidth = newWidth * scaleX;
                        newHeight = newHeight * scaleY;
                    }
                }

                var newShape = _equationShapeService.Update(shape, _lastRender, newWidth, newHeight);
                int autoNumberLineCount = GetAutoNumberLineCount(_lastRender.Latex);
                var newMeta = BuildMeta(_lastRender, autoNumberLineCount > 0, autoNumberLineCount);
                _metadataStore.Write(new PowerPointShapeTagAccessor(newShape), newMeta);
                DiagLog.Info("UpdateSelectedFormula succeeded.");
            }
            catch (Exception ex)
            {
                DiagLog.Error("UpdateSelectedFormula failed.", ex);
                ShowWarning(LocalizationManager.Format("warning.update_failed", ex.Message));
            }
        }

        /// <summary>
        /// Detaches event handlers and releases task pane resources.
        /// </summary>
        public void Dispose()
        {
            DiagLog.Debug("Controller.Dispose begin.");
            if (_selectionTimer != null)
            {
                _selectionTimer.Stop();
                _selectionTimer.Tick -= OnSelectionTimerTick;
                _selectionTimer.Dispose();
                _selectionTimer = null;
            }

            if (_taskPaneControl != null)
            {
                _taskPaneControl.RenderNotificationReceived -= OnRenderNotificationReceived;
                _taskPaneControl.CommandRequested -= OnCommandRequested;
                _taskPaneControl.FormulaOcrRequested -= OnFormulaOcrRequested;
            }

            _formulaOcrService.Dispose();

            if (_taskPane != null)
            {
                _taskPane.VisibleChanged -= OnTaskPaneVisibleChanged;
                _addIn.CustomTaskPanes.Remove(_taskPane);
                _taskPane = null;
            }

            DiagLog.Debug("Controller.Dispose end.");
        }

        private void OnTaskPaneVisibleChanged(object sender, EventArgs e)
        {
            if (_taskPane != null && _taskPane.Visible)
            {
                _lastAutoEditShapeKey = null;
                TryAutoEditSelected();
                if (_selectionTimer != null)
                {
                    _selectionTimer.Start();
                }
            }
            else
            {
                if (_selectionTimer != null)
                {
                    _selectionTimer.Stop();
                }

                _lastAutoEditShapeKey = null;
            }
        }

        private void OnSelectionTimerTick(object sender, EventArgs e)
        {
            if (_taskPane == null || !_taskPane.Visible || !_webViewInitialized || _isBusyRendering)
            {
                return;
            }

            TryAutoEditSelected();
        }

        /// <summary>
        /// Pulls metadata from the currently selected shape and mirrors it back into the pane.
        /// </summary>
        private void TryAutoEditSelected()
        {
            if (_isBusyRendering || _taskPaneControl == null || !_taskPaneControl.IsWebViewReady)
            {
                return;
            }

            try
            {
                var shape = GetSelectedShape();
                if (shape == null)
                {
                    _lastAutoEditShapeKey = null;
                    return;
                }

                var key = GetShapeKey(shape);
                if (key == null)
                {
                    return;
                }

                if (string.Equals(key, _lastAutoEditShapeKey, StringComparison.Ordinal))
                {
                    return;
                }

                ShapeMetaV1 meta;
                if (!_metadataStore.TryRead(new PowerPointShapeTagAccessor(shape), out meta) || meta == null)
                {
                    _lastAutoEditShapeKey = key;
                    return;
                }

                _lastAutoEditShapeKey = key;
                DiagLog.Debug("TryAutoEditSelected loading shape. key=" + key);
                RenderInPane(meta.LatexSource, meta.RenderOptions);
            }
            catch (Exception ex)
            {
                DiagLog.Warn("TryAutoEditSelected failed. " + ex.Message);
            }
        }

        private static string GetShapeKey(dynamic shape)
        {
            try
            {
                return (string)shape.Name + ":" + (int)shape.Id;
            }
            catch
            {
                return null;
            }
        }

        private void OnRenderNotificationReceived(object sender, RenderNotificationEventArgs args)
        {
            DiagLog.Debug("OnRenderNotificationReceived isSuccess=" + (args != null && args.IsSuccess) + " isBusy=" + _isBusyRendering);
            if (args == null || !args.IsSuccess || string.IsNullOrWhiteSpace(args.Payload))
            {
                return;
            }

            try
            {
                var payload = _serializer.Deserialize<RenderSuccessPayload>(args.Payload);
                if (payload == null || !payload.IsSuccess)
                {
                    return;
                }

                if (payload.Options == null)
                {
                    payload.Options = new RenderOptionsDto();
                }

                _lastRender = payload;
                DiagLog.Debug("OnRenderNotificationReceived payload accepted.");
            }
            catch (Exception ex)
            {
                // Ignore malformed payload from WebUI and keep previous render state.
                DiagLog.Warn("OnRenderNotificationReceived payload parse failed: " + ex.Message);
            }
        }

        private void OnCommandRequested(object sender, HostCommandRequestedEventArgs args)
        {
            if (args == null || args.Payload == null)
            {
                DiagLog.Warn("OnCommandRequested ignored because payload is null.");
                return;
            }

            DiagLog.Info("OnCommandRequested type=" + args.Payload.CommandType);

            // Defer execution via BeginInvoke so the WebView2 COM callback returns first.
            // Without this, any call back into WebView2 (e.g. ExecuteScript) from within
            // the command handler would deadlock due to COM reentrancy.
            var commandType = args.Payload.CommandType;
            _taskPaneControl.BeginInvoke(new Action(() => ExecuteCommand(commandType)));
        }

        private void OnFormulaOcrRequested(object sender, FormulaOcrRequestedEventArgs args)
        {
            if (args == null || string.IsNullOrWhiteSpace(args.ImageBase64))
            {
                NotifyFormulaOcrError("BAD_IMAGE", "OCR input image is empty.");
                return;
            }

            var options = ParseFormulaOcrOptions(args.OptionsJson);
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    var inferenceTask = System.Threading.Tasks.Task.Run(() => _formulaOcrService.Recognize(args.ImageBase64, options));
                    if (!inferenceTask.Wait(options.TimeoutMs))
                    {
                        throw new FormulaOcrException(OcrErrorCode.Timeout, "Formula OCR timed out.");
                    }

                    var result = inferenceTask.Result;
                    sw.Stop();
                    if (result != null && result.ElapsedMs <= 0)
                    {
                        result.ElapsedMs = sw.ElapsedMilliseconds;
                    }
                    NotifyFormulaOcrSuccess(result);
                }
                catch (FormulaOcrException ex)
                {
                    NotifyFormulaOcrError(ToHostOcrCode(ex.Code), ex.Message);
                }
                catch (Exception ex)
                {
                    NotifyFormulaOcrError("INFERENCE_FAILED", ex.Message);
                }
            });
        }

        private FormulaOcrOptions ParseFormulaOcrOptions(string optionsJson)
        {
            var options = FormulaOcrOptions.Default;
            if (string.IsNullOrWhiteSpace(optionsJson))
            {
                return options;
            }

            try
            {
                var map = _serializer.DeserializeObject(optionsJson) as Dictionary<string, object>;
                if (map == null)
                {
                    return options;
                }

                if (map.ContainsKey("maxTokens"))
                {
                    options.MaxTokens = SafeConvertToInt(map["maxTokens"], options.MaxTokens);
                }
                if (map.ContainsKey("timeoutMs"))
                {
                    options.TimeoutMs = SafeConvertToInt(map["timeoutMs"], options.TimeoutMs);
                }

                options.MaxTokens = Math.Max(16, Math.Min(1024, options.MaxTokens));
                options.TimeoutMs = Math.Max(1000, Math.Min(60000, options.TimeoutMs));
                return options;
            }
            catch
            {
                return options;
            }
        }

        private void NotifyFormulaOcrSuccess(FormulaOcrResult result)
        {
            if (_taskPaneControl == null || !_taskPaneControl.IsWebViewReady)
            {
                return;
            }

            var payload = _serializer.Serialize(new
            {
                latex = result != null ? result.Latex : string.Empty,
                elapsedMs = result != null ? result.ElapsedMs : 0L,
                engine = result != null ? result.Engine : "onnxruntime-cpu"
            });

            _taskPaneControl.BeginInvoke(new Action(() =>
            {
                var script = "window.slideTex && window.slideTex.onFormulaOcrSuccess(" + payload + ");";
                _taskPaneControl.ExecuteScript(script);
            }));
        }

        private void NotifyFormulaOcrError(string code, string message)
        {
            if (_taskPaneControl == null || !_taskPaneControl.IsWebViewReady)
            {
                return;
            }

            var payload = _serializer.Serialize(new
            {
                code = string.IsNullOrWhiteSpace(code) ? "INFERENCE_FAILED" : code,
                message = string.IsNullOrWhiteSpace(message) ? "Formula OCR failed." : message
            });

            _taskPaneControl.BeginInvoke(new Action(() =>
            {
                var script = "window.slideTex && window.slideTex.onFormulaOcrError(" + payload + ");";
                _taskPaneControl.ExecuteScript(script);
            }));
        }

        private static string ToHostOcrCode(OcrErrorCode code)
        {
            switch (code)
            {
                case OcrErrorCode.ModelNotFound:
                    return "MODEL_NOT_FOUND";
                case OcrErrorCode.ModelInitFailed:
                    return "MODEL_INIT_FAILED";
                case OcrErrorCode.Timeout:
                    return "TIMEOUT";
                case OcrErrorCode.BadImage:
                    return "BAD_IMAGE";
                default:
                    return "INFERENCE_FAILED";
            }
        }

        private static int SafeConvertToInt(object value, int fallback)
        {
            if (value == null)
            {
                return fallback;
            }

            int intValue;
            if (value is int)
            {
                return (int)value;
            }
            if (value is long)
            {
                return (int)(long)value;
            }
            if (value is double)
            {
                return (int)(double)value;
            }
            if (int.TryParse(value.ToString(), out intValue))
            {
                return intValue;
            }

            return fallback;
        }

        /// <summary>
        /// Routes host commands coming from the WebUI bridge to controller actions.
        /// </summary>
        private void ExecuteCommand(HostCommandType commandType)
        {
            switch (commandType)
            {
                case HostCommandType.OpenPane:
                    OpenPane();
                    break;
                case HostCommandType.Insert:
                    InsertCurrentFormula();
                    break;
                case HostCommandType.Update:
                    UpdateSelectedFormula();
                    break;
                case HostCommandType.EditSelected:
                    EditSelectedFormula();
                    break;
                case HostCommandType.Renumber:
                    RenumberAllEquations();
                    break;
                default:
                    DiagLog.Warn("ExecuteCommand received unsupported command: " + commandType);
                    break;
            }
        }

        /// <summary>
        /// Validates that a successful render payload is available for insert/update actions.
        /// </summary>
        private bool EnsureLastRender()
        {
            if (_lastRender == null || !_lastRender.IsSuccess || string.IsNullOrWhiteSpace(_lastRender.PngBase64))
            {
                DiagLog.Warn("EnsureLastRender failed: no successful render payload is available.");
                ShowWarning(LocalizationManager.Get("warning.render_first"));
                return false;
            }

            if (string.IsNullOrWhiteSpace(_lastRender.Latex))
            {
                DiagLog.Warn("EnsureLastRender failed: current render payload does not contain latex source.");
                ShowWarning(LocalizationManager.Get("warning.render_no_latex"));
                return false;
            }

            return true;
        }

        private dynamic GetSelectedShape()
        {
            try
            {
                dynamic app = GetApplication();
                dynamic window = app.ActiveWindow;
                if (window == null)
                {
                    return null;
                }

                dynamic selection = window.Selection;
                if (selection == null)
                {
                    return null;
                }

                // 2 => ppSelectionShapes
                if ((int)selection.Type != 2)
                {
                    return null;
                }

                dynamic shapeRange = selection.ShapeRange;
                if (shapeRange == null || (int)shapeRange.Count < 1)
                {
                    return null;
                }

                return shapeRange[1];
            }
            catch
            {
                return null;
            }
        }

        private dynamic GetActiveSlide()
        {
            try
            {
                dynamic app = GetApplication();
                if (app == null)
                {
                    DiagLog.Warn("GetActiveSlide: Application is null.");
                    return null;
                }

                dynamic window = app.ActiveWindow;
                if (window == null)
                {
                    DiagLog.Warn("GetActiveSlide: ActiveWindow is null.");
                    return null;
                }

                dynamic view = window.View;
                if (view == null)
                {
                    DiagLog.Warn("GetActiveSlide: View is null.");
                    return null;
                }

                int viewType = (int)view.Type;
                DiagLog.Debug("GetActiveSlide: viewType=" + viewType);

                // ppViewNormal=9, ppViewSlide=1 support view.Slide
                // Other view types may not have a current slide
                if (viewType != 9 && viewType != 1)
                {
                    DiagLog.Warn("GetActiveSlide: Unsupported view type " + viewType + ". Try switching to Normal view.");
                    return null;
                }

                dynamic slide = view.Slide;
                if (slide == null)
                {
                    DiagLog.Warn("GetActiveSlide: view.Slide is null.");
                    return null;
                }

                DiagLog.Debug("GetActiveSlide: Found slide index=" + slide.SlideIndex);
                return slide;
            }
            catch (Exception ex)
            {
                DiagLog.Error("GetActiveSlide exception.", ex);
                return null;
            }
        }

        /// <summary>
        /// Computes placement and size for insertion, preserving scale when replacing a shape.
        /// </summary>
        private PowerPointInsertContext BuildInsertContext(dynamic slide, RenderSuccessPayload payload)
        {
            var options = payload.Options ?? new RenderOptionsDto();
            options.Validate();

            var newNaturalWidth = PowerPointEquationShapeService.PixelsToPoints(payload.PixelWidth, options.Dpi);
            var newNaturalHeight = PowerPointEquationShapeService.PixelsToPoints(payload.PixelHeight, options.Dpi);

            var selectedShape = GetSelectedShape();
            if (selectedShape != null)
            {
                // When replacing a selected formula, preserve its on-slide visual scale
                // relative to the previous natural pixel size.
                float newWidth = newNaturalWidth;
                float newHeight = newNaturalHeight;

                ShapeMetaV1 meta;
                if (_metadataStore.TryRead(new PowerPointShapeTagAccessor(selectedShape), out meta)
                    && meta != null && meta.PixelWidth > 0 && meta.PixelHeight > 0)
                {
                    var oldNaturalWidth = PowerPointEquationShapeService.PixelsToPoints(meta.PixelWidth, meta.RenderOptions.Dpi);
                    var oldNaturalHeight = PowerPointEquationShapeService.PixelsToPoints(meta.PixelHeight, meta.RenderOptions.Dpi);

                    if (oldNaturalWidth > 0f && oldNaturalHeight > 0f)
                    {
                        var scaleX = (float)selectedShape.Width / oldNaturalWidth;
                        var scaleY = (float)selectedShape.Height / oldNaturalHeight;
                        newWidth = newNaturalWidth * scaleX;
                        newHeight = newNaturalHeight * scaleY;
                    }
                }

                return new PowerPointInsertContext
                {
                    Slide = slide,
                    Left = (float)selectedShape.Left,
                    Top = (float)selectedShape.Top,
                    Width = newWidth,
                    Height = newHeight
                };
            }

            float slideWidth = 960f;
            float slideHeight = 540f;
            try
            {
                dynamic app = GetApplication();
                slideWidth = (float)app.ActivePresentation.PageSetup.SlideWidth;
                slideHeight = (float)app.ActivePresentation.PageSetup.SlideHeight;
            }
            catch
            {
                // Keep a safe default size when slide metrics are unavailable.
            }

            return new PowerPointInsertContext
            {
                Slide = slide,
                Left = Math.Max(0f, (slideWidth - newNaturalWidth) / 2f),
                Top = Math.Max(0f, (slideHeight - newNaturalHeight) / 2f),
                Width = newNaturalWidth,
                Height = newNaturalHeight
            };
        }

        /// <summary>
        /// Builds persistent shape metadata from the latest render payload.
        /// </summary>
        private ShapeMetaV1 BuildMeta(RenderSuccessPayload payload, bool autoNumbered = false, int autoNumberLineCount = 0)
        {
            var options = payload.Options ?? new RenderOptionsDto();
            return new ShapeMetaV1
            {
                LatexSource = payload.Latex,
                RenderOptions = options,
                PluginVersion = "0.1.0",
                ContentHash = ContentHashCalculator.Compute(payload.Latex, options),
                TimestampUtc = DateTimeOffset.UtcNow,
                PixelWidth = payload.PixelWidth,
                PixelHeight = payload.PixelHeight,
                AutoNumbered = autoNumbered,
                AutoNumberLineCount = autoNumbered ? Math.Max(0, autoNumberLineCount) : 0
            };
        }

        /// <summary>
        /// Sends a host-initiated render request to WebUI with a stable request envelope.
        /// </summary>
        private void RenderInPane(string latex, RenderOptionsDto options)
        {
            if (_taskPaneControl == null || !_taskPaneControl.IsWebViewReady)
            {
                return;
            }

            // Keep request shape stable so WebUI can reuse one entry point
            // for both host-initiated and user-initiated rendering.
            var payload = _serializer.Serialize(new
            {
                latex = latex,
                options = options
            });
            var script = "window.slideTex && window.slideTex.renderFromHost(" + payload + ");";
            _taskPaneControl.ExecuteScript(script);
        }

        private string ResolveWebUiPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var sourceWebUiPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "SlideTeX.WebUI", "index.html"));
#if DEBUG
            if (File.Exists(sourceWebUiPath))
            {
                DiagLog.Debug("ResolveWebUiPath hit (debug source): " + sourceWebUiPath);
                return sourceWebUiPath;
            }
#endif

            var candidates = new[]
            {
                Path.Combine(baseDir, "WebUI", "index.html"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "WebUI", "index.html")),
                sourceWebUiPath
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    DiagLog.Debug("ResolveWebUiPath hit: " + path);
                    return path;
                }
            }

            DiagLog.Warn("ResolveWebUiPath no candidate exists.");
            return null;
        }

        /// <summary>
        /// Lazily initializes WebView and reports localized user-facing diagnostics on failure.
        /// </summary>
        private void EnsureWebViewInitialized()
        {
            var stopwatch = Stopwatch.StartNew();
            DiagLog.Info("EnsureWebViewInitialized begin.");

            if (_webViewInitialized || _taskPaneControl == null)
            {
                DiagLog.Debug("EnsureWebViewInitialized skipped. initialized=" + _webViewInitialized + ", controlNull=" + (_taskPaneControl == null));
                return;
            }

            if (string.IsNullOrWhiteSpace(_webUiPagePath))
            {
                DiagLog.Warn("EnsureWebViewInitialized no web ui page path.");
                ShowWarning(LocalizationManager.Get("warning.webui_not_found_init"));
                return;
            }

            try
            {
                DiagLog.Debug("EnsureWebViewInitialized calling TaskPaneHostControl.Initialize.");
                _taskPaneControl.Initialize(_webUiPagePath, LocalizationManager.UICultureName);
                if (_taskPaneControl.IsWebViewReady)
                {
                    _webViewInitialized = true;
                    stopwatch.Stop();
                    DiagLog.Info("EnsureWebViewInitialized success. elapsedMs=" + stopwatch.ElapsedMilliseconds);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(_taskPaneControl.LastInitializationError))
                {
                    stopwatch.Stop();
                    DiagLog.Warn("EnsureWebViewInitialized finished with LastInitializationError=" + _taskPaneControl.LastInitializationError + ", elapsedMs=" + stopwatch.ElapsedMilliseconds);
                    ShowWarning(LocalizationManager.Format("warning.taskpane_init_failed", _taskPaneControl.LastInitializationError));
                }
                else
                {
                    stopwatch.Stop();
                    DiagLog.Warn("EnsureWebViewInitialized finished not-ready without explicit error. elapsedMs=" + stopwatch.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                DiagLog.Error("EnsureWebViewInitialized exception. elapsedMs=" + stopwatch.ElapsedMilliseconds, ex);
                ShowWarning(LocalizationManager.Format("warning.taskpane_init_exception", ex.Message));
            }
        }

        private static void ShowWarning(string message)
        {
            MessageBox.Show(
                message,
                LocalizationManager.Get("app.caption"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private static bool IsAutoNumberedLatex(string latex)
        {
            return GetAutoNumberLineCount(latex) > 0;
        }

        /// <summary>
        /// Counts auto-numbered equation lines based on environment and tag/nonumber markers.
        /// </summary>
        private static int GetAutoNumberLineCount(string latex)
        {
            ParsedNumberingEnvironment parsed;
            if (!TryParseNumberingEnvironment(latex, out parsed) || parsed.IsStarred)
            {
                return 0;
            }

            if (IsPerLineNumberingEnvironment(parsed.EnvironmentName))
            {
                int count = 0;
                var lines = SplitEnvironmentLines(parsed.Content);
                foreach (var line in lines)
                {
                    var info = AnalyzeLineNumbering(line);
                    if (!info.HasCustomTag && !info.SuppressAutoNumber)
                    {
                        count++;
                    }
                }

                return count;
            }

            // equation/multline are single-number environments.
            var envInfo = AnalyzeLineNumbering(parsed.Content);
            if (envInfo.HasCustomTag || envInfo.SuppressAutoNumber)
            {
                return 0;
            }

            return 1;
        }

        /// <summary>
        /// Rewrites equation environments with explicit \tag values starting from a seed number.
        /// </summary>
        private static string BuildNumberedLatex(string latex, int startNumber, out int consumedCount)
        {
            consumedCount = 0;
            ParsedNumberingEnvironment parsed;
            if (!TryParseNumberingEnvironment(latex, out parsed) || parsed.IsStarred)
            {
                return latex;
            }

            if (!IsPerLineNumberingEnvironment(parsed.EnvironmentName))
            {
                int singleNumber = Math.Max(1, startNumber);
                var info = AnalyzeLineNumbering(parsed.Content);
                var cleanedContent = StripLineNumberingCommands(parsed.Content).TrimEnd();
                string tagCommand = null;

                if (info.HasCustomTag)
                {
                    tagCommand = BuildTagCommand(info.CustomTagContent, info.CustomTagStarred);
                }
                else if (!info.SuppressAutoNumber)
                {
                    tagCommand = BuildTagCommand(singleNumber.ToString(), false);
                    consumedCount = 1;
                }

                if (consumedCount <= 0)
                {
                    return latex;
                }

                string singleBeginReplacement = "\\begin{" + parsed.EnvironmentName + "*}";
                string singleEndReplacement = "\\end{" + parsed.EnvironmentName + "*}";
                var singleBuilder = new StringBuilder();
                singleBuilder.Append(latex.Substring(0, parsed.BeginIndex));
                singleBuilder.Append(singleBeginReplacement);
                singleBuilder.Append(AppendTagToLine(cleanedContent, tagCommand));
                singleBuilder.Append(singleEndReplacement);
                singleBuilder.Append(latex.Substring(parsed.EndIndex + parsed.EndTokenLength));
                return singleBuilder.ToString();
            }

            var lines = SplitEnvironmentLines(parsed.Content);
            if (lines.Count == 0)
            {
                return latex;
            }

            int nextNumber = Math.Max(1, startNumber);
            var rebuiltLines = new List<string>(lines.Count);

            foreach (var line in lines)
            {
                var info = AnalyzeLineNumbering(line);
                var cleanedLine = StripLineNumberingCommands(line).TrimEnd();
                string tagCommand = null;

                if (info.HasCustomTag)
                {
                    tagCommand = BuildTagCommand(info.CustomTagContent, info.CustomTagStarred);
                }
                else if (!info.SuppressAutoNumber)
                {
                    tagCommand = BuildTagCommand(nextNumber.ToString(), false);
                    nextNumber++;
                    consumedCount++;
                }

                rebuiltLines.Add(AppendTagToLine(cleanedLine, tagCommand));
            }

            if (consumedCount <= 0)
            {
                return latex;
            }

            string beginReplacement = "\\begin{" + parsed.EnvironmentName + "*}";
            string endReplacement = "\\end{" + parsed.EnvironmentName + "*}";
            var builder = new StringBuilder();
            builder.Append(latex.Substring(0, parsed.BeginIndex));
            builder.Append(beginReplacement);
            builder.Append(string.Join(@"\\", rebuiltLines.ToArray()));
            builder.Append(endReplacement);
            builder.Append(latex.Substring(parsed.EndIndex + parsed.EndTokenLength));
            return builder.ToString();
        }

        private static bool IsPerLineNumberingEnvironment(string environmentName)
        {
            return string.Equals(environmentName, "align", StringComparison.Ordinal)
                || string.Equals(environmentName, "gather", StringComparison.Ordinal);
        }

        /// <summary>
        /// Parses the first supported numbering environment and its exact begin/end offsets.
        /// </summary>
        private static bool TryParseNumberingEnvironment(string latex, out ParsedNumberingEnvironment parsed)
        {
            parsed = null;
            if (string.IsNullOrWhiteSpace(latex))
            {
                return false;
            }

            var beginMatch = NumberingEnvBeginRegex.Match(latex);
            if (!beginMatch.Success)
            {
                return false;
            }

            string environmentName = beginMatch.Groups[1].Value;
            bool isStarred = beginMatch.Groups[2].Success;
            string endToken = "\\end{" + environmentName + (isStarred ? "*" : string.Empty) + "}";
            int endIndex = latex.IndexOf(endToken, beginMatch.Index + beginMatch.Length, StringComparison.Ordinal);
            if (endIndex < 0)
            {
                string fallbackEndToken = "\\end{" + environmentName + "}";
                endIndex = latex.IndexOf(fallbackEndToken, beginMatch.Index + beginMatch.Length, StringComparison.Ordinal);
                if (endIndex < 0)
                {
                    fallbackEndToken = "\\end{" + environmentName + "*}";
                    endIndex = latex.IndexOf(fallbackEndToken, beginMatch.Index + beginMatch.Length, StringComparison.Ordinal);
                    if (endIndex < 0)
                    {
                        return false;
                    }
                }

                endToken = fallbackEndToken;
                isStarred = endToken.EndsWith("*}", StringComparison.Ordinal);
            }

            int contentStart = beginMatch.Index + beginMatch.Length;
            parsed = new ParsedNumberingEnvironment
            {
                BeginIndex = beginMatch.Index,
                BeginTokenLength = beginMatch.Length,
                EndIndex = endIndex,
                EndTokenLength = endToken.Length,
                EnvironmentName = environmentName,
                IsStarred = isStarred,
                Content = latex.Substring(contentStart, endIndex - contentStart)
            };
            return true;
        }

        private static List<string> SplitEnvironmentLines(string content)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(content))
            {
                lines.Add(string.Empty);
                return lines;
            }

            var parts = LineBreakRegex.Split(content);
            if (parts.Length == 0)
            {
                lines.Add(content);
                return lines;
            }

            lines.AddRange(parts);
            return lines;
        }

        /// <summary>
        /// Extracts numbering directives on a single logical line (tag/nonumber/notag).
        /// </summary>
        private static LineNumberingInfo AnalyzeLineNumbering(string line)
        {
            var info = new LineNumberingInfo
            {
                SuppressAutoNumber = NumberingSuppressionRegex.IsMatch(line ?? string.Empty)
            };

            int searchStart = 0;
            int tagStart;
            while (TryFindTagCommand(line, searchStart, out tagStart))
            {
                int tagEnd;
                string tagContent;
                bool isTagStarred;
                if (TryParseTagCommand(line, tagStart, out tagEnd, out tagContent, out isTagStarred))
                {
                    info.HasCustomTag = true;
                    info.CustomTagContent = tagContent;
                    info.CustomTagStarred = isTagStarred;
                    return info;
                }

                searchStart = tagStart + 4;
            }

            return info;
        }

        /// <summary>
        /// Removes numbering directives so renumbering can rebuild deterministic tag commands.
        /// </summary>
        private static string StripLineNumberingCommands(string line)
        {
            string cleaned = NumberingSuppressionRegex.Replace(line ?? string.Empty, string.Empty);
            int searchStart = 0;
            int cursor = 0;
            StringBuilder builder = null;

            int tagStart;
            while (TryFindTagCommand(cleaned, searchStart, out tagStart))
            {
                int tagEnd;
                string tagContent;
                bool isTagStarred;
                if (!TryParseTagCommand(cleaned, tagStart, out tagEnd, out tagContent, out isTagStarred))
                {
                    searchStart = tagStart + 4;
                    continue;
                }

                if (builder == null)
                {
                    builder = new StringBuilder(cleaned.Length);
                }

                builder.Append(cleaned, cursor, tagStart - cursor);
                cursor = tagEnd;
                searchStart = tagEnd;
            }

            if (builder == null)
            {
                return cleaned;
            }

            builder.Append(cleaned, cursor, cleaned.Length - cursor);
            return builder.ToString();
        }

        private static bool TryFindTagCommand(string line, int startIndex, out int tagStart)
        {
            tagStart = -1;
            if (string.IsNullOrEmpty(line) || startIndex >= line.Length)
            {
                return false;
            }

            for (int i = Math.Max(0, startIndex); i <= line.Length - 4; i++)
            {
                if (line[i] != '\\')
                {
                    continue;
                }

                if (line[i + 1] != 't' || line[i + 2] != 'a' || line[i + 3] != 'g')
                {
                    continue;
                }

                int next = i + 4;
                if (next < line.Length && char.IsLetter(line[next]))
                {
                    continue;
                }

                tagStart = i;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Parses a \tag or \tag* command, including nested braces, and returns the parse span.
        /// </summary>
        private static bool TryParseTagCommand(
            string line,
            int tagStart,
            out int tagEnd,
            out string tagContent,
            out bool isTagStarred)
        {
            tagEnd = -1;
            tagContent = string.Empty;
            isTagStarred = false;

            if (string.IsNullOrEmpty(line) || tagStart < 0 || tagStart + 4 > line.Length)
            {
                return false;
            }

            int index = tagStart + 4;
            if (index < line.Length && line[index] == '*')
            {
                isTagStarred = true;
                index++;
            }

            while (index < line.Length && char.IsWhiteSpace(line[index]))
            {
                index++;
            }

            if (index >= line.Length || line[index] != '{')
            {
                return false;
            }

            int contentStart = index + 1;
            int depth = 1;
            index++;
            while (index < line.Length && depth > 0)
            {
                if (line[index] == '{')
                {
                    depth++;
                }
                else if (line[index] == '}')
                {
                    depth--;
                }

                index++;
            }

            if (depth != 0)
            {
                return false;
            }

            tagEnd = index;
            tagContent = line.Substring(contentStart, index - contentStart - 1);
            return true;
        }

        private static string BuildTagCommand(string content, bool starred)
        {
            return "\\tag" + (starred ? "*" : string.Empty) + "{" + (content ?? string.Empty) + "}";
        }

        private static string AppendTagToLine(string line, string tagCommand)
        {
            if (string.IsNullOrEmpty(tagCommand))
            {
                return line ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                return tagCommand;
            }

            return line.TrimEnd() + " " + tagCommand;
        }

        /// <summary>
        /// Scans all slides and returns auto-numbered shapes in visual reading order.
        /// </summary>
        private List<AutoNumberedShapeInfo> ScanAutoNumberedShapes()
        {
            var results = new List<AutoNumberedShapeInfo>();
            try
            {
                dynamic app = GetApplication();
                if (app == null || app.ActivePresentation == null)
                {
                    return results;
                }

                dynamic slides = app.ActivePresentation.Slides;
                int slideCount = (int)slides.Count;
                for (int si = 1; si <= slideCount; si++)
                {
                    dynamic slide = slides[si];
                    dynamic shapes = slide.Shapes;
                    int shapeCount = (int)shapes.Count;
                    for (int shi = 1; shi <= shapeCount; shi++)
                    {
                        dynamic shape = shapes[shi];
                        ShapeMetaV1 meta;
                        if (_metadataStore.TryRead(new PowerPointShapeTagAccessor(shape), out meta)
                            && meta != null && meta.AutoNumbered)
                        {
                            results.Add(new AutoNumberedShapeInfo
                            {
                                Shape = shape,
                                Meta = meta,
                                SlideIndex = si,
                                Top = (float)shape.Top,
                                Left = (float)shape.Left,
                                AutoNumberLineCount = meta.AutoNumberLineCount > 0
                                    ? meta.AutoNumberLineCount
                                    : Math.Max(1, GetAutoNumberLineCount(meta.LatexSource))
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DiagLog.Warn("ScanAutoNumberedShapes failed: " + ex.Message);
            }

            // Re-number equations by visual reading order (slide, top, left).
            results.Sort((a, b) =>
            {
                int cmp = a.SlideIndex.CompareTo(b.SlideIndex);
                if (cmp != 0) return cmp;
                cmp = a.Top.CompareTo(b.Top);
                if (cmp != 0) return cmp;
                return a.Left.CompareTo(b.Left);
            });

            return results;
        }

        /// <summary>
        /// Wraps synchronous render waiting with busy-state and timer suspension safeguards.
        /// </summary>
        private RenderSuccessPayload RenderAndWait(
            string latex,
            RenderOptionsDto options,
            string renderLatex = null,
            int timeoutMs = 10000)
        {
            DiagLog.Debug("RenderAndWait begin. timeoutMs=" + timeoutMs + ", hasRenderLatex=" + !string.IsNullOrWhiteSpace(renderLatex));
            if (_taskPaneControl == null || !_taskPaneControl.IsWebViewReady)
            {
                DiagLog.Warn("RenderAndWait skipped: task pane host is not ready.");
                return null;
            }

            _isBusyRendering = true;
            if (_selectionTimer != null)
            {
                _selectionTimer.Stop();
                DiagLog.Debug("RenderAndWait selection timer stopped.");
            }
            try
            {
                return RenderAndWaitCore(latex, options, renderLatex, timeoutMs);
            }
            finally
            {
                _isBusyRendering = false;
                if (_selectionTimer != null && _taskPane != null && _taskPane.Visible)
                {
                    _selectionTimer.Start();
                }
                DiagLog.Debug("RenderAndWait end. isBusy reset to false.");
            }
        }

        /// <summary>
        /// Performs host-to-WebUI render request and waits for callback while pumping UI messages.
        /// </summary>
        private RenderSuccessPayload RenderAndWaitCore(
            string latex,
            RenderOptionsDto options,
            string renderLatex,
            int timeoutMs)
        {
            var tcs = new System.Threading.ManualResetEvent(false);
            RenderSuccessPayload result = null;
            Exception renderError = null;

            EventHandler<RenderNotificationEventArgs> handler = null;
            handler = (s, e) =>
            {
                DiagLog.Debug("RenderAndWaitCore handler fired. isSuccess=" + e.IsSuccess);
                _taskPaneControl.RenderNotificationReceived -= handler;
                if (e.IsSuccess && !string.IsNullOrWhiteSpace(e.Payload))
                {
                    try
                    {
                        var payload = _serializer.Deserialize<RenderSuccessPayload>(e.Payload);
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

            _taskPaneControl.RenderNotificationReceived += handler;
            DiagLog.Debug("RenderAndWaitCore handler subscribed. Calling ExecuteScript.");

            var renderPayload = _serializer.Serialize(new
            {
                latex = latex,
                options = options,
                renderLatex = !string.IsNullOrWhiteSpace(renderLatex) ? renderLatex : null
            });
            var script = "window.slideTex && window.slideTex.renderFromHost(" + renderPayload + ");";
            _taskPaneControl.ExecuteScript(script);
            DiagLog.Debug("RenderAndWaitCore ExecuteScript returned. Entering wait loop.");

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            int loopCount = 0;
            // Keep pumping UI messages while waiting for WebView callback to avoid COM/UI deadlock.
            while (!tcs.WaitOne(0) && DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(10);
                loopCount++;
                if (loopCount % 100 == 0)
                {
                    DiagLog.Debug("RenderAndWaitCore still waiting. loopCount=" + loopCount);
                }
            }

            DiagLog.Debug("RenderAndWaitCore loop exited. loopCount=" + loopCount + " signaled=" + tcs.WaitOne(0));

            if (!tcs.WaitOne(0))
            {
                _taskPaneControl.RenderNotificationReceived -= handler;
                DiagLog.Warn("RenderAndWaitCore timeout.");
                throw new TimeoutException(LocalizationManager.Get("error.render_timeout"));
            }

            if (renderError != null)
            {
                DiagLog.Warn("RenderAndWaitCore failed: " + renderError.Message);
                throw renderError;
            }

            return result;
        }

        /// <summary>
        /// Reassigns equation numbers across the whole presentation using metadata order rules.
        /// </summary>
        public void RenumberAllEquations()
        {
            DiagLog.Info("RenumberAllEquations begin.");
            if (_taskPaneControl == null || !_taskPaneControl.IsWebViewReady)
            {
                EnsureWebViewInitialized();
                if (_taskPaneControl == null || !_taskPaneControl.IsWebViewReady)
                {
                    ShowWarning(LocalizationManager.Get("warning.open_panel_first"));
                    return;
                }
            }

            try
            {
                var shapes = ScanAutoNumberedShapes();
                if (shapes.Count == 0)
                {
                    DiagLog.Info("RenumberAllEquations skipped: no auto-numbered equations found.");
                    MessageBox.Show(LocalizationManager.Get("info.renumber_none"), LocalizationManager.Get("app.caption"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int number = 1;
                foreach (var info in shapes)
                {
                    int consumedCount;
                    string numberedLatex = BuildNumberedLatex(info.Meta.LatexSource, number, out consumedCount);
                    if (consumedCount <= 0)
                    {
                        continue;
                    }

                    var rendered = RenderAndWait(info.Meta.LatexSource, info.Meta.RenderOptions, numberedLatex);
                    if (rendered == null)
                    {
                        continue;
                    }

                    var newOptions = rendered.Options ?? new RenderOptionsDto();
                    float newWidth = PowerPointEquationShapeService.PixelsToPoints(rendered.PixelWidth, newOptions.Dpi);
                    float newHeight = PowerPointEquationShapeService.PixelsToPoints(rendered.PixelHeight, newOptions.Dpi);

                    if (info.Meta.PixelWidth > 0 && info.Meta.PixelHeight > 0)
                    {
                        var oldW = PowerPointEquationShapeService.PixelsToPoints(info.Meta.PixelWidth, info.Meta.RenderOptions.Dpi);
                        var oldH = PowerPointEquationShapeService.PixelsToPoints(info.Meta.PixelHeight, info.Meta.RenderOptions.Dpi);
                        if (oldW > 0f && oldH > 0f)
                        {
                            var sx = (float)info.Shape.Width / oldW;
                            var sy = (float)info.Shape.Height / oldH;
                            newWidth = newWidth * sx;
                            newHeight = newHeight * sy;
                        }
                    }

                    var newShape = _equationShapeService.Update(info.Shape, rendered, newWidth, newHeight);
                    var meta = BuildMeta(rendered, true, consumedCount);
                    meta.LatexSource = info.Meta.LatexSource;
                    _metadataStore.Write(new PowerPointShapeTagAccessor(newShape), meta);
                    number += consumedCount;
                }

                MessageBox.Show(LocalizationManager.Format("info.renumber_done", number - 1), LocalizationManager.Get("app.caption"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                DiagLog.Info("RenumberAllEquations completed. updatedCount=" + (number - 1));
            }
            catch (Exception ex)
            {
                DiagLog.Error("RenumberAllEquations failed.", ex);
                ShowWarning(LocalizationManager.Format("warning.renumber_failed", ex.Message));
            }
        }

        private sealed class AutoNumberedShapeInfo
        {
            public dynamic Shape { get; set; }
            public ShapeMetaV1 Meta { get; set; }
            public int SlideIndex { get; set; }
            public float Top { get; set; }
            public float Left { get; set; }
            public int AutoNumberLineCount { get; set; }
        }

        private sealed class ParsedNumberingEnvironment
        {
            public int BeginIndex { get; set; }
            public int BeginTokenLength { get; set; }
            public int EndIndex { get; set; }
            public int EndTokenLength { get; set; }
            public string EnvironmentName { get; set; }
            public bool IsStarred { get; set; }
            public string Content { get; set; }
        }

        private sealed class LineNumberingInfo
        {
            public bool SuppressAutoNumber { get; set; }
            public bool HasCustomTag { get; set; }
            public bool CustomTagStarred { get; set; }
            public string CustomTagContent { get; set; }
        }
    }
}
