// Opt-in measurements of the installed profile. Never included in the mod package.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using FistVR;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class MeasurementFrames : MonoBehaviour
{
    public int Count, Over50;
    public double Maximum;
    private long previous;
    public void Reset() { Count = Over50 = 0; Maximum = 0; previous = Stopwatch.GetTimestamp(); }
    private void Update()
    {
        long now = Stopwatch.GetTimestamp();
        if (previous != 0)
        {
            double ms = (now - previous) * 1000.0 / Stopwatch.Frequency;
            ++Count; if (ms > 50) ++Over50; Maximum = Math.Max(Maximum, ms);
        }
        previous = now;
    }
}

public static class ModdedMeasurements
{
    private static readonly System.Reflection.FieldInfo loading = AccessTools.Field(typeof(AnvilAsset), "m_loadingState");
    private static T Get<T>(object target, string name) { return (T)AccessTools.Field(target.GetType(), name).GetValue(target); }
    private static object Call(object target, string name, params object[] args) { return AccessTools.Method(target.GetType(), name).Invoke(target, args); }
    private static string N(double value) { return value.ToString("F2", CultureInfo.InvariantCulture); }
    private static string Clean(string text) { return (text ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '); }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCounters
    {
        public uint Size, PageFaultCount;
        public UIntPtr PeakWorkingSet, WorkingSet, PeakPagedPool, PagedPool, PeakNonPagedPool, NonPagedPool, Pagefile, PeakPagefile, PrivateUsage;
    }
    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, out MemoryCounters counters, uint size);
    private static long PrivateBytes()
    {
        // Unity 5's Mono reports zero for Process.PrivateMemorySize64 on this installation.
        MemoryCounters counters;
        if (!GetProcessMemoryInfo(new IntPtr(-1), out counters, (uint)Marshal.SizeOf(typeof(MemoryCounters))))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return (long)counters.PrivateUsage.ToUInt64();
    }
    private static int LoadedCount() { return IM.OD.Values.Count(o => { var c = loading.GetValue(o) as AnvilCallback<GameObject>; return c != null && c.IsCompleted; }); }
    private static void Snapshot(string label, MonoBehaviour controller, Action<string> log)
    {
        var index = controller.GetType().Assembly.GetType("Gundomizer.ConnectorIndex");
        log("MEMORY " + label + " privateMiB=" + N(PrivateBytes() / 1048576.0) + " managedMiB=" + N(GC.GetTotalMemory(false) / 1048576.0)
            + " completedCallbacks=" + LoadedCount() + " indexed=" + AccessTools.Property(index, "Count").GetValue(null, null)
            + " sweeps=" + AccessTools.Field(index, "Sweeps").GetValue(null)
            + " maxIndexSliceMs=" + AccessTools.Field(index, "MaxSliceMilliseconds").GetValue(null));
    }

    public static IEnumerator Run(ItemSpawnerV2 spawner, MonoBehaviour controller, string directory, Action<string> log)
    {
        var plugin = BepInEx.Bootstrap.Chainloader.PluginInfos["quaternion.gundomizer"].Instance;
        var instant = (ConfigEntry<bool>)AccessTools.Field(plugin.GetType(), "SpawnItemInstantly").GetValue(null);
        bool priorInstant = instant.Value, priorSave = plugin.Config.SaveOnConfigSet;
        var hands = GM.CurrentMovementManager.Hands;
        var heldField = AccessTools.Field(typeof(FVRViveHand), "m_currentInteractable");
        var priorHeld = hands.Select(h => h.CurrentInteractable).ToArray();
        var priorEnabled = hands.Select(h => h.enabled).ToArray();
        var priorHead = GM.CurrentPlayerBody.Head.position;
        var bridge = Get<object>(controller, "bridge");
        bool integrationOnly = File.Exists(Path.Combine(directory, "integration-only.txt"));
        var entries = ManagerSingleton<IM>.Instance.PageItemLists.Values.SelectMany(ids => ids).Distinct()
            .Where(IM.HasSpawnedID).Select(IM.GetSpawnerID).Where(e => e.MainObject != null).ToArray();
        File.WriteAllLines(Path.Combine(directory, "catalog.tsv"), new[] { "spawner\tname\tobject\tcategory\tmodded\tcallback" }.Concat(entries.Select(e =>
            string.Join("\t", new[] { Clean(e.ItemID), Clean(e.DisplayName), Clean(e.MainObject.ItemID), e.MainObject.Category.ToString(),
                e.MainObject.IsModContent.ToString(), (loading.GetValue(e.MainObject) != null).ToString() }))).ToArray());
        log("MODDED CATALOG objects=" + IM.OD.Count + " spawnerEntries=" + entries.Length + " modEntries=" + entries.Count(e => e.MainObject.IsModContent));
        var output = Path.Combine(directory, integrationOnly ? "integration-only.tsv" : "measurements.tsv");
        File.WriteAllText(output, "held\tsection\troll\tpool\tms\tprivateDeltaMiB\tcompletedDelta\tmaxFrameMs\tframesOver50\tstatus\tselected\n");
        var meter = spawner.gameObject.AddComponent<MeasurementFrames>();
        GameObject heldObject = null;
        plugin.Config.SaveOnConfigSet = false; instant.Value = false;
        try
        {
            foreach (var hand in hands) { hand.enabled = false; heldField.SetValue(hand, null); }
            GM.CurrentPlayerBody.Head.position = spawner.transform.position + Vector3.back;
            spawner.BTN_SetPageMode(3); spawner.BTN_SimpleMode_SwitchToTagSearch(); spawner.BTN_Tag_ClearSelectedTags();
            yield return null;
            Snapshot("range-before-idle", controller, log);
            var callbacks = IM.OD.Values.Distinct().ToDictionary(o => o, o => loading.GetValue(o));
            meter.Reset(); yield return new WaitForSecondsRealtime(integrationOnly ? .1f : 15f);
            log("IDLE callbacksChanged=" + callbacks.Count(p => loading.GetValue(p.Key) != p.Value)
                + " maxFrameMs=" + N(meter.Maximum) + " framesOver50=" + meter.Over50);
            Snapshot("range-after-idle", controller, log);

            var guns = new List<ItemSpawnerID> { IM.GetSpawnerID("SMGUziMini") };
            foreach (string key in new[] { "pm", "xm8", "f2000" })
            {
                var gun = entries.Where(e => e.MainObject.IsModContent && e.MainObject.Category == FVRObject.ObjectCategory.Firearm)
                    .OrderBy(e => e.ItemID, StringComparer.Ordinal).FirstOrDefault(e => (e.ItemID + " " + e.DisplayName).ToLowerInvariant().Contains(key));
                if (gun != null && !guns.Contains(gun)) guns.Add(gun); else log("NO MODDED FIREARM found for " + key);
            }
            foreach (var entry in guns)
            {
                if (integrationOnly && entry != guns[guns.Count - 1]) continue;
                var watch = Stopwatch.StartNew(); long memory = PrivateBytes();
                log("HELD LOAD begin " + entry.ItemID + " priorCallback=" + (loading.GetValue(entry.MainObject) != null));
                var request = entry.MainObject.GetGameObjectAsync();
                float deadline = Time.realtimeSinceStartup + 90;
                while (request.keepWaiting && Time.realtimeSinceStartup < deadline) yield return null;
                if (!request.IsCompleted || request.Result == null) throw new Exception("Held item load failed: " + entry.ItemID);
                var transaction = controller.GetType().Assembly.GetType("Gundomizer.SpawnTransaction");
                object[] spawn = { request.Result, spawner.transform.position + Vector3.up, Quaternion.identity, entry, null, null };
                if (!(bool)AccessTools.Method(transaction, "TrySpawn").Invoke(null, spawn))
                { log("HELD FAILED " + entry.ItemID + " " + spawn[5]); continue; }
                heldObject = (GameObject)spawn[4];
                foreach (var rb in heldObject.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
                var physical = heldObject.GetComponent<FVRPhysicalObject>();
                heldField.SetValue(hands[0], physical);
                yield return new WaitForSecondsRealtime(1f); // Let modular defaults initialize before checking wells/mounts.
                Call(controller, "RefreshHeldItem", new object[] { null });
                log("HELD READY " + entry.ItemID + " loadAndSettleMs=" + N(watch.Elapsed.TotalMilliseconds)
                    + " privateDeltaMiB=" + N((PrivateBytes() - memory) / 1048576.0)
                    + " wells=" + physical.GetComponentsInChildren<FVRFireArmReloadTriggerWell>(true).Length
                    + " mounts=" + physical.GetComponentsInChildren<FVRFireArmAttachmentMount>(true).Length);
                Snapshot("held-" + entry.ItemID, controller, log);

                foreach (int page in integrationOnly ? new int[0] : new[] { 2, 3 })
                {
                    spawner.BTN_SetPageMode(page); spawner.BTN_Tag_ClearSelectedTags();
                    if (page == 2) AddTag(spawner, TagType.Category, "Magazine");
                    var pool = (List<ItemSpawnerID>)Call(bridge, "CaptureSection");
                    log("SECTION " + entry.ItemID + " page=" + page + " pool=" + pool.Count + " modded=" + pool.Count(e => e.MainObject.IsModContent));
                    for (int roll = 0; roll < 3; ++roll)
                    {
                        yield return new WaitForSecondsRealtime(.35f);
                        Call(controller, "RefreshHeldItem", new object[] { null });
                        if (!(bool)Call(controller, "CanClick", true)) throw new Exception("Compatible button not ready for " + entry.ItemID);
                        int loadedBefore = LoadedCount(); memory = PrivateBytes(); meter.Reset(); watch.Reset(); watch.Start();
                        log("MEASURE BEGIN " + entry.ItemID + " page=" + page + " roll=" + roll);
                        Call(controller, "Click", true, null);
                        deadline = Time.realtimeSinceStartup + 180;
                        while (Get<bool>(controller, "busy") && Time.realtimeSinceStartup < deadline) yield return null;
                        watch.Stop();
                        if (Get<bool>(controller, "busy")) throw new Exception("Search exceeded the test harness's 180-second limit");
                        string selected = Get<string>(spawner, "m_selectedID"), status = Get<string>(controller, "status");
                        string row = string.Join("\t", new[] { entry.ItemID, page == 2 ? "ammo" : "attachments", roll.ToString(), pool.Count.ToString(),
                            N(watch.Elapsed.TotalMilliseconds), N((PrivateBytes() - memory) / 1048576.0), (LoadedCount() - loadedBefore).ToString(),
                            N(meter.Maximum), meter.Over50.ToString(), Clean(status), Clean(selected) });
                        File.AppendAllText(output, row + "\n"); log("MEASURE " + row);
                        if (status.StartsWith("Selected", StringComparison.Ordinal))
                        {
                            var chosen = (ItemSpawnerID)AccessTools.Method(controller.GetType().Assembly.GetType("Gundomizer.OtherLoaderBridge"), "Resolve").Invoke(null, new object[] { selected });
                            var callback = loading.GetValue(chosen.MainObject) as AnvilCallback<GameObject>;
                            bool fits = callback != null && callback.IsCompleted && (bool)AccessTools.Method(controller.GetType().Assembly.GetType("Gundomizer.Compatibility"), "Matches")
                                .Invoke(null, new object[] { physical, callback.Result });
                            if (!fits || !pool.Contains(chosen)) throw new Exception("Selected item no longer matches its held target/scope: " + selected);
                            log("PASS measured selection matches held target and section: " + selected);
                        }
                    }
                }
                Snapshot("after-searches-" + entry.ItemID, controller, log);
                if (entry == guns[guns.Count - 1])
                {
                    var integration = OtherLoaderChecks.Run(spawner, controller, log);
                    try { while (integration.MoveNext()) yield return integration.Current; }
                    finally { (integration as IDisposable).Dispose(); }
                }
                heldField.SetValue(hands[0], null); Object.Destroy(heldObject); heldObject = null; yield return null;
            }
            Snapshot("finished", controller, log);
            log("ALL MODDED MEASUREMENTS COMPLETED");
        }
        finally
        {
            Call(controller, "CancelRoll");
            instant.Value = priorInstant; plugin.Config.SaveOnConfigSet = priorSave;
            GM.CurrentPlayerBody.Head.position = priorHead;
            for (int i = 0; i < hands.Length; ++i) { heldField.SetValue(hands[i], priorHeld[i]); hands[i].enabled = priorEnabled[i]; }
            if (heldObject != null) Object.Destroy(heldObject);
            Object.Destroy(meter);
            log("Restored modded-measurement settings and hands");
        }
    }

    private static void AddTag(ItemSpawnerV2 spawner, TagType type, string value)
    {
        var page = Get<ItemSpawnerV2.PageMode>(spawner, "PMode");
        var categories = Get<Dictionary<ItemSpawnerV2.PageMode, List<TagType>>>(spawner, "m_pageModeTagTypes")[page];
        spawner.BTN_Tag_SelectCategory(categories.IndexOf(type));
        int index = ManagerSingleton<IM>.Instance.MetaTagListByPageMode[page][type].IndexOf(value);
        if (index < 0) throw new Exception("Missing measurement tag " + value);
        for (int p = 0; p < index / 20; ++p) spawner.BTN_Tag_PageNext();
        spawner.BTN_Tag_AddTagEntry(index % 20);
    }
}
