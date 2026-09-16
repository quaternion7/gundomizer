using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Text;
using Gundomizer.Indexing;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 3) return 2;
        var stop = new ManualResetEvent(false);
        try
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
            var parent = Process.GetProcessById(int.Parse(args[2]));
            // Exit cooperatively even if the game crashes and cannot cancel its worker.
            var monitor = new Thread(() =>
            {
                while (!stop.WaitOne(250))
                {
                    try { if (!parent.HasExited) continue; } catch (InvalidOperationException) { }
                    stop.Set(); break;
                }
            }) { IsBackground = true };
            monitor.Start();
            if (new FileInfo(args[0]).Length > 8 * 1024 * 1024) throw new IOException("Oversized reader request");
            string bundle, cacheDirectory, game, plugins;
            var types = new Dictionary<string, ConnectorKind>();
            HashSet<string> targets = null;
            using (var reader = new BinaryReader(File.OpenRead(args[0])))
            {
                if (ReaderProtocol.ReadString(reader) != ReaderProtocol.Version) throw new IOException("Reader protocol mismatch");
                bundle = ReaderProtocol.ReadString(reader);
                cacheDirectory = ReaderProtocol.ReadString(reader); game = ReaderProtocol.ReadString(reader); plugins = ReaderProtocol.ReadString(reader);
                int count = ReaderProtocol.Count(reader);
                for (int i = 0; i < count; ++i)
                {
                    string name = ReaderProtocol.ReadString(reader);
                    byte kind = reader.ReadByte();
                    if (kind < 1 || kind > 3) throw new IOException("Invalid connector kind");
                    types.Add(name, (ConnectorKind)kind);
                }
                if (!reader.ReadBoolean())
                {
                    targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    count = ReaderProtocol.Count(reader);
                    for (int i = 0; i < count; ++i) targets.Add(ReaderProtocol.ReadString(reader));
                }
            }
            var budget = new ReadBudget(stop, true);
            string sourceFingerprint = IndexCache.Fingerprint(bundle, game, plugins, budget);
            string targetKey = "all";
            if (targets != null)
            {
                var names = new List<string>(targets); names.Sort(StringComparer.Ordinal);
                targetKey = IndexCache.Hash(Encoding.UTF8.GetBytes(string.Join("\n", names.ToArray())));
            }
            string fingerprint = sourceFingerprint + ":" + targetKey;
            string cachePath = Path.Combine(cacheDirectory, IndexCache.Key(bundle) + ".gidx");
            var facts = IndexCache.Load(cachePath, fingerprint);
            bool cacheHit = facts != null;
            if (!cacheHit)
            {
                try { facts = new BundleMetadataReader(types, budget).Read(bundle, targets); }
                catch (NotSupportedException ex) { facts = new BundleFacts { SkipReason = ex.Message }; facts.Seal(); }
                if (IndexCache.Fingerprint(bundle, game, plugins, budget) != sourceFingerprint) throw new IOException("Bundle changed while indexing");
                try { IndexCache.Save(cachePath, fingerprint, facts); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { facts.CacheWarning = ex.Message; }
            }
            if (stop.WaitOne(0)) return 3;
            IndexCache.Save(args[1], ReaderProtocol.Version, facts);
            using (var writer = new BinaryWriter(File.Create(args[1] + ".stats")))
            { writer.Write(facts.BytesRead); writer.Write(cacheHit); ReaderProtocol.WriteString(writer, facts.CacheWarning); }
            return 0;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(args[1] + ".error", ex.GetType().Name + ": " + ex.Message); } catch { }
            return 1;
        }
        finally { stop.Set(); }
    }
}
