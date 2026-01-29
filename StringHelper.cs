using System;
using System.Globalization;
using System.Text;

namespace Narcopelago
{
    /// <summary>
    /// Helper class for string operations, particularly for handling special characters
    /// like accented letters that may have different encodings.
    /// </summary>
    public static class StringHelper
    {
        /// <summary>
        /// Normalizes a string by removing diacritical marks (accents) and converting to lowercase.
        /// This allows matching "Pérez" with "Perez" or "PeÌrez" (corrupted encoding).
        /// </summary>
        /// <param name="text">The string to normalize.</param>
        /// <returns>A normalized string without accents, in lowercase.</returns>
        public static string NormalizeForComparison(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Normalize to FormD (decomposed form) - separates base characters from diacritical marks
            string normalizedString = text.Normalize(NormalizationForm.FormD);
            
            // Build a new string without diacritical marks
            var stringBuilder = new StringBuilder();
            foreach (char c in normalizedString)
            {
                // UnicodeCategory.NonSpacingMark includes diacritical marks
                UnicodeCategory unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            // Return normalized string in lowercase
            return stringBuilder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        /// <summary>
        /// Compares two strings for equality, ignoring accents and case.
        /// This handles cases like "Javier Pérez" == "Javier Perez" == "Javier PeÌrez".
        /// </summary>
        /// <param name="a">First string to compare.</param>
        /// <param name="b">Second string to compare.</param>
        /// <returns>True if the strings are equal after normalization.</returns>
        public static bool EqualsNormalized(string a, string b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;

            return NormalizeForComparison(a) == NormalizeForComparison(b);
        }

        /// <summary>
        /// Creates a normalized key for dictionary lookups.
        /// </summary>
        /// <param name="text">The string to create a key from.</param>
        /// <returns>A normalized key string.</returns>
        public static string GetNormalizedKey(string text)
        {
            return NormalizeForComparison(text);
        }
    }

    /// <summary>
    /// A string comparer that ignores diacritical marks (accents).
    /// Use this for Dictionary keys that may have accented characters.
    /// </summary>
    public class NormalizedStringComparer : StringComparer
    {
        public static readonly NormalizedStringComparer Instance = new NormalizedStringComparer();

        public override int Compare(string x, string y)
        {
            return string.Compare(
                StringHelper.NormalizeForComparison(x),
                StringHelper.NormalizeForComparison(y),
                StringComparison.Ordinal);
        }

        public override bool Equals(string x, string y)
        {
            return StringHelper.EqualsNormalized(x, y);
        }

        public override int GetHashCode(string obj)
        {
            if (obj == null) return 0;
            return StringHelper.NormalizeForComparison(obj).GetHashCode();
        }
    }
}
