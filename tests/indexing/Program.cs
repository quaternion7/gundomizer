using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading;
using Gundomizer.Indexing;

static class Program
{
    static void Main(string[] args)
    {
        Policies();
        BlockStreamChecks.Run();
        SelectedFieldChecks.Run();
        if (args.Length == 2 && args[0] == "--worker") { WorkerChecks(args[1]); return; }
        if (args.Length == 2 && args[0] == "--cancel") { CancelReader(args[1]); return; }
        var types = new Dictionary<string, ConnectorKind> {
            { "FistVR.FVRFireArmAttachment", ConnectorKind.Attachment },
            { "FistVR.Suppressor", ConnectorKind.Attachment },
            { "FistVR.MuzzleDevice", ConnectorKind.Attachment },
            { "FistVR.MuzzleBrake", ConnectorKind.Attachment },
            { "FistVR.AttachableBipod", ConnectorKind.Attachment },
            { "FistVR.FVRFireArmMagazine", ConnectorKind.Magazine },
            { "FistVR.FVRFireArmClip", ConnectorKind.Clip } };
        var watch = Stopwatch.StartNew();
        var reader = new BundleMetadataReader(types, new ReadBudget(new ManualResetEvent(false), false));
        foreach (var path in args)
        {
            var result = reader.Read(path);
            Console.WriteLine(path + ": " + result.Assets.Count + " assets, " + result.Assets.Values.Count(v => v != null)
                + " mapped connector roots; " + result.BytesRead + " bytes; elapsed=" + watch.ElapsedMilliseconds + "ms");
            foreach (var pair in result.Assets.Where(p => p.Value != null).Take(8))
                Console.WriteLine(pair.Key + " => " + pair.Value.Kind + ":" + pair.Value.Connector);
            if (result.Assets.Values.Any(v => v != null))
            {
                var selected = new HashSet<string>(result.Assets.Where(p => p.Value != null).Take(3).Select(p => p.Key));
                var targeted = reader.Read(path, selected);
                Check(targeted.Assets.Count == result.Assets.Count && selected.All(k => targeted.Find(k) != null
                    && targeted.Find(k).Connector == result.Find(k).Connector), "targeted reading retains complete container ambiguity checks and the requested connector facts");
                Check(targeted.Assets.All(p => selected.Contains(p.Key) || p.Value == null), "unrequested prefab components are not deserialized");
                Console.WriteLine("Targeted bytes=" + targeted.BytesRead + " vs full=" + result.BytesRead);
            }
        }
    }

