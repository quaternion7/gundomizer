using System;
using System.Collections.Generic;

namespace Gundomizer
{
    // Remember exclusions by caliber + variant for this game session, across all panels.
    internal sealed class AmmoSelection
    {
        internal static readonly AmmoSelection Shared = new AmmoSelection();
        private readonly HashSet<string> excluded = new HashSet<string>(StringComparer.Ordinal);
        internal int Revision { get; private set; }
        internal static string Key(int caliber, int variant) => caliber + ":" + variant;
        internal bool Includes(string key) => !excluded.Contains(key);
        internal void Set(string key, bool include)
        {
            if (include ? excluded.Remove(key) : excluded.Add(key)) ++Revision;
        }
    }
}
