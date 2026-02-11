// SlideTeX Note: PowerPoint interop helpers for equation insertion, tagging, and synchronization.

namespace SlideTeX.VstoAddin.PowerPoint
{
    /// <summary>
    /// Immutable-by-convention placement data used when inserting rendered formula images.
    /// </summary>
    public sealed class PowerPointInsertContext
    {
        /// <summary>
        /// Target slide object where the new equation shape is inserted.
        /// </summary>
        public dynamic Slide { get; set; }

        /// <summary>
        /// Left coordinate in PowerPoint points.
        /// </summary>
        public float Left { get; set; }

        /// <summary>
        /// Top coordinate in PowerPoint points.
        /// </summary>
        public float Top { get; set; }

        /// <summary>
        /// Shape width in PowerPoint points.
        /// </summary>
        public float Width { get; set; }

        /// <summary>
        /// Shape height in PowerPoint points.
        /// </summary>
        public float Height { get; set; }
    }
}


