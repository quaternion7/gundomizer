using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Gundomizer.Indexing
{
    internal sealed class IndexWorker : IDisposable
    {
        private sealed class IndexedBundle { internal BundleFacts Facts; }
        private sealed class Job { internal string Path; internal HashSet<string> Targets; }
        private readonly object gate = new object();
        private readonly Dictionary<string, IndexedBundle> ready = new Dictionary<string, IndexedBundle>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> pending = new Queue<string>();
        private readonly HashSet<string> queued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Job> latest = new Dictionary<string, Job>(StringComparer.OrdinalIgnoreCase);
        private readonly ManualResetEvent stop = new ManualResetEvent(false);
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Thread thread;
        private readonly Dictionary<string, ConnectorKind> types;
        private readonly string directory, game, plugins, readerExecutable;
        private readonly Action<string> log;
        private int cacheHits, rebuilt, failed, factsCount;
        private long bytesRead;
        private bool finished;
        internal IndexWorker(string directory, string game, string plugins, Dictionary<string, ConnectorKind> types, Action<string> log, string readerExecutable = null)
        {
            this.directory = directory; this.game = game; this.plugins = plugins; this.types = types; this.log = log;
            this.readerExecutable = readerExecutable ?? Path.Combine(Path.GetDirectoryName(typeof(IndexWorker).Assembly.Location), "Gundomizer.Reader.exe");
            thread = new Thread(Run) { IsBackground = true, Name = "Gundomizer metadata index", Priority = ThreadPriority.BelowNormal };
            thread.Start();
        }

        internal void Queue(string path, IEnumerable<string> targets = null)
        {
            var job = new Job { Path = path, Targets = targets == null ? null : new HashSet<string>(targets, StringComparer.OrdinalIgnoreCase) };
            lock (gate)
            {
                if (finished) return;
                latest[path] = job;
                if (queued.Add(path)) pending.Enqueue(path);
                wake.Set();
            }
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
            finally { lock (gate) { finished = true; pending.Clear(); queued.Clear(); latest.Clear(); } log("Metadata index stopped. " + Status); }
        }

        private void ProcessQueue()
        {
            var budget = new ReadBudget(stop, true);
            var reader = new ExternalMetadataReader(readerExecutable, directory, game, plugins, types, budget);
            while (!stop.WaitOne(0, false))
            {
                string path = null;
                Job job = null;
                lock (gate) if (pending.Count > 0) { path = pending.Dequeue(); job = latest[path]; }
                if (path == null) { wake.WaitOne(1000, false); continue; }
                try
                {
                    var facts = reader.Read(path, job.Targets);
                    lock (gate)
                    {
                        if (ready.TryGetValue(path, out var previous)) factsCount -= Count(previous.Facts);
                        ready[path] = new IndexedBundle { Facts = facts };
                        if (facts.CacheHit) ++cacheHits; else ++rebuilt;
                        if (facts.SkipReason.Length > 0) ++failed;
                        bytesRead += facts.BytesRead; factsCount += Count(facts);
                    }
                    if (facts.SkipReason.Length > 0) log("Index fallback for " + Path.GetFileName(path) + ": " + facts.SkipReason);
                    if (facts.CacheWarning.Length > 0) log("Index cache write unavailable: " + facts.CacheWarning);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    lock (gate) { if (ready.TryGetValue(path, out var previous)) factsCount -= Count(previous.Facts); ready.Remove(path); ++failed; }
                    log("Index fallback for " + Path.GetFileName(path) + ": " + ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    lock (gate)
                    {
                        // A late catalog registration can change this bundle's target set while
                        // it is being read. Process that snapshot next instead of losing it.
                        if (latest[path] != job) pending.Enqueue(path);
                        else { queued.Remove(path); latest.Remove(path); }
                    }
                }
            }
        }
        private static int Count(BundleFacts facts) { int count = 0; foreach (var pair in facts.Assets) if (pair.Value != null) ++count; return count; }

        public void Dispose()
        {
            stop.Set(); wake.Set();
            // No main-thread join: outstanding disk reads finish cooperatively on the background thread.
        }
    }
}
