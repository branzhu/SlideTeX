// SlideTeX Note: Data class for auto-numbered equation shapes during renumbering.

using SlideTeX.VstoAddin.Metadata;

namespace SlideTeX.VstoAddin.Models
{
    internal sealed class AutoNumberedShapeInfo
    {
        public dynamic Shape { get; set; }
        public ShapeMetaV1 Meta { get; set; }
        public int SlideIndex { get; set; }
        public float Top { get; set; }
        public float Left { get; set; }
        public int AutoNumberLineCount { get; set; }
    }
}
