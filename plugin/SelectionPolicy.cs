using System;
using System.Collections.Generic;

namespace Gundomizer
{
    // No Unity dependencies: section scope and random traversal can be verified outside VR.
    internal static class SelectionPolicy
    {
        internal static List<string> SectionIds(bool categoryOverview,
            IEnumerable<string> pageIds, IEnumerable<string> filteredIds)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var source = categoryOverview ? pageIds : filteredIds;
            if (source != null)
                foreach (var id in source)
                    if (!string.IsNullOrEmpty(id) && seen.Add(id)) result.Add(id);
            return result;
        }

        internal static void Shuffle<T>(IList<T> items, Random random)
        {
            for (int i = items.Count - 1; i > 0; --i)
            {
                int j = random.Next(i + 1);
                T swap = items[i]; items[i] = items[j]; items[j] = swap;
            }
        }

        internal static bool MagazineFits(int magazineType, bool integrated, bool beltBox,
            int wellType, bool attachableOrSecondary, bool beltBoxWell, bool hasBelt)
        {
            if (integrated || magazineType == 0 || magazineType != wellType) return false;
            return attachableOrSecondary || (beltBox == beltBoxWell && (beltBox || !hasBelt));
        }
    }
}
