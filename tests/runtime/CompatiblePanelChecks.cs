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

public static class CompatiblePanelChecks
{
    private static T Get<T>(object obj, string name) => (T)AccessTools.Field(obj.GetType(), name).GetValue(obj);
    private static object Call(object obj, string name, params object[] args) => AccessTools.Method(obj.GetType(), name).Invoke(obj, args);
    private static object Prop(object obj, string name) => AccessTools.Property(obj.GetType(), name).GetValue(obj, null);
    private static void Check(bool value, string text, Action<string> log) { if (!value) throw new Exception(text); log("PASS " + text); }

    public static IEnumerator Run(ItemSpawnerV2 spawner, MonoBehaviour controller, string directory, Action<string> log)
    {
        var assembly = controller.GetType().Assembly;
        var modelType = assembly.GetType("Gundomizer.CompatibleSelection");
        var choices = AccessTools.Field(modelType, "Shared").GetValue(null);
        var compatibility = assembly.GetType("Gundomizer.Compatibility");
        var kindType = assembly.GetType("Gundomizer.CompatibilityKind");
        var magazineKind = Enum.Parse(kindType, "Magazine");
        var attachmentKind = Enum.Parse(kindType, "Attachment");
        var beforeExcluded = new HashSet<string>(Get<HashSet<string>>(choices, "excluded"));
        bool beforeScope = (bool)Prop(choices, "AllItems");
        var plugin = BepInEx.Bootstrap.Chainloader.PluginInfos["quaternion.gundomizer"].Instance;
        var instant = (ConfigEntry<bool>)AccessTools.Field(plugin.GetType(), "SpawnItemInstantly").GetValue(null);
        bool beforeInstant = instant.Value, beforeSave = plugin.Config.SaveOnConfigSet;
        var hands = GM.CurrentMovementManager.Hands;
        var heldField = AccessTools.Field(typeof(FVRViveHand), "m_currentInteractable");
        var priorHeld = hands.Select(h => h.CurrentInteractable).ToArray(); var priorEnabled = hands.Select(h => h.enabled).ToArray();
        var priorHead = GM.CurrentPlayerBody.Head.position;
        var bridge = Get<object>(controller, "bridge"); var panel = Get<object>(controller, "compatiblePanel");
        var root = Get<RectTransform>(controller, "uiRoot");
        GameObject held = null;
        GameObject tablet = null;
        plugin.Config.SaveOnConfigSet = false;
        try
        {
            var source = IM.OD["BP15"]; var load = source.GetGameObjectAsync();
            float deadline = Time.realtimeSinceStartup + 120;
            while (!load.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Check(load.IsCompleted && load.Result != null, "test STANAG firearm loaded", log);
            held = Object.Instantiate(load.Result, spawner.transform.position + Vector3.up, Quaternion.identity);
            foreach (var hand in hands) { hand.enabled = false; heldField.SetValue(hand, null); }
            var physical = held.GetComponent<FVRPhysicalObject>(); heldField.SetValue(hands[1], physical);
            GM.CurrentPlayerBody.Head.position = spawner.transform.position + Vector3.back;
            spawner.BTN_SetPageMode(2); spawner.BTN_SimpleMode_SwitchToTagSearch(); spawner.BTN_Tag_ClearSelectedTags();
            yield return new WaitForSecondsRealtime(.4f); Call(controller, "RefreshHeldItem", hands[0]);
            Check((bool)Prop(choices, "AllItems"), "configurable compatible choices default to all items", log);
            var sectionButton = Get<MonoBehaviour>(controller, "compatibleButton");
            var allButton = Get<MonoBehaviour>(controller, "allCompatibleButton");
            Check(sectionButton != allButton && !Get<bool>(Get<object>(sectionButton, "Icon"), "AllItems") && Get<bool>(Get<object>(allButton, "Icon"), "AllItems"),
                "section and all-items rolls have distinct grid and globe icons", log);
            yield return new WaitForEndOfFrame(); CaptureFooter(root, Path.Combine(directory, "spawner-footer.png"));
            var rowButtons = root.GetComponentsInChildren<MonoBehaviour>(true).Where(b => b.GetType().FullName == "Gundomizer.RandomizerButton" && b.transform.parent == root).ToArray();
            for (int i = 0; i < rowButtons.Length; ++i) for (int j = i + 1; j < rowButtons.Length; ++j)
            {
                var a = RectTransformUtility.CalculateRelativeRectTransformBounds(root, rowButtons[i].transform);
                var b = RectTransformUtility.CalculateRelativeRectTransformBounds(root, rowButtons[j].transform);
                float overlapX = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x);
                float overlapY = Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y);
                Check(overlapX < .25f || overlapY < .25f, "spawner button areas remain separate: " + rowButtons[i].name + " / " + rowButtons[j].name, log);
            }
            Call(Get<object>(panel, "toggle"), "Activate", hands[0]);
            Check((bool)Prop(panel, "IsOpen"), "actual dropdown opens the compatible popup", log);
            var popupButtons = Get<RectTransform>(panel, "popup").GetComponentsInChildren<MonoBehaviour>(true)
                .Where(b => b.GetType().FullName == "Gundomizer.RandomizerButton").Select(b => Get<Text>(b, "Caption")).Where(t => t != null);
            Check(!popupButtons.Any(t => t.text.Contains("Current section") || t.text.Contains("Current tags") || t.text.Contains("All items")), "filter popup has no scope-switch controls", log);
            foreach (object kind in Enum.GetValues(kindType)) Call(choices, "Set", kind, null, kind.Equals(magazineKind));
            Call(panel, "Redraw"); yield return null;
            var loading = AccessTools.Field(typeof(AnvilAsset), "m_loadingState");
            var callbacks = IM.OD.Values.Distinct().ToDictionary(o => o, o => loading.GetValue(o));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var catalog = new List<ItemSpawnerID>(); var collect = (IEnumerator)Call(bridge, "CollectAll", catalog);
            try { while (collect.MoveNext()) yield return collect.Current; } finally { (collect as IDisposable).Dispose(); }
            Check(catalog.Count > 100 && catalog.Select(e => e.MainObject.ItemID).Distinct().Count() == catalog.Count,
                "global pool is deduplicated and spans the catalog", log);
            Check(catalog.All(e => (bool)AccessTools.Method(assembly.GetType("Gundomizer.SpawnerBridge"), "IsAvailable").Invoke(null, new object[] { e })),
                "global pool preserves unlock checks", log);
            var query = AccessTools.Method(compatibility, "CaptureFiltered").Invoke(null, new object[] { physical, Call(choices, "Snapshot") });
            var pool = new List<ItemSpawnerID>(catalog); Call(query, "Prefilter", pool);
            clock.Stop();
            Check(callbacks.All(p => loading.GetValue(p.Key) == p.Value), "global catalog and metadata filtering load zero prefabs", log);
            Check(pool.Any(e => e.MainObject.ItemID == "Magazine_DanielDefense_32") && pool.Any(e => !e.MainObject.IsModContent),
                "global STANAG pool combines ModulAR2 and vanilla magazines while browsing Ammo", log);
            Check(pool.All(e => e.MainObject.Category == FVRObject.ObjectCategory.Magazine), "magazine-only filter excludes attachments and guns", log);
            log("COMPATIBLE POOL global=" + catalog.Count + " magazines=" + pool.Count + " elapsedMs=" + clock.Elapsed.TotalMilliseconds.ToString("F2"));
            yield return new WaitForEndOfFrame();
            AccessTools.Method(typeof(ReadmePreview), "Capture").Invoke(null, new object[] { root.parent as RectTransform, Path.Combine(directory, "compatible-panel.png") });
            AccessTools.Method(typeof(ReadmePreview), "Capture").Invoke(null, new object[] { Get<RectTransform>(panel, "popup"), Path.Combine(directory, "compatible-panel-closeup.png") });

            // Execute real rolls in both modes through the actual button/owner path.
            foreach (bool spawn in new[] { false, true })
            {
                instant.Value = spawn; yield return new WaitForSecondsRealtime(.4f);
                var before = new HashSet<int>(Object.FindObjectsOfType<FVRFireArmMagazine>().Select(m => m.GetInstanceID()));
                Call(allButton, "Activate", hands[0]);
                deadline = Time.realtimeSinceStartup + 180;
                while (Get<bool>(controller, "busy") && Time.realtimeSinceStartup < deadline) yield return null;
                Check(!Get<bool>(controller, "busy"), "compatible roll completed", log);
                string status = Get<string>(controller, "status"); log("COMPATIBLE ROLL " + status);
                Check(status.StartsWith(spawn ? "Spawned " : "Selected "), "global roll honors " + (spawn ? "instant spawning" : "preview selection"), log);
                var added = Object.FindObjectsOfType<FVRFireArmMagazine>().Where(m => !before.Contains(m.GetInstanceID())).ToArray();
                Check(added.Length == (spawn ? 1 : 0), "roll creates exactly the expected number of magazines", log);
                foreach (var mag in added) Object.Destroy(mag.gameObject);
            }
            var wrist = Resources.FindObjectsOfTypeAll<FVRWristMenuSection_Spawn>().First(w => w.ToolBoxPrefab != null);
            var box = wrist.ToolBoxPrefab.GetComponent<FistVR.ToolGuns.ToolBox>();
            var tabletPrefab = box.Drawers.SelectMany(d => d.Bays).Select(b => b.ActualPrefab)
                .First(p => p != null && p.GetComponent<FistVR.ToolGuns.TP_Palette>() != null);
            tablet = Object.Instantiate(tabletPrefab, spawner.transform.position + Vector3.up, Quaternion.identity);
            foreach (var rb in tablet.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
            yield return null;
            var portable = tablet.GetComponent<FistVR.ToolGuns.TP_Palette>().ItemSpawner;
            var portableController = portable.GetComponent(controller.GetType()) as MonoBehaviour;
            Check(portableController != null && portableController.enabled, "compatible choices initialize on the toolbox tablet", log);
            portable.BTN_SetPageMode(2); portable.BTN_SimpleMode_SwitchToTagSearch();
            instant.Value = false; yield return new WaitForSecondsRealtime(.4f); Call(portableController, "RefreshHeldItem", hands[0]);
            Check((bool)Prop(Get<object>(portableController, "compatiblePanel"), "HasChoices"), "tablet shares current compatible choices", log);
            var beforeTablet = new HashSet<int>(Object.FindObjectsOfType<FVRFireArmMagazine>().Select(m => m.GetInstanceID()));
            Call(Get<object>(portableController, "allCompatibleButton"), "Activate", hands[0]);
            deadline = Time.realtimeSinceStartup + 180;
            while (Get<bool>(portableController, "busy") && Time.realtimeSinceStartup < deadline) yield return null;
            Check(Get<string>(portableController, "status").StartsWith("Selected "), "tablet previews a compatible magazine from all categories", log);
            var placement = portable.transform.position + Vector3.up * .5f;
            yield return new WaitForSecondsRealtime(.4f); portable.ExternalSpawnFromLaserToPoint(placement);
            deadline = Time.realtimeSinceStartup + 120;
            while (Get<bool>(portableController, "busy") && Time.realtimeSinceStartup < deadline) yield return null;
            var tabletMags = Object.FindObjectsOfType<FVRFireArmMagazine>().Where(m => !beforeTablet.Contains(m.GetInstanceID())).ToArray();
            Check(tabletMags.Length == 1 && (tabletMags[0].transform.position - placement).sqrMagnitude < .01f,
                "tablet accepts the preview and spawns exactly one magazine at the stylus point", log);
            foreach (var mag in tabletMags) Object.Destroy(mag.gameObject);
            // Narrow by connector: the only STANAG family is removed, with no fallback.
            int connector = (int)source.MagazineType; Call(choices, "Set", magazineKind, (int?)connector, false); Call(panel, "Redraw");
            Check(!(bool)Prop(panel, "HasChoices"), "excluding the only magazine family disables rolling", log);
            var excludedQuery = AccessTools.Method(compatibility, "CaptureFiltered").Invoke(null, new object[] { physical, Call(choices, "Snapshot") });
            var candidate = IM.OD["Magazine_DanielDefense_32"].GetGameObjectAsync();
            while (!candidate.IsCompleted) yield return null;
            Check(!(bool)Call(excludedQuery, "Matches", candidate.Result), "live compatibility honors a connector exclusion", log);
            instant.Value = false;
            yield return new WaitForSecondsRealtime(.4f); yield return new WaitForEndOfFrame();
            Check(sectionButton.GetComponent<Button>().interactable && !allButton.GetComponent<Button>().interactable,
                "global exclusions disable only the all-items roll", log);
            Call(sectionButton, "Activate", hands[0]);
            deadline = Time.realtimeSinceStartup + 120;
            while (Get<bool>(controller, "busy") && Time.realtimeSinceStartup < deadline) yield return null;
            Check(Get<string>(controller, "status").StartsWith("Selected "), "section roll still works when global choices exclude its magazines", log);
            var selected = (ItemSpawnerID)AccessTools.Method(assembly.GetType("Gundomizer.OtherLoaderBridge"), "Resolve").Invoke(null, new object[] { Get<string>(spawner, "m_selectedID") });
            var sectionPool = (List<ItemSpawnerID>)Call(bridge, "CaptureSection");
            Check(selected != null && sectionPool.Any(e => e.MainObject.ItemID == selected.MainObject.ItemID), "section roll stays inside the native category/tag pool", log);
            Call(choices, "Set", magazineKind, (int?)connector, true);
            Call(choices, "Set", magazineKind, null, false); Call(choices, "Set", attachmentKind, null, true); Call(panel, "Redraw");
            var unfiltered = AccessTools.Method(compatibility, "Capture").Invoke(null, new object[] { physical });
            var mounts = (List<int>)Call(unfiltered, "Connectors", attachmentKind);
            Check(mounts.Count > 0, "installed attachment mount types are available as filter choices", log);
            foreach (int mount in mounts) Call(choices, "Set", attachmentKind, (int?)mount, false);
            Call(panel, "Redraw"); Check(!(bool)Prop(panel, "HasChoices"), "all attachment connectors excluded leaves an empty selection", log);
            var disabledMountQuery = AccessTools.Method(compatibility, "CaptureFiltered").Invoke(null, new object[] { physical, Call(choices, "Snapshot") });
            var unknownAttachment = catalog.First(e => e.MainObject.Category == FVRObject.ObjectCategory.Attachment);
            Check(!(bool)Call(disabledMountQuery, "CouldMatch", unknownAttachment, false), "disabled mount families reject even unindexed attachments without loading", log);
            Call(choices, "Set", attachmentKind, (int?)mounts[0], true); Call(panel, "Redraw");
            Check((bool)Prop(panel, "HasChoices"), "re-enabling one attachment connector restores rolling", log);
            spawner.BTN_SetPageMode(3); spawner.BTN_SimpleMode_SwitchToSimpleMode(); Call(panel, "Redraw");
            yield return null;
            Check(root.gameObject.activeInHierarchy, "compatible group remains visible in classic mode", log);
            Call(Get<object>(panel, "toggle"), "Activate", hands[0]);
            yield return null; // Let the owner's Update hide the previous roll tooltip.
            yield return new WaitForEndOfFrame();
            AccessTools.Method(typeof(ReadmePreview), "Capture").Invoke(null, new object[] { Get<RectTransform>(panel, "popup"), Path.Combine(directory, "attachment-panel-closeup.png") });
            yield return new WaitForSecondsRealtime(.4f);
            var ammo = Get<object>(controller, "ammoPanel"); Call(Get<object>(ammo, "toggleButton"), "Activate", hands[0]);
            Check((bool)Prop(ammo, "IsOpen") && !(bool)Prop(panel, "IsOpen"), "opening ammo closes the compatible popup", log);
            yield return new WaitForSecondsRealtime(.2f); Call(Get<object>(panel, "toggle"), "Activate", hands[0]);
            Check((bool)Prop(panel, "IsOpen") && !(bool)Prop(ammo, "IsOpen"), "opening compatible choices closes the ammo popup", log);
            int currentRevision = (int)Prop(choices, "Revision");
            AccessTools.Field(controller.GetType(), "compatibleRevision").SetValue(controller, currentRevision);
            Call(choices, "Set", attachmentKind, (int?)mounts[0], false);
            Check(!(bool)Call(controller, "StillValid", null, true, physical, hands[0], false, -1), "changed choices invalidate a pending compatible roll", log);
            AccessTools.Field(controller.GetType(), "compatibleRevision").SetValue(controller, -1);
            Check((bool)Call(controller, "StillValid", Call(bridge, "CaptureContext"), true, physical, hands[0], false, -1), "global choice changes do not invalidate a section roll", log);
            heldField.SetValue(hands[1], null); Call(controller, "RefreshHeldItem", hands[0]);
            Check(!(bool)Prop(panel, "IsOpen") && !(bool)Prop(panel, "HasChoices"), "empty hands close the panel and disable compatible rolling", log);
            log("COMPATIBLE PANEL checks finished");
        }
        finally
        {
            Call(controller, "CancelRoll"); Call(panel, "Hide");
            var excluded = Get<HashSet<string>>(choices, "excluded"); excluded.Clear(); foreach (string key in beforeExcluded) excluded.Add(key);
            Call(choices, "SetScope", beforeScope); Call(panel, "Redraw");
            instant.Value = beforeInstant; plugin.Config.SaveOnConfigSet = beforeSave;
            for (int i = 0; i < hands.Length; ++i) { heldField.SetValue(hands[i], priorHeld[i]); hands[i].enabled = priorEnabled[i]; }
            GM.CurrentPlayerBody.Head.position = priorHead;
            if (held != null) Object.Destroy(held);
            if (tablet != null) Object.Destroy(tablet);
        }
    }

