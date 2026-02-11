// SlideTeX Note: Ribbon command handlers that route user actions to add-in workflows.

using System;
using System.Runtime.InteropServices;
using Office = Microsoft.Office.Core;
using SlideTeX.VstoAddin.Diagnostics;
using SlideTeX.VstoAddin.Localization;

namespace SlideTeX.VstoAddin
{
    /// <summary>
    /// Defines PowerPoint Ribbon XML and callbacks for launching SlideTeX pane actions.
    /// </summary>
    [ComVisible(true)]
    public sealed class SlideTeXRibbon : Office.IRibbonExtensibility
    {
        private readonly ThisAddIn _addIn;

        public SlideTeXRibbon(ThisAddIn addIn)
        {
            _addIn = addIn ?? throw new ArgumentNullException(nameof(addIn));
        }

        /// <summary>
        /// Returns Ribbon XML only for the PowerPoint presentation surface.
        /// </summary>
        public string GetCustomUI(string ribbonId)
        {
            return ribbonId == "Microsoft.PowerPoint.Presentation" ? BuildRibbonXml() : null;
        }

        /// <summary>
        /// Ribbon load callback required by Office customUI contract.
        /// </summary>
        public void OnRibbonLoad(Office.IRibbonUI ribbonUi)
        {
            _ = ribbonUi;
        }

        /// <summary>
        /// Ribbon button callback that opens the SlideTeX task pane.
        /// </summary>
        public void OnOpenPane(Office.IRibbonControl control)
        {
            _ = control;
            DiagLog.Info("Ribbon.OnOpenPane clicked.");
            _addIn.OpenPaneFromRibbon();
        }

        private static string BuildRibbonXml()
        {
            var groupLabel = EscapeXmlAttribute(LocalizationManager.Get("ribbon.group_main_label"));
            var buttonLabel = EscapeXmlAttribute(LocalizationManager.Get("ribbon.open_pane_label"));

            return @"
<customUI xmlns='http://schemas.microsoft.com/office/2009/07/customui' onLoad='OnRibbonLoad'>
  <ribbon>
    <tabs>
      <tab idMso='TabInsert'>
        <group id='SlideTeX.GroupMain' label='" + groupLabel + @"'>
          <button id='SlideTeX.BtnOpenPane'
                  label='" + buttonLabel + @"'
                  size='large'
                  imageMso='EquationInsertGallery'
                  onAction='OnOpenPane'/>
        </group>
      </tab>
    </tabs>
  </ribbon>
</customUI>";
        }

        private static string EscapeXmlAttribute(string value)
        {
            return (value ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("'", "&apos;")
                .Replace("\"", "&quot;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }
    }
}


