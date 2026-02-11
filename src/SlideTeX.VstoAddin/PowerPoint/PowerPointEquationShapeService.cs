// SlideTeX Note: PowerPoint interop helpers for equation insertion, tagging, and synchronization.

using System;
using System.IO;
using SlideTeX.VstoAddin.Metadata;

namespace SlideTeX.VstoAddin.PowerPoint
{
    /// <summary>
    /// Creates and replaces equation picture shapes using rendered PNG payloads.
    /// </summary>
    public sealed class PowerPointEquationShapeService
    {
        /// <summary>
        /// Inserts a new picture shape onto the target slide from a render payload.
        /// </summary>
        public dynamic Insert(RenderSuccessPayload payload, PowerPointInsertContext insertContext)
        {
            ValidatePayload(payload);
            if (insertContext == null)
            {
                throw new ArgumentNullException("insertContext");
            }

            var tempFile = WriteTempPng(payload.PngBase64);
            try
            {
                return insertContext.Slide.Shapes.AddPicture(tempFile, 0, -1, insertContext.Left, insertContext.Top, insertContext.Width, insertContext.Height);
            }
            finally
            {
                TryDeleteTempFile(tempFile);
            }
        }

        /// <summary>
        /// Replaces an existing equation shape while preserving position, z-order, and rotation.
        /// </summary>
        public dynamic Update(dynamic originalShape, RenderSuccessPayload payload, float newWidth, float newHeight)
        {
            if (originalShape == null)
            {
                throw new ArgumentNullException("originalShape");
            }

            ValidatePayload(payload);

            dynamic slide = originalShape.Parent;
            float left = originalShape.Left;
            float top = originalShape.Top;
            float rotation = originalShape.Rotation;
            int zOrderPosition = originalShape.ZOrderPosition;
            int lockAspect = originalShape.LockAspectRatio;

            var tempFile = WriteTempPng(payload.PngBase64);
            dynamic newShape;

            try
            {
                newShape = slide.Shapes.AddPicture(tempFile, 0, -1, left, top, newWidth, newHeight);
            }
            finally
            {
                TryDeleteTempFile(tempFile);
            }

            newShape.Rotation = rotation;
            newShape.LockAspectRatio = lockAspect;

            int currentPos = (int)newShape.ZOrderPosition;
            while (currentPos > zOrderPosition)
            {
                newShape.ZOrder(3);
                currentPos--;
            }

            originalShape.Delete();
            return newShape;
        }

        /// <summary>
        /// Converts pixel dimensions into PowerPoint points using render DPI.
        /// </summary>
        public static float PixelsToPoints(int pixels, int dpi)
        {
            if (dpi <= 0)
            {
                throw new ArgumentOutOfRangeException("dpi");
            }

            return pixels * 72f / dpi;
        }

        /// <summary>
        /// Ensures the render payload has usable PNG data and image dimensions.
        /// </summary>
        private static void ValidatePayload(RenderSuccessPayload payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException("payload");
            }

            if (!payload.IsSuccess)
            {
                throw new InvalidOperationException(payload.ErrorMessage ?? "Render failed.");
            }

            if (string.IsNullOrWhiteSpace(payload.PngBase64))
            {
                throw new InvalidOperationException("Render succeeded but no PNG payload was provided.");
            }

            if (payload.PixelWidth <= 0 || payload.PixelHeight <= 0)
            {
                throw new InvalidOperationException("Rendered image size must be positive.");
            }
        }

        private static string WriteTempPng(string base64)
        {
            var bytes = Convert.FromBase64String(base64);
            var tempFile = Path.Combine(Path.GetTempPath(), "slidetex-" + Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(tempFile, bytes);
            return tempFile;
        }

        private static void TryDeleteTempFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }
    }
}


