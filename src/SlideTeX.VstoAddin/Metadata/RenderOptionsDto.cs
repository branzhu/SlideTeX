// SlideTeX Note: Metadata schema and utility code for persisted render state on shapes.

using System;

namespace SlideTeX.VstoAddin.Metadata
{
    /// <summary>
    /// Render options shared between WebUI and host metadata persistence.
    /// </summary>
    public sealed class RenderOptionsDto
    {
        /// <summary>
        /// Target font size in points.
        /// </summary>
        public double FontPt { get; set; } = 24;

        /// <summary>
        /// Output DPI used for PNG generation.
        /// </summary>
        public int Dpi { get; set; } = 300;

        /// <summary>
        /// Foreground color in #RRGGBB form.
        /// </summary>
        public string ColorHex { get; set; } = "#000000";

        /// <summary>
        /// Whether rendered PNG background is transparent.
        /// </summary>
        public bool IsTransparent { get; set; } = true;

        /// <summary>
        /// Preferred display mode: auto/inline/display.
        /// </summary>
        public string DisplayMode { get; set; } = "auto";

        /// <summary>
        /// Compatibility mode for KaTeX strict handling.
        /// </summary>
        public string ToleranceMode { get; set; } = "strict";

        /// <summary>
        /// Enables strict parsing behavior when true.
        /// </summary>
        public bool StrictMode { get; set; } = true;

        /// <summary>
        /// Validates option boundaries accepted by current rendering pipeline.
        /// </summary>
        public void Validate()
        {
            if (FontPt <= 0)
            {
                throw new ArgumentOutOfRangeException("FontPt", "FontPt must be greater than zero.");
            }

            if (Dpi != 150 && Dpi != 300 && Dpi != 600)
            {
                throw new ArgumentOutOfRangeException("Dpi", "Dpi must be one of 150, 300 or 600.");
            }

            if (string.IsNullOrWhiteSpace(ColorHex) || ColorHex.Length != 7 || ColorHex[0] != '#')
            {
                throw new ArgumentException("ColorHex must be in #RRGGBB format.", "ColorHex");
            }
        }
    }
}


