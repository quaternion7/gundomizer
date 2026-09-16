// Exercises the real modded AM catalog, popup and spawning paths. Never shipped.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Anvil;
using BepInEx.Configuration;
using FistVR;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

public static class AmmoModChecks
{
    private sealed class Round
    {
        internal FireArmRoundType Type;
        internal FireArmRoundClass Class;
        internal FVRFireArmRoundDisplayData.DisplayDataClass Data;
        internal string Pack;
    }
    private static T Get<T>(object target, string name) { return (T)AccessTools.Field(target.GetType(), name).GetValue(target); }
    private static object Call(object target, string name, params object[] args) { return AccessTools.Method(target.GetType(), name).Invoke(target, args); }
    private static void Check(bool condition, string message, Action<string> log)
    { if (!condition) throw new Exception(message); log("PASS " + message); }

    public static IEnumerator Prepare(string directory, Action<string> log)
    {
        // A3A/OtherLoader may still be registering metadata after the native scene is ready.
        var assembly = BepInEx.Bootstrap.Chainloader.PluginInfos["h3vr.otherloader"].Instance.GetType().Assembly;
        var status = assembly.GetType("OtherLoader.LoaderStatus");
        float deadline = Time.realtimeSinceStartup + 180;
        yield return new WaitForSecondsRealtime(3);
        while (Time.realtimeSinceStartup < deadline && ((int)AccessTools.Property(status, "NumActiveLoaders").GetValue(null, null) != 0
            || (float)AccessTools.Method(status, "GetLoaderProgress").Invoke(null, null) < 1f
            || Time.time - (float)AccessTools.Field(status, "LastLoadEventTime").GetValue(null) < 3f)) yield return null;
        Check(Time.realtimeSinceStartup < deadline, "OtherLoader startup registration finished", log);
    }

