using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Gundomizer.Indexing
{
    internal sealed class IndexWorker : IDisposable
    {
        private sealed class IndexedBundle { internal string Fingerprint; internal BundleFacts Facts; }
        private readonly object gate = new object();
        private readonly Dictionary<string, IndexedBundle> ready = new Dictionary<string, IndexedBundle>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> pending = new Queue<string>();
        private readonly HashSet<string> queued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly ManualResetEvent stop = new ManualResetEvent(false);
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Thread thread;
        private readonly Dictionary<string, ConnectorKind> types;
        private readonly string directory, game, plugins;
        private readonly Action<string> log;
        private int cacheHits, rebuilt, failed, factsCount;
        private long bytesRead;
        internal IndexWorker(string directory, string game, string plugins, Dictionary<string, ConnectorKind> types, Action<string> log)
        {
            this.directory = directory; this.game = game; this.plugins = plugins; this.types = types; this.log = log;
            thread = new Thread(Run) { IsBackground = true, Name = "Gundomizer metadata index", Priority = ThreadPriority.BelowNormal };
            thread.Start();
        }

        internal void Queue(string path)
        {
            lock (gate) if (queued.Add(path)) { pending.Enqueue(path); wake.Set(); }
        }

        internal ConnectorFacts Find(string path, string asset)
        {
            lock (gate) { IndexedBundle bundle; return ready.TryGetValue(path, out bundle) ? bundle.Facts.Find(asset) : null; }
        }

        internal string Status
        {
            get { lock (gate) return "bundles=" + ready.Count + " pending=" + queued.Count + " cacheHits=" + cacheHits
                + " rebuilt=" + rebuilt + " skipped=" + failed + " roots=" + factsCount + " metadataBytes=" + bytesRead; }
        }
        internal bool Idle { get { lock (gate) return queued.Count == 0; } }

        private void Run()
        {
            // Keep library/JIT initialization inside an outer catch: a missing/incompatible
            // optional reader assembly must never become an unhandled background exception.
            try { ProcessQueue(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { log("Persistent reader unavailable; keeping live checks: " + ex); }
            finally { lock (gate) { pending.Clear(); queued.Clear(); } log("Metadata index stopped. " + Status); }
        }

        private void ProcessQueue()
        {
            var budget = new ReadBudget(stop, true);
            var reader = new BundleMetadataReader(types, budget);
            try
            {
                while (!stop.WaitOne(0, false))
                {
                    string path = null;
                    lock (gate) if (pending.Count > 0) path = pending.Dequeue();
                    if (path == null) { wake.WaitOne(1000, false); continue; }
                    try
                    {
                        string fingerprint = IndexCache.Fingerprint(path, game, plugins, budget);
                        bool unchanged;
                        lock (gate)
                        {
                            IndexedBundle previous;
                            unchanged = ready.TryGetValue(path, out previous) && previous.Fingerprint == fingerprint;
                            // Stop consulting old facts as soon as a changed source is detected.
                            if (!unchanged && previous != null) { factsCount -= Count(previous.Facts); ready.Remove(path); }
                        }
                        if (!unchanged)
                        {
                            string cachePath = Path.Combine(directory, IndexCache.Key(path) + ".gidx");
                            var facts = IndexCache.Load(cachePath, fingerprint);
                            bool cached = facts != null;
                            if (!cached)
                            {
                                try { facts = reader.Read(path); }
                                catch (NotSupportedException ex) { facts = new BundleFacts { SkipReason = ex.Message }; facts.Seal(); }
                                // Do not publish/cache a source that was replaced during the read.
                                if (IndexCache.Fingerprint(path, game, plugins, budget) != fingerprint)
                                    throw new IOException("Bundle changed while indexing");
                                try { IndexCache.Save(cachePath, fingerprint, facts); }
                                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                                { log("Index cache write unavailable: " + ex.Message); }
                            }
                            lock (gate)
                            {
                                ready[path] = new IndexedBundle { Fingerprint = fingerprint, Facts = facts };
                                if (cached) ++cacheHits; else ++rebuilt;
                                if (facts.SkipReason.Length > 0) ++failed;
                                bytesRead += facts.BytesRead; factsCount += Count(facts);
                            }
                            if (facts.SkipReason.Length > 0) log("Index fallback for " + Path.GetFileName(path) + ": " + facts.SkipReason);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        lock (gate) { if (ready.TryGetValue(path, out var previous)) factsCount -= Count(previous.Facts); ready.Remove(path); ++failed; }
                        log("Index fallback for " + Path.GetFileName(path) + ": " + ex.GetType().Name + ": " + ex.Message);
                    }
                    finally { lock (gate) queued.Remove(path); }
                }
            }
            finally { }
        }
        private static int Count(BundleFacts facts) { int count = 0; foreach (var pair in facts.Assets) if (pair.Value != null) ++count; return count; }

        public void Dispose()
        {
            stop.Set(); wake.Set();
            // No main-thread join: outstanding disk reads finish cooperatively on the background thread.
        }
    }
}
