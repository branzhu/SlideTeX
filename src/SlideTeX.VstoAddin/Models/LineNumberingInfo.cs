// SlideTeX Note: Numbering directive analysis for a single logical line.

namespace SlideTeX.VstoAddin.Models
{
    internal sealed class LineNumberingInfo
    {
        public bool SuppressAutoNumber { get; set; }
        public bool HasCustomTag { get; set; }
        public bool CustomTagStarred { get; set; }
        public string CustomTagContent { get; set; }
    }
}
