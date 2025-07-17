using Sitecore.Data.Items;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CWXPMigration
{
    public static class PathFormatter
    {
        private static readonly HashSet<string> LowercaseWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
        // Articles
        "a", "an", "the",
        // Coordinating Conjunctions
        "and", "but", "for", "nor", "or", "so", "yet",
        // Subordinating Conjunctions
        "as", "because", "if", "than", "that", "till", "when", "where", "while",
        // Prepositions
        "at", "by", "down", "from", "in", "into", "like", "near", "of", "off",
        "on", "onto", "out", "over", "past", "to", "under", "up", "upon", "with", "within"
        };
        public static string GetParentPath(string fullPath, int index)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return string.Empty;

            // Remove trailing slash if present
            fullPath = fullPath.TrimEnd('/');

            // Split into segments
            var segments = fullPath.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries);

            // If only one segment or empty, no parent
            if (segments.Length <= 1)
                return string.Empty;

            // Join all segments except the last
            return "/" + string.Join("/", segments.Take(index));
        }

        public static int GetUrlSegmentsCount(string fullPath)
        {
            // Remove trailing slash if present
            fullPath = fullPath.TrimEnd('/');

            // Split into segments
            var segments = fullPath.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries);

            return segments.Length;
        }

        public static string FormatItemName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var words = name.ToLowerInvariant().Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < words.Length; i++)
            {
                bool isFirstOrLast = i == 0 || i == words.Length - 1;
                if (isFirstOrLast || !LowercaseWords.Contains(words[i]))
                {
                    words[i] = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words[i]);
                }
            }

            return ItemUtil.ProposeValidItemName(string.Join(" ", words));
        }

        public static string GetLastPathSegment(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var segment = path.TrimEnd('/').Split('/').LastOrDefault() ?? "";
            return FormatItemName(segment);
        }
    }
}