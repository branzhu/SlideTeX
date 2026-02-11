// SlideTeX Note: Metadata schema and utility code for persisted render state on shapes.

using System;
using System.Web.Script.Serialization;
using SlideTeX.VstoAddin.PowerPoint;

namespace SlideTeX.VstoAddin.Metadata
{
    /// <summary>
    /// Reads and writes SlideTeX metadata fields stored in PowerPoint shape tags.
    /// </summary>
    public sealed class ShapeTagMetadataStore
    {
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();

        /// <summary>
        /// Attempts to reconstruct metadata from shape tags for previously inserted equations.
        /// </summary>
        public bool TryRead(PowerPointShapeTagAccessor tagAccessor, out ShapeMetaV1 meta)
        {
            if (tagAccessor == null)
            {
                throw new ArgumentNullException("tagAccessor");
            }

            meta = null;
            var version = tagAccessor.GetTag(MetadataKeys.MetaVersion);
            if (!string.Equals(version, MetadataKeys.CurrentVersion, StringComparison.Ordinal))
            {
                return false;
            }

            var latex = tagAccessor.GetTag(MetadataKeys.Latex);
            var optionsJson = tagAccessor.GetTag(MetadataKeys.RenderOptions);
            var contentHash = tagAccessor.GetTag(MetadataKeys.ContentHash);
            var pluginVersion = tagAccessor.GetTag(MetadataKeys.PluginVersion) ?? "unknown";
            var timestampRaw = tagAccessor.GetTag(MetadataKeys.TimestampUtc);
            var pixelWidthRaw = tagAccessor.GetTag(MetadataKeys.PixelWidth);
            var pixelHeightRaw = tagAccessor.GetTag(MetadataKeys.PixelHeight);
            var autoNumberLineCountRaw = tagAccessor.GetTag(MetadataKeys.AutoNumberLineCount);

            if (string.IsNullOrWhiteSpace(latex) || string.IsNullOrWhiteSpace(optionsJson) || string.IsNullOrWhiteSpace(contentHash))
            {
                return false;
            }

            RenderOptionsDto options;
            try
            {
                options = _serializer.Deserialize<RenderOptionsDto>(optionsJson);
            }
            catch
            {
                return false;
            }

            if (options == null)
            {
                return false;
            }

            int pixelWidth = 0;
            int pixelHeight = 0;
            if (!string.IsNullOrWhiteSpace(pixelWidthRaw))
            {
                int.TryParse(pixelWidthRaw, out pixelWidth);
            }

            if (!string.IsNullOrWhiteSpace(pixelHeightRaw))
            {
                int.TryParse(pixelHeightRaw, out pixelHeight);
            }

            meta = new ShapeMetaV1
            {
                LatexSource = latex,
                RenderOptions = options,
                PluginVersion = pluginVersion,
                ContentHash = contentHash,
                TimestampUtc = ParseTimestamp(timestampRaw),
                PixelWidth = pixelWidth,
                PixelHeight = pixelHeight
            };

            var autoNumberedStr = tagAccessor.GetTag(MetadataKeys.AutoNumbered);
            meta.AutoNumbered = string.Equals(autoNumberedStr, "true", StringComparison.OrdinalIgnoreCase);
            int autoNumberLineCount = 0;
            if (!string.IsNullOrWhiteSpace(autoNumberLineCountRaw))
            {
                int.TryParse(autoNumberLineCountRaw, out autoNumberLineCount);
            }

            meta.AutoNumberLineCount = autoNumberLineCount;

            return true;
        }

        /// <summary>
        /// Persists normalized metadata fields back to shape tags.
        /// </summary>
        public void Write(PowerPointShapeTagAccessor tagAccessor, ShapeMetaV1 meta)
        {
            if (tagAccessor == null)
            {
                throw new ArgumentNullException("tagAccessor");
            }

            if (meta == null)
            {
                throw new ArgumentNullException("meta");
            }

            meta.RenderOptions.Validate();
            tagAccessor.SetTag(MetadataKeys.MetaVersion, MetadataKeys.CurrentVersion);
            tagAccessor.SetTag(MetadataKeys.Latex, meta.LatexSource);
            tagAccessor.SetTag(MetadataKeys.RenderOptions, _serializer.Serialize(meta.RenderOptions));
            tagAccessor.SetTag(MetadataKeys.ContentHash, meta.ContentHash);
            tagAccessor.SetTag(MetadataKeys.PluginVersion, meta.PluginVersion);
            tagAccessor.SetTag(MetadataKeys.TimestampUtc, meta.TimestampUtc.ToString("O"));
            if (meta.PixelWidth > 0)
            {
                tagAccessor.SetTag(MetadataKeys.PixelWidth, meta.PixelWidth.ToString());
            }

            if (meta.PixelHeight > 0)
            {
                tagAccessor.SetTag(MetadataKeys.PixelHeight, meta.PixelHeight.ToString());
            }

            tagAccessor.SetTag(MetadataKeys.AutoNumbered, meta.AutoNumbered ? "true" : "false");
            if (meta.AutoNumberLineCount > 0)
            {
                tagAccessor.SetTag(MetadataKeys.AutoNumberLineCount, meta.AutoNumberLineCount.ToString());
            }
            else
            {
                tagAccessor.SetTag(MetadataKeys.AutoNumberLineCount, "0");
            }
        }

        /// <summary>
        /// Parses UTC timestamp tags and falls back to current time when missing/invalid.
        /// </summary>
        private static DateTimeOffset ParseTimestamp(string raw)
        {
            DateTimeOffset timestamp;
            if (!string.IsNullOrWhiteSpace(raw) && DateTimeOffset.TryParse(raw, out timestamp))
            {
                return timestamp;
            }

            return DateTimeOffset.UtcNow;
        }
    }
}