    public static IEnumerator Run(ItemSpawnerV2 spawner, MonoBehaviour controller, string directory, Action<string> log)
    {
        var assembly = controller.GetType().Assembly;
        var catalog = assembly.GetType("Gundomizer.AmmoCatalog");
        var adapter = assembly.GetType("Gundomizer.OtherLoaderBridge");
        var selectionType = assembly.GetType("Gundomizer.AmmoSelection");
        var selection = AccessTools.Field(selectionType, "Shared").GetValue(null);
        var loading = AccessTools.Field(typeof(AnvilAsset), "m_loadingState");
        var addressField = AccessTools.Field(typeof(AnvilAsset), "m_anvilPrefab");
        var rounds = new List<Round>();
        foreach (var caliber in AM.STypeDic)
        foreach (var pair in caliber.Value)
        {
            if (pair.Value == null || pair.Value.ObjectID == null) continue;
            var source = pair.Value.ObjectID;
            var address = (AssetID)addressField.GetValue(source);
            if (string.IsNullOrEmpty(address.Bundle) && IM.OD.ContainsKey(source.ItemID))
                address = (AssetID)addressField.GetValue(IM.OD[source.ItemID]);
            string bundle = (address.Bundle ?? "").ToLowerInvariant();
            string pack = bundle.Contains("exotic12gau") ? "A3A" : bundle.Contains("pissinhot") ? "Bubba" : null;
            if (pack != null) rounds.Add(new Round { Type = caliber.Key, Class = pair.Key, Data = pair.Value, Pack = pack });
        }
        log("AMMO REGISTRATION A3A=" + rounds.Count(r => r.Pack == "A3A") + " Bubba=" + rounds.Count(r => r.Pack == "Bubba"));
        Check(rounds.Count(r => r.Pack == "A3A") == 23, "A3A registered all 23 custom rounds in the native ammo catalog", log);
        Check(rounds.Count(r => r.Pack == "Bubba") == 12,
            "Bubba 1.2.5 registered its 12 available variants (.40 S&W is absent from this download's registered objects)", log);
        var sources = IM.OD.Values.Where(o => o != null).Distinct().ToArray();
        foreach (var source in sources)
        {
            var addr = (AssetID)addressField.GetValue(source);
            if ((addr.Bundle ?? "").Contains("pissinhot")) log("BUBBA OBJECT " + source.ItemID + " name=" + source.DisplayName
                + " category=" + source.Category + " caliber=" + source.RoundType + " AM=" + rounds.Count(r => r.Data.ObjectID.ItemID == source.ItemID));
        }
        var callbacks = sources.ToDictionary(o => o, o => loading.GetValue(o));
        var allVariants = new List<object>();
        using (var report = new StreamWriter(Path.Combine(directory, "ammo-mods.tsv")))
        {
            report.WriteLine("pack\tcaliber\tclass\tname\tobject\tspawnedFrom\tpresent\tproperties");
            foreach (var group in rounds.GroupBy(r => r.Type))
            {
                var variants = ((IEnumerable)AccessTools.Method(catalog, "Read").Invoke(null,
                    new object[] { new HashSet<FireArmRoundType> { group.Key } })).Cast<object>().ToList();
                allVariants.AddRange(variants);
                foreach (var round in group)
                {
                    var variant = variants.FirstOrDefault(v => Get<FireArmRoundClass>(v, "Class") == round.Class);
                    var resolved = (ItemSpawnerID)AccessTools.Method(adapter, "Resolve").Invoke(null, new object[] { round.Data.ObjectID.ItemID });
                    var loader = BepInEx.Bootstrap.Chainloader.PluginInfos["h3vr.otherloader"].Instance.GetType();
                    var modern = (IDictionary)AccessTools.Field(loader, "SpawnerEntriesByID").GetValue(null);
                    var legacy = (IDictionary)AccessTools.Field(loader, "SpawnerIDsByMainObject").GetValue(null);
                    log("AMMO RESOLVE " + round.Data.ObjectID.ItemID + " native=" + IM.HasSpawnedID(round.Data.ObjectID.SpawnedFromId)
                        + " modern=" + modern.Contains(round.Data.ObjectID.ItemID) + " legacy=" + legacy.Contains(round.Data.ObjectID.ItemID)
                        + " resolved=" + (resolved == null ? "null" : resolved.ItemID + "/" + resolved.MainObject.ItemID)
                        + " available=" + AccessTools.Method(adapter, "Available").Invoke(null, new object[] { resolved }));
                    string row = round.Pack + "\t" + round.Type + "\t" + (int)round.Class + "\t" + round.Data.Name + "\t"
                        + round.Data.ObjectID.ItemID + "\t" + round.Data.ObjectID.SpawnedFromId + "\t" + (variant != null) + "\t"
                        + (variant == null ? "" : Get<string>(variant, "Properties"));
                    report.WriteLine(row); log("AMMO MOD " + row);
                }
            }
        }
        Check(sources.All(o => ReferenceEquals(callbacks[o], loading.GetValue(o))), "building modded ammo choices requests zero prefabs", log);
        Check(rounds.All(r => allVariants.Any(v => Get<FireArmRoundType>(v, "Type") == r.Type && Get<FireArmRoundClass>(v, "Class") == r.Class)),
            "all " + rounds.Count + " registered custom variants are offered by Gundomizer", log);
        var pageCounts = ManagerSingleton<IM>.Instance.PageItemLists.ToDictionary(p => p.Key, p => p.Value.Count);
        var repeated = ((IEnumerable)AccessTools.Method(catalog, "Read").Invoke(null,
            new object[] { new HashSet<FireArmRoundType>(rounds.Select(r => r.Type)) })).Cast<object>().ToList();
        Check(repeated.Count == allVariants.Count && repeated.All(v => allVariants.Any(old => Get<string>(old, "Key") == Get<string>(v, "Key")
            && Get<ItemSpawnerID>(old, "Entry") == Get<ItemSpawnerID>(v, "Entry")))
            && pageCounts.All(p => ManagerSingleton<IM>.Instance.PageItemLists[p.Key].Count == p.Value),
            "catalog refresh reuses stable selection entries without adding browser items", log);

        var plugin = BepInEx.Bootstrap.Chainloader.PluginInfos["quaternion.gundomizer"].Instance;
        var instant = (ConfigEntry<bool>)AccessTools.Field(plugin.GetType(), "SpawnItemInstantly").GetValue(null);
        var autoFill = (ConfigEntry<bool>)AccessTools.Field(plugin.GetType(), "AutoFillHeldItem").GetValue(null);
        bool priorInstant = instant.Value, priorFill = autoFill.Value, priorSave = plugin.Config.SaveOnConfigSet;
        var hands = GM.CurrentMovementManager.Hands;
        var heldField = AccessTools.Field(typeof(FVRViveHand), "m_currentInteractable");
        var priorHeld = hands.Select(h => h.CurrentInteractable).ToArray();
        var priorEnabled = hands.Select(h => h.enabled).ToArray();
        var priorHead = GM.CurrentPlayerBody.Head.position;
        var exclusions = allVariants.ToDictionary(v => Get<string>(v, "Key"), v => (bool)Call(selection, "Includes", Get<string>(v, "Key")));
        var spawned = new List<GameObject>();
        var panel = Get<object>(controller, "ammoPanel");
        plugin.Config.SaveOnConfigSet = false;
        try
        {
            autoFill.Value = true;
            foreach (var hand in hands) { hand.enabled = false; heldField.SetValue(hand, null); }
            GM.CurrentPlayerBody.Head.position = spawner.transform.position + Vector3.back;
            spawner.BTN_SetPageMode(2); spawner.BTN_SimpleMode_SwitchToTagSearch(); spawner.BTN_Tag_ClearSelectedTags();
            foreach (var group in rounds.GroupBy(r => r.Type))
            {
                var gunChoices = sources.Where(o => !o.IsModContent && o.Category == FVRObject.ObjectCategory.Firearm
                    && o.UsesRoundTypeFlag && o.RoundType == group.Key).OrderBy(o => o.ItemID, StringComparer.Ordinal).ToArray();
                Check(gunChoices.Length > 0, "native held firearm fixture exists for " + group.Key, log);
                FVRObject gun = null;
                GameObject held = null;
                float deadline;
                foreach (var candidate in gunChoices)
                {
                    var load = candidate.GetGameObjectAsync();
                    deadline = Time.realtimeSinceStartup + 90;
                    while (!load.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                    Check(load.IsCompleted && load.Result != null, "held firearm loaded: " + candidate.ItemID, log);
                    var instance = Object.Instantiate(load.Result, spawner.transform.position + Vector3.up, Quaternion.identity);
                    spawned.Add(instance);
                    foreach (var rb in instance.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
                    yield return null;
                    // Some native weapons (AMagII in this build) declare one caliber on
                    // the gun and a different one on their chamber. Keep production's exact
                    // check and use a fixture
                    // with a declared matching chamber for the positive refill assertion.
                    var fa = instance.GetComponent<FVRFireArm>();
                    if (fa != null && fa.GetChambers().Any(c => c != null && c.RoundType == group.Key))
                    { gun = candidate; held = instance; break; }
                    log("FILL FIXTURE SKIPPED " + candidate.ItemID + ": no chamber declares " + group.Key);
                    Object.Destroy(instance); yield return null;
                }
                Check(held != null, "held firearm has a chamber declaring " + group.Key, log);
                heldField.SetValue(hands[0], held.GetComponent<FVRPhysicalObject>());
                yield return null; // Native Start may finish chamber initialization.
                Call(controller, "RefreshHeldItem", new object[] { null });
                Call(panel, "Refresh", held.GetComponent<FVRPhysicalObject>(), true);
                var choices = Get<IList>(panel, "variants").Cast<object>().ToList();
                foreach (var choice in choices)
                {
                    string key = Get<string>(choice, "Key");
                    if (!exclusions.ContainsKey(key)) exclusions[key] = (bool)Call(selection, "Includes", key);
                }
                var actualGun = held.GetComponent<FVRFireArm>();
                var liveTypes = new HashSet<FireArmRoundType> { actualGun.RoundType };
                foreach (var chamber in actualGun.GetChambers()) if (chamber != null) liveTypes.Add(chamber.RoundType);
                log("HELD AMMO TYPES " + gun.ItemID + "=" + string.Join(",", liveTypes.Select(t => t.ToString()).ToArray()));
                Check(choices.All(v => liveTypes.Contains(Get<FireArmRoundType>(v, "Type")))
                    && group.All(r => choices.Any(v => Get<FireArmRoundType>(v, "Type") == r.Type && Get<FireArmRoundClass>(v, "Class") == r.Class)),
                    "popup contains every custom variant and only calibers declared by the live gun/chambers: " + gun.ItemID, log);
                foreach (var round in group)
                {
                    var variant = choices.Single(v => Get<FireArmRoundType>(v, "Type") == round.Type && Get<FireArmRoundClass>(v, "Class") == round.Class);
                    int index = choices.IndexOf(variant);
                    Call(panel, "SetAll", false);
                    AccessTools.Field(panel.GetType(), "page").SetValue(panel, index / 7);
                    Call(panel, "Toggle", index % 7);
                    var enabled = ((IEnumerable)Call(panel, "EnabledVariants")).Cast<object>().ToArray();
                    Check(enabled.Length == 1 && Get<string>(enabled[0], "Key") == Get<string>(variant, "Key"),
                        "popup exclusion and row toggle isolate " + round.Data.Name, log);
                    var entry = Get<ItemSpawnerID>(variant, "Entry");
                    foreach (bool immediate in new[] { false, true })
                    {
                        instant.Value = immediate;
                        if (immediate) spawner.BTN_SimpleMode_SwitchToTagSearch();
                        else spawner.BTN_SimpleMode_SwitchToSimpleMode();
                        yield return new WaitForSecondsRealtime(.35f);
                        var before = new HashSet<int>(Object.FindObjectsOfType<FVRPhysicalObject>().Select(o => o.GetInstanceID()));
                        Call(controller, "ClickAmmo", new object[] { null });
                        deadline = Time.realtimeSinceStartup + 60;
                        while (Get<bool>(controller, "busy") && Time.realtimeSinceStartup < deadline) yield return null;
                        Check(!Get<bool>(controller, "busy"), "ammo roll completes: " + round.Data.Name, log);
                        if (!immediate)
                        {
                            string expected = (string)AccessTools.Method(adapter, "SelectionId").Invoke(null, new object[] { entry });
                            var placeholder = Get<Component>(controller, "previewFallback");
                            bool previewVisible = spawner.IM_Detail.enabled && spawner.IM_Detail.sprite != null
                                || placeholder != null && placeholder.gameObject.activeInHierarchy;
                            Check(Get<string>(spawner, "m_selectedID") == expected && spawner.BTN_SpawnSelectedObject.activeSelf
                                && spawner.TXT_Title.text == entry.DisplayName && previewVisible
                                && spawner.IM_FavButtons.All(b => !b.gameObject.activeSelf)
                                && Object.FindObjectsOfType<FVRPhysicalObject>().All(o => before.Contains(o.GetInstanceID())),
                                "classic selection-only shows the exact round with preview, without spawning: " + round.Data.Name, log);
                            spawner.BTN_Details_Spawn();
                            deadline = Time.realtimeSinceStartup + 60;
                            while (Get<bool>(controller, "busy") && Time.realtimeSinceStartup < deadline) yield return null;
                        }
                        yield return null;
                        var added = Object.FindObjectsOfType<FVRPhysicalObject>().Where(o => !before.Contains(o.GetInstanceID())).ToArray();
                        spawned.AddRange(added.Select(o => o.gameObject));
                        Check(added.Length == 1 && added[0] is FVRFireArmRound
                            && ((FVRFireArmRound)added[0]).RoundType == round.Type && ((FVRFireArmRound)added[0]).RoundClass == round.Class
                            && added[0].ObjectWrapper != null && added[0].ObjectWrapper.ItemID == round.Data.ObjectID.ItemID,
                            (immediate ? "instant" : "native accept") + " spawns exactly one correct round: " + round.Data.Name, log);
                        var matchingChambers = actualGun.GetChambers().Where(c => c != null && c.RoundType == round.Type).ToArray();
                        Check(matchingChambers.Length > 0 && matchingChambers.All(c => c.IsFull && !c.IsSpent
                            && c.GetRound().RoundClass == round.Class), "held gun chambers filled with " + round.Data.Name, log);
                        foreach (var item in added) Object.Destroy(item.gameObject);
                        yield return null;
                    }
                }
                heldField.SetValue(hands[0], null); Object.Destroy(held); yield return null;
            }
        }
        finally
        {
            foreach (var pair in exclusions) Call(selection, "Set", pair.Key, pair.Value);
            instant.Value = priorInstant; autoFill.Value = priorFill; plugin.Config.SaveOnConfigSet = priorSave;
            for (int i = 0; i < hands.Length; ++i) { heldField.SetValue(hands[i], priorHeld[i]); hands[i].enabled = priorEnabled[i]; }
            GM.CurrentPlayerBody.Head.position = priorHead;
            foreach (var obj in spawned) if (obj != null) Object.Destroy(obj);
        }
        log("ALL REGISTERED AMMO MOD CHECKS PASSED: " + rounds.Count + " variants, popup toggles, selection-only + native accept, instant spawn; no ballistics test");
        var reset = ResetChecks(controller, plugin, directory, log);
        try { while (reset.MoveNext()) yield return reset.Current; }
        finally { (reset as IDisposable).Dispose(); }
    }

    private static IEnumerator ResetChecks(MonoBehaviour controller, BepInEx.BaseUnityPlugin plugin, string directory, Action<string> log)
    {
        var index = controller.GetType().Assembly.GetType("Gundomizer.PersistentConnectorIndex");
        var idle = AccessTools.Property(index, "Idle");
        float deadline = Time.realtimeSinceStartup + 180;
        while (!(bool)idle.GetValue(null, null) && Time.realtimeSinceStartup < deadline) yield return null;
        Check((bool)idle.GetValue(null, null), "index ready before reset", log);
        var objects = IM.OD.Values.Where(o => o != null).Distinct().ToArray();
        var loading = AccessTools.Field(typeof(AnvilAsset), "m_loadingState");
        var callbacks = objects.ToDictionary(o => o, o => loading.GetValue(o));
        var find = AccessTools.Method(index, "Find");
        var known = objects.First(o => find.Invoke(null, new object[] { o }) != null);
        var setting = (ConfigEntry<bool>)AccessTools.Field(plugin.GetType(), "ResetMetadataIndexing").GetValue(null);
        Check(!setting.Value, "reset setting defaults to off", log);
        setting.Value = true; Call(plugin, "Update");
        Check(!setting.Value && File.ReadAllText(plugin.Config.ConfigFilePath).Contains("Reset Metadata Indexing = false"),
            "reset executes once and persists its toggle back to false", log);
        Check(find.Invoke(null, new object[] { known }) == null, "reset immediately discards saved facts so unresolved items use the live fallback", log);
        deadline = Time.realtimeSinceStartup + 180;
        while (!(bool)idle.GetValue(null, null) && Time.realtimeSinceStartup < deadline) yield return null;
        string status = (string)AccessTools.Property(index, "Status").GetValue(null, null);
        log("RESET COMPLETE " + status);
        Check((bool)idle.GetValue(null, null) && status.Contains("cacheHits=0") && status.Contains("skipped=0")
            && find.Invoke(null, new object[] { known }) != null, "reset rebuilds the cache successfully", log);
        Check(objects.All(o => ReferenceEquals(callbacks[o], loading.GetValue(o))), "reset and rebuild request zero Unity prefabs", log);
        File.WriteAllText(Path.Combine(directory, "reset-index-status.txt"), status);
    }
}
