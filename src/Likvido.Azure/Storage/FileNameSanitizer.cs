using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Likvido.Azure.Storage
{
    /// <summary>
    /// Provides utilities for sanitizing file names to ensure they contain only ASCII characters
    /// and are safe for use in file systems and HTTP headers.
    /// </summary>
    public static class FileNameSanitizer
    {
        private const int MaxFileNameLength = 255;

        /// <summary>
        /// Sanitizes a filename by converting accented characters to their ASCII equivalents,
        /// removing invalid characters, and ensuring the result is safe for use in HTTP headers
        /// and file systems.
        /// </summary>
        /// <param name="fileName">The filename to sanitize</param>
        /// <param name="replacement">The character to use for replacing invalid characters (default: underscore)</param>
        /// <returns>A sanitized filename containing only ASCII characters</returns>
        public static string Sanitize(string fileName, char replacement = '_')
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "file";
            }

            // Separate filename and extension
            var extension = Path.GetExtension(fileName);
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

            if (string.IsNullOrWhiteSpace(nameWithoutExtension))
            {
                nameWithoutExtension = "file";
            }

            while (nameWithoutExtension.Contains($"{replacement}{replacement}"))
            {
                nameWithoutExtension = nameWithoutExtension.Replace($"{replacement}{replacement}", replacement.ToString());
            }

            var hadLeadingUnderscore = nameWithoutExtension.StartsWith(replacement.ToString());
            var hadTrailingUnderscore = nameWithoutExtension.EndsWith(replacement.ToString());

            // Sanitize both parts
            var sanitizedName = RemoveAccentsAndNonAscii(nameWithoutExtension, replacement);

            var processedExtension = extension;
            if (!string.IsNullOrEmpty(processedExtension))
            {
                while (processedExtension.Contains($"{replacement}{replacement}"))
                {
                    processedExtension = processedExtension.Replace($"{replacement}{replacement}", replacement.ToString());
                }
            }

            var sanitizedExtension = string.IsNullOrEmpty(processedExtension)
                ? string.Empty
                : RemoveAccentsAndNonAscii(processedExtension, replacement);

            // If sanitization resulted in empty string (e.g., all non-ASCII characters), use fallback
            if (string.IsNullOrEmpty(sanitizedName))
            {
                sanitizedName = "file";
            }

            // Only trim leading/trailing underscores if they were in the original input
            // This preserves underscores created by sanitization (e.g., "file<name>" -> "file_name_")
            // but removes original underscores (e.g., "_leading" -> "leading")
            if (hadLeadingUnderscore && sanitizedName.StartsWith(replacement.ToString()))
            {
                sanitizedName = sanitizedName.TrimStart(replacement);
            }
            if (hadTrailingUnderscore && sanitizedName.EndsWith(replacement.ToString()))
            {
                sanitizedName = sanitizedName.TrimEnd(replacement);
            }

            // Combine and enforce length limit
            var result = sanitizedName + sanitizedExtension;
            if (result.Length > MaxFileNameLength)
            {
                var maxNameLength = MaxFileNameLength - sanitizedExtension.Length;
                result = sanitizedName.Substring(0, Math.Max(1, maxNameLength)) + sanitizedExtension;
            }

            return result;
        }

        private static string RemoveAccentsAndNonAscii(string input, char replacement)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            // Preprocess special characters that don't decompose well with Unicode normalization
            input = PreprocessSpecialCharacters(input);

            // Normalize to FormD (decompose accented characters into base + diacritic)
            var normalized = input.Normalize(NormalizationForm.FormD);
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

                // Remove apostrophes entirely (e.g., "O'Brien" -> "OBrien")
                if (ch == '\'')
                {
                    continue;
                }

                // Check if character is safe for filenames
                if (IsSafeFileNameChar(ch))
                {
                    builder.Append(ch);
                }
                else
                {
                    builder.Append(replacement);
                }
            }

            var result = builder.ToString();

            if (string.IsNullOrWhiteSpace(result) || result.Trim(replacement).Length == 0)
            {
                return string.Empty;
            }

            return result;
        }

        private static string PreprocessSpecialCharacters(string input)
        {
            // Handle Scandinavian and other special characters that don't decompose well
            var result = input
                .Replace("ø", "o").Replace("Ø", "O")
                .Replace("æ", "a").Replace("Æ", "A")
                .Replace("å", "a").Replace("Å", "A")
                .Replace("ł", "l").Replace("Ł", "L")
                .Replace("đ", "d").Replace("Đ", "D")
                .Replace("ß", "ss")
                .Replace("œ", "oe").Replace("Œ", "OE");

            return result;
        }

        private static bool IsSafeFileNameChar(char ch)
        {
            // Must be ASCII
            if (ch > 127)
            {
                return false;
            }

            // Check against invalid filename characters
            if (Path.GetInvalidFileNameChars().Contains(ch))
            {
                return false;
            }

            // Replace spaces with underscore for better compatibility
            if (char.IsWhiteSpace(ch))
            {
                return false;
            }

            // Allow alphanumeric characters
            if (char.IsLetterOrDigit(ch))
            {
                return true;
            }

            // Allow specific safe punctuation characters
            // dot (.), dash (-), underscore (_), parentheses, brackets
            return ch == '.' || ch == '-' || ch == '_' || ch == '(' || ch == ')' || ch == '[' || ch == ']';
        }
    }
}
