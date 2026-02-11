// SlideTeX Note: VSTO startup and shutdown entrypoint that initializes add-in services.

using Office = Microsoft.Office.Core;
using SlideTeX.VstoAddin.Diagnostics;
using SlideTeX.VstoAddin.Localization;

namespace SlideTeX.VstoAddin
{
    /// <summary>
    /// VSTO add-in entrypoint that owns controller lifetime for PowerPoint sessions.
    /// </summary>
    public partial class ThisAddIn
    {
        private SlideTeXAddinController _controller;

        /// <summary>
        /// Initializes localization and controller resources when PowerPoint loads the add-in.
        /// </summary>
        private void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            DiagLog.Info("ThisAddIn_Startup begin.");
            LocalizationManager.Initialize(this);
            _controller = new SlideTeXAddinController(this);
            _controller.Initialize();
            DiagLog.Info("ThisAddIn_Startup end.");
        }

        /// <summary>
        /// Disposes controller resources during add-in shutdown.
        /// </summary>
        private void ThisAddIn_Shutdown(object sender, System.EventArgs e)
        {
            DiagLog.Info("ThisAddIn_Shutdown begin.");
            if (_controller != null)
            {
                _controller.Dispose();
                _controller = null;
            }

            DiagLog.Info("ThisAddIn_Shutdown end.");
        }

        /// <summary>
        /// Provides Ribbon implementation for this add-in.
        /// </summary>
        protected override Office.IRibbonExtensibility CreateRibbonExtensibilityObject()
        {
            LocalizationManager.Initialize(this);
            return new SlideTeXRibbon(this);
        }

        /// <summary>
        /// Ribbon callback entry used by SlideTeXRibbon button actions.
        /// </summary>
        internal void OpenPaneFromRibbon()
        {
            DiagLog.Info("OpenPaneFromRibbon invoked.");
            if (_controller != null)
            {
                _controller.OpenPane();
            }
        }

        #region VSTO 生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InternalStartup()
        {
            this.Startup += new System.EventHandler(ThisAddIn_Startup);
            this.Shutdown += new System.EventHandler(ThisAddIn_Shutdown);
        }
        
        #endregion
    }
}