    static void WorkerChecks(string smallBundle)
    {
        string root = Path.Combine(Path.GetTempPath(), "Gundomizer-worker-tests-" + Guid.NewGuid().ToString("N"));
        string plugins = Path.Combine(root, "plugins"), cache = Path.Combine(root, "cache");
        string a = Path.Combine(plugins, "pack-a"), b = Path.Combine(plugins, "pack-b");
        Directory.CreateDirectory(a); Directory.CreateDirectory(b);
        string sourceA = Path.Combine(a, "bundle"), sourceB = Path.Combine(b, "bundle");
        if (new FileInfo(smallBundle).Length > 1024 * 1024) throw new Exception("Use a small metadata bundle for worker tests");
        File.Copy(smallBundle, sourceA); File.Copy(smallBundle, sourceB);
        File.WriteAllText(Path.Combine(a, "manifest.json"), "version=1");
        File.WriteAllText(Path.Combine(b, "manifest.json"), "version=1");
        Func<string> run = () =>
        {
            using (var worker = new IndexWorker(cache, "game", plugins, new Dictionary<string, ConnectorKind>(), Console.WriteLine))
            {
                worker.Queue(sourceA); worker.Queue(sourceB);
                var watch = Stopwatch.StartNew();
                while (!worker.Idle && watch.ElapsedMilliseconds < 20000) Thread.Sleep(10);
                Check(worker.Idle, "worker completes queued source checks");
                return worker.Status;
            }
        };
        string first = run();
        Check(first.Contains("rebuilt=2") && first.Contains("cacheHits=0") && first.Contains("skipped=0"), "cold worker builds both package entries: " + first);
        string warm = run();
        Check(warm.Contains("cacheHits=2") && warm.Contains("rebuilt=0") && warm.Contains("metadataBytes=0"), "warm worker uses both caches without deserializing bundle metadata: " + warm);
        using (var worker = new IndexWorker(cache, "game", plugins, new Dictionary<string, ConnectorKind>(), Console.WriteLine))
        {
            worker.Queue(sourceA); worker.Queue(sourceB);
            // Reset while work may be in flight; pre-reset results must not reappear.
            Thread.Sleep(30); worker.Reset();
            var watch = Stopwatch.StartNew();
            while (!worker.Idle && watch.ElapsedMilliseconds < 20000) Thread.Sleep(10);
            Check(worker.Idle && worker.Status.Contains("rebuilt=2") && worker.Status.Contains("cacheHits=0"),
                "reset serializes behind the active reader and rebuilds all known bundles: " + worker.Status);
        }
        Check(run().Contains("cacheHits=2"), "reset rebuild is reused on the next launch");
        using (var worker = new IndexWorker(cache, "game", plugins, new Dictionary<string, ConnectorKind>(), Console.WriteLine))
        {
            worker.Reset();
            var watch = Stopwatch.StartNew();
            while (!worker.Idle && watch.ElapsedMilliseconds < 5000) Thread.Sleep(10);
            Check(worker.Idle && Directory.GetFiles(cache, "*.gidx").Length == 0 && worker.Status.Contains("rebuilt=0"),
                "reset with indexing disabled clears disk entries without scheduling any source reads");
        }
        run(); // Restore both caches before exercising selective invalidation.
        File.WriteAllText(Path.Combine(a, "manifest.json"), "version=2");
        string updated = run();
        Check(updated.Contains("cacheHits=1") && updated.Contains("rebuilt=1"), "one package version rebuilds one entry: " + updated);
        string cacheA = Path.Combine(cache, IndexCache.Key(sourceA) + ".gidx");
        File.WriteAllText(cacheA, "corrupt");
        string corrupt = run();
        Check(corrupt.Contains("cacheHits=1") && corrupt.Contains("rebuilt=1"), "corrupt package cache rebuilds independently: " + corrupt);
        var partial = new BundleFacts(); partial.Add("assets/known.prefab", new ConnectorFacts { Kind = ConnectorKind.Clip, Connector = 2 }); partial.Seal();
        IndexCache.Save(cacheA, IndexCache.Fingerprint(sourceA, "game", plugins, new ReadBudget(new ManualResetEvent(false), false)) + ":all", partial);
        using (var worker = new IndexWorker(cache, "game", plugins, new Dictionary<string, ConnectorKind>(), Console.WriteLine))
        {
            worker.Queue(sourceA); worker.Queue(sourceB);
            bool partialAvailable = false;
            var watch = Stopwatch.StartNew();
            while (!worker.Idle && watch.ElapsedMilliseconds < 20000)
            {
                if (worker.Find(sourceA, "known") != null && !worker.Idle) partialAvailable = true;
                CheckUnknown(worker.Find(sourceB, "unknown"));
                Thread.Sleep(1);
            }
            Check(partialAvailable, "completed cache data is usable while another source is still pending; unresolved lookup remains unknown");
        }
        File.Delete(sourceA);
        string removed = run();
        Check(removed.Contains("bundles=1") && removed.Contains("cacheHits=1"), "removed source cannot serve an old disk cache: " + removed);
        Console.WriteLine("Worker test artifacts: " + root);
    }

    static void CheckUnknown(ConnectorFacts fact) { if (fact != null) throw new Exception("Unknown item received fabricated connector data"); }

    static void CancelReader(string bundle)
    {
        string root = Path.Combine(Path.GetTempPath(), "Gundomizer-cancel-tests-" + Guid.NewGuid().ToString("N"));
        var stop = new ManualResetEvent(false);
        Exception failure = null;
        var reader = new CachedMetadataReader(root,
            "game", root, new Dictionary<string, ConnectorKind>(), new ReadBudget(stop, true));
        var thread = new Thread(() => { try { reader.Read(bundle, null); } catch (Exception ex) { failure = ex; } });
        thread.Start();
        Thread.Sleep(250);
        stop.Set();
        Check(thread.Join(5000) && failure is OperationCanceledException, "cancelling an active reader returns promptly without publishing partial data");
        Check(!Directory.Exists(root) || Directory.GetFiles(root, "*.gidx").Length == 0, "cancelled reader does not publish a cache entry");
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL " + message);
        Console.WriteLine("PASS " + message);
    }

