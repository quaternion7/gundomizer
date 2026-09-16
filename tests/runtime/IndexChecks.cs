// Opt-in runtime verification. Never included in the mod package.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FistVR;
using HarmonyLib;
using UnityEngine;

public static class IndexChecks
{
    private static void Check(bool condition, string message, Action<string> log)
    { if (!condition) throw new Exception(message); log("PASS " + message); }

    public static IEnumerator Run(MonoBehaviour controller, string directory, Action<string> log)
    {
        var index = controller.GetType().Assembly.GetType("Gundomizer.PersistentConnectorIndex", true);
        var find = AccessTools.Method(index, "Find");
        var status = AccessTools.Property(index, "Status");
        var idle = AccessTools.Property(index, "Idle");
        var loading = AccessTools.Field(typeof(AnvilAsset), "m_loadingState");
        var objects = IM.OD.Values.Where(o => o != null).Distinct().ToArray();
        var before = objects.ToDictionary(o => o, o => loading.GetValue(o));
        float started = Time.realtimeSinceStartup, deadline = started + 180;
        log("INDEX START " + status.GetValue(null, null));
        // Startup may have captured an empty catalog before the loader registered its items.
        while (Time.realtimeSinceStartup < deadline)
        {
            int count = objects.Count(o => find.Invoke(null, new object[] { o }) != null);
            if ((bool)idle.GetValue(null, null) && count > 100) break;
            yield return new WaitForSecondsRealtime(1);
        }
        log("INDEX READY " + status.GetValue(null, null) + " waitSeconds=" + (Time.realtimeSinceStartup - started)
            + " maxMainSliceMs=" + AccessTools.Field(index, "MaxSliceMilliseconds").GetValue(null));
        Check((bool)idle.GetValue(null, null), "background index drained without blocking game frames", log);
        int changed = objects.Count(o => !ReferenceEquals(before[o], loading.GetValue(o)));
        log("INDEX CALLBACK CHANGES while waiting=" + changed + " (other mods may also load assets)");
        int indexed = 0, compared = 0, mismatches = 0;
        using (var writer = new StreamWriter(Path.Combine(directory, "persistent-index.tsv")))
        {
            writer.WriteLine("item\tcategory\tkind\tconnector\tloaded\tmatch");
            foreach (var obj in objects)
            {
                var fact = find.Invoke(null, new object[] { obj });
                if (fact == null) continue;
                ++indexed;
                int connector = (int)AccessTools.Field(fact.GetType(), "Connector").GetValue(fact);
                string kind = AccessTools.Field(fact.GetType(), "Kind").GetValue(fact).ToString();
                bool integrated = (bool)AccessTools.Field(fact.GetType(), "Integrated").GetValue(fact);
                var callback = loading.GetValue(obj) as AnvilCallback<GameObject>;
                bool loaded = callback != null && callback.IsCompleted;
                bool match = true;
                if (loaded)
                {
                    ++compared;
                    var root = callback.Result.GetComponent<FVRPhysicalObject>();
                    match = kind == "Attachment" && root is FVRFireArmAttachment && (int)((FVRFireArmAttachment)root).Type == connector
                        || kind == "Magazine" && root is FVRFireArmMagazine && (int)((FVRFireArmMagazine)root).MagazineType == connector
                            && ((FVRFireArmMagazine)root).IsIntegrated == integrated
                        || kind == "Clip" && root is FVRFireArmClip && (int)((FVRFireArmClip)root).ClipType == connector;
                    if (!match) ++mismatches;
                }
                writer.WriteLine(obj.ItemID + "\t" + obj.Category + "\t" + kind + "\t" + connector + "\t" + loaded + "\t" + match);
            }
        }
        Check(indexed > 100, "catalog items resolve to persisted prefab roots: " + indexed, log);
        Check(mismatches == 0, "serialized facts agree with " + compared + " already-loaded native components", log);
        // A Find-only pass must not create a native loading callback, regardless of cache state.
        var lookupBefore = objects.ToDictionary(o => o, o => loading.GetValue(o));
        foreach (var obj in objects) find.Invoke(null, new object[] { obj });
        Check(objects.All(o => ReferenceEquals(lookupBefore[o], loading.GetValue(o))), "index lookups create zero native asset requests", log);
        File.WriteAllText(Path.Combine(directory, "persistent-index-status.txt"), (string)status.GetValue(null, null));
        if (File.Exists(Path.Combine(directory, "cold-index-checks.txt")))
        {
            var cold = ColdWorker(controller, directory, index, log);
            try { while (cold.MoveNext()) yield return cold.Current; }
            finally { (cold as IDisposable).Dispose(); }
        }
    }

