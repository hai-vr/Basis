using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Basis.BasisUI
{
    /// <summary>
    /// Reads a version out of a content NAME, for library entries that have no
    /// <see cref="BasisBundleDescription.ContentGroupId"/> to group by.
    ///
    /// <para>ContentGroupId is authored in the SDK, so nothing built before it exists carries one —
    /// and that is precisely the content a creator has been re-uploading by hand under names like
    /// "My Avatar v2". Falling back to the name lets those stack into one card the same way a
    /// grouped upload does, instead of scattering across the library as unrelated entries.</para>
    ///
    /// <para>This is a heuristic over creator-chosen text, so it is deliberately conservative: it
    /// only ever strips a version-looking token from the END of a name, and never strips one when
    /// doing so would leave nothing but punctuation and digits behind.</para>
    /// </summary>
    public static class BasisContentNameVersion
    {
        /// <summary>
        /// A trailing version token in any of the spellings creators actually use:
        /// "(2)", "[v3]", " v1.4", "_V2", " - 3", "5". Alternatives share the <c>num</c> group,
        /// which .NET allows, so the numeric part is read back the same way whichever matched.
        /// </summary>
        private static readonly Regex TrailingVersion = new Regex(
            @"(?:" +
            @"[\s_\-.]*[\(\[]\s*[vV]?\s*(?<num>\d+(?:\.\d+)*)\s*[\)\]]" + // (2) [v3] (1.4)
            @"|[\s_\-.]*[vV]\s*(?<num>\d+(?:\.\d+)*)" +                   // v2  _V1.4  -v3
            @"|[\s_\-]*(?<num>\d+(?:\.\d+)*)" +                           // " 2"  "_1.0"  "Sailor3"
            @")\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Trailing separators left dangling once a version token is removed ("Avatar - v2").</summary>
        private static readonly Regex TrailingSeparators = new Regex(@"[\s_\-.]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex WhitespaceRun = new Regex(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// The name with any trailing version token removed. Returns the name unchanged when there
        /// is nothing version-like to remove, or when removing it would leave no letters — so "v2"
        /// and "2024" stay whole rather than collapsing to an empty group key that would pile every
        /// such entry into one stack.
        /// </summary>
        public static string StripVersionSuffix(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            string working = name.Trim();

            // Looped so "My Avatar v2 (3)" reduces all the way to "My Avatar".
            while (true)
            {
                Match match = TrailingVersion.Match(working);
                if (!match.Success || match.Index == 0)
                {
                    break;
                }

                string candidate = TrailingSeparators.Replace(working.Substring(0, match.Index), string.Empty);
                if (!HasLetter(candidate))
                {
                    break;
                }

                working = candidate;
            }

            return working;
        }

        /// <summary>
        /// Stack key for a name: the version-stripped base, lowercased with whitespace collapsed so
        /// "My  Avatar v2" and "my avatar" land together. Empty when the name cannot group anything.
        /// </summary>
        public static string GroupKeyFromName(string name)
        {
            string stripped = StripVersionSuffix(name);
            if (string.IsNullOrWhiteSpace(stripped))
            {
                return string.Empty;
            }

            return WhitespaceRun.Replace(stripped, " ").Trim().ToLowerInvariant();
        }

        /// <summary>
        /// The numeric components of the trailing version token ("Avatar v1.4" gives [1, 4]), or an
        /// empty list when the name carries no version. Used to order a stack when the entries have
        /// no creation date to sort by, which is the norm for the older content this exists for.
        /// </summary>
        public static IReadOnlyList<int> ExtractVersion(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Array.Empty<int>();
            }

            Match match = TrailingVersion.Match(name.Trim());
            if (!match.Success || match.Index == 0)
            {
                return Array.Empty<int>();
            }

            string[] parts = match.Groups["num"].Value.Split('.');
            List<int> components = new List<int>(parts.Length);
            foreach (string part in parts)
            {
                // Overflow on an absurd run of digits is not worth failing the whole comparison over.
                components.Add(int.TryParse(part, out int value) ? value : 0);
            }
            return components;
        }

        /// <summary>
        /// Orders two names newest-version-first. Names without a version sort AFTER names with one,
        /// on the reading that an unversioned "My Avatar" is the original and "My Avatar v2" the
        /// later upload.
        /// </summary>
        public static int CompareVersionDescending(string leftName, string rightName)
        {
            IReadOnlyList<int> left = ExtractVersion(leftName);
            IReadOnlyList<int> right = ExtractVersion(rightName);

            if (left.Count == 0 && right.Count == 0) return 0;
            if (left.Count == 0) return 1;
            if (right.Count == 0) return -1;

            int shared = Math.Min(left.Count, right.Count);
            for (int index = 0; index < shared; index++)
            {
                if (left[index] != right[index])
                {
                    return right[index].CompareTo(left[index]);
                }
            }

            // "v1.1" outranks "v1" once the shared components tie.
            return right.Count.CompareTo(left.Count);
        }

        private static bool HasLetter(string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsLetter(value[index]))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
