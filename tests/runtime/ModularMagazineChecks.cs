using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Anvil;
using FistVR;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

// Opt-in checks against installed real packages; never distributed with the mod.
public static class ModularMagazineChecks
{
    private static readonly System.Reflection.FieldInfo Address = AccessTools.Field(typeof(AnvilAsset), "m_anvilPrefab");
    private static string Bundle(FVRObject obj) { return ((AssetID)Address.GetValue(obj)).Bundle ?? ""; }
    private static void Check(bool condition, string message, Action<string> log)
    { if (!condition) throw new Exception(message); log("PASS " + message); }

    public static IEnumerator Run(ItemSpawnerV2 spawner, MonoBehaviour controller, string directory, Action<string> log)
    {
        var assembly = controller.GetType().Assembly;
        var adapter = assembly.GetType("Gundomizer.OtherLoaderBridge");
        var compatibility = assembly.GetType("Gundomizer.Compatibility");
        var persistent = assembly.GetType("Gundomizer.PersistentConnectorIndex");
        var sources = IM.OD.Values.Where(o => o != null).Distinct().ToArray();
        bool verify = File.Exists(Path.Combine(directory, "verify-modular-fix.txt"));
        if (verify)
        {
            var loading = AccessTools.Field(typeof(AnvilAsset), "m_loadingState");
            var callbacks = sources.ToDictionary(o => o, o => loading.GetValue(o));
            float deadline = Time.realtimeSinceStartup + 120;
            while (!(bool)AccessTools.Property(persistent, "Idle").GetValue(null, null) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Check((bool)AccessTools.Property(persistent, "Idle").GetValue(null, null), "background index finished", log);
            Check(callbacks.All(p => loading.GetValue(p.Key) == p.Value), "background metadata indexing requested no Unity prefabs", log);
            log("MODULAR INDEX " + AccessTools.Property(persistent, "Status").GetValue(null, null));
        }
        File.WriteAllLines(Path.Combine(directory, "modular-catalog.tsv"), new[] { "id\tname\tcategory\tmagazineType\tbundle" }.Concat(
            sources.Where(o => o.IsModContent).Select(o => o.ItemID + "\t" + o.DisplayName + "\t" + o.Category + "\t" + o.MagazineType + "\t" + Bundle(o))).ToArray());
        var candidates = sources.Where(o => o.IsModContent && (Bundle(o).ToLowerInvariant().Contains("modular2")
            || Bundle(o).ToLowerInvariant().Contains("modulak"))).ToArray();
        log("MODULAR registry matches=" + candidates.Length + " bundles=" + string.Join(",", candidates.Select(Bundle).Distinct().ToArray()));
        var magazines = candidates.Where(o => (o.DisplayName + " " + o.ItemID).ToLowerInvariant().Contains("mag"))
            .GroupBy(o => Bundle(o).ToLowerInvariant().Contains("modulak") ? "AK" : "AR")
            .SelectMany(g => g.OrderBy(o => o.ItemID).Take(3)).ToArray();
        Check(magazines.Length == 6, "three real examples from each modular magazine package found", log);
        int verified = 0;
        foreach (var source in magazines)
        {
            var entry = (ItemSpawnerID)AccessTools.Method(adapter, "Resolve").Invoke(null, new object[] { source.ItemID });
            var facts = AccessTools.Method(persistent, "Find").Invoke(null, new object[] { source });
            log("MODULAR BEFORE id=" + source.ItemID + " category=" + source.Category + " declaredMag=" + source.MagazineType
                + " entry=" + (entry == null ? "null" : entry.ItemID) + " indexed=" + (facts != null));
            if (entry == null) continue;
            if (verify && Bundle(source).ToLowerInvariant().Contains("modulak"))
                Check(facts != null, "ModulAK magazine indexed before prefab loading " + source.ItemID, log);
            var load = source.GetGameObjectAsync();
            float deadline = Time.realtimeSinceStartup + 180;
            while (!load.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Check(load.IsCompleted && load.Result != null, "example prefab loaded " + source.ItemID, log);
            var magazine = load.Result.GetComponent<FVRFireArmMagazine>();
            log("MODULAR COMPONENTS " + source.ItemID + " " + string.Join(",", load.Result.GetComponents<Component>()
                .Where(c => c != null).Select(c => c.GetType().FullName).ToArray()));
            if (magazine == null) { log("MODULAR named example is not a root magazine"); continue; }
            var firearmSource = sources.FirstOrDefault(o => !o.IsModContent && o.Category == FVRObject.ObjectCategory.Firearm
                && o.MagazineType == magazine.MagazineType);
            if (firearmSource == null) { log("MODULAR no vanilla target for " + magazine.MagazineType); continue; }
            var firearmLoad = firearmSource.GetGameObjectAsync();
            deadline = Time.realtimeSinceStartup + 180;
            while (!firearmLoad.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Check(firearmLoad.IsCompleted && firearmLoad.Result != null, "compatible reference gun loaded " + firearmSource.ItemID, log);
            var held = Object.Instantiate(firearmLoad.Result, spawner.transform.position + Vector3.up, Quaternion.identity);
            try
            {
                yield return null;
                var query = AccessTools.Method(compatibility, "Capture").Invoke(null, new object[] { held.GetComponent<FVRPhysicalObject>() });
                bool cold = (bool)AccessTools.Method(query.GetType(), "CouldMatch").Invoke(query, new object[] { entry, false });
                bool warm = (bool)AccessTools.Method(query.GetType(), "CouldMatch").Invoke(query, new object[] { entry, true });
                bool live = (bool)AccessTools.Method(query.GetType(), "Matches").Invoke(query, new object[] { load.Result });
                string pages = string.Join(",", ManagerSingleton<IM>.Instance.PageItemLists.Where(p => p.Value.Contains(entry.ItemID)).Select(p => p.Key.ToString()).ToArray());
                log("MODULAR RESULT " + source.ItemID + " actualMag=" + magazine.MagazineType + " gun=" + firearmSource.ItemID
                    + " pages=" + pages + " cold=" + cold + " warm=" + warm + " native=" + live);
                Check(live && cold && warm, "real magazine matches reference gun with and without index", log);
                Check(source.Category == FVRObject.ObjectCategory.Magazine && pages == "Firearms",
                    "magazine function is independent of Firearms spawner placement", log);
                spawner.BTN_SetPageMode(2); spawner.BTN_SimpleMode_SwitchToTagSearch(); spawner.BTN_Tag_ClearSelectedTags();
                yield return null;
                var bridge = AccessTools.Field(controller.GetType(), "bridge").GetValue(controller);
                var ammoPool = (List<ItemSpawnerID>)AccessTools.Method(bridge.GetType(), "CaptureSection").Invoke(bridge, null);
                Check(!ammoPool.Any(e => e.MainObject.ItemID == source.ItemID), "Ammo scope excludes magazine authored under Firearms", log);
                spawner.BTN_SetPageMode(1); spawner.BTN_Tag_ClearSelectedTags();
                yield return null;
                var firearmPool = (List<ItemSpawnerID>)AccessTools.Method(bridge.GetType(), "CaptureSection").Invoke(bridge, null);
                Check(firearmPool.Any(e => e.MainObject.ItemID == source.ItemID), "Firearms scope includes modular magazine", log);
                ++verified;
            }
            finally { Object.Destroy(held); }
            yield return null;
        }
        Check(verified == 6, "all six real magazine cases verified", log);
        log("MODULAR checks finished");
    }
}
