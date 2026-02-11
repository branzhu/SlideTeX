// SlideTeX Note: PowerPoint interop helpers for equation insertion, tagging, and synchronization.

using System;

namespace SlideTeX.VstoAddin.PowerPoint
{
    /// <summary>
    /// Lightweight wrapper around PowerPoint shape tags with null-safe reads and overwrites.
    /// </summary>
    public sealed class PowerPointShapeTagAccessor
    {
        private readonly dynamic _shape;

        public PowerPointShapeTagAccessor(dynamic shape)
        {
            _shape = shape ?? throw new ArgumentNullException("shape");
        }

        /// <summary>
        /// Reads a single tag value and normalizes missing/blank values to null.
        /// </summary>
        public string GetTag(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("key is required.", "key");
            }

            try
            {
                var value = _shape.Tags[key];
                return string.IsNullOrWhiteSpace(value) ? null : (string)value;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Replaces a tag value by deleting any existing key first.
        /// </summary>
        public void SetTag(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("key is required.", "key");
            }

            if (value == null)
            {
                throw new ArgumentNullException("value");
            }

            try
            {
                _shape.Tags.Delete(key);
            }
            catch
            {
                // ignore
            }

            _shape.Tags.Add(key, value);
        }
    }
}


