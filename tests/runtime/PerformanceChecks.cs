using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BepInEx.Configuration;
using FistVR;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class PerformanceChecks
{
    private static readonly Dictionary<AnvilAsset, GameObject> fixtures = new Dictionary<AnvilAsset, GameObject>();
    private static int requests;
    private static bool stall;
    private static AnvilCallback<GameObject> stalled;
    private static readonly Dictionary<int, int> requestsPerFrame = new Dictionary<int, int>();
    private static readonly System.Reflection.FieldInfo loading = AccessTools.Field(typeof(AnvilAsset), "m_loadingState");
    private static T Get<T>(object target, string name) => (T)AccessTools.Field(target.GetType(), name).GetValue(target);
    private static object Call(object target, string name, params object[] args) => AccessTools.Method(target.GetType(), name).Invoke(target, args);
    private static void Check(bool condition, string text, Action<string> log)
    { if (!condition) throw new Exception(text); log("PASS " + text); }

    private static bool FakeLoad(AnvilAsset __instance, ref AnvilCallback<GameObject> __result)
    {
        GameObject prefab;
        if (!fixtures.TryGetValue(__instance, out prefab)) return true;
        ++requests;
        int count; requestsPerFrame.TryGetValue(Time.frameCount, out count);
        requestsPerFrame[Time.frameCount] = count + 1;
        __result = new AnvilCallback<GameObject>(stall ? null : new AnvilDummyOperation(prefab), null);
        loading.SetValue(__instance, __result);
        if (stall) stalled = __result;
        return false;
    }

    public static IEnumerator Run(ItemSpawnerV2 spawner, MonoBehaviour controller, object bridge, Action<string> log)
    {
        var assembly = controller.GetType().Assembly;
        var plugin = BepInEx.Bootstrap.Chainloader.PluginInfos["quaternion.gundomizer"].Instance;
        var instant = (ConfigEntry<bool>)AccessTools.Field(plugin.GetType(), "SpawnItemInstantly").GetValue(null);
        bool priorInstant = instant.Value;
        bool priorSave = plugin.Config.SaveOnConfigSet;
        plugin.Config.SaveOnConfigSet = false;
        spawner.BTN_SetPageMode(3);
        spawner.BTN_SimpleMode_SwitchToTagSearch();
        spawner.BTN_Tag_ClearSelectedTags();
        // Paired, warmed measurement of the old redraw-and-capture path versus the new snapshot.
        var beforeTimes = new List<double>(); var afterTimes = new List<double>();
        for (int i = 0; i < 25; ++i)
        {
            var watch = Stopwatch.StartNew();
            Call(spawner, "RedrawListCanvas");
            var before = (List<ItemSpawnerID>)Call(bridge, "CaptureSection");
            watch.Stop(); beforeTimes.Add(watch.Elapsed.TotalMilliseconds);
            watch.Reset(); watch.Start();
            var after = (List<ItemSpawnerID>)Call(bridge, "CaptureSection");
            watch.Stop(); afterTimes.Add(watch.Elapsed.TotalMilliseconds);
            Check(new HashSet<string>(before.Select(e => e.ItemID)).SetEquals(after.Select(e => e.ItemID)), "snapshot preserves native attachment results " + i, log);
            yield return null;
        }
        beforeTimes.Sort(); afterTimes.Sort();
        log("CAPTURE BENCHMARK median old=" + beforeTimes[12].ToString("F3") + "ms new=" + afterTimes[12].ToString("F3") + "ms (25 pairs; excludes asset loading)");

        var indexType = assembly.GetType("Gundomizer.ConnectorIndex");
        int sweep = (int)AccessTools.Field(indexType, "Sweeps").GetValue(null);
        var objects = IM.OD.Values.ToArray();
        var callbacks = objects.Select(o => loading.GetValue(o)).ToArray();
        float deadline = Time.realtimeSinceStartup + 30f;
        while ((int)AccessTools.Field(indexType, "Sweeps").GetValue(null) == sweep && Time.realtimeSinceStartup < deadline) yield return null;
        Check((int)AccessTools.Field(indexType, "Sweeps").GetValue(null) > sweep, "background connector sweep completes", log);
        Check(objects.Select((o, i) => loading.GetValue(o) == callbacks[i]).All(s => s), "background index starts zero catalog prefab requests", log);
        log("INDEX records=" + AccessTools.Property(indexType, "Count").GetValue(null, null)
            + " catalog=" + objects.Length + " maximum observed slice=" + AccessTools.Field(indexType, "MaxSliceMilliseconds").GetValue(null) + "ms");

        var registry = ManagerSingleton<IM>.Instance;
        var ids = Get<Dictionary<string, ItemSpawnerID>>(registry, "SpawnerIDDic");
        var originalPage = registry.PageItemLists[ItemSpawnerV2.PageMode.Attachments];
        var originalWorking = Get<List<string>>(spawner, "WorkingItemIDs");
        var entries = new List<ItemSpawnerID>();
        var hook = new Harmony("quaternion.gundomizer.performancefixture." + Guid.NewGuid());
        requests = 0; stall = false; stalled = null;
        requestsPerFrame.Clear();
        try
        {
            hook.Patch(AccessTools.Method(typeof(AnvilAsset), "GetGameObjectAsync"), prefix: new HarmonyMethod(typeof(PerformanceChecks), nameof(FakeLoad)));
            for (int i = 0; i < 12; ++i)
            {
                var obj = ScriptableObject.CreateInstance<FVRObject>();
                obj.ItemID = "Gundomizer-test-" + Guid.NewGuid(); obj.Category = FVRObject.ObjectCategory.Attachment;
                var prefab = new GameObject(obj.ItemID); prefab.SetActive(false);
                prefab.AddComponent<FVRFireArmAttachment>().Type = (FVRFireArmAttachementMountType)987654;
                fixtures.Add(obj, prefab);
                var entry = ScriptableObject.CreateInstance<ItemSpawnerID>();
                entry.ItemID = obj.ItemID; entry.DisplayName = "Gundomizer fixture"; entry.MainObject = obj;
                entry.Secondaries = new ItemSpawnerID[0]; entry.Secondaries_ByStringID = new List<string>(); entry.TutorialBlocks = new List<string>();
                ids.Add(entry.ItemID, entry); entries.Add(entry);
            }
            var fixtureIds = entries.Select(e => e.ItemID).ToList();
            registry.PageItemLists[ItemSpawnerV2.PageMode.Attachments] = fixtureIds;
            AccessTools.Field(spawner.GetType(), "WorkingItemIDs").SetValue(spawner, fixtureIds);
            instant.Value = false;
            yield return new WaitForSecondsRealtime(.35f);
            Get<MonoBehaviour>(controller, "compatibleButton").GetComponent<Button>().onClick.Invoke();
            deadline = Time.realtimeSinceStartup + 10;
            while (Get<bool>(controller, "busy") && Time.realtimeSinceStartup < deadline) yield return null;
            Check(!Get<bool>(controller, "busy") && requests == 12, "one click automatically checks all 12 cold candidates, exceeding the former limit", log);
            Check(requestsPerFrame.Values.All(v => v == 1), "new requests are paced to at most one per frame", log);
            Check(Get<string>(controller, "status").StartsWith("No compatible"), "only an exhausted search reports no compatible items", log);
            // A normal selection roll must not issue any prefab requests, even for an uncached item.
            foreach (var entry in entries) loading.SetValue(entry.MainObject, null);
            int beforeRequests = requests;
            yield return new WaitForSecondsRealtime(.35f);
            Get<MonoBehaviour>(controller, "randomButton").GetComponent<Button>().onClick.Invoke();
            Check(requests == beforeRequests && fixtureIds.Contains(Get<string>(spawner, "m_selectedID")), "plain selection uses metadata without loading a prefab", log);

            // Cancellation must leave the shared load gate occupied until its request completes.
            stall = true; instant.Value = true;
            yield return new WaitForSecondsRealtime(.35f);
            Get<MonoBehaviour>(controller, "randomButton").GetComponent<Button>().onClick.Invoke();
            yield return null;
            Check(stalled != null && Get<bool>(controller, "busy"), "fixture starts one genuinely pending load", log);
            yield return new WaitForSecondsRealtime(10.5f);
            var button = Get<MonoBehaviour>(controller, "randomButton").GetComponent<Button>();
            Check(Get<bool>(controller, "busy") && button.interactable && Get<string>(controller, "status").Contains("Click again to cancel"),
                "slow load continues beyond ten seconds with progress and an active cancel button", log);
            var originalStalled = stalled;
            var originalStalledObject = entries.First(e => loading.GetValue(e.MainObject) == stalled).MainObject;
            var beforeCancel = new HashSet<int>(Object.FindObjectsOfType<FVRPhysicalObject>().Select(o => o.GetInstanceID()));
            button.onClick.Invoke();
            yield return null;
            Check(!Get<bool>(controller, "busy") && Get<string>(controller, "status") == "Randomizer cancelled.", "clicking the active button cancels its roll", log);
            var access = assembly.GetType("Gundomizer.AssetAccess");
            var different = entries.First(e => loading.GetValue(e.MainObject) == null).MainObject;
            object[] requestArgs = { different, null, false };
            Check(!(bool)AccessTools.Method(access, "TryRequest").Invoke(null, requestArgs), "another panel cannot add a new load after cancellation", log);
            AccessTools.Field(spawner.GetType(), "WorkingItemIDs").SetValue(spawner, new List<string> { different.ItemID });
            int countBeforeWait = requests;
            yield return new WaitForSecondsRealtime(.35f);
            button.onClick.Invoke();
            yield return new WaitForSecondsRealtime(.4f);
            Check(Get<bool>(controller, "busy") && requests == countBeforeWait && Get<string>(controller, "status").StartsWith("Waiting"),
                "next roll waits automatically behind the cancelled shared load", log);
            originalStalled.Request = new AnvilDummyOperation(fixtures[originalStalledObject]);
            originalStalled.Pump();
            yield return null; yield return null;
            Check(requests == countBeforeWait + 1 && stalled != originalStalled && Get<bool>(controller, "busy"),
                "waiting roll starts its request automatically when the shared gate opens", log);
            button.onClick.Invoke(); yield return null;
            stalled.Request = new AnvilDummyOperation(fixtures[different]); stalled.Pump();
            yield return null;
            Check(Object.FindObjectsOfType<FVRPhysicalObject>().All(o => beforeCancel.Contains(o.GetInstanceID())),
                "finishing cancelled loads never spawns an item", log);

            // Native Spawn has no timeout either, and can cancel without cancelling Anvil.
            loading.SetValue(different, null);
            instant.Value = false;
            yield return new WaitForSecondsRealtime(.35f);
            button.onClick.Invoke();
            spawner.BTN_Details_Spawn();
            yield return new WaitForSecondsRealtime(10.5f);
            Check(Get<bool>(controller, "busy"), "native Spawn keeps waiting past the former timeout", log);
            spawner.BTN_Details_Spawn(); yield return null;
            Check(!Get<bool>(controller, "busy") && Get<string>(controller, "status") == "Randomizer cancelled.", "native Spawn can cancel its pending acceptance", log);
            stalled.Request = new AnvilDummyOperation(fixtures[different]); stalled.Pump();
            yield return null;
            Check(Object.FindObjectsOfType<FVRPhysicalObject>().All(o => beforeCancel.Contains(o.GetInstanceID())), "cancelled native acceptance leaves no late object", log);

            loading.SetValue(different, null); instant.Value = true;
            yield return new WaitForSecondsRealtime(.35f); button.onClick.Invoke(); yield return null;
            spawner.BTN_SetPageMode(4); yield return null;
            Check(!Get<bool>(controller, "busy"), "changing section still cancels a waiting roll", log);
        }
        finally
        {
            if (stalled != null && !stalled.IsCompleted) { stalled.Request = new AnvilDummyOperation(null); stalled.Pump(); }
            hook.UnpatchSelf();
            Call(controller, "CancelRoll");
            instant.Value = priorInstant; plugin.Config.SaveOnConfigSet = priorSave;
            registry.PageItemLists[ItemSpawnerV2.PageMode.Attachments] = originalPage;
            AccessTools.Field(spawner.GetType(), "WorkingItemIDs").SetValue(spawner, originalWorking);
            foreach (var entry in entries) { ids.Remove(entry.ItemID); Object.Destroy(entry.MainObject); Object.Destroy(entry); }
            foreach (var prefab in fixtures.Values) Object.Destroy(prefab);
            fixtures.Clear();
            Call(bridge, "SelectEntry", IM.GetSpawnerID("SMGUziMini"));
            spawner.BTN_SetPageMode(3);
            log("Restored performance fixtures and settings");
        }
    }
}
