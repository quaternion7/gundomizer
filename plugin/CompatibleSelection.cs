using System;
using System.Collections.Generic;

namespace Gundomizer
{
    // Session-wide choices, shared by wall panels and tablets. Exclusions preserve newly
    // encountered connectors as enabled; changing guns never silently resets user choices.
    internal sealed class CompatibleSelection
    {
        internal static readonly CompatibleSelection Shared = new CompatibleSelection();
        private readonly HashSet<string> excluded = new HashSet<string>(StringComparer.Ordinal);
        internal bool AllItems { get; private set; }
        internal int Revision { get; private set; }
        private static string Key(CompatibilityKind kind, int? connector) => kind + ":" + (connector.HasValue ? connector.Value.ToString() : "*");
        internal bool Includes(CompatibilityKind kind, int? connector = null)
            => !excluded.Contains(Key(kind, null)) && (!connector.HasValue || !excluded.Contains(Key(kind, connector)));
        internal void Set(CompatibilityKind kind, int? connector, bool include)
        { if (include ? excluded.Remove(Key(kind, connector)) : excluded.Add(Key(kind, connector))) ++Revision; }
        internal void SetScope(bool allItems)
        { if (AllItems != allItems) { AllItems = allItems; ++Revision; } }
        internal CompatibleSelection Snapshot()
        {
            var copy = new CompatibleSelection { AllItems = AllItems, Revision = Revision };
            foreach (var key in excluded) copy.excluded.Add(key);
            return copy;
        }
    }
}
