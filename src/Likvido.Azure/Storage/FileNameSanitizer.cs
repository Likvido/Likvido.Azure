using System.Globalization;
using System.Text;

namespace Likvido.Azure.Storage
{
    /// <summary>
    /// Provides utilities for sanitizing file names to ensure they contain only ASCII characters
    /// for use in HTTP headers.
    /// </summary>
    public static class FileNameSanitizer
    {
        /// <summary>
        /// Sanitizes a filename by converting non-ASCII characters to ASCII equivalents.
        /// </summary>
        /// <param name="fileName">The filename to sanitize</param>
        /// <returns>A sanitized filename containing only ASCII characters</returns>
        public static string Sanitize(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }

            // Handle special characters that don't decompose with FormD
            fileName = ReplaceDanishCharacters(fileName);

            // Normalize to FormD (decompose accented characters into base + diacritic)
            var normalized = fileName.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();

            foreach (var ch in normalized)
            {
                // Get the Unicode category
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);

                // Skip non-spacing marks (the diacritics/accents)
                if (category == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                // If ASCII, keep it
                if (ch <= 127)
                {
                    builder.Append(ch);
                }
                else
                {
                    // Replace non-ASCII with underscore
                    builder.Append('_');
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Replaces Danish and other special characters that don't decompose with FormD.
        /// </summary>
        private static string ReplaceDanishCharacters(string input)
        {
            return input
                .Replace("ø", "o")
                .Replace("Ø", "O")
                .Replace("æ", "ae")
                .Replace("Æ", "AE");
        }
    }
}