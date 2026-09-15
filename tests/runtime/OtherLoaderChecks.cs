using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using FistVR;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

public static class OtherLoaderChecks
{
    private static T Get<T>(object target, string name) { return (T)AccessTools.Field(target.GetType(), name).GetValue(target); }
    private static object Call(object target, string name, params object[] args) { return AccessTools.Method(target.GetType(), name).Invoke(target, args); }
    private static void Check(bool condition, string message, Action<string> log) { if (!condition) throw new Exception(message); log("PASS " + message); }
    public static IEnumerator Run(ItemSpawnerV2 spawner, MonoBehaviour controller, Action<string> log)
    {
        var adapter = controller.GetType().Assembly.GetType("Gundomizer.OtherLoaderBridge");
        if (!(bool)AccessTools.Property(adapter, "Active").GetValue(null, null)) yield break;
        var bridge = Get<object>(controller, "bridge");
        var loader = BepInEx.Bootstrap.Chainloader.PluginInfos["h3vr.otherloader"].Instance.GetType();
        var entries = (IDictionary)AccessTools.Field(loader, "SpawnerEntriesByID").GetValue(null);
        spawner.BTN_SetPageMode(3); spawner.BTN_SimpleMode_SwitchToTagSearch(); spawner.BTN_Tag_ClearSelectedTags();
        var pool = (List<ItemSpawnerID>)Call(bridge, "CaptureSection");
        var working = Get<List<string>>(spawner, "WorkingItemIDs");
        var expected = working.Select(id => (ItemSpawnerID)AccessTools.Method(adapter, "Resolve").Invoke(null, new object[] { id }))
            .Where(e => e != null && (bool)AccessTools.Method(adapter, "Available").Invoke(null, new object[] { e }))
            .Select(e => e.MainObject.ItemID);
        Check(new HashSet<string>(pool.Select(e => e.MainObject.ItemID)).SetEquals(expected) && pool.Count > 343,
            "OtherLoader tag results retain native and modded attachment IDs across pages", log);
        spawner.BTN_List_PageNext();
        Check(new HashSet<string>(pool.Select(e => e.MainObject.ItemID)).SetEquals(((List<ItemSpawnerID>)Call(bridge, "CaptureSection")).Select(e => e.MainObject.ItemID)),
            "OtherLoader tag pagination preserves the full pool", log);

        spawner.BTN_SetPageMode(1); spawner.BTN_SimpleMode_SwitchToSimpleMode();
        pool = (List<ItemSpawnerID>)Call(bridge, "CaptureSection");
        Check(pool.Any(e => e.ItemID == "SMGUziMini") && pool.Any(e => e.ItemID == "Volks_ModulXM8_Base"),
            "OtherLoader classic root includes native and modded firearms from descendant categories", log);
        spawner.BTN_SimpleMode_NextPage();
        Check(new HashSet<string>(pool.Select(e => e.ItemID)).SetEquals(((List<ItemSpawnerID>)Call(bridge, "CaptureSection")).Select(e => e.ItemID)),
            "OtherLoader classic pagination preserves the pool", log);
        spawner.BTN_SimpleMode_PrevPage();
        var data = spawner.GetComponent(loader.Assembly.GetType("OtherLoader.ItemSpawnerData"));
        var visible = Get<IList>(data, "VisibleEntries");
        int folder = -1;
        for (int i = 0; i < visible.Count; ++i)
            if ((bool)AccessTools.Method(loader, "DoesEntryHaveChildren").Invoke(null, new[] { visible[i] })) { folder = i; break; }
        Check(folder >= 0, "OtherLoader classic test finds a category tile", log);
        var context = Call(bridge, "CaptureContext");
        spawner.BTN_SimpleMode_SelectTile(folder);
        var childPool = (List<ItemSpawnerID>)Call(bridge, "CaptureSection");
        string path = Get<string>(data, "CurrentPath");
        Check(!(bool)Call(bridge, "MatchesContext", context) && childPool.Count > 0 && childPool.Count < pool.Count
            && childPool.All(e => Get<string>(entries[e.MainObject.ItemID], "EntryPath").StartsWith(path + "/", StringComparison.Ordinal)),
            "OtherLoader classic navigation narrows the pool and invalidates the old context", log);

        var plugin = BepInEx.Bootstrap.Chainloader.PluginInfos["quaternion.gundomizer"].Instance;
        var instant = (ConfigEntry<bool>)AccessTools.Field(plugin.GetType(), "SpawnItemInstantly").GetValue(null);
        bool original = instant.Value;
        var created = new List<GameObject>();
        try
        {
            spawner.BTN_SetPageMode(2);
            instant.Value = false;
            yield return new WaitForSecondsRealtime(.35f);
            Call(controller, "ClickAmmo", new object[] { null });
            float deadline = Time.realtimeSinceStartup + 30;
            while (Get<bool>(controller, "busy") && Time.realtimeSinceStartup < deadline) yield return null;
            string selected = Get<string>(spawner, "m_selectedID");
            var entry = (ItemSpawnerID)AccessTools.Method(adapter, "Resolve").Invoke(null, new object[] { selected });
            Check(entry != null && selected == entry.MainObject.ItemID && entry.MainObject.Category == FVRObject.ObjectCategory.Cartridge
                && spawner.BTN_SpawnSelectedObject.activeSelf && spawner.TXT_Title.text == Get<string>(entries[selected], "DisplayName"),
                "ammo selection uses the OtherLoader object ID and populates the real details panel", log);
            var before = new HashSet<int>(Object.FindObjectsOfType<FVRPhysicalObject>().Select(o => o.GetInstanceID()));
            spawner.BTN_Details_Spawn();
            deadline = Time.realtimeSinceStartup + 30;
            while (Get<bool>(controller, "busy") && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            var added = Object.FindObjectsOfType<FVRPhysicalObject>().Where(o => !before.Contains(o.GetInstanceID())).ToArray();
            created.AddRange(added.Select(o => o.gameObject));
            log("NATIVE AMMO ACCEPT count=" + added.Length + " expected=" + entry.MainObject.ItemID
                + " actual=" + string.Join(",", added.Select(o => o.ObjectWrapper == null ? "null wrapper" : o.ObjectWrapper.ItemID).ToArray()));
            Check(added.Length == 1 && added[0].IDSpawnedFrom == entry && added[0] is FVRFireArmRound
                && added[0].ObjectWrapper != null && added[0].ObjectWrapper.ItemID == entry.MainObject.ItemID,
                "accepting ammo with native Spawn creates one object with both Harmony prefixes installed", log);

            var bundled = IM.GetSpawnerID("MFPrimaryBigBoomer");
            Call(bridge, "SelectEntry", bundled);
            var sources = (List<FVRObject>)AccessTools.Method(adapter, "SpawnSources").Invoke(null, new object[] { bundled });
            Check(sources.Count > 1, "OtherLoader bundled fixture has authored companion objects", log);
            before = new HashSet<int>(Object.FindObjectsOfType<FVRPhysicalObject>().Select(o => o.GetInstanceID()));
            spawner.BTN_Details_Spawn();
            deadline = Time.realtimeSinceStartup + 30;
            while (Get<bool>(controller, "busy") && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            added = Object.FindObjectsOfType<FVRPhysicalObject>().Where(o => !before.Contains(o.GetInstanceID())).ToArray();
            created.AddRange(added.Select(o => o.gameObject));
            Check(added.Length == sources.Count && new HashSet<string>(added.Select(o => o.ObjectWrapper.ItemID)).SetEquals(sources.Select(o => o.ItemID)),
                "native acceptance preserves OtherLoader's SpawnWithIDs without duplicate spawning", log);

            instant.Value = true;
            yield return new WaitForSecondsRealtime(.35f);
            before = new HashSet<int>(Object.FindObjectsOfType<FVRPhysicalObject>().Select(o => o.GetInstanceID()));
            Call(controller, "ClickAmmo", new object[] { null });
            deadline = Time.realtimeSinceStartup + 30;
            while (Get<bool>(controller, "busy") && Time.realtimeSinceStartup < deadline) yield return null;
            added = Object.FindObjectsOfType<FVRPhysicalObject>().Where(o => !before.Contains(o.GetInstanceID())).ToArray();
            created.AddRange(added.Select(o => o.gameObject));
            Check(added.Length == 1 && added[0] is FVRFireArmRound, "instant ammo roll also spawns once under OtherLoader", log);
        }
        finally
        {
            instant.Value = original;
            foreach (var obj in created) if (obj != null) Object.Destroy(obj);
        }
        var safety = SafetyChecks.Run(spawner, controller, log);
        try { while (safety.MoveNext()) yield return safety.Current; }
        finally { (safety as IDisposable).Dispose(); }
        log("ALL OTHERLOADER INTEGRATION CHECKS PASSED");
    }
}
