// SlideTeX Note: Metadata schema and utility code for persisted render state on shapes.

namespace SlideTeX.VstoAddin.Metadata
{
    /// <summary>
    /// Canonical shape tag keys used to persist SlideTeX metadata in PowerPoint.
    /// </summary>
    public static class MetadataKeys
    {
        public const string MetaVersion = "SLIDETEX_META_VERSION";
        public const string Latex = "SLIDETEX_LATEX";
        public const string RenderOptions = "SLIDETEX_RENDER_OPTIONS";
        public const string ContentHash = "SLIDETEX_CONTENT_HASH";
        public const string PluginVersion = "SLIDETEX_PLUGIN_VERSION";
        public const string TimestampUtc = "SLIDETEX_TIMESTAMP_UTC";
        public const string PixelWidth = "SLIDETEX_PIXEL_WIDTH";
        public const string PixelHeight = "SLIDETEX_PIXEL_HEIGHT";
        public const string AutoNumbered = "SLIDETEX_AUTO_NUMBERED";
        public const string AutoNumberLineCount = "SLIDETEX_AUTO_NUMBER_LINE_COUNT";

        public const string CurrentVersion = "1";
    }
}


