// SlideTeX Note: OCR options passed from WebUI to host-side inference service.

namespace SlideTeX.VstoAddin.Ocr
{
    internal sealed class FormulaOcrOptions
    {
        public int MaxTokens { get; set; }

        public int TimeoutMs { get; set; }

        public static FormulaOcrOptions Default
        {
            get
            {
                return new FormulaOcrOptions
                {
                    MaxTokens = 256,
                    TimeoutMs = 15000
                };
            }
        }
    }
}