    private static void CaptureFooter(RectTransform root, string path)
    {
        var canvas = root.GetComponentInParent<Canvas>(); var panel = (RectTransform)canvas.transform;
        var bounds = new Bounds(panel.InverseTransformPoint(root.position), Vector3.zero); bool first = true;
        foreach (var button in root.GetComponentsInChildren<MonoBehaviour>())
        {
            if (button.GetType().FullName != "Gundomizer.RandomizerButton" || button.transform.parent != root) continue;
            var b = RectTransformUtility.CalculateRelativeRectTransformBounds(panel, button.transform);
            if (first) { bounds = b; first = false; } else bounds.Encapsulate(b);
        }
        var obj = new GameObject("Spawner footer capture"); var camera = obj.AddComponent<Camera>();
        var target = new RenderTexture(1200, 300, 24); var pixels = new Texture2D(1200, 300, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        var layers = canvas.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.gameObject, t => t.gameObject.layer);
        try
        {
            foreach (var go in layers.Keys) go.layer = 31;
            camera.transform.rotation = panel.rotation; camera.transform.position = panel.TransformPoint(bounds.center) - panel.forward * 2;
            camera.orthographic = true; camera.orthographicSize = Mathf.Max(bounds.size.y * Mathf.Abs(panel.lossyScale.y), bounds.size.x * Mathf.Abs(panel.lossyScale.x) / 4) * .6f;
            camera.nearClipPlane = .01f; camera.farClipPlane = 4; camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .04f, .04f);
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1200, 300), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            foreach (var entry in layers) if (entry.Key != null) entry.Key.layer = entry.Value;
            RenderTexture.active = previous; camera.targetTexture = null;
            Object.Destroy(obj); Object.Destroy(target); Object.Destroy(pixels);
        }
    }
}
