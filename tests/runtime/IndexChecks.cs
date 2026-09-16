// Opt-in runtime verification. Never included in the mod package.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    }
}
