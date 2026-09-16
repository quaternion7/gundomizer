using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Anvil;
using BepInEx;
using FistVR;
using Gundomizer.Indexing;
using HarmonyLib;
using UnityEngine;

namespace Gundomizer
{
    internal static class PersistentConnectorIndex
    {
        private static readonly FieldInfo Prefab = AccessTools.Field(typeof(AnvilAsset), "m_anvilPrefab");
        private static readonly Dictionary<string, string> paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> mismatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> submitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static IndexWorker worker;
        private static string streaming;
        internal static int Sweeps;
        internal static float MaxSliceMilliseconds;
        internal static string Status => worker == null ? "not started" : worker.Status;
        internal static bool Idle => worker != null && worker.Idle && Sweeps > 0;

        internal static void Start(MonoBehaviour host)
        {
            if (worker != null || Prefab == null || !Plugin.PersistentIndex.Value) return;
            streaming = Application.streamingAssetsPath;
            var types = new Dictionary<string, ConnectorKind>();
            // Reflection on type definitions only; no prefab construction or asset lookup.
            foreach (var type in typeof(FVRPhysicalObject).Assembly.GetTypes())
            {
                if (typeof(FVRFireArmAttachment).IsAssignableFrom(type)) types[type.FullName] = ConnectorKind.Attachment;
                else if (typeof(FVRFireArmMagazine).IsAssignableFrom(type)) types[type.FullName] = ConnectorKind.Magazine;
                else if (typeof(FVRFireArmClip).IsAssignableFrom(type)) types[type.FullName] = ConnectorKind.Clip;
            }
            worker = new IndexWorker(Path.Combine(Paths.CachePath, "Gundomizer"), typeof(FVRPhysicalObject).Module.ModuleVersionId.ToString(),
                Paths.PluginPath, types, message => Plugin.Log.LogInfo(message));
            host.StartCoroutine(Catalog());
            Plugin.Log.LogInfo("Persistent connector index started: background metadata reads, 8 MiB/s maximum I/O; unresolved items keep live checks.");
        }

        private static AssetID Address(FVRObject source)
        {
            var address = (AssetID)Prefab.GetValue(source);
            // Mirror OtherLoader's empty-bundle alias resolution without invoking its load patch.
            if (OtherLoaderBridge.Active && string.IsNullOrEmpty(address.Bundle) && IM.OD != null)
            {
                FVRObject original;
                if (IM.OD.TryGetValue(source.ItemID, out original) && original != null)
                    address.Bundle = ((AssetID)Prefab.GetValue(original)).Bundle;
            }
            return address;
        }

        internal static ConnectorFacts Find(FVRObject source)
        {
            if (worker == null || source == null) return null;
            var address = Address(source);
            string path;
            if (string.IsNullOrEmpty(address.Bundle) || !paths.TryGetValue(address.Bundle, out path)
                || string.IsNullOrEmpty(address.AssetName) || mismatches.Contains(path + "|" + address.AssetName)) return null;
            return worker.Find(path, address.AssetName);
        }

        internal static void Observe(FVRObject source, FVRPhysicalObject component)
        {
            var facts = Find(source);
            if (facts == null) return;
            bool matches = facts.Kind == ConnectorKind.Attachment && component is FVRFireArmAttachment attachment && facts.Connector == (int)attachment.Type
                || facts.Kind == ConnectorKind.Magazine && component is FVRFireArmMagazine magazine
                    && facts.Connector == (int)magazine.MagazineType && facts.Integrated == magazine.IsIntegrated
                || facts.Kind == ConnectorKind.Clip && component is FVRFireArmClip clip && facts.Connector == (int)clip.ClipType;
            if (!matches)
            {
                var address = Address(source);
                mismatches.Add(paths[address.Bundle] + "|" + address.AssetName);
                Plugin.Log.LogWarning("Serialized connector changed at runtime for " + source.ItemID + "; using live checks for this item.");
            }
        }

        private static IEnumerator Catalog()
        {
            string lastStatus = "";
            while (worker != null)
            {
                if (ManagerSingleton<IM>.Instance != null && IM.OD != null && IM.OD.Count > 0)
                {
                    var en = IM.OD.Values.GetEnumerator();
                    try
                    {
                        bool more = true;
                        while (more)
                        {
                            var slice = Stopwatch.StartNew();
                            int count = 0;
                            do
                            {
                                try { more = en.MoveNext(); } catch (InvalidOperationException) { more = false; }
                                if (!more) break;
                                var obj = en.Current;
                                if (obj == null) continue;
                                // These are the candidate kinds whose expensive prefab connector
                                // reads can be replaced safely. Firearms/speedloaders retain native metadata.
                                if (obj.Category != FVRObject.ObjectCategory.Attachment && obj.Category != FVRObject.ObjectCategory.Magazine
                                    && obj.Category != FVRObject.ObjectCategory.Clip) continue;
                                try
                                {
                                    var address = Address(obj);
                                    if (string.IsNullOrEmpty(address.Bundle) || string.IsNullOrEmpty(address.AssetName)) continue;
                                    string path = OtherLoaderBridge.BundlePath(address.Bundle) ?? Path.Combine(streaming, address.Bundle);
                                    paths[address.Bundle] = path;
                                    if (submitted.Add(path)) worker.Queue(path);
                                }
                                catch (Exception ex) { Plugin.Log.LogDebug("Index source unavailable: " + ex.Message); }
                            } while (++count < 32 && slice.Elapsed.TotalMilliseconds < 0.5);
                            MaxSliceMilliseconds = Math.Max(MaxSliceMilliseconds, (float)slice.Elapsed.TotalMilliseconds);
                            yield return null;
                        }
                    }
                    finally { en.Dispose(); }
                    ++Sweeps;
                }
                // Poll progress while the worker runs; rescan registry for late mod registration.
                for (int i = 0; i < (Sweeps == 0 ? 1 : 15); ++i)
                {
                    if (worker == null) yield break;
                    string status = worker.Status;
                    if (status != lastStatus) { lastStatus = status; Plugin.Log.LogInfo("Connector index: " + status); }
                    yield return new WaitForSecondsRealtime(2f);
                }
            }
        }

        internal static void Stop()
        {
            var previous = worker; worker = null;
            if (previous != null) previous.Dispose();
            paths.Clear(); mismatches.Clear(); submitted.Clear();
        }
    }
}
