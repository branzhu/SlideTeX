// SlideTeX Note: Error code contract for formula OCR execution.

namespace SlideTeX.VstoAddin.Ocr
{
    internal enum OcrErrorCode
    {
        ModelNotFound = 0,
        ModelInitFailed = 1,
        InferenceFailed = 2,
        Timeout = 3,
        BadImage = 4
    }
}

