// SlideTeX Note: OCR result payload produced by host-side inference service.

namespace SlideTeX.VstoAddin.Ocr
{
    internal sealed class FormulaOcrResult
    {
        public string Latex { get; set; }

        public long ElapsedMs { get; set; }

        public string Engine { get; set; }
    }
}

