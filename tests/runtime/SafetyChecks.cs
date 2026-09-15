using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FistVR;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class ExplodingAwakeFixture : MonoBehaviour
{
    public static int Awakes;
    public static int Updates;
    public static bool RootWasCorrect;
    private void Awake()
    {
        ++Awakes;
        RootWasCorrect = transform.parent == null;
        throw new InvalidOperationException("Intentional Gundomizer runtime-test Awake failure");
    }
    private void Update() { ++Updates; }
}

public static class SafetyChecks
{
    private static void Check(bool condition, string message, Action<string> log)
    { if (!condition) throw new Exception(message); log("PASS " + message); }

    public static IEnumerator Run(ItemSpawnerV2 spawner, MonoBehaviour controller, Action<string> log)
    {
        var method = AccessTools.Method(controller.GetType().Assembly.GetType("Gundomizer.SpawnTransaction"), "TrySpawn");
        var entry = IM.GetSpawnerID(AM.STypeDic[FireArmRoundType.a9_19_Parabellum].Values.First(v => v.ObjectID != null).ObjectID.SpawnedFromId);
        var request = entry.MainObject.GetGameObjectAsync();
        while (request.keepWaiting) yield return null;
        var parent = new GameObject("Inactive safety fixture"); parent.SetActive(false);
        GameObject source = null;
        GameObject surviving = null;
        var created = new List<GameObject>();
        var loading = AccessTools.Field(typeof(AnvilAsset), "m_loadingState");
        var originalLoad = loading.GetValue(entry.MainObject);
        try
        {
            source = Object.Instantiate(request.Result, parent.transform, false);
            ExplodingAwakeFixture.Awakes = 0; ExplodingAwakeFixture.Updates = 0;
            source.AddComponent<ExplodingAwakeFixture>();
            Check(ExplodingAwakeFixture.Awakes == 0, "inactive ancestry prevents fixture initialization during cloning", log);
            object[] args = { source, spawner.transform.position + Vector3.up, Quaternion.identity, entry, null, null };
            bool succeeded = (bool)method.Invoke(null, args);
            Check(!succeeded && args[4] == null && ((string)args[5]).Contains("Intentional Gundomizer"),
                "generic spawn transaction detects Unity-logged Awake failures", log);
            Check(ExplodingAwakeFixture.Awakes == 1 && ExplodingAwakeFixture.RootWasCorrect,
                "activation sees the ordinary parentless spawn hierarchy", log);
            yield return null;
            Check(ExplodingAwakeFixture.Updates == 0 && Object.FindObjectsOfType<ExplodingAwakeFixture>().Length == 0,
                "failed instance is removed before it can Update or remain interactable", log);
            args[0] = request.Result;
            Check((bool)method.Invoke(null, args), "a valid prefab still initializes after a failed transaction", log);
            surviving = (GameObject)args[4];
            Check(surviving != null && surviving.activeInHierarchy && surviving.transform.parent == null
                && surviving.GetComponent<FVRPhysicalObject>().IDSpawnedFrom == entry,
                "successful spawn retains native hierarchy, activity and source metadata", log);
            var bridge = AccessTools.Field(controller.GetType(), "bridge").GetValue(controller);
            var select = AccessTools.Method(bridge.GetType(), "SelectEntry");
            var badRequest = new AnvilCallback<GameObject>(new AnvilDummyOperation(source), null);
            badRequest.Pump(); loading.SetValue(entry.MainObject, badRequest);
            select.Invoke(bridge, new object[] { entry });
            int pad = (int)AccessTools.Field(spawner.GetType(), "m_curSmallPos").GetValue(spawner);
            spawner.BTN_Details_Spawn();
            yield return null;
            Check(ExplodingAwakeFixture.Awakes == 2 && Object.FindObjectsOfType<ExplodingAwakeFixture>().Length == 0
                && (int)AccessTools.Field(spawner.GetType(), "m_curSmallPos").GetValue(spawner) == pad,
                "native Spawn accepting a Gundomizer selection also removes failures without advancing its pad", log);
            loading.SetValue(entry.MainObject, originalLoad);
            var bundled = ManagerSingleton<IM>.Instance.PageItemLists.Values.SelectMany(ids => ids)
                .Distinct().Select(IM.GetSpawnerID).First(e => e.MainObject != null && e.SecondObject != null
                    && e.MainObject.Category == FVRObject.ObjectCategory.Firearm && GM.Rewards.RewardUnlocks.IsRewardUnlocked(e));
            log("NATIVE BUNDLED SELECTION " + bundled.ItemID + " + " + bundled.SecondObject.ItemID);
            select.Invoke(bridge, new object[] { bundled });
            var before = new HashSet<int>(Object.FindObjectsOfType<FVRPhysicalObject>().Select(o => o.GetInstanceID()));
            spawner.BTN_Details_Spawn();
            float deadline = Time.realtimeSinceStartup + 60;
            while ((bool)AccessTools.Field(controller.GetType(), "busy").GetValue(controller) && Time.realtimeSinceStartup < deadline) yield return null;
            var added = Object.FindObjectsOfType<FVRPhysicalObject>().Where(o => !before.Contains(o.GetInstanceID())).ToArray();
            created.AddRange(added.Select(o => o.gameObject));
            Check(added.Length == 2 && added.All(o => o.IDSpawnedFrom == bundled),
                "native acceptance preserves the weapon's main and bundled secondary objects", log);
            // Revisit the originally reported item through the generic path, with the current profile.
            var ham = IM.GetSpawnerID("AttScopeHAMComboscope");
            var hamRequest = ham.MainObject.GetGameObjectAsync();
            while (hamRequest.keepWaiting) yield return null;
            args[0] = hamRequest.Result; args[3] = ham;
            bool hamSucceeded = (bool)method.Invoke(null, args);
            log("HAM generic transaction outcome=" + (hamSucceeded ? "initialized" : "removed failed instance") + " detail=" + args[5]);
            if (hamSucceeded) Object.Destroy((GameObject)args[4]);
            yield return null;
            Check(!Object.FindObjectsOfType<FVRPhysicalObject>().Any(o => o.IDSpawnedFrom == ham),
                "HAM investigation leaves no failed live object behind", log);
        }
        finally
        {
            loading.SetValue(entry.MainObject, originalLoad);
            foreach (var obj in created) if (obj != null) Object.Destroy(obj);
            if (surviving != null) Object.Destroy(surviving);
            if (source != null) Object.Destroy(source);
            Object.Destroy(parent);
        }
    }
}
