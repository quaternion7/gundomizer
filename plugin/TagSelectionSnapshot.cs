using System;
using System.Collections.Generic;

namespace Gundomizer
{
    // Copy mutable native tag lists once; checking them during a load does not allocate strings.
    internal sealed class TagSelectionSnapshot<TTag>
    {
        private readonly Dictionary<TTag, HashSet<string>> groups = new Dictionary<TTag, HashSet<string>>();

        internal TagSelectionSnapshot(IDictionary<TTag, List<string>> selected)
        {
            if (selected == null) return;
            foreach (var pair in selected)
                groups.Add(pair.Key, pair.Value == null ? new HashSet<string>(StringComparer.Ordinal)
                    : new HashSet<string>(pair.Value, StringComparer.Ordinal));
        }

        internal bool Matches(IDictionary<TTag, List<string>> selected)
        {
            if (selected == null) return groups.Count == 0;
            if (selected.Count != groups.Count) return false;
            foreach (var pair in selected)
            {
                HashSet<string> tags;
                if (!groups.TryGetValue(pair.Key, out tags)) return false;
                if (pair.Value == null) { if (tags.Count != 0) return false; }
                else if (!tags.SetEquals(pair.Value)) return false;
            }
            return true;
        }
    }
}
