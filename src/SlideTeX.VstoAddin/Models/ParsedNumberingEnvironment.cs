// SlideTeX Note: Parsed result of a LaTeX numbering environment (equation/align/gather).

namespace SlideTeX.VstoAddin.Models
{
    internal sealed class ParsedNumberingEnvironment
    {
        public int BeginIndex { get; set; }
        public int BeginTokenLength { get; set; }
        public int EndIndex { get; set; }
        public int EndTokenLength { get; set; }
        public string EnvironmentName { get; set; }
        public bool IsStarred { get; set; }
        public string Content { get; set; }
    }
}
