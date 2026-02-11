// SlideTeX Note: Metadata schema and utility code for persisted render state on shapes.

using System;

namespace SlideTeX.VstoAddin.Metadata
{
    /// <summary>
    /// Persisted metadata schema attached to each SlideTeX-generated shape.
    /// </summary>
    public sealed class ShapeMetaV1
    {
        /// <summary>
        /// Canonical latex source used to regenerate the formula.
        /// </summary>
        public string LatexSource { get; set; } = string.Empty;

        /// <summary>
        /// Render options used for current shape content.
        /// </summary>
        public RenderOptionsDto RenderOptions { get; set; } = new RenderOptionsDto();

        /// <summary>
        /// Host plugin version that wrote this metadata.
        /// </summary>
        public string PluginVersion { get; set; } = string.Empty;

        /// <summary>
        /// Deterministic hash of latex source and render options.
        /// </summary>
        public string ContentHash { get; set; } = string.Empty;

        /// <summary>
        /// UTC timestamp when metadata was last written.
        /// </summary>
        public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Rendered image width in pixels.
        /// </summary>
        public int PixelWidth { get; set; }

        /// <summary>
        /// Rendered image height in pixels.
        /// </summary>
        public int PixelHeight { get; set; }

        /// <summary>
        /// Whether this formula participates in automatic equation numbering.
        /// </summary>
        public bool AutoNumbered { get; set; }

        /// <summary>
        /// Number of equation labels consumed by this shape during numbering.
        /// </summary>
        public int AutoNumberLineCount { get; set; }
    }
}


