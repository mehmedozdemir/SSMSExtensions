using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace SsmsToolkit.Data.Text
{
    /// <summary>Case-insensitive '*' / '?' wildcard matching, used for production server name patterns.</summary>
    public static class WildcardMatcher
    {
        /// <param name="patterns">Patterns separated by ';' or ','. Empty entries are ignored.</param>
        public static bool MatchesAny(string value, string patterns)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrWhiteSpace(patterns))
            {
                return false;
            }

            return patterns
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .Any(p => IsMatch(value, p));
        }

        public static bool IsMatch(string value, string pattern)
        {
            var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
    }
}
