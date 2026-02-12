// SlideTeX Note: Typed OCR exception surfaced from model/runtime pipeline.

using System;

namespace SlideTeX.VstoAddin.Ocr
{
    internal sealed class FormulaOcrException : Exception
    {
        public FormulaOcrException(OcrErrorCode code, string message)
            : base(message)
        {
            Code = code;
        }

        public FormulaOcrException(OcrErrorCode code, string message, Exception innerException)
            : base(message, innerException)
        {
            Code = code;
        }

        public OcrErrorCode Code { get; private set; }
    }
}

