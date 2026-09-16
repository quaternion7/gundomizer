using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using FistVR;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class PatchedOtherLoaderChecks
{
    private static T Get<T>(object target, string name) { return (T)AccessTools.Field(target.GetType(), name).GetValue(target); }
    private static object Call(object target, string name, params object[] args) { return AccessTools.Method(target.GetType(), name).Invoke(target, args); }
    private static void Check(bool condition, string message, Action<string> log)
    { if (!condition) throw new Exception(message); log("PASS " + message); }

    public static IEnumerator Run(ItemSpawnerV2 spawner, MonoBehaviour controller, string directory, Action<string> log)
    {
        BepInEx.PluginInfo loader;
        if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("h3vr.otherloader", out loader))
            throw new Exception("PatchedLoaderChecks requires OtherLoaderPatched and MLOK Rails in the test profile.");
        var assembly = loader.Instance.GetType().Assembly;
        var customType = assembly.GetType("OtherLoader.ItemSpawner.CustomCategories.CustomCategoriesController");
        Check(customType != null, "patched-loader suite is running against OtherLoaderPatched", log);
        var custom = spawner.GetComponent(customType);
        Check(custom != null, "OtherLoaderPatched custom browser is attached", log);
        var adapter = controller.GetType().Assembly.GetType("Gundomizer.OtherLoaderBridge");
        Check((bool)AccessTools.Property(adapter, "Active").GetValue(null, null), "optional OtherLoaderPatched adapter is active", log);
        var bridge = Get<object>(controller, "bridge");
        spawner.BTN_SetPageMode(3); spawner.BTN_SimpleMode_SwitchToSimpleMode();
        yield return null;
        var nativeContext = Call(bridge, "CaptureContext");
        var nativePool = (List<ItemSpawnerID>)Call(bridge, "CaptureSection");
        Check(nativePool.Count > 100, "patched loader preserves the native attachment overview", log);
        var expected = new List<string>(ManagerSingleton<IM>.Instance.PageItemLists[ItemSpawnerV2.PageMode.Attachments]);
        var sorter = assembly.GetType("OtherLoader.ItemSpawner.VanillaCategories.SpawnerIDSorter");
        AccessTools.Method(sorter, "FilterAndSortItemsSimpleMode").Invoke(null, new object[] { expected });
        var expectedObjects = expected.Select(IM.GetSpawnerID)
            .Where(e => (bool)AccessTools.Method(adapter, "Available").Invoke(null, new object[] { e }))
            .Select(e => e.MainObject.ItemID).Distinct().ToArray();
        log("OVERVIEW level=" + Get<Dictionary<ItemSpawnerV2.PageMode, ItemSpawnerV2.SimpleDisplayLevel>>(spawner, "m_displayLevel")[ItemSpawnerV2.PageMode.Attachments]
            + " actual=" + nativePool.Count + " expected=" + expectedObjects.Count()
            + " extra=" + string.Join(",", nativePool.Select(e => e.MainObject.ItemID).Except(expectedObjects).Take(15).ToArray())
            + " missing=" + string.Join(",", expectedObjects.Except(nativePool.Select(e => e.MainObject.ItemID)).Take(15).ToArray()));
        Check(new HashSet<string>(nativePool.Select(e => e.MainObject.ItemID)).SetEquals(expectedObjects),
            "native overview respects patched loader hidden/custom-category filtering", log);
        var address = AccessTools.Field(typeof(AnvilAsset), "m_anvilPrefab");
        var installedRails = IM.OD.Values.Where(o => o != null && o.IsModContent
            && (((Anvil.AssetID)address.GetValue(o)).Bundle ?? "").ToLowerInvariant().Contains("mlok_rails")).ToArray();
        Check(installedRails.Length > 0, "MLOK Rails dependant registered objects", log);
        log("MLOK OBJECTS " + string.Join(", ", installedRails.Select(o => o.ItemID).ToArray()));
        var railEntry = installedRails.Select(o => (ItemSpawnerID)AccessTools.Method(adapter, "Resolve").Invoke(null, new object[] { o.ItemID }))
            .FirstOrDefault(e => e != null && e.MainObject != null && (bool)AccessTools.Method(adapter, "Available").Invoke(null, new object[] { e }));
        Check(railEntry != null, "dependant object IDs resolve to native spawner entries", log);
        var railPage = ManagerSingleton<IM>.Instance.PageItemLists.First(p => p.Value.Contains(railEntry.ItemID)).Key;
        log("RAIL ENTRY id=" + railEntry.ItemID + " page=" + railPage + " visible=" + railEntry.IsDisplayedInMainEntry);

        Call(custom, "EnableCustomCategoryMode");
        yield return null;
        Check(Get<RectTransform>(controller, "uiRoot").gameObject.activeInHierarchy, "Gundomizer buttons visible in patched custom categories", log);
        Check(!(bool)Call(bridge, "MatchesContext", nativeContext), "entering patched categories invalidates native roll context", log);
        var rootPool = (List<ItemSpawnerID>)Call(bridge, "CaptureSection");
        Check(rootPool.Count > 0, "custom category root includes descendant items", log);
        var root = Get<object>(custom, "CurrentSelectedFolderTile");
        var rootContext = Call(bridge, "CaptureContext");
        Call(custom, "GoToNextPage");
        var paged = (List<ItemSpawnerID>)Call(bridge, "CaptureSection");
        Check(new HashSet<string>(rootPool.Select(e => e.ItemID)).SetEquals(paged.Select(e => e.ItemID)),
            "custom category pool includes every page", log);
        Call(custom, "GoToPreviousPage");
        var folders = (IList)Call(root, "GetFilteredCopyOfChildren");
        int child = -1;
        for (int i = 0; i < Math.Min(18, folders.Count); ++i)
            if (AccessTools.Method(folders[i].GetType(), "GetFilteredCopyOfChildren") != null) { child = i; break; }
        Check(child >= 0, "custom category has a navigable subfolder", log);
        Call(custom, "HandleClickOnTile", child);
        var childPool = (List<ItemSpawnerID>)Call(bridge, "CaptureSection");
        Check(childPool.Count > 0 && childPool.All(e => rootPool.Any(p => p.ItemID == e.ItemID))
            && !(bool)Call(bridge, "MatchesContext", rootContext), "custom subfolder narrows pool and invalidates prior context", log);

        var plugin = BepInEx.Bootstrap.Chainloader.PluginInfos["quaternion.gundomizer"].Instance;
        var instant = (ConfigEntry<bool>)AccessTools.Field(plugin.GetType(), "SpawnItemInstantly").GetValue(null);
        bool previousInstant = instant.Value, previousSave = plugin.Config.SaveOnConfigSet;
        var created = new List<GameObject>();
        plugin.Config.SaveOnConfigSet = false;
        try
        {
            instant.Value = false;
            yield return new WaitForSecondsRealtime(.4f);
            // Real button click, still inside the custom subfolder.
            Get<MonoBehaviour>(controller, "randomButton").GetComponent<Button>().onClick.Invoke();
            float deadline = Time.realtimeSinceStartup + 90;
            while (Get<bool>(controller, "busy") && Time.realtimeSinceStartup < deadline) yield return null;
            Check(!Get<bool>(controller, "busy") && childPool.Any(e => e.ItemID == Get<string>(spawner, "m_selectedID")),
                "random button selects from the patched custom subfolder", log);
            spawner.BTN_SetPageMode((int)railPage); spawner.BTN_SimpleMode_SwitchToTagSearch(); spawner.BTN_Tag_ClearSelectedTags();
            yield return null;
            var tagPool = (List<ItemSpawnerID>)Call(bridge, "CaptureSection");
            Check(tagPool.Any(e => e.ItemID == railEntry.ItemID), "dependant rails participate in their native tag results", log);
            Call(bridge, "SelectEntry", railEntry);
            Check(Get<string>(spawner, "m_selectedID") == railEntry.ItemID, "patched selection uses native spawner ID", log);
            var sourceCount = ((List<FVRObject>)AccessTools.Method(adapter, "SpawnSources").Invoke(null, new object[] { railEntry })).Count;
            var before = new HashSet<int>(Object.FindObjectsOfType<FVRPhysicalObject>().Select(o => o.GetInstanceID()));
            spawner.BTN_Details_Spawn();
            deadline = Time.realtimeSinceStartup + 90;
            while (Get<bool>(controller, "busy") && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSecondsRealtime(.4f);
            var spawned = Object.FindObjectsOfType<FVRPhysicalObject>().Where(o => !before.Contains(o.GetInstanceID())).ToArray();
            created.AddRange(spawned.Select(o => o.gameObject));
            Check(!Get<bool>(controller, "busy") && spawned.Length == sourceCount && spawned.Any(o => o.ObjectWrapper != null
                && o.ObjectWrapper.ItemID == railEntry.MainObject.ItemID), "native Spawn creates dependant rail exactly once, without competing loader spawn", log);
            File.WriteAllText(Path.Combine(directory, "patched-loader-result.txt"), "OtherLoaderPatched and dependant checks passed.");
        }
        finally
        {
            instant.Value = previousInstant; plugin.Config.SaveOnConfigSet = previousSave;
            if ((bool)AccessTools.Property(customType, "IsCustomCategoryModeActive").GetValue(custom, null)) Call(custom, "DisableCustomCategoryMode");
            foreach (var obj in created) if (obj != null) Object.Destroy(obj);
        }
        log("ALL PATCHED OTHERLOADER CHECKS PASSED");
    }
}
