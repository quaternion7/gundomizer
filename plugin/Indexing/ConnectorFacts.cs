using System;
using System.Collections.Generic;
using System.IO;

namespace Gundomizer.Indexing
{
    // Plain managed data only: safe to build on the worker and persist between sessions.
    internal enum ConnectorKind : byte { Attachment = 1, Magazine = 2, Clip = 3 }
    internal sealed class ConnectorFacts
    {
        internal ConnectorKind Kind;
        internal int Connector;
        internal bool Integrated;
    }

    internal sealed class BundleFacts
    {
        internal readonly Dictionary<string, ConnectorFacts> Assets = new Dictionary<string, ConnectorFacts>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, ConnectorFacts> aliases;
        internal string SkipReason = "";
        internal long BytesRead;

        internal void Add(string path, ConnectorFacts facts)
        {
            path = Normalize(path);
            if (Assets.ContainsKey(path)) Assets[path] = null; // Ambiguous container mapping is unknown.
            else Assets.Add(path, facts);
            aliases = null;
        }

        internal void Seal()
        {
            var result = new Dictionary<string, ConnectorFacts>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in Assets)
            {
                string name = Path.GetFileName(pair.Key), stem = Path.GetFileNameWithoutExtension(pair.Key);
                AddAlias(result, name, pair.Value);
                if (stem != name) AddAlias(result, stem, pair.Value);
            }
            aliases = result;
        }

        private static void AddAlias(Dictionary<string, ConnectorFacts> map, string key, ConnectorFacts facts)
        {
            if (map.ContainsKey(key)) map[key] = null;
            else map.Add(key, facts);
        }

        internal ConnectorFacts Find(string asset)
        {
            ConnectorFacts result;
            asset = Normalize(asset);
            if (Assets.TryGetValue(asset, out result)) return result;
            return aliases != null && aliases.TryGetValue(asset, out result) ? result : null;
        }

        internal static string Normalize(string value) => (value ?? "").Replace('\\', '/').ToLowerInvariant();
    }
}
