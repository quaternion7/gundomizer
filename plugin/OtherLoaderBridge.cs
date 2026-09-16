using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FistVR;
using HarmonyLib;
using UnityEngine;

namespace Gundomizer
{
    // Optional metadata-only integration. OtherLoader replaces the browser's ID namespace
    // and classic tree; loading its assets is never necessary to resolve those entries.
    internal static class OtherLoaderBridge
    {
        private static Type loader;
        private static Type dataType;
        private static FieldInfo entries, paths, legacyIds, unlocks, currentPath, nodeEntry, children, visible, objectId, spawnWith;
        private static MethodInfo isUnlocked;
        private static FieldInfo bundles;
        internal static bool Active => loader != null;
        internal static MethodInfo SpawnHandler { get; private set; }

        internal static void Initialize()
        {
            BepInEx.PluginInfo info;
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("h3vr.otherloader", out info)) return;
            var assembly = info.Instance.GetType().Assembly;
            var type = assembly.GetType("OtherLoader.OtherLoader", true);
            dataType = assembly.GetType("OtherLoader.ItemSpawnerData", true);
            var node = assembly.GetType("OtherLoader.EntryNode", true);
            var entry = assembly.GetType("OtherLoader.ItemSpawnerEntry", true);
            entries = Required(type, "SpawnerEntriesByID"); paths = Required(type, "SpawnerEntriesByPath");
            legacyIds = Required(type, "SpawnerIDsByMainObject"); unlocks = Required(type, "UnlockSaveData");
            currentPath = Required(dataType, "CurrentPath"); nodeEntry = Required(node, "entry"); children = Required(node, "childNodes");
            visible = Required(entry, "IsDisplayedInMainEntry"); objectId = Required(entry, "MainObjectID"); spawnWith = Required(entry, "SpawnWithIDs");
            isUnlocked = AccessTools.Method(unlocks.FieldType, "IsItemUnlocked", new[] { typeof(string) })
                ?? throw new MissingMethodException("OtherLoader.IsItemUnlocked");
            SpawnHandler = AccessTools.Method(assembly.GetType("OtherLoader.Patches.ItemSpawningPatches", true), "SpawnItemDetails")
                ?? throw new MissingMethodException("OtherLoader.SpawnItemDetails");
            loader = type;
            bundles = AccessTools.Field(type, "ManagedBundles");
            Plugin.Log.LogInfo("Using OtherLoader's browser IDs, category tree and unlock state.");
        }

        private static FieldInfo Required(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
        private static IDictionary Entries => (IDictionary)entries.GetValue(null);
        internal static string BundlePath(string bundle)
        {
            var map = bundles == null ? null : bundles.GetValue(null) as IDictionary;
            return map != null && map.Contains(bundle) ? map[bundle] as string : null;
        }
        internal static string Path(ItemSpawnerV2 spawner)
        {
            if (!Active) return null;
            var data = spawner.GetComponent(dataType);
            return data == null ? null : (string)currentPath.GetValue(data);
        }

        internal static ItemSpawnerID Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (Active)
            {
                var map = (IDictionary)legacyIds.GetValue(null);
                if (map.Contains(id)) return map[id] as ItemSpawnerID;
                FVRObject obj;
                if (IM.OD.TryGetValue(id, out obj) && obj != null && !string.IsNullOrEmpty(obj.SpawnedFromId) && IM.HasSpawnedID(obj.SpawnedFromId))
                {
                    var resolved = IM.GetSpawnerID(obj.SpawnedFromId);
                    if (resolved.MainObject == obj) return resolved;
                }
            }
            return IM.HasSpawnedID(id) ? IM.GetSpawnerID(id) : null;
        }

        internal static string SelectionId(ItemSpawnerID entry) => Active && Entries.Contains(entry.MainObject.ItemID) ? entry.MainObject.ItemID : entry.ItemID;
        internal static bool Available(ItemSpawnerID entry)
        {
            if (entry == null || entry.MainObject == null) return false;
            if (Active && Entries.Contains(entry.MainObject.ItemID))
            {
                var save = unlocks.GetValue(null);
                return save != null && (bool)isUnlocked.Invoke(save, new object[] { entry.MainObject.ItemID });
            }
            return GM.Rewards != null && GM.Rewards.RewardUnlocks.IsRewardUnlocked(entry);
        }

        internal static List<string> ClassicIds(ItemSpawnerV2 spawner)
        {
            var result = new List<string>();
            string path = Path(spawner);
            var tree = (IDictionary)paths.GetValue(null);
            if (path == null || !tree.Contains(path)) return result;
            var pending = new Stack<object>();
            foreach (var child in (IEnumerable)children.GetValue(tree[path])) pending.Push(child);
            var visited = new HashSet<object>();
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (!visited.Add(node)) continue;
                var entry = nodeEntry.GetValue(node);
                if (entry == null || !(bool)visible.GetValue(entry)) continue;
                var descendants = (IList)children.GetValue(node);
                if (descendants.Count == 0)
                {
                    string id = (string)objectId.GetValue(entry);
                    if (!string.IsNullOrEmpty(id)) result.Add(id);
                }
                else foreach (var child in descendants) pending.Push(child);
            }
            return result;
        }

        internal static List<FVRObject> SpawnSources(ItemSpawnerID entry)
        {
            var result = new List<FVRObject> { entry.MainObject };
            if (Active && Entries.Contains(entry.MainObject.ItemID))
            {
                foreach (string id in (IEnumerable)spawnWith.GetValue(Entries[entry.MainObject.ItemID]))
                {
                    FVRObject source;
                    if (!IM.OD.TryGetValue(id, out source) || source == null) throw new InvalidOperationException("Missing bundled item " + id);
                    result.Add(source);
                }
            }
            else if (entry.SecondObject != null) result.Add(entry.SecondObject);
            return result;
        }
    }
}
