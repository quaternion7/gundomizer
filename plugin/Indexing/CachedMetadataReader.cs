using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Gundomizer.Indexing
{
    // Called only by the background worker. No Unity loading APIs or child processes.
    internal sealed class CachedMetadataReader
    {
        private readonly string directory, game, plugins;
        private readonly ReadBudget budget;
        private readonly BundleMetadataReader reader;
        internal CachedMetadataReader(string directory, string game, string plugins, Dictionary<string, ConnectorKind> types, ReadBudget budget)
        {
            this.directory = directory; this.game = game; this.plugins = plugins; this.budget = budget;
            reader = new BundleMetadataReader(types, budget);
        }
        internal BundleFacts Read(string bundle, HashSet<string> targets)
        {
            budget.BeginJob();
            budget.Check();
            string sourceFingerprint = IndexCache.Fingerprint(bundle, game, plugins, budget);
            string targetKey = "all";
            if (targets != null)
            {
                var names = new List<string>(targets); names.Sort(StringComparer.Ordinal);
                targetKey = IndexCache.Hash(Encoding.UTF8.GetBytes(string.Join("\n", names.ToArray())));
            }
            string fingerprint = sourceFingerprint + ":" + targetKey;
            string cachePath = Path.Combine(directory, IndexCache.Key(bundle) + ".gidx");
            var facts = IndexCache.Load(cachePath, fingerprint);
            if (facts != null) { facts.CacheHit = true; budget.Check(); return facts; }
            try { facts = reader.Read(bundle, targets); }
            catch (NotSupportedException ex) { facts = new BundleFacts { SkipReason = ex.Message }; facts.Seal(); }
            budget.Check();
            if (IndexCache.Fingerprint(bundle, game, plugins, budget) != sourceFingerprint)
                throw new IOException("Bundle changed while indexing");
            try { IndexCache.Save(cachePath, fingerprint, facts); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { facts.CacheWarning = ex.Message; }
            budget.Check();
            return facts;
        }
    }
}