    private static IEnumerator ColdWorker(MonoBehaviour controller, string directory, Type index, Action<string> log)
    {
        var live = AccessTools.Field(index, "worker").GetValue(null);
        var type = live.GetType();
        var targets = (IDictionary)AccessTools.Field(index, "submitted").GetValue(null);
        var frames = controller.gameObject.AddComponent<MeasurementFrames>();
        object worker = null;
        bool control = File.Exists(Path.Combine(directory, "idle-index-control.txt"));
        try
        {
            frames.Reset();
            yield return new WaitForSecondsRealtime(10);
            log("INDEX BASELINE frames=" + frames.Count + " maxFrameMs=" + frames.Maximum + " over50=" + frames.Over50);
            var sourceObjects = IM.OD.Values.Where(o => o != null).Distinct().ToArray();
            var loading = AccessTools.Field(typeof(AnvilAsset), "m_loadingState");
            var before = sourceObjects.ToDictionary(o => o, o => loading.GetValue(o));
            var privateBytes = AccessTools.Method(typeof(ModdedMeasurements), "PrivateBytes");
            long startMemory = (long)privateBytes.Invoke(null, null), peakMemory = startMemory;
            long startManaged = GC.GetTotalMemory(false), peakManaged = startManaged;
            string output = Path.Combine(directory, "isolated-cold-index-" + Guid.NewGuid().ToString("N"));
            // Use the shipped worker and real target snapshot with a fresh TEST cache directory.
            // Production cache and button state remain usable throughout this isolated scan.
            if (!control)
            {
                worker = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                    new object[] { output, AccessTools.Field(type, "game").GetValue(live), AccessTools.Field(type, "plugins").GetValue(live),
                        AccessTools.Field(type, "types").GetValue(live), new Action<string>(message => { }),
                        AccessTools.Field(type, "readerExecutable").GetValue(live) }, null);
                foreach (DictionaryEntry pair in targets) AccessTools.Method(type, "Queue").Invoke(worker, new[] { pair.Key, pair.Value });
            }
            object measured = control ? live : worker;
            var idle = AccessTools.Property(type, "Idle");
            var status = AccessTools.Property(type, "Status");
            var find = AccessTools.Method(type, "Find");
            bool partial = false;
            frames.Reset();
            float started = Time.realtimeSinceStartup;
            while (control ? Time.realtimeSinceStartup - started < 43
                : !(bool)idle.GetValue(measured, null) && Time.realtimeSinceStartup - started < 180)
            {
                foreach (DictionaryEntry pair in targets)
                {
                    foreach (string name in (IEnumerable)pair.Value)
                        if (find.Invoke(measured, new[] { pair.Key, name }) != null && !(bool)idle.GetValue(measured, null)) { partial = true; break; }
                    if (partial) break;
                }
                peakMemory = Math.Max(peakMemory, (long)privateBytes.Invoke(null, null));
                peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(false));
                yield return new WaitForSecondsRealtime(1);
            }
            string report = status.GetValue(measured, null) + " elapsedSeconds=" + (Time.realtimeSinceStartup - started)
                + " frames=" + frames.Count + " maxFrameMs=" + frames.Maximum + " over50=" + frames.Over50
                + " sampledPrivatePeakDeltaMiB=" + ((peakMemory - startMemory) / 1048576.0)
                + " sampledManagedPeakDeltaMiB=" + ((peakManaged - startManaged) / 1048576.0);
            log((control ? "INDEX IDLE CONTROL " : "INDEX ISOLATED COLD ") + report);
            File.WriteAllText(Path.Combine(directory, control ? "index-idle-control-results.txt" : "isolated-cold-index.txt"), report);
            Check((bool)idle.GetValue(measured, null), "isolated worker/control completed", log);
            if (!control) Check(partial, "verified connector lookups work before background indexing completes", log);
            Check(sourceObjects.All(o => ReferenceEquals(before[o], loading.GetValue(o))), "isolated worker/control created zero native asset requests", log);
        }
        finally
        {
            if (worker != null) ((IDisposable)worker).Dispose();
            UnityEngine.Object.Destroy(frames);
        }
    }
}
