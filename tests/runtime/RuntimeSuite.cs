using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using FistVR;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class RuntimeSuite
{
    private static bool NativeLoading { get { return GM.IsAsyncLoading
        || (bool)AccessTools.Property(typeof(GM), "IsLoadingVaultFile").GetValue(null, null)
        || (GM.CurrentSceneSettings != null && GM.CurrentSceneSettings.IsInvoking("LoadDefaultSceneRoutine")); } }
    private static T Get<T>(object target, string name) { return (T)AccessTools.Field(target.GetType(), name).GetValue(target); }
    private static object Call(object target, string name, params object[] args) { return AccessTools.Method(target.GetType(), name).Invoke(target, args); }
    private static void Check(bool condition, string message, Action<string> log)
    {
        if (!condition) throw new Exception(message);
        log("PASS " + message);
    }

    public static IEnumerator Run(string directory, Action<string> log)
    {
        foreach (var pair in AM.STypeDic[FireArmRoundType.a9_19_Parabellum])
        {
            var obj = pair.Value.ObjectID;
            string id = obj == null ? "" : obj.SpawnedFromId;
            log("AMMO CATALOG " + pair.Key + " name=" + pair.Value.Name + " object=" + (obj == null ? "null" : obj.ItemID)
                + " spawner=" + id + " registered=" + (!string.IsNullOrEmpty(id) && IM.HasSpawnedID(id))
                + " exact=" + (!string.IsNullOrEmpty(id) && IM.HasSpawnedID(id) && IM.GetSpawnerID(id).MainObject == obj));
        }
        // Each run starts from the same native scene, including any authored scene defaults.
        {
            log("Loading WarehouseRange_Rebuilt without VR");
            var load = SceneManager.LoadSceneAsync("WarehouseRange_Rebuilt");
            float deadline = Time.realtimeSinceStartup + 120;
            while (!load.isDone && Time.realtimeSinceStartup < deadline) yield return null;
            Check(load.isDone, "range scene loaded", log);
            yield return null;
        }
        // Scene settings schedule default vault restoration after Start. Wait for that
        // work too before counting objects created by a roll.
        yield return new WaitForSecondsRealtime(1f);
        if (NativeLoading) log("Waiting for native scene/vault loading to finish");
        float sceneDeadline = Time.realtimeSinceStartup + 180;
        while (NativeLoading && Time.realtimeSinceStartup < sceneDeadline) yield return null;
        Check(!NativeLoading, "native scene/vault loading finished", log);
        var spawner = Object.FindObjectOfType<ItemSpawnerV2>();
        Check(spawner != null, "live native ItemSpawnerV2 exists", log);
        var controller = spawner.GetComponents<MonoBehaviour>().FirstOrDefault(c => c.GetType().FullName == "Gundomizer.RandomizerController");
        Check(controller != null && controller.enabled, "Gundomizer attached and initialized", log);
        var bridge = Get<object>(controller, "bridge");
        var root = Get<RectTransform>(controller, "uiRoot");
        spawner.BTN_SetPageMode(1);
        spawner.BTN_SimpleMode_SwitchToSimpleMode();
        yield return null;
        yield return new WaitForEndOfFrame();
        Check(root.gameObject.activeInHierarchy, "classic controls visible", log);
        var originalPosition = root.position;
        Screenshot(root.parent as RectTransform, Path.Combine(directory, "classic.png"));
        spawner.BTN_SimpleMode_SwitchToTagSearch();
        yield return null;
        Check(root.gameObject.activeInHierarchy && root.position == originalPosition, "tag controls visible at the same position", log);
        spawner.BTN_Tag_ClearSelectedTags();
        var all = (List<ItemSpawnerID>)Call(bridge, "CaptureSection");
        Check(all.Count > 12 && all.Count == Get<List<string>>(spawner, "WorkingItemIDs").Count,
            "tag pool contains all " + all.Count + " native results, beyond the visible grid page", log);
        spawner.BTN_List_PageNext();
        var next = (List<ItemSpawnerID>)Call(bridge, "CaptureSection");
        Check(new HashSet<string>(all.Select(e => e.ItemID)).SetEquals(next.Select(e => e.ItemID)), "pagination keeps the same random pool", log);
        var meta = ManagerSingleton<IM>.Instance.ItemMetaDic["SMGUziMini"];
        AddTag(spawner, TagType.Caliber, meta[TagType.Caliber][0]);
        AddTag(spawner, TagType.Caliber, "a45_ACP");
        AddTag(spawner, TagType.Era, meta[TagType.Era][0]);
        var filtered = (List<ItemSpawnerID>)Call(bridge, "CaptureSection");
        var registry = ManagerSingleton<IM>.Instance;
        var expected = all.Where(e => registry.ItemMetaDic[e.ItemID].ContainsKey(TagType.Caliber) && registry.ItemMetaDic[e.ItemID].ContainsKey(TagType.Era)
            && registry.ItemMetaDic[e.ItemID][TagType.Caliber].Any(t => t == meta[TagType.Caliber][0] || t == "a45_ACP")
            && registry.ItemMetaDic[e.ItemID][TagType.Era].Contains(meta[TagType.Era][0])).Select(e => e.ItemID);
        Check(filtered.Count > 0 && filtered.Count < all.Count && new HashSet<string>(expected).SetEquals(filtered.Select(e => e.ItemID)),
            "native OR within caliber and AND with era respected (" + filtered.Count + " results)", log);
        var context = Call(bridge, "CaptureContext");
        spawner.BTN_List_SetMode_List();
        Check((bool)Call(bridge, "MatchesContext", context), "grid/list switching preserves a pending roll context", log);
        Screenshot(root.parent as RectTransform, Path.Combine(directory, "tag-list.png"));
        spawner.BTN_Tag_ClearSelectedTags();
        Check(!(bool)Call(bridge, "MatchesContext", context), "changing selected tags invalidates the pending roll context", log);
        spawner.BTN_List_SetMode_Pictures();
        var pages = Get<Dictionary<ItemSpawnerV2.PageMode, List<TagType>>>(spawner, "m_pageModeTagTypes");
        foreach (var page in pages) log("Visible tag categories " + page.Key + "=" + page.Value.Count);
        Screenshot(root.parent as RectTransform, Path.Combine(directory, "tags.png"));
        log("Player body=" + GM.CurrentPlayerBody + " head=" + GM.CurrentPlayerBody.Head.position + " spawner=" + spawner.transform.position);
        foreach (var hand in GM.CurrentMovementManager.Hands) log("Hand=" + hand + " enabled=" + hand.enabled + " held=" + hand.CurrentInteractable);
        var behaviorChecks = Rolls(spawner, controller, bridge, directory, log);
        try { while (behaviorChecks.MoveNext()) yield return behaviorChecks.Current; }
        finally { (behaviorChecks as IDisposable).Dispose(); }
        spawner.BTN_SetPageMode(0);
        yield return null;
        Check(!root.gameObject.activeInHierarchy, "controls hidden on the main menu", log);
        log("ALL RUNTIME CHECKS PASSED");
    }

    private static void AddTag(ItemSpawnerV2 spawner, TagType type, string value)
    {
        var page = Get<ItemSpawnerV2.PageMode>(spawner, "PMode");
        var categories = Get<Dictionary<ItemSpawnerV2.PageMode, List<TagType>>>(spawner, "m_pageModeTagTypes")[page];
        spawner.BTN_Tag_SelectCategory(categories.IndexOf(type));
        var values = ManagerSingleton<IM>.Instance.MetaTagListByPageMode[page][type];
        int index = values.IndexOf(value); // Selecting the category sorts this native list.
        if (index < 0) throw new Exception("Missing test tag " + type + ":" + value);
        for (int p = 0; p < index / 20; ++p) spawner.BTN_Tag_PageNext();
        spawner.BTN_Tag_AddTagEntry(index % 20);
    }

    private static IEnumerator Rolls(ItemSpawnerV2 spawner, MonoBehaviour controller, object bridge, string directory, Action<string> log)
    {
        var plugin = BepInEx.Bootstrap.Chainloader.PluginInfos["quaternion.gundomizer"].Instance;
        var config = (ConfigEntry<bool>)AccessTools.Field(plugin.GetType(), "SpawnItemInstantly").GetValue(null);
        bool originalSetting = config.Value;
        bool originalAutoSave = plugin.Config.SaveOnConfigSet;
        var hands = GM.CurrentMovementManager.Hands;
        var heldField = AccessTools.Field(typeof(FVRViveHand), "m_currentInteractable");
        var originalHeld = hands.Select(h => h.CurrentInteractable).ToArray();
        var originalEnabled = hands.Select(h => h.enabled).ToArray();
        var originalHead = GM.CurrentPlayerBody.Head.position;
        var createdObjects = new List<GameObject>();
        GameObject gun = null;
        plugin.Config.SaveOnConfigSet = false;
        try
        {
            foreach (var hand in hands) { hand.enabled = false; heldField.SetValue(hand, null); }
            Call(controller, "RefreshHeldItem", new object[] { null });
            Check(!(bool)Call(controller, "CanClick", true), "compatible button disabled with empty hands", log);
            var uzi = IM.GetSpawnerID("SMGUziMini");
            var load = uzi.MainObject.GetGameObjectAsync();
            float deadline = Time.realtimeSinceStartup + 60;
            while (load.keepWaiting && Time.realtimeSinceStartup < deadline) yield return null;
            Check(!load.keepWaiting, "held firearm prefab loaded asynchronously", log);
            gun = Object.Instantiate(uzi.MainObject.GetGameObject(), spawner.transform.position + Vector3.up, Quaternion.identity);
            foreach (var rigidbody in gun.GetComponentsInChildren<Rigidbody>()) rigidbody.isKinematic = true;
            var firearm = gun.GetComponent<FVRPhysicalObject>();
            heldField.SetValue(hands[0], firearm);
            Call(controller, "RefreshHeldItem", new object[] { null });
            Check((bool)Call(controller, "CanClick", true) && Get<FVRPhysicalObject>(controller, "cachedHeldItem") == firearm,
                "actual hand detector resolves the simulated held firearm", log);
            GM.CurrentPlayerBody.Head.position = spawner.transform.position + Vector3.right * 9;
            Call(controller, "RefreshHeldItem", new object[] { null });
            Check(!(bool)Call(controller, "CanClick", true), "compatible button disabled beyond eight metres", log);
            GM.CurrentPlayerBody.Head.position = originalHead;
            spawner.BTN_SetPageMode(2);
            spawner.BTN_Tag_ClearSelectedTags();
            AddTag(spawner, TagType.Category, "Magazine");
            var scope = new HashSet<string>(((List<ItemSpawnerID>)Call(bridge, "CaptureSection")).Select(e => e.ItemID));
            Check(scope.Count > 12, "magazine tag yields an all-pages pool of " + scope.Count, log);
            var compatibility = controller.GetType().Assembly.GetType("Gundomizer.Compatibility", true);
            for (int i = 0; i < 6; ++i)
            {
                bool compatible = i != 0;
                config.Value = i >= 3;
                var before = new HashSet<int>(Object.FindObjectsOfType<FVRPhysicalObject>().Select(o => o.GetInstanceID()));
                int padBefore = Get<int>(spawner, "m_curSmallPos");
                yield return new WaitForSecondsRealtime(0.35f);
                log("ROLL " + i + " compatible=" + compatible + " instant=" + config.Value);
                var button = Get<MonoBehaviour>(controller, compatible ? "compatibleButton" : "randomButton").GetComponent<Button>();
                button.onClick.Invoke();
                deadline = Time.realtimeSinceStartup + 90;
                while (Get<bool>(controller, "busy") && Time.realtimeSinceStartup < deadline) yield return null;
                Check(!Get<bool>(controller, "busy"), "roll " + i + " completed", log);
                string selected = Get<string>(spawner, "m_selectedID");
                Check(scope.Contains(selected), "roll " + i + " selected an item in the native tag scope: " + selected, log);
                var added = Object.FindObjectsOfType<FVRPhysicalObject>().Where(o => !before.Contains(o.GetInstanceID())).ToArray();
                foreach (var addedObject in added)
                    if (addedObject.IDSpawnedFrom != null && addedObject.IDSpawnedFrom.ItemID == selected) createdObjects.Add(addedObject.gameObject);
                Check(config.Value ? added.Length == 1 && added[0].IDSpawnedFrom.ItemID == selected : added.Length == 0 && Get<int>(spawner, "m_curSmallPos") == padBefore,
                    "roll " + i + (config.Value ? " spawned exactly one selected object" : " selected without spawning or advancing pads"), log);
                if (compatible)
                    Check((bool)AccessTools.Method(compatibility, "Matches").Invoke(null, new object[] { firearm, IM.GetSpawnerID(selected).MainObject.GetGameObject() }),
                        "roll " + i + " passes live native magazine compatibility", log);
            }
            // Both tags are valid, but a magazine cannot be both Uzi and .45 ACP in this catalog.
            AddTag(spawner, TagType.MagazineType, "mUzi_9mm");
            AddTag(spawner, TagType.Caliber, "a45_ACP");
            Check(((List<ItemSpawnerID>)Call(bridge, "CaptureSection")).Count == 0, "empty native tag intersection stays empty", log);
            yield return new WaitForSecondsRealtime(0.35f);
            string prior = Get<string>(spawner, "m_selectedID");
            Get<MonoBehaviour>(controller, "randomButton").GetComponent<Button>().onClick.Invoke();
            Check(!Get<bool>(controller, "busy") && Get<string>(spawner, "m_selectedID") == prior,
                "empty-pool click keeps selection unchanged", log);
            Screenshot(Get<RectTransform>(controller, "uiRoot").parent as RectTransform, Path.Combine(directory, "empty-tags.png"));
            var ammoChecks = AmmoChecks(spawner, controller, firearm, config, createdObjects, directory, log);
            try { while (ammoChecks.MoveNext()) yield return ammoChecks.Current; }
            finally { (ammoChecks as IDisposable).Dispose(); }
            spawner.BTN_Tag_ClearSelectedTags();
            spawner.BTN_SetPageMode(3);
            for (int i = 0; i < 3; ++i)
            {
                config.Value = i == 2;
                yield return new WaitForSecondsRealtime(0.35f);
                log("ATTACHMENT ROLL " + i + " instant=" + config.Value);
                Get<MonoBehaviour>(controller, "compatibleButton").GetComponent<Button>().onClick.Invoke();
                deadline = Time.realtimeSinceStartup + 90;
                while (Get<bool>(controller, "busy") && Time.realtimeSinceStartup < deadline) yield return null;
                Check(!Get<bool>(controller, "busy"), "attachment roll " + i + " completed", log);
                string selected = Get<string>(spawner, "m_selectedID");
                var attachment = IM.GetSpawnerID(selected).MainObject.GetGameObject().GetComponent<FVRFireArmAttachment>();
                Check(attachment != null && attachment.CanAttach(), "selected attachment permits attachment: " + selected, log);
                var mount = firearm.GetComponentsInChildren<FVRFireArmAttachmentMount>().FirstOrDefault(m => m.enabled
                    && m.GetComponent<Collider>() != null && m.GetComponent<Collider>().enabled && m.Type == attachment.Type && m.isMountableOn(attachment));
                Check(mount != null, "native mount accepts the selected attachment connector", log);
                if (config.Value)
                {
                    var spawned = Object.FindObjectsOfType<FVRFireArmAttachment>().Single(a => a.IDSpawnedFrom != null && a.IDSpawnedFrom.ItemID == selected);
                    createdObjects.Add(spawned.gameObject);
                    spawned.AttachToMount(mount, false);
                    Check(spawned.curMount == mount && mount.AttachmentsList.Contains(spawned), "spawned attachment actually connects through native AttachToMount", log);
                }
            }
        }
        finally
        {
            config.Value = originalSetting;
            plugin.Config.SaveOnConfigSet = originalAutoSave;
            GM.CurrentPlayerBody.Head.position = originalHead;
            for (int i = 0; i < hands.Length; ++i) { heldField.SetValue(hands[i], originalHeld[i]); hands[i].enabled = originalEnabled[i]; }
            foreach (var obj in createdObjects) if (obj != null) Object.Destroy(obj);
            if (gun != null) Object.Destroy(gun);
            log("Restored settings and hand state; removed test objects");
        }
    }

    private static IEnumerator AmmoChecks(ItemSpawnerV2 spawner, MonoBehaviour controller, FVRPhysicalObject firearm,
        ConfigEntry<bool> config, List<GameObject> createdObjects, string directory, Action<string> log)
    {
        var panel = Get<object>(controller, "ammoPanel");
        var selectionType = controller.GetType().Assembly.GetType("Gundomizer.AmmoSelection", true);
        var selection = AccessTools.Field(selectionType, "Shared").GetValue(null);
        var loading = AccessTools.Field(typeof(AnvilAsset), "m_loadingState");
        var ammoObjects = AM.STypeDic[FireArmRoundType.a9_19_Parabellum].Values.Select(v => v.ObjectID).ToArray();
        var callbacks = ammoObjects.Select(o => loading.GetValue(o)).ToArray();
        Call(panel, "Refresh", firearm, true);
        Check(ammoObjects.Select((o, i) => loading.GetValue(o) == callbacks[i]).All(v => v), "ammo catalog reads do not start prefab loads", log);
        var variants = Get<IList>(panel, "variants");
        Check(variants.Count == ammoObjects.Length, "all native 9 mm variants appear with exact spawner entries", log);
        var originalChoices = new Dictionary<string, bool>();
        foreach (var item in variants)
        {
            string itemKey = Get<string>(item, "Key");
            originalChoices.Add(itemKey, (bool)Call(selection, "Includes", itemKey));
        }
        var root = Get<RectTransform>(controller, "uiRoot");
        var popup = Get<RectTransform>(panel, "popup");
        var roll = Get<MonoBehaviour>(panel, "rollButton");
        var toggle = Get<MonoBehaviour>(panel, "toggleButton");
        try
        {
            spawner.BTN_SetPageMode(1);
            spawner.BTN_SimpleMode_SwitchToSimpleMode();
            yield return new WaitForSecondsRealtime(0.35f);
            toggle.GetComponent<Button>().onClick.Invoke();
            yield return null;
            Check(popup.gameObject.activeInHierarchy, "ammo dropdown opens an anchored popup in classic mode", log);
            Screenshot(root.parent as RectTransform, Path.Combine(directory, "ammo-classic.png"));
            Call(panel, "SetAll", false);
            yield return new WaitForEndOfFrame();
            Check(!roll.GetComponent<Button>().interactable && toggle.GetComponent<Button>().interactable,
                "all-off choices disable rolling while keeping choices accessible", log);
            var variant = variants[0];
            string key = Get<string>(variant, "Key");
            ((MonoBehaviour)Get<IList>(panel, "rows")[0]).GetComponent<Button>().onClick.Invoke();
            Check((bool)Call(selection, "Includes", key), "clicking a variant row enables that variant", log);
            Call(panel, "Update");
            var entry = Get<ItemSpawnerID>(variant, "Entry");
            spawner.BTN_SetPageMode(4);
            for (int i = 0; i < 2; ++i)
            {
                config.Value = i == 1;
                var before = new HashSet<int>(Object.FindObjectsOfType<FVRPhysicalObject>().Select(o => o.GetInstanceID()));
                int pad = Get<int>(spawner, "m_curSmallPos");
                yield return new WaitForSecondsRealtime(0.35f);
                roll.GetComponent<Button>().onClick.Invoke();
                float deadline = Time.realtimeSinceStartup + 60;
                while (Get<bool>(controller, "busy") && Time.realtimeSinceStartup < deadline) yield return null;
                Check(!Get<bool>(controller, "busy") && Get<string>(spawner, "m_selectedID") == entry.ItemID,
                    "ammo roll respects the single enabled variant even in the Melee section", log);
                var added = Object.FindObjectsOfType<FVRPhysicalObject>().Where(o => !before.Contains(o.GetInstanceID())).ToArray();
                foreach (var obj in added) createdObjects.Add(obj.gameObject);
                if (i == 0) Check(added.Length == 0 && Get<int>(spawner, "m_curSmallPos") == pad, "ammo selection creates no cartridge and leaves spawn pads unchanged", log);
                else
                {
                    var round = added.Length == 1 ? added[0] as FVRFireArmRound : null;
                    Check(round != null && round.RoundType == Get<FireArmRoundType>(variant, "Type")
                        && round.RoundClass == Get<FireArmRoundClass>(variant, "Class"), "ammo instant mode spawns exactly one cartridge with the selected native caliber and class", log);
                }
            }
            spawner.BTN_SetPageMode(1);
            spawner.BTN_SimpleMode_SwitchToTagSearch();
            yield return new WaitForSecondsRealtime(0.35f);
            Screenshot(root.parent as RectTransform, Path.Combine(directory, "ammo-tags.png"));
            Check(popup.gameObject.activeInHierarchy, "ammo popup also works in tag mode", log);
            var load = AM.STypeDic[FireArmRoundType.a12g_Shotgun].Values.First(v => v.ObjectID != null).ObjectID.GetGameObjectAsync();
            float loadDeadline = Time.realtimeSinceStartup + 60;
            while (load.keepWaiting && Time.realtimeSinceStartup < loadDeadline) yield return null;
            Check(!load.keepWaiting, "shotgun test cartridge loaded", log);
            var shell = Object.Instantiate(load.Result, firearm.transform.position, Quaternion.identity);
            createdObjects.Add(shell);
            var heldField = AccessTools.Field(typeof(FVRViveHand), "m_currentInteractable");
            heldField.SetValue(GM.CurrentMovementManager.Hands[0], shell.GetComponent<FVRPhysicalObject>());
            Call(controller, "RefreshHeldItem", new object[] { null });
            Check(!popup.gameObject.activeSelf, "changing held objects closes the stale ammo popup", log);
            toggle.GetComponent<Button>().onClick.Invoke();
            yield return null;
            var shotgunVariants = Get<IList>(panel, "variants");
            Check(shotgunVariants.Count > 7, "shotgun ammo provides a multi-page variant list", log);
            Get<MonoBehaviour>(panel, "next").GetComponent<Button>().onClick.Invoke();
            Check(Get<int>(panel, "page") == 1, "ammo popup pagination reaches additional variants", log);
            Call(panel, "EnabledVariants");
            Check(Get<int>(panel, "page") == 1, "refreshing the ammo roll pool preserves the popup page", log);
            Screenshot(root.parent as RectTransform, Path.Combine(directory, "ammo-shotgun-page2.png"));
            heldField.SetValue(GM.CurrentMovementManager.Hands[0], firearm);
            Call(controller, "RefreshHeldItem", new object[] { null });
            Check(Get<int>(panel, "enabledCount") == 1, "9 mm choices survive switching to another caliber and back", log);
        }
        finally
        {
            foreach (var pair in originalChoices) Call(selection, "Set", pair.Key, pair.Value);
            Call(panel, "Hide");
        }
    }

    private static void Screenshot(RectTransform canvas, string path)
    {
        Canvas.ForceUpdateCanvases();
        var cameraObject = new GameObject("Gundomizer test camera");
        var camera = cameraObject.AddComponent<Camera>();
        var target = new RenderTexture(1600, 900, 24);
        var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            var center = canvas.TransformPoint(canvas.rect.center);
            camera.transform.position = center - canvas.forward * 2;
            camera.transform.rotation = canvas.rotation;
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(canvas.rect.height * canvas.lossyScale.y,
                canvas.rect.width * canvas.lossyScale.x * 900 / 1600) * 0.54f;
            camera.nearClipPlane = 0.01f; camera.farClipPlane = 4f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.04f, 0.04f, 0.04f);
            camera.cullingMask = 1 << canvas.gameObject.layer;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            Object.Destroy(cameraObject); Object.Destroy(target); Object.Destroy(pixels);
        }
    }
}
