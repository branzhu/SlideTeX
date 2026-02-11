// SlideTeX Note: Metadata schema and utility code for persisted render state on shapes.

using System;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace SlideTeX.VstoAddin.Metadata
{
    /// <summary>
    /// Computes deterministic SHA-256 hash for latex source and normalized render options.
    /// </summary>
    public static class ContentHashCalculator
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        /// <summary>
        /// Computes uppercase hex digest used as metadata identity for render content.
        /// </summary>
        public static string Compute(string latexSource, RenderOptionsDto options)
        {
            if (string.IsNullOrWhiteSpace(latexSource))
            {
                throw new ArgumentException("latexSource is required.", "latexSource");
            }

            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            options.Validate();

            var payload = latexSource + "\n" + Serializer.Serialize(options).ToUpperInvariant();
            var bytes = Encoding.UTF8.GetBytes(payload);

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    sb.Append(b.ToString("X2"));
                }

                return sb.ToString();
            }
        }
    }
}


