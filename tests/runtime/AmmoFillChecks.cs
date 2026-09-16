// Opt-in real-game refill tests; excluded from the distributable plugin.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using FistVR;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

public static class AmmoFillChecks
{
    private static T Get<T>(object target, string name) => (T)AccessTools.Field(target.GetType(), name).GetValue(target);
    private static object Call(object target, string name, params object[] args) => AccessTools.Method(target.GetType(), name).Invoke(target, args);
    private static void Check(bool ok, string label, Action<string> log)
    { if (!ok) throw new Exception(label); log("PASS " + label); }

    private static IEnumerator Load(FVRObject source, List<GameObject> cleanup, Action<GameObject> ready, Action<string> log)
    {
        var request = source.GetGameObjectAsync();
        float end = Time.realtimeSinceStartup + 90;
        while (!request.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
        Check(request.IsCompleted && request.Result != null, "fill fixture loaded: " + source.ItemID, log);
        var item = Object.Instantiate(request.Result, GM.CurrentPlayerBody.Head.position + Vector3.up * 2, Quaternion.identity);
        cleanup.Add(item);
        foreach (var rb in item.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
        yield return null;
        ready(item);
    }

    private static IEnumerator Wait(MonoBehaviour controller, Action<string> log)
    {
        float end = Time.realtimeSinceStartup + 60;
        while (Get<bool>(controller, "busy") && Time.realtimeSinceStartup < end) yield return null;
        Check(!Get<bool>(controller, "busy"), "ammo fill roll completed", log);
    }

    private static bool Full(FVRFireArmMagazine mag, FireArmRoundClass cls) => mag.m_numRounds == mag.m_capacity
        && mag.LoadedRounds.Take(mag.m_numRounds).All(r => r != null && r.LR_Class == cls);

    public static IEnumerator Run(ItemSpawnerV2 spawner, MonoBehaviour controller, string directory, Action<string> log)
    {
        var plugin = BepInEx.Bootstrap.Chainloader.PluginInfos["quaternion.gundomizer"].Instance;
        var instant = (ConfigEntry<bool>)AccessTools.Field(plugin.GetType(), "SpawnItemInstantly").GetValue(null);
        var autoFill = (ConfigEntry<bool>)AccessTools.Field(plugin.GetType(), "AutoFillHeldItem").GetValue(null);
        Check((bool)autoFill.DefaultValue, "Auto Fill Held Item defaults to ON", log);
        bool oldInstant = instant.Value, oldFill = autoFill.Value, oldSave = plugin.Config.SaveOnConfigSet;
        var hands = GM.CurrentMovementManager.Hands;
        var heldField = AccessTools.Field(typeof(FVRViveHand), "m_currentInteractable");
        var oldHeld = hands.Select(h => h.CurrentInteractable).ToArray();
        var oldEnabled = hands.Select(h => h.enabled).ToArray();
        var oldHead = GM.CurrentPlayerBody.Head.position;
        var cleanup = new List<GameObject>();
        var selectionType = controller.GetType().Assembly.GetType("Gundomizer.AmmoSelection");
        var selection = AccessTools.Field(selectionType, "Shared").GetValue(null);
        var exclusions = new Dictionary<string, bool>();
        var panel = Get<object>(controller, "ammoPanel");
        var fillMethod = AccessTools.Method(controller.GetType().Assembly.GetType("Gundomizer.AmmoFill"), "Apply");
        plugin.Config.SaveOnConfigSet = false;
        try
        {
            foreach (var hand in hands) { hand.enabled = false; heldField.SetValue(hand, null); }
            GM.CurrentPlayerBody.Head.position = spawner.transform.position + Vector3.back;
            spawner.BTN_SetPageMode(2); spawner.BTN_SimpleMode_SwitchToSimpleMode();
            FVRFireArm firearm = null;
            var routine = Load(IM.GetSpawnerID("SMGUziMini").MainObject, cleanup, o => firearm = o.GetComponent<FVRFireArm>(), log);
            while (routine.MoveNext()) yield return routine.Current;
            FVRFireArmMagazine magazine = null;
            routine = Load(firearm.ObjectWrapper.CompatibleMagazines[0], cleanup, o => magazine = o.GetComponent<FVRFireArmMagazine>(), log);
            while (routine.MoveNext()) yield return routine.Current;
            heldField.SetValue(hands[0], magazine);
            Call(controller, "RefreshHeldItem", new object[] { null });
            Call(panel, "Refresh", magazine, true);
            var choices = Get<IList>(panel, "variants").Cast<object>().ToList();
            foreach (var v in choices) exclusions[Get<string>(v, "Key")] = (bool)Call(selection, "Includes", Get<string>(v, "Key"));
            var chosen = choices.First(v => Get<FireArmRoundClass>(v, "Class") != FireArmRoundClass.FMJ);
            int index = choices.IndexOf(chosen);
            Call(panel, "SetAll", false);
            AccessTools.Field(panel.GetType(), "page").SetValue(panel, index / 7);
            Call(panel, "Toggle", index % 7);
            var cls = Get<FireArmRoundClass>(chosen, "Class");
            FVRFireArmRound loose = null;
            foreach (int scenario in new[] { 0, 1, 2 })
            {
                autoFill.Value = scenario != 1;
                instant.Value = scenario != 2;
                magazine.ReloadMagWithTypeUpToAmount(FireArmRoundClass.FMJ, 2);
                yield return new WaitForSecondsRealtime(.35f);
                var before = new HashSet<int>(Object.FindObjectsOfType<FVRPhysicalObject>().Select(o => o.GetInstanceID()));
                Call(controller, "ClickAmmo", new object[] { null });
                routine = Wait(controller, log); while (routine.MoveNext()) yield return routine.Current;
                if (scenario == 2)
                {
                    Check(magazine.m_numRounds == 2 && magazine.LoadedRounds.Take(2).All(r => r.LR_Class == FireArmRoundClass.FMJ)
                        && Object.FindObjectsOfType<FVRPhysicalObject>().All(o => before.Contains(o.GetInstanceID())),
                        "selection-only neither fills nor spawns", log);
                    spawner.BTN_Details_Spawn();
                    routine = Wait(controller, log); while (routine.MoveNext()) yield return routine.Current;
                }
                yield return null;
                var added = Object.FindObjectsOfType<FVRPhysicalObject>().Where(o => !before.Contains(o.GetInstanceID())).ToArray();
                cleanup.AddRange(added.Select(o => o.gameObject));
                Check(added.Length == 1 && added[0] is FVRFireArmRound && ((FVRFireArmRound)added[0]).RoundClass == cls,
                    "one correct loose round remains after scenario " + scenario, log);
                loose = (FVRFireArmRound)added[0];
                Check(scenario == 1 ? magazine.m_numRounds == 2 && magazine.LoadedRounds.Take(2).All(r => r.LR_Class == FireArmRoundClass.FMJ)
                    : Full(magazine, cls), scenario == 1 ? "option OFF preserves existing rounds and empty capacity"
                    : "option ON replaces old rounds and fills capacity (scenario " + scenario + ")", log);
            }
            autoFill.Value = true;
            // Native methods can also fill already-full magazines and a gun with an inserted one.
            magazine.ReloadMagWithType(FireArmRoundClass.FMJ);
            fillMethod.Invoke(null, new object[] { magazine, loose });
            Check(Full(magazine, cls), "already-full magazine is converted to the rolled class", log);
            magazine.Load(firearm);
            foreach (var chamber in firearm.GetChambers()) chamber.SetRound(null, false);
            magazine.ReloadMagWithTypeUpToAmount(FireArmRoundClass.FMJ, 1);
            var result = fillMethod.Invoke(null, new object[] { firearm, loose });
            Check((int)AccessTools.Property(result.GetType(), "Failed").GetValue(result, null) == 0
                && Full(magazine, cls) && firearm.GetChambers().Where(c => c.RoundType == loose.RoundType)
                .All(c => c.IsFull && !c.IsSpent && c.GetRound().RoundClass == cls),
                "held firearm fills its inserted magazine and matching chambers", log);
            // A target changed during loading cannot receive the post-spawn refill.
            magazine.ReloadMagWithTypeUpToAmount(FireArmRoundClass.FMJ, 1);
            heldField.SetValue(hands[0], firearm);
            Call(controller, "FillSpawnedAmmo", loose.gameObject, magazine, null);
            Check(magazine.m_numRounds == 1 && magazine.LoadedRounds[0].LR_Class == FireArmRoundClass.FMJ,
                "post-spawn guard skips a target no longer held", log);

            // Integrated tube and chamber with modded 12-gauge ammo, plus caliber rejection.
            var shotgunSource = IM.OD.Values.First(o => o != null && o.ItemID == "1887FullLength");
            FVRFireArm shotgun = null;
            routine = Load(shotgunSource, cleanup, o => shotgun = o.GetComponent<FVRFireArm>(), log);
            while (routine.MoveNext()) yield return routine.Current;
            var custom = AM.STypeDic[shotgun.RoundType].Values.First(d => d.ObjectID != null
                && d.ObjectID.ItemID.StartsWith("andr3a.", StringComparison.OrdinalIgnoreCase));
            FVRFireArmRound modRound = null;
            routine = Load(custom.ObjectID, cleanup, o => modRound = o.GetComponent<FVRFireArmRound>(), log);
            while (routine.MoveNext()) yield return routine.Current;
            fillMethod.Invoke(null, new object[] { firearm, modRound });
            Check(magazine.m_numRounds == 1 && magazine.LoadedRounds[0].LR_Class == FireArmRoundClass.FMJ,
                "different caliber cannot refill the weapon", log);
            result = fillMethod.Invoke(null, new object[] { shotgun, modRound });
            Check((int)AccessTools.Property(result.GetType(), "Failed").GetValue(result, null) == 0
                && shotgun.Magazine != null && Full(shotgun.Magazine, modRound.RoundClass)
                && shotgun.GetChambers().Where(c => c.RoundType == modRound.RoundType)
                    .All(c => c.IsFull && c.GetRound().RoundClass == modRound.RoundClass),
                "custom A3A ammo fills integrated magazine and chamber", log);
            Check(loose != null && modRound != null, "refilling never consumes either loose round", log);
            var wrist = Resources.FindObjectsOfTypeAll<FVRWristMenuSection_Spawn>().First(w => w.ToolBoxPrefab != null);
            var box = wrist.ToolBoxPrefab.GetComponent<FistVR.ToolGuns.ToolBox>();
            var tabletPrefab = box.Drawers.SelectMany(d => d.Bays).Select(b => b.ActualPrefab)
                .First(p => p != null && p.GetComponent<FistVR.ToolGuns.TP_Palette>() != null);
            var tablet = Object.Instantiate(tabletPrefab, spawner.transform.position + Vector3.up, Quaternion.identity);
            cleanup.Add(tablet);
            foreach (var rb in tablet.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
            yield return null;
            var portable = tablet.GetComponent<FistVR.ToolGuns.TP_Palette>().ItemSpawner;
            var portableController = portable.GetComponent(controller.GetType()) as MonoBehaviour;
            Check(portableController != null && portableController.enabled, "Gundomizer initializes on the actual toolbox tablet", log);
            log("TABLET portable=" + portable.IsInPortableMode + " smallPads=" + portable.SpawnPoints_Small.Count
                + " largePad=" + (portable.SpawnPoint_Large != null) + " hugePad=" + (portable.SpawnPoint_Huge != null));
            portable.BTN_SetPageMode(2);
            var tabletPanel = Get<object>(portableController, "ammoPanel");
            heldField.SetValue(hands[0], firearm);
            Call(portableController, "RefreshHeldItem", new object[] { null });
            Call(tabletPanel, "Refresh", firearm, true);
            foreach (bool immediate in new[] { true, false })
            {
                instant.Value = immediate;
                magazine.ReloadMagWithTypeUpToAmount(FireArmRoundClass.FMJ, 1);
                if (immediate) portable.BTN_SimpleMode_SwitchToSimpleMode(); else portable.BTN_SimpleMode_SwitchToTagSearch();
                yield return new WaitForSecondsRealtime(.35f);
                var before = new HashSet<int>(Object.FindObjectsOfType<FVRPhysicalObject>().Select(o => o.GetInstanceID()));
                Call(portableController, "ClickAmmo", new object[] { null });
                routine = Wait(portableController, log); while (routine.MoveNext()) yield return routine.Current;
                var placement = portable.transform.position + Vector3.up * .5f;
                if (!immediate)
                {
                    log("TABLET selection spawnButton=" + portable.BTN_SpawnSelectedObject.activeSelf + " rounds=" + magazine.m_numRounds);
                    Check(magazine.m_numRounds == 1 && Object.FindObjectsOfType<FVRPhysicalObject>().All(o => before.Contains(o.GetInstanceID())),
                        "tablet selection waits for acceptance without filling or spawning", log);
                    int pad = Get<int>(portable, "m_curSmallPos");
                    portable.ExternalSpawnFromLaserToPoint(placement);
                    routine = Wait(portableController, log); while (routine.MoveNext()) yield return routine.Current;
                    Check(Get<int>(portable, "m_curSmallPos") == pad, "stylus placement preserves the small pad cursor", log);
                }
                var added = Object.FindObjectsOfType<FVRPhysicalObject>().Where(o => !before.Contains(o.GetInstanceID())).ToArray();
                cleanup.AddRange(added.Select(o => o.gameObject));
                Check(added.Length == 1 && added[0] is FVRFireArmRound && ((FVRFireArmRound)added[0]).RoundClass == cls
                    && Full(magazine, cls), "tablet " + (immediate ? "instant roll" : "stylus acceptance") + " spawns exactly one round and fills held gun", log);
                if (!immediate) Check((added[0].transform.position - placement).sqrMagnitude < .01f, "tablet spawns at the supplied ray hit point", log);
            }
            log("AMMO FILL CHECKS COMPLETE");
        }
        finally
        {
            instant.Value = oldInstant; autoFill.Value = oldFill; plugin.Config.SaveOnConfigSet = oldSave;
            foreach (var pair in exclusions) Call(selection, "Set", pair.Key, pair.Value);
            for (int i = 0; i < hands.Length; ++i) { heldField.SetValue(hands[i], oldHeld[i]); hands[i].enabled = oldEnabled[i]; }
            GM.CurrentPlayerBody.Head.position = oldHead;
            foreach (var obj in cleanup) if (obj != null) Object.Destroy(obj);
        }
    }
}
