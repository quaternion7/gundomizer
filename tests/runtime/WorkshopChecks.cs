// Real Modular Workshop fixtures, optional and never included in the mod package.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FistVR;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class WorkshopChecks
{
    private static object Read(object obj, string name) => AccessTools.Field(obj.GetType(), name).GetValue(obj);
    private static object Property(object obj, string name) => AccessTools.Property(obj.GetType(), name).GetValue(obj, null);
    private static object Call(object obj, string name, params object[] args) => AccessTools.Method(obj.GetType(), name).Invoke(obj, args);
    private static void Check(bool condition, string message, Action<string> log)
    { if (!condition) throw new Exception(message); log("PASS " + message); }
    private static bool IsMenu(MonoBehaviour component) => component != null && component.GetType().FullName == "ModularWorkshop.ModularWorkshopUI";
    private static Dictionary<string, string> Selected(object target)
    {
        var result = new Dictionary<string, string>();
        foreach (DictionaryEntry entry in (IDictionary)Property(target, "AllAttachmentPoints"))
            result.Add((string)entry.Key, (string)Read(entry.Value, "SelectedModularWeaponPart"));
        return result;
    }

    public static IEnumerator Run(ItemSpawnerV2 spawner, MonoBehaviour controller, string directory, Action<string> log)
    {
        BepInEx.PluginInfo info;
        Check(BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("h3vr.cityrobo.ModularWorkshopManager", out info), "optional Modular Workshop loaded", log);
        var mw = info.Instance.GetType().Assembly;
        var manager = mw.GetType("ModularWorkshop.ModularWorkshopManager");
        var groups = (IDictionary)AccessTools.Field(manager, "ModularWorkshopPartsGroupsDictionary").GetValue(null);
        float deadline = Time.realtimeSinceStartup + 60;
        while (groups.Count == 0 && Time.realtimeSinceStartup < deadline) yield return null;
        Check(groups.Count > 0, "native modular part definitions registered", log);
        log("WORKSHOP groups=" + groups.Count);
        foreach (var source in IM.OD.Values.Where(o => o.ItemID.ToLowerInvariant().Contains("platform")
            || (o.Category == FVRObject.ObjectCategory.Firearm && (o.ItemID.ToLowerInvariant().Contains("xm8") || o.ItemID.ToLowerInvariant().Contains("pmm")))))
            log("WORKSHOP CANDIDATE " + source.ItemID + " name=" + source.DisplayName);
        var created = new List<GameObject>();
        try
        {
            MonoBehaviour platform = null;
            foreach (var source in IM.OD.Values.Where(o => o.ItemID.ToLowerInvariant().Contains("platform")))
            {
                var request = source.GetGameObjectAsync();
                while (!request.IsCompleted) yield return null;
                if (request.Result == null || request.Result.GetComponentInChildren(mw.GetType("ModularWorkshop.ModularWorkshopPlatform"), true) == null) continue;
                var instance = Object.Instantiate(request.Result, spawner.transform.position + Vector3.right * 2, Quaternion.identity);
                created.Add(instance);
                foreach (var rb in instance.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
                platform = (MonoBehaviour)instance.GetComponentInChildren(mw.GetType("ModularWorkshop.ModularWorkshopPlatform"), true);
                break;
            }
            Check(platform != null, "real workshop platform spawned", log);
            var slot = (FVRQuickBeltSlot)Read(platform, "QuickBeltSlot");
            int fixtureCount = 0, rollCount = 0;
            var candidates = IM.OD.Values.Where(o => o.Category == FVRObject.ObjectCategory.Firearm
                && (o.ItemID.ToLowerInvariant().Contains("xm8") || o.ItemID.ToLowerInvariant().Contains("pmm"))).ToArray();
            foreach (var source in candidates)
            {
                var request = source.GetGameObjectAsync();
                while (!request.IsCompleted) yield return null;
                if (request.Result == null || !request.Result.GetComponents<MonoBehaviour>().Any(c => c != null
                    && c.GetType().GetInterfaces().Any(i => i.FullName == "ModularWorkshop.IModularWeapon"))) continue;
                var instance = Object.Instantiate(request.Result, platform.transform.position + Vector3.up, Quaternion.identity);
                created.Add(instance); var physical = instance.GetComponent<FVRPhysicalObject>();
                physical.SetQuickBeltSlot(slot);
                yield return null; yield return null;
                var screens = (IDictionary)Read(platform, "_UIScreens");
                Check(screens.Count > 0, source.ItemID + " docking creates native menus", log);
                var menus = screens.Values.Cast<GameObject>().SelectMany(g => g.GetComponents<MonoBehaviour>()).Where(IsMenu).ToArray();
                yield return new WaitForEndOfFrame();
                CapturePlatform(platform.gameObject, instance, menus[0], Path.Combine(directory, source.ItemID + "-platform.jpg"));
                var stockMenu = menus.FirstOrDefault(m => Read(m, "PartType").ToString() == "Stock" && !(bool)Read(m, "_skinOnlyMode"));
                if (stockMenu != null)
                {
                    Call(stockMenu, "PButton_ShowUI"); yield return null; yield return new WaitForEndOfFrame();
                    Capture(stockMenu, Path.Combine(directory, source.ItemID + "-stock-menu.png"));
                    Call(stockMenu, "PButton_HideUI");
                }
                foreach (var menu in menus)
                {
                    log("WORKSHOP MENU " + source.ItemID + " group=" + Read(menu, "ModularPartsGroupID") + " type=" + Read(menu, "PartType")
                        + " skinOnly=" + Read(menu, "_skinOnlyMode") + " buttons=" + ((GameObject[])Read(menu, "PartButtons")).Length);
                    if ((bool)Read(menu, "_skinOnlyMode")) continue;
                    string group = (string)Read(menu, "ModularPartsGroupID");
                    object target = Read(menu, "ModularWeapon") ?? Read(menu, "ModularPhysicalObject");
                    var points = (IDictionary)Property(target, "AllAttachmentPoints");
                    var point = points[group];
                    var definition = groups[group];
                    var pool = (IList)Property(definition, "TakeAndHoldSpawnPool");
                    if (pool.Count < 2 || (bool)Read(point, "IsPointDisabled") || (bool)Read(point, "DisallowTakeAndHoldRandomization")) continue;
                    // Start with a simple slot: no child points or cross-slot occupation.
                    var prefabs = (IList)Read(definition, "ModularPrefabs");
                    if (prefabs.Cast<GameObject>().Any(p => ((Array)Read(p.GetComponent(mw.GetType("ModularWorkshop.ModularWeaponPart")), "SubAttachmentPoints")).Length > 0
                        || ((string[])Read(p.GetComponent(mw.GetType("ModularWorkshop.ModularWeaponPart")), "AlsoOccupiesPointWithModularPartsGroupIDs")).Length > 0)) continue;
                    Call(menu, "PButton_ShowUI"); yield return null; yield return new WaitForEndOfFrame();
                    Capture(menu, Path.Combine(directory, source.ItemID + "-slot-before.png"));
                    var roll = AccessTools.Method(controller.GetType().Assembly.GetType("Gundomizer.ModularWorkshopBridge"), "TryRandomizeSlot");
                    var before = Selected(target); var chosen = new HashSet<string>();
                    for (int i = 0; i < 8; ++i)
                    {
                        object[] args = { menu, null };
                        Check((bool)roll.Invoke(null, args), source.ItemID + " slot roll " + i + " succeeds: " + args[1], log);
                        yield return null;
                        var after = Selected(target); chosen.Add(after[group]);
                        Check(pool.Cast<string>().Contains(after[group]), "selected part belongs to native weighted pool", log);
                        Check(before.All(p => p.Key == group || (after.ContainsKey(p.Key) && after[p.Key] == p.Value)), "unrelated slots retain their selections", log);
                        ++rollCount;
                    }
                    Check(chosen.Count > 1, "repeated native rolls produce different choices", log);
                    yield return new WaitForEndOfFrame(); Capture(menu, Path.Combine(directory, source.ItemID + "-slot-after.png"));
                    bool prior = (bool)Read(point, "DisallowTakeAndHoldRandomization");
                    AccessTools.Field(point.GetType(), "DisallowTakeAndHoldRandomization").SetValue(point, true);
                    object[] excludedArgs = { menu, null };
                    try { Check(!(bool)roll.Invoke(null, excludedArgs), "author-excluded slots cannot be randomized", log); }
                    finally { AccessTools.Field(point.GetType(), "DisallowTakeAndHoldRandomization").SetValue(point, prior); }
                    Call(menu, "PButton_HideUI");
                    break;
                }
                physical.SetQuickBeltSlot(null); yield return null;
                Check(((IDictionary)Read(platform, "_UIScreens")).Count == 0, "undocking removes generated menus", log);
                Object.Destroy(instance); ++fixtureCount;
                if (fixtureCount >= 2) break;
            }
            Check(fixtureCount >= 2 && rollCount >= 16, "two real weapon fixtures exercised one-slot randomization", log);
            log("WORKSHOP PROTOTYPE CHECKS PASSED");
        }
        finally { foreach (var instance in created) if (instance != null) Object.Destroy(instance); }
    }

    private static void Capture(MonoBehaviour menu, string path)
    {
        Canvas.ForceUpdateCanvases();
        var panel = ((GameObject)Read(menu, "MainCanvas")).GetComponent<RectTransform>();
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(panel);
        var cameraObject = new GameObject("Workshop capture camera");
        var camera = cameraObject.AddComponent<Camera>();
        var texture = new RenderTexture(1200, 900, 24);
        var pixels = new Texture2D(1200, 900, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.transform.rotation = panel.rotation;
            camera.transform.position = panel.TransformPoint(bounds.center) - panel.forward * 2;
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(bounds.size.y * Mathf.Abs(panel.lossyScale.y), bounds.size.x * Mathf.Abs(panel.lossyScale.x) * .75f) * .55f;
            camera.nearClipPlane = .01f; camera.farClipPlane = 4;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .04f, .04f);
            camera.cullingMask = 1 << panel.GetComponentInParent<Canvas>().gameObject.layer;
            camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
            pixels.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null;
            Object.Destroy(cameraObject); Object.Destroy(texture); Object.Destroy(pixels);
        }
    }

    private static void CapturePlatform(GameObject platform, GameObject weapon, MonoBehaviour menu, string path)
    {
        var objects = new[] { platform.transform.root.gameObject, weapon };
        var bounds = new Bounds(weapon.transform.position, Vector3.zero);
        foreach (var obj in objects)
        {
            foreach (var renderer in obj.GetComponentsInChildren<Renderer>())
                if (renderer.enabled) bounds.Encapsulate(renderer.bounds);
            foreach (var graphic in obj.GetComponentsInChildren<Graphic>())
            {
                if (!graphic.enabled) continue;
                var corners = new Vector3[4]; graphic.rectTransform.GetWorldCorners(corners);
                foreach (var corner in corners) bounds.Encapsulate(corner);
            }
        }
        var cameraObject = new GameObject("Workshop overview camera"); var camera = cameraObject.AddComponent<Camera>();
        var texture = new RenderTexture(1600, 1000, 24); var pixels = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        var layers = objects.SelectMany(o => o.GetComponentsInChildren<Transform>(true)).Distinct()
            .ToDictionary(t => t.gameObject, t => t.gameObject.layer);
        try
        {
            foreach (var obj in layers.Keys) obj.layer = 31;
            camera.transform.rotation = menu.transform.rotation;
            camera.transform.position = bounds.center - menu.transform.forward * 4;
            float maxX = 0, maxY = 0;
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
            {
                var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                var relative = camera.transform.InverseTransformPoint(corner);
                maxX = Mathf.Max(maxX, Mathf.Abs(relative.x)); maxY = Mathf.Max(maxY, Mathf.Abs(relative.y));
            }
            camera.orthographic = true; camera.orthographicSize = Mathf.Max(maxY, maxX / 1.6f) * 1.08f;
            camera.nearClipPlane = .01f; camera.farClipPlane = 10;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .04f, .04f);
            camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
            pixels.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToJPG(75));
        }
        finally
        {
            foreach (var entry in layers) if (entry.Key != null) entry.Key.layer = entry.Value;
            RenderTexture.active = previous; camera.targetTexture = null;
            Object.Destroy(cameraObject); Object.Destroy(texture); Object.Destroy(pixels);
        }
    }
}
