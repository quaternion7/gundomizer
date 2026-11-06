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
                yield return null;
                PlaceOnTable(spawner, instance, log);
                break;
            }
            Check(platform != null, "real workshop platform spawned", log);
            var spectatorObject = new GameObject("Workshop test spectator"); created.Add(spectatorObject);
            var spectator = spectatorObject.AddComponent<Camera>();
            spectator.depth = 1000; spectator.nearClipPlane = .01f; spectator.farClipPlane = 50;
            spectator.clearFlags = CameraClearFlags.Skybox;
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
                Watch(spectator, platform, instance, menus[0]);
                if (File.Exists(Path.Combine(directory, "workshop-placement-mode.txt")))
                {
                    var buildControl = platform.GetComponent(controller.GetType().Assembly.GetType("Gundomizer.WorkshopBuildControl"));
                    var dice = (MonoBehaviour)Read(buildControl, "button");
                    var editor = spectatorObject.AddComponent<WorkshopPlacementEditor>();
                    editor.Setup(dice.transform.parent, spectator, directory, log);
                    var geo = platform.GetComponentInParent<FVRPhysicalObject>().GetComponentsInChildren<MeshRenderer>().First(r => r.name == "Geo");
                    var front = ViewerFront(spawner);
                    log("PLACEMENT CAMERA center=" + geo.bounds.center.ToString("F4") + " front=" + front + " spawnerForward=" + spawner.transform.forward);
                    editor.FramePlatform(geo.bounds.center + Vector3.up * .04f, front);
                    yield return null;
                    var beforeOrbit = spectator.transform.rotation;
                    editor.ProcessCameraEvent(new Event { type = EventType.MouseDown, button = 1, mousePosition = new Vector2(800, 350) }, 0);
                    editor.ProcessCameraEvent(new Event { type = EventType.MouseDrag, button = 1, delta = new Vector2(60, 20) }, 0);
                    editor.ProcessCameraEvent(new Event { type = EventType.MouseUp, button = 1 }, 0);
                    yield return null;
                    Check(Quaternion.Angle(beforeOrbit, spectator.transform.rotation) > 10, "placement camera responds to mouse drag events without game input axes", log);
                    editor.FramePlatform(geo.bounds.center + Vector3.up * .04f, front);
                    yield return new WaitForEndOfFrame();
                    Application.CaptureScreenshot(Path.Combine(directory, "placement-editor-screen.png"));
                    log("PLACEMENT EDITOR READY — move/rotate the rainbow build control; edits autosave locally.");
                    while (!File.Exists(Path.Combine(directory, "workshop-placement-finish.txt"))) yield return null;
                    yield break;
                }
                yield return new WaitForEndOfFrame();
                CapturePlatform(platform.gameObject, instance, menus[0], Path.Combine(directory, source.ItemID + "-platform.jpg"));
                var stockMenu = menus.FirstOrDefault(m => Read(m, "PartType").ToString() == "Stock" && !(bool)Read(m, "_skinOnlyMode"));
                if (stockMenu != null)
                {
                    Call(stockMenu, "PButton_ShowUI"); yield return null; yield return new WaitForEndOfFrame();
                    WatchMenu(spectator, stockMenu);
                    CheckControl(stockMenu, log);
                    Capture(stockMenu, Path.Combine(directory, source.ItemID + "-stock-menu.png"));
                    Call(ControlButton(stockMenu), "OnPointerEnter", new object[] { null });
                    yield return new WaitForEndOfFrame();
                    var stockControl = stockMenu.GetComponent(controller.GetType().Assembly.GetType("Gundomizer.WorkshopSlotControl"));
                    var tooltip = (Text)Read(stockControl, "tooltip");
                    Check(tooltip.transform.parent.gameObject.activeInHierarchy && tooltip.text.Contains("Randomize"), "hover identifies the slot action", log);
                    Check(!tooltip.raycastTarget && !tooltip.transform.parent.GetComponent<Image>().raycastTarget, "hover feedback does not intercept native pointer input", log);
                    Capture(stockMenu, Path.Combine(directory, source.ItemID + "-stock-hover.png"));
                    Call(ControlButton(stockMenu), "OnPointerExit", new object[] { null });
                    if (((GameObject)Read(stockMenu, "ShowSkinsButton")).activeInHierarchy)
                    {
                        var dicePosition = ControlButton(stockMenu).transform.localPosition;
                        Call(stockMenu, "PButton_ShowSkins"); yield return null;
                        Check(ControlButton(stockMenu).gameObject.activeInHierarchy && ControlButton(stockMenu).transform.localPosition == dicePosition, "slot dice stays fixed when switching to skins", log);
                        var skinChecks = SkinRollChecks(stockMenu, directory, source.ItemID + "-part", log);
                        try { while (skinChecks.MoveNext()) yield return skinChecks.Current; }
                        finally { (skinChecks as IDisposable).Dispose(); }
                        Call(stockMenu, "PButton_HideSkins"); yield return null;
                        Check(ControlButton(stockMenu).gameObject.activeInHierarchy && ControlButton(stockMenu).transform.localPosition == dicePosition, "slot dice stays fixed when returning to parts", log);
                    }
                    Call(stockMenu, "PButton_HideUI");
                    Check(!ControlButton(stockMenu).gameObject.activeInHierarchy, "collapsed menu hides its slot dice", log);
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
                    WatchMenu(spectator, menu);
                    CheckControl(menu, log);
                    Capture(menu, Path.Combine(directory, source.ItemID + "-slot-before.png"));
                    var roll = AccessTools.Method(controller.GetType().Assembly.GetType("Gundomizer.ModularWorkshopBridge"), "TryRandomizeSlot");
                    var before = Selected(target); var chosen = new HashSet<string>();
                    var dicePosition = ControlButton(menu).transform.localPosition;
                    for (int i = 0; i < 8; ++i)
                    {
                        string previousPart = Selected(target)[group];
                        yield return new WaitForSecondsRealtime(.2f);
                        ControlButton(menu).GetComponent<Button>().onClick.Invoke();
                        string status = (string)Read(menu.GetComponent(controller.GetType().Assembly.GetType("Gundomizer.WorkshopSlotControl")), "status");
                        Check(status != null && status.StartsWith("Selected "), source.ItemID + " dice click " + i + " succeeds: " + status, log);
                        yield return null;
                        var after = Selected(target); chosen.Add(after[group]);
                        Check(after[group] != previousPart, "individual roll never reselects the current part", log);
                        Check(ControlButton(menu).transform.localPosition == dicePosition, "dice does not move after a part roll", log);
                        Check(pool.Cast<string>().Contains(after[group]), "selected part belongs to native weighted pool", log);
                        Check(before.All(p => p.Key == group || (after.ContainsKey(p.Key) && after[p.Key] == p.Value)), "unrelated slots retain their selections", log);
                        ++rollCount;
                    }
                    Check(chosen.Count > 1, "repeated native rolls produce different choices", log);
                    yield return new WaitForEndOfFrame(); Capture(menu, Path.Combine(directory, source.ItemID + "-slot-after.png"));
                    bool prior = (bool)Read(point, "DisallowTakeAndHoldRandomization");
                    AccessTools.Field(point.GetType(), "DisallowTakeAndHoldRandomization").SetValue(point, true);
                    object[] excludedArgs = { menu, null };
                    try
                    {
                        Call(menu, "UpdateDisplay"); yield return new WaitForEndOfFrame();
                        Check(!ControlButton(menu).GetComponent<Button>().interactable, "author-excluded slot dice is disabled", log);
                        Check(!(bool)roll.Invoke(null, excludedArgs), "author-excluded slots cannot be randomized", log);
                    }
                    finally { AccessTools.Field(point.GetType(), "DisallowTakeAndHoldRandomization").SetValue(point, prior); Call(menu, "UpdateDisplay"); }
                    Call(menu, "PButton_HideUI");
                    break;
                }
                var receiverMenu = menus.FirstOrDefault(m => (bool)Read(m, "_receiverSkinMode"));
                if (receiverMenu != null)
                {
                    Call(receiverMenu, "PButton_ShowUI"); yield return null;
                    var receiverChecks = SkinRollChecks(receiverMenu, directory, source.ItemID + "-receiver", log);
                    try { while (receiverChecks.MoveNext()) yield return receiverChecks.Current; }
                    finally { (receiverChecks as IDisposable).Dispose(); }
                    Call(receiverMenu, "PButton_HideUI");
                }
                if (source.ItemID == "PMM9mm")
                {
                    var parentMenu = menus.First(m => !(bool)Read(m, "_skinOnlyMode") && Read(m, "PartType").ToString() == "Barrel");
                    var parentTarget = Read(parentMenu, "ModularWeapon");
                    int preservedChildren = 0;
                    Call(parentMenu, "PButton_ShowUI");
                    for (int attempt = 0; attempt < 6; ++attempt)
                    {
                        var childrenBefore = ((IList)Property(parentTarget, "SubAttachmentPoints")).Cast<object>()
                            .ToDictionary(p => (string)Read(p, "ModularPartsGroupID"), p => (string)Read(p, "SelectedModularWeaponPart"));
                        object[] args = { parentMenu, null };
                        Check((bool)AccessTools.Method(controller.GetType().Assembly.GetType("Gundomizer.ModularWorkshopBridge"), "TryRandomizeSlot").Invoke(null, args), "parent slot reroll succeeds", log);
                        yield return null;
                        foreach (var child in ((IList)Property(parentTarget, "SubAttachmentPoints")))
                        {
                            string prior;
                            if (!childrenBefore.TryGetValue((string)Read(child, "ModularPartsGroupID"), out prior)) continue;
                            Check((string)Read(child, "SelectedModularWeaponPart") == prior, "single-slot roll preserves a surviving child selection", log);
                            ++preservedChildren;
                        }
                    }
                    Check(preservedChildren > 0, "non-recursive slot behavior exercised against real child points", log);
                    Call(parentMenu, "PButton_HideUI");
                }
                var build = (MonoBehaviour)platform.GetComponent(controller.GetType().Assembly.GetType("Gundomizer.WorkshopBuildControl"));
                Check(build != null, "platform has whole-build control", log);
                var buildButton = (MonoBehaviour)Read(build, "button");
                Watch(spectator, platform, instance, menus[0]);
                Check(buildButton != null && buildButton.gameObject.activeInHierarchy, "compact build dice is visible beside cog", log);
                Check((bool)Read(buildButton, "Rainbow"), "build dice uses the standard animated rainbow", log);
                Check(buildButton.transform.IsChildOf(platform.GetComponentInParent<FVRPhysicalObject>().transform), "build control moves with platform", log);
                var body = platform.GetComponentInParent<FVRPhysicalObject>().transform;
                var relative = body.InverseTransformPoint(buildButton.transform.position);
                var bodyPosition = body.position; var bodyRotation = body.rotation; var bodyScale = body.localScale;
                try
                {
                    body.position += new Vector3(.2f, .1f, -.1f);
                    body.rotation = Quaternion.AngleAxis(40, Vector3.up) * body.rotation;
                    body.localScale *= .8f;
                    yield return null;
                    Check(Vector3.Distance(buildButton.transform.position, body.TransformPoint(relative)) < .0001f,
                        "build control retains its placement through platform movement, rotation and scaling", log);
                }
                finally { body.position = bodyPosition; body.rotation = bodyRotation; body.localScale = bodyScale; }
                var cog = buildButton.transform.parent.parent.GetComponent<BoxCollider>();
                var diceBox = buildButton.GetComponent<BoxCollider>();
                var inCogFrame = new Bounds(cog.transform.InverseTransformPoint(diceBox.transform.TransformPoint(diceBox.center)), Vector3.zero);
                foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
                    inCogFrame.Encapsulate(cog.transform.InverseTransformPoint(diceBox.transform.TransformPoint(diceBox.center + Vector3.Scale(diceBox.size * .5f, new Vector3(x, y, z)))));
                Check(!new Bounds(cog.center, cog.size).Intersects(inCogFrame), "build dice hitbox is separate from native cog", log);
                Call(buildButton, "OnPointerEnter", new object[] { null }); yield return new WaitForEndOfFrame();
                Check(((Text)Read(build, "tooltip")).text.StartsWith("Randomize build"), "build hover identifies the whole-build action", log);
                CaptureBuild(platform, buildButton, ViewerFront(spawner), Path.Combine(directory, source.ItemID + "-build-control.png"));
                CapturePlatform(platform.gameObject, instance, menus[0], Path.Combine(directory, source.ItemID + "-build-hover.jpg"));
                Call(buildButton, "OnPointerExit", new object[] { null });
                var states = new HashSet<string>(); int nested = 0;
                var targetObject = Read(menus.First(m => !(bool)Read(m, "_skinOnlyMode")), "ModularWeapon");
                var rootPoints = (IDictionary)Property(targetObject, "AllAttachmentPoints");
                var excludedMenu = menus.Last(m => !(bool)Read(m, "_skinOnlyMode") && Read(m, "PartType").ToString() != "SubAttachmentPoint");
                var excluded = rootPoints[(string)Read(excludedMenu, "ModularPartsGroupID")];
                bool excludedBefore = (bool)Read(excluded, "DisallowTakeAndHoldRandomization");
                string excludedSelection = (string)Read(excluded, "SelectedModularWeaponPart");
                AccessTools.Field(excluded.GetType(), "DisallowTakeAndHoldRandomization").SetValue(excluded, true);
                try
                {
                    for (int i = 0; i < 8; ++i)
                    {
                        yield return new WaitForSecondsRealtime(.2f);
                        string receiverBefore = (string)Read(Property(targetObject, "GetModularFVRFireArm"), "CurrentSelectedReceiverSkinID");
                        buildButton.GetComponent<Button>().onClick.Invoke();
                        Check((bool)Property(build, "Busy"), "whole-build click starts a cooperative roll", log);
                        object[] busyArgs = { menus.First(m => !(bool)Read(m, "_skinOnlyMode")), null };
                        Check(!(bool)AccessTools.Method(controller.GetType().Assembly.GetType("Gundomizer.ModularWorkshopBridge"), "TryRandomizeSlot").Invoke(null, busyArgs), "slot clicks cannot interleave with build roll", log);
                        float finish = Time.realtimeSinceStartup + 20;
                        while ((bool)Property(build, "Busy") && Time.realtimeSinceStartup < finish) yield return null;
                        Check(!(bool)Property(build, "Busy") && ((string)Property(build, "Status")).StartsWith("Randomized "), "whole-build roll finishes: " + Property(build, "Status"), log);
                        Check(slot.HeldObject == physical, "whole-build roll retains the original receiver", log);
                        var skinDefinitions = (IDictionary)AccessTools.Field(manager, "ModularWorkshopSkinsDictionary").GetValue(null);
                        var backend = Property(targetObject, "GetModularFVRFireArm");
                        string receiverPath = (string)Read(backend, "SkinPath");
                        bool receiverHasChoices = ((IDictionary)Property(skinDefinitions[receiverPath], "SkinDictionary")).Count > 1;
                        int eligibleSkins = receiverHasChoices ? 1 : 0;
                        foreach (DictionaryEntry entry in (IDictionary)Property(targetObject, "AllAttachmentPoints"))
                        {
                            var p = entry.Value;
                            if ((bool)Read(p, "IsPointDisabled") || (bool)Read(p, "DisallowTakeAndHoldRandomization")) continue;
                            string path = entry.Key + "/" + Read(p, "SelectedModularWeaponPart");
                            if (skinDefinitions.Contains(path) && ((IDictionary)Property(skinDefinitions[path], "SkinDictionary")).Count > 1) ++eligibleSkins;
                        }
                        Check((int)Property(build, "SkinsChanged") == eligibleSkins, "whole-build roll includes all eligible skins (" + eligibleSkins + ")", log);
                        if (receiverHasChoices) Check((string)Read(backend, "CurrentSelectedReceiverSkinID") != receiverBefore, "whole-build roll chooses a different receiver skin", log);
                        Check((string)Read(excluded, "SelectedModularWeaponPart") == excludedSelection, "whole-build roll respects author-excluded point", log);
                        var selections = Selected(targetObject);
                        states.Add(string.Join(";", selections.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value).ToArray()));
                        foreach (DictionaryEntry entry in (IDictionary)Property(targetObject, "AllAttachmentPoints"))
                        {
                            if ((bool)Read(entry.Value, "IsPointDisabled") || (bool)Read(entry.Value, "DisallowTakeAndHoldRandomization")) continue;
                            var pool = (IList)Property(groups[entry.Key], "TakeAndHoldSpawnPool");
                            if (pool.Count > 0) Check(pool.Contains(Read(entry.Value, "SelectedModularWeaponPart")), "build part belongs to authored pool: " + entry.Key, log);
                        }
                        nested = Math.Max(nested, ((IList)Property(targetObject, "SubAttachmentPoints")).Count);
                    }
                }
                finally { AccessTools.Field(excluded.GetType(), "DisallowTakeAndHoldRandomization").SetValue(excluded, excludedBefore); }
                Check(states.Count > 1, "repeated build rolls produce varied assemblies", log);
                if (source.ItemID == "PMM9mm") Check(nested > 0, "build roll traversed real nested slots", log);
                log("WORKSHOP BUILD " + source.ItemID + " distinct=" + states.Count + " maxNested=" + nested);
                var savedSelections = Selected(targetObject);
                var savedSkins = SkinSnapshot(targetObject);
                var restored = Object.Instantiate(request.Result, platform.transform.position + Vector3.up * 2, Quaternion.identity);
                created.Add(restored); yield return null;
                restored.GetComponent<FVRPhysicalObject>().ConfigureFromFlagDic(physical.GetFlagDic());
                yield return null; yield return null;
                var restoredTarget = restored.GetComponents<MonoBehaviour>().First(c => c != null && c.GetType().GetInterfaces().Any(t => t.FullName == "ModularWorkshop.IModularWeapon"));
                var restoredSelections = Selected(restoredTarget);
                Check(savedSelections.Count == restoredSelections.Count && savedSelections.All(p => restoredSelections.ContainsKey(p.Key) && restoredSelections[p.Key] == p.Value), "randomized build survives native save/load flags", log);
                var restoredSkins = SkinSnapshot(restoredTarget);
                Check(savedSkins.Count == restoredSkins.Count && savedSkins.All(p => restoredSkins.ContainsKey(p.Key) && restoredSkins[p.Key] == p.Value), "randomized skins survive native save/load flags", log);
                Object.Destroy(restored);
                Call(build, "Activate"); Call(build, "Activate"); yield return null; yield return null;
                Check(!(bool)Property(build, "Busy") && ((string)Property(build, "Status")).Contains("cancelled"), "second activation cancels a pending build roll", log);
                var freshMenus = ((IDictionary)Read(platform, "_UIScreens")).Values.Cast<GameObject>().SelectMany(g => g.GetComponents<MonoBehaviour>()).Where(IsMenu).ToArray();
                CapturePlatform(platform.gameObject, instance, freshMenus[0], Path.Combine(directory, source.ItemID + "-build-complete.jpg"));
                yield return new WaitForSecondsRealtime(.2f);
                buildButton.GetComponent<Button>().onClick.Invoke();
                Check((bool)Property(build, "Busy"), "build roll active before undocking", log);
                physical.SetQuickBeltSlot(null); yield return null; yield return null;
                Check(!(bool)Property(build, "Busy") && ((string)Property(build, "Status")).Contains("cancelled"), "undocking cancels the pending build roll", log);
                yield return new WaitForEndOfFrame();
                Check(!buildButton.GetComponent<Button>().interactable, "empty platform disables build dice", log);
                Check(((IDictionary)Read(platform, "_UIScreens")).Count == 0, "undocking removes generated menus", log);
                Object.Destroy(instance); ++fixtureCount;
                if (fixtureCount >= 2) break;
            }
            Check(fixtureCount >= 2 && rollCount >= 16, "two real weapon fixtures exercised one-slot randomization", log);
            log("WORKSHOP PROTOTYPE CHECKS PASSED");
        }
        finally { foreach (var instance in created) if (instance != null) Object.Destroy(instance); }
    }

    private static MonoBehaviour ControlButton(MonoBehaviour menu)
    {
        var control = menu.GetComponents<MonoBehaviour>().First(c => c.GetType().FullName == "Gundomizer.WorkshopSlotControl");
        return (MonoBehaviour)Read(control, "button");
    }

    private static Dictionary<string, string> SkinSnapshot(object target)
    {
        var result = new Dictionary<string, string>();
        foreach (DictionaryEntry entry in (IDictionary)Property(target, "AllAttachmentPoints"))
            if (!(bool)Read(entry.Value, "IsPointDisabled")) result.Add((string)entry.Key, (string)Property(entry.Value, "CurrentSkin"));
        result.Add("receiver", (string)Read(Property(target, "GetModularFVRFireArm"), "CurrentSelectedReceiverSkinID"));
        return result;
    }

    private static string SkinSelection(MonoBehaviour menu)
    {
        var target = Read(menu, "ModularWeapon");
        if ((bool)Read(menu, "_receiverSkinMode")) return (string)Read(Property(target, "GetModularFVRFireArm"), "CurrentSelectedReceiverSkinID");
        var points = (IDictionary)Property(target, "AllAttachmentPoints");
        return (string)Property(points[(string)Read(menu, "ModularPartsGroupID")], "CurrentSkin");
    }

    private static IEnumerator SkinRollChecks(MonoBehaviour menu, string directory, string name, Action<string> log)
    {
        yield return new WaitForEndOfFrame();
        CheckControl(menu, log);
        var names = (string[])Read(menu, "_skinNames");
        if (names.Distinct().Count() < 2)
        { Check(!ControlButton(menu).GetComponent<Button>().interactable, "single-choice skin dice is disabled", log); yield break; }
        var position = ControlButton(menu).transform.localPosition;
        var target = Read(menu, "ModularWeapon"); var parts = Selected(target);
        for (int i = 0; i < 4; ++i)
        {
            string before = SkinSelection(menu);
            yield return new WaitForSecondsRealtime(.2f);
            ControlButton(menu).GetComponent<Button>().onClick.Invoke(); yield return null;
            string after = SkinSelection(menu);
            Check(after != before && names.Contains(after), "skin dice selects a different native skin: " + name, log);
            Check(Selected(target).All(p => parts.ContainsKey(p.Key) && parts[p.Key] == p.Value), "skin roll does not change parts", log);
            Check(ControlButton(menu).transform.localPosition == position, "skin roll keeps dice position fixed", log);
        }
        yield return new WaitForEndOfFrame(); Capture(menu, Path.Combine(directory, name + "-skin-dice.png"));
    }

    private static void PlaceOnTable(ItemSpawnerV2 spawner, GameObject instance, Action<string> log)
    {
        var pad = spawner.SpawnPoint_Large;
        var geo = instance.GetComponentsInChildren<MeshFilter>().First(f => f.name == "Geo");
        var size = geo.sharedMesh.bounds.size;
        var normal = geo.transform.TransformDirection(size.x < size.y && size.x < size.z ? Vector3.right : size.y < size.z ? Vector3.up : Vector3.forward);
        var cog = instance.GetComponentsInChildren<MonoBehaviour>(true).First(c => c != null && c.GetType().FullName == "AndrewFTW.SimpleSwitch");
        var dock = instance.GetComponentInChildren<FVRQuickBeltSlot>(true);
        if (Vector3.Dot(normal, dock.PoseOverride.position - geo.GetComponent<Renderer>().bounds.center) < 0) normal = -normal;
        instance.transform.rotation = Quaternion.FromToRotation(normal, Vector3.up) * instance.transform.rotation;
        var facing = Vector3.ProjectOnPlane(cog.transform.position - geo.GetComponent<Renderer>().bounds.center, Vector3.up).normalized;
        var front = ViewerFront(spawner);
        if (facing.sqrMagnitude > .01f)
            instance.transform.rotation = Quaternion.AngleAxis(Mathf.Atan2(Vector3.Cross(facing, front).y, Vector3.Dot(facing, front)) * Mathf.Rad2Deg, Vector3.up) * instance.transform.rotation;
        var hits = Physics.RaycastAll(new Ray(pad.position + Vector3.up, Vector3.down), 3);
        var surface = hits.Where(h => h.normal.y > .9f && !h.collider.isTrigger && !h.collider.transform.IsChildOf(instance.transform)
            && h.collider.GetComponentInParent<FVRPhysicalObject>() == null && Mathf.Abs(h.point.y - pad.position.y) < .6f)
            .OrderBy(h => h.distance).ToArray();
        Check(surface.Length > 0, "found native spawner table surface beneath large spawn pad", log);
        var bounds = geo.GetComponent<Renderer>().bounds;
        instance.transform.position += new Vector3(pad.position.x - bounds.center.x, surface[0].point.y + .005f - bounds.min.y, pad.position.z - bounds.center.z);
        var upright = geo.transform.TransformDirection(size.x < size.y && size.x < size.z ? Vector3.right : size.y < size.z ? Vector3.up : Vector3.forward);
        Check(Mathf.Abs(Vector3.Dot(upright, Vector3.up)) > .999f, "platform base is horizontal in world space", log);
        Check(dock.PoseOverride.position.y > geo.GetComponent<Renderer>().bounds.max.y, "weapon dock is above the platform top", log);
        log("PLACEMENT TABLE " + surface[0].collider.name + " top=" + surface[0].point.y + " platformBottom=" + geo.GetComponent<Renderer>().bounds.min.y);
    }

    private static Vector3 ViewerFront(ItemSpawnerV2 spawner)
        => -Vector3.ProjectOnPlane(spawner.MainMenuCanvases[0].transform.forward, Vector3.up).normalized;

    private static void Watch(Camera camera, MonoBehaviour platform, GameObject weapon, MonoBehaviour menu)
    {
        var body = platform.GetComponentInParent<FVRPhysicalObject>();
        var bounds = new Bounds(body.transform.position, Vector3.zero);
        foreach (var renderer in weapon.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
        camera.transform.rotation = menu.transform.rotation;
        camera.transform.position = bounds.center + Vector3.up * .15f - menu.transform.forward * 3;
        camera.orthographic = true; camera.orthographicSize = Mathf.Max(.65f, bounds.extents.magnitude * 1.2f);
    }

    private static void WatchMenu(Camera camera, MonoBehaviour menu)
    {
        var panel = ((GameObject)Read(menu, "MainCanvas")).GetComponent<RectTransform>();
        var bounds = MenuBounds(panel, menu);
        camera.transform.rotation = panel.rotation;
        camera.transform.position = panel.TransformPoint(bounds.center) - panel.forward * 2;
        camera.orthographic = true;
        camera.orthographicSize = Mathf.Max(bounds.size.y * Mathf.Abs(panel.lossyScale.y), bounds.size.x * Mathf.Abs(panel.lossyScale.x) / camera.aspect) * .65f;
    }

    private static void CaptureBuild(MonoBehaviour platform, MonoBehaviour button, Vector3 front, string path)
    {
        var obj = new GameObject("Build control capture"); var camera = obj.AddComponent<Camera>();
        var texture = new RenderTexture(1000, 600, 24); var pixels = new Texture2D(1000, 600, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.transform.position = button.transform.position + front * .28f + Vector3.up * .16f;
            camera.transform.LookAt(button.transform.position, Vector3.up);
            camera.orthographic = true; camera.orthographicSize = .09f; camera.nearClipPlane = .001f; camera.farClipPlane = 2;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .04f, .04f);
            camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
            pixels.ReadPixels(new Rect(0, 0, 1000, 600), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null;
            Object.Destroy(obj); Object.Destroy(texture); Object.Destroy(pixels);
        }
    }

    private static Bounds MenuBounds(RectTransform panel, MonoBehaviour menu)
    {
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(panel);
        var control = menu.GetComponents<MonoBehaviour>().FirstOrDefault(c => c.GetType().FullName == "Gundomizer.WorkshopSlotControl");
        var button = control == null ? null : Read(control, "button") as MonoBehaviour;
        if (button != null && button.gameObject.activeInHierarchy)
        {
            var corners = new Vector3[4]; ((RectTransform)button.transform).GetWorldCorners(corners);
            foreach (var corner in corners) bounds.Encapsulate(panel.InverseTransformPoint(corner));
        }
        return bounds;
    }

    private static void CheckControl(MonoBehaviour menu, Action<string> log)
    {
        var button = ControlButton(menu); var rect = (RectTransform)button.transform; var parent = rect.parent;
        Check(button.gameObject.activeInHierarchy, "native part menu has an active slot dice", log);
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, rect);
        foreach (string name in new[] { "HideButton", "ShowSkinsButton", "HideSkinsButton", "BackButton", "NextButton" })
        {
            var obj = (GameObject)Read(menu, name); if (obj == null) continue;
            var native = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, obj.transform);
            Check(bounds.max.y < native.min.y, "dice sits below active " + name, log);
        }
        var box = new Rect(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y);
        foreach (var native in menu.GetComponentsInChildren<FVRPointableButton>())
        {
            var other = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, native.transform);
            Check(!box.Overlaps(new Rect(other.min.x, other.min.y, other.size.x, other.size.y)), "dice does not overlap " + native.name, log);
        }
    }

    private static void Capture(MonoBehaviour menu, string path)
    {
        Canvas.ForceUpdateCanvases();
        var panel = ((GameObject)Read(menu, "MainCanvas")).GetComponent<RectTransform>();
        var bounds = MenuBounds(panel, menu);
        var cameraObject = new GameObject("Workshop capture camera");
        var camera = cameraObject.AddComponent<Camera>();
        var texture = new RenderTexture(1200, 900, 24);
        var pixels = new Texture2D(1200, 900, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        var layers = menu.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.gameObject, t => t.gameObject.layer);
        try
        {
            foreach (var obj in layers.Keys) obj.layer = 31;
            camera.transform.rotation = panel.rotation;
            camera.transform.position = panel.TransformPoint(bounds.center) - panel.forward * 2;
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(bounds.size.y * Mathf.Abs(panel.lossyScale.y), bounds.size.x * Mathf.Abs(panel.lossyScale.x) * .75f) * .55f;
            camera.nearClipPlane = .01f; camera.farClipPlane = 4;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .04f, .04f);
            camera.cullingMask = 1 << 31;
            camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
            pixels.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            foreach (var entry in layers) if (entry.Key != null) entry.Key.layer = entry.Value;
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
