// SlideTeX Note: Metadata schema and utility code for persisted render state on shapes.

namespace SlideTeX.VstoAddin.Metadata
{
    /// <summary>
    /// WebUI render result payload consumed by host insert/update workflows.
    /// </summary>
    public sealed class RenderSuccessPayload
    {
        /// <summary>
        /// Indicates whether render request succeeded.
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// Error message when render fails.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Base64-encoded PNG output without data URL prefix.
        /// </summary>
        public string PngBase64 { get; set; }

        /// <summary>
        /// Width of rendered PNG in pixels.
        /// </summary>
        public int PixelWidth { get; set; }

        /// <summary>
        /// Height of rendered PNG in pixels.
        /// </summary>
        public int PixelHeight { get; set; }

        /// <summary>
        /// Original latex source used for rendering.
        /// </summary>
        public string Latex { get; set; }

        /// <summary>
        /// Effective render options applied during this render.
        /// </summary>
        public RenderOptionsDto Options { get; set; } = new RenderOptionsDto();
    }
}