    static void Policies()
    {
        var fact = new ConnectorFacts { Kind = ConnectorKind.Attachment, Connector = 7 };
        var facts = new BundleFacts();
        facts.Add("assets/a/rail.prefab", fact);
        facts.Add("assets/b/rail.prefab", null);
        facts.Add("assets/unique.prefab", fact);
        facts.Seal();
        Check(facts.Find("ASSETS\\A\\RAIL.PREFAB") == fact && facts.Find("rail") == null
            && facts.Find("rail.prefab") == null && facts.Find("unique") == fact, "exact paths and unique aliases; ambiguous unknown assets never acquire a connector");
        string root = Path.Combine(Path.GetTempPath(), "Gundomizer-index-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string a = Path.Combine(root, "plugins", "pack-a"), b = Path.Combine(root, "plugins", "pack-b");
        Directory.CreateDirectory(a); Directory.CreateDirectory(b);
        string sourceA = Path.Combine(a, "bundle"), sourceB = Path.Combine(b, "bundle");
        File.WriteAllBytes(sourceA, new byte[1000]); File.WriteAllBytes(sourceB, new byte[1000]);
        string manifestA = Path.Combine(a, "manifest.json"), manifestB = Path.Combine(b, "manifest.json");
        File.WriteAllText(manifestA, "version=1"); File.WriteAllText(manifestB, "version=1");
        var stop = new ManualResetEvent(false);
        var budget = new ReadBudget(stop, false);
        string plugins = Path.Combine(root, "plugins");
        Func<string, string> fingerprint = path => IndexCache.Fingerprint(path, "game-1", plugins, budget);
        string firstA = fingerprint(sourceA), firstB = fingerprint(sourceB);
        string cache = Path.Combine(root, "entry.gidx");
        IndexCache.Save(cache, firstA, facts);
        var loaded = IndexCache.Load(cache, firstA);
        Check(loaded != null && loaded.Find("unique").Connector == 7 && loaded.Find("rail") == null, "persistent round trip preserves facts and ambiguous aliases");
        Check(IndexCache.Load(cache, "other-game-or-schema") == null, "stale fingerprint rejected");
        File.WriteAllText(manifestA, "version=2");
        Check(fingerprint(sourceA) != firstA && fingerprint(sourceB) == firstB, "package version invalidates only that package");
        firstA = fingerprint(sourceA);
        DateTime modified = File.GetLastWriteTimeUtc(sourceA);
        byte[] changed = File.ReadAllBytes(sourceA); changed[500] = 1; File.WriteAllBytes(sourceA, changed); File.SetLastWriteTimeUtc(sourceA, modified);
        Check(fingerprint(sourceA) != firstA, "sample fingerprint catches replacement even with preserved length and modification time");
        Check(IndexCache.Fingerprint(sourceB, "game-2", plugins, budget) != firstB, "game script identity invalidates cached connector interpretation");
        IndexCache.Save(cache, "updated", facts);
        Check(IndexCache.Load(cache, "updated") != null && IndexCache.Load(cache, firstA) == null, "atomic cache replacement updates an existing entry");
        byte[] broken = File.ReadAllBytes(cache); broken[broken.Length / 2] ^= 1; File.WriteAllBytes(cache, broken);
        Check(IndexCache.Load(cache, "updated") == null, "checksum rejects corrupt cache without publishing facts");
        File.WriteAllBytes(cache, new byte[5]);
        Check(IndexCache.Load(cache, "updated") == null, "truncated cache falls back");
        stop.Set();
        bool cancelled = false;
        try { budget.Check(); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "cancellation interrupts worker cooperatively");
        var timed = new ReadBudget(new ManualResetEvent(false), false);
        timed.BeginJob(1); Thread.Sleep(10);
        bool expired = false;
        try { timed.Check(); } catch (TimeoutException) { expired = true; }
        Check(expired, "per-bundle deadline interrupts the reader cooperatively");
        timed.BeginJob(); timed.Check();
        string resetDirectory = Path.Combine(root, "reset"); Directory.CreateDirectory(resetDirectory);
        string owned = Path.Combine(resetDirectory, IndexCache.Key(sourceA) + ".gidx");
        File.WriteAllText(owned, "cached");
        string unrelated = Path.Combine(resetDirectory, "notes.gidx"); File.WriteAllText(unrelated, "keep");
        string nested = Path.Combine(resetDirectory, "reader-job"); Directory.CreateDirectory(nested);
        string nestedFile = Path.Combine(nested, Path.GetFileName(owned)); File.WriteAllText(nestedFile, "keep");
        Check(IndexCache.Clear(resetDirectory) == 1 && !File.Exists(owned) && File.Exists(unrelated) && File.Exists(nestedFile),
            "reset deletes only owned top-level cache entries, preserving other files and directories");
        Check(IndexCache.Clear(resetDirectory) == 0, "reset is safe to repeat with an empty cache");
        // Temp directory intentionally retained as inspectable evidence; no recursive deletion.
        Console.WriteLine("Cache test artifacts: " + root);
    }
}
