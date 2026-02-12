// SlideTeX Note: Host command and notification contract shared across add-in components.

using System;

namespace SlideTeX.VstoAddin.Contracts
{
    /// <summary>
    /// Event args carrying image payload and OCR options from WebUI to host.
    /// </summary>
    public sealed class FormulaOcrRequestedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates OCR request event payload.
        /// </summary>
        public FormulaOcrRequestedEventArgs(string imageBase64, string optionsJson)
        {
            ImageBase64 = imageBase64;
            OptionsJson = optionsJson;
        }

        /// <summary>
        /// Image content encoded as Base64 or data URL.
        /// </summary>
        public string ImageBase64 { get; }

        /// <summary>
        /// Optional OCR options JSON.
        /// </summary>
        public string OptionsJson { get; }
    }
}

