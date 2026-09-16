using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FistVR;
using HarmonyLib;

namespace Gundomizer
{
    // OtherLoaderPatched registers native ItemSpawnerIDs. Bind its optional API only
    // when installed; neither loader's assembly is a compile-time dependency.
    internal sealed class PatchedOtherLoaderBridge
    {
        private readonly Type controllerType, folderType, categoryType;
        private readonly FieldInfo selectedFolder, entries, spawnWith, bundles;
        private readonly PropertyInfo customActive, objectId;
        private readonly MethodInfo children, categoryPath, isUnlocked, filterClassic;
        internal readonly MethodInfo SpawnHandler;

        internal PatchedOtherLoaderBridge(Assembly assembly)
        {
            controllerType = assembly.GetType("OtherLoader.ItemSpawner.CustomCategories.CustomCategoriesController", true);
            folderType = assembly.GetType("OtherLoader.ItemSpawner.CustomCategories.SpawnerTile+FolderTile", true);
            categoryType = assembly.GetType("OtherLoader.ItemSpawner.CustomCategories.SpawnerTile+FolderTile+Category", true);
            var objectType = assembly.GetType("OtherLoader.ItemSpawner.CustomCategories.SpawnerTile+ObjectTile", true);
            selectedFolder = Field(controllerType, "CurrentSelectedFolderTile");
            customActive = Property(controllerType, "IsCustomCategoryModeActive");
            children = Method(folderType, "GetFilteredCopyOfChildren", Type.EmptyTypes);
            categoryPath = Method(categoryType, "ConstructEntryPath", Type.EmptyTypes);
            objectId = Property(objectType, "ItemSpawnerID");
            var loader = assembly.GetType("OtherLoader.OtherLoader", true);
            entries = Field(loader, "SpawnerEntriesByID");
            bundles = Field(loader, "ManagedBundles");
            spawnWith = Field(assembly.GetType("OtherLoader.ItemSpawnerEntry", true), "SpawnWithIDs");
            isUnlocked = Method(assembly.GetType("OtherLoader.Unlockathon.UnlockathonInventoryManager", true),
                "IsItemSpawnerIDUnlocked", new[] { typeof(string) });
            filterClassic = Method(assembly.GetType("OtherLoader.ItemSpawner.VanillaCategories.SpawnerIDSorter", true),
                "FilterAndSortItemsSimpleMode", new[] { typeof(List<string>) });
            SpawnHandler = Method(assembly.GetType("OtherLoader.ItemSpawner.Patches.ItemSpawnerV2SpawningPatcher", true),
                "BTN_Details_Spawn_Prefix", new[] { typeof(ItemSpawnerV2) });
        }

        private static FieldInfo Field(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
        private static PropertyInfo Property(Type type, string name) => AccessTools.Property(type, name) ?? throw new MissingMemberException(type.FullName, name);
        private static MethodInfo Method(Type type, string name, Type[] args) => AccessTools.Method(type, name, args) ?? throw new MissingMethodException(type.FullName, name);

        private object Folder(ItemSpawnerV2 spawner)
        {
            var controller = spawner.GetComponent(controllerType);
            return controller != null && (bool)customActive.GetValue(controller, null) ? selectedFolder.GetValue(controller) : null;
        }

        internal string Path(ItemSpawnerV2 spawner)
        {
            var folder = Folder(spawner);
            if (folder == null) return null;
            return "OtherLoaderPatched:/" + (categoryType.IsInstanceOfType(folder) ? (string)categoryPath.Invoke(folder, null) : "");
        }

        internal List<string> ClassicIds(ItemSpawnerV2 spawner)
        {
            var result = new List<string>();
            var root = Folder(spawner);
            if (root == null) return result;
            var pending = new Stack<object>();
            var visited = new HashSet<object>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var tile = pending.Pop();
                if (tile == null || !visited.Add(tile)) continue;
                if (folderType.IsInstanceOfType(tile))
                    foreach (var child in (IEnumerable)children.Invoke(tile, null)) pending.Push(child);
                else if (objectId.DeclaringType.IsInstanceOfType(tile))
                {
                    var id = (string)objectId.GetValue(tile, null);
                    if (!string.IsNullOrEmpty(id)) result.Add(id);
                }
            }
            return result;
        }

        internal void FilterClassicOverview(List<string> ids) => filterClassic.Invoke(null, new object[] { ids });

        internal ItemSpawnerID Resolve(string id)
        {
            if (IM.HasSpawnedID(id)) return IM.GetSpawnerID(id);
            // Ammo metadata may supply an object ID rather than its native spawner ID.
            FVRObject source;
            if (!IM.OD.TryGetValue(id, out source) || source == null || string.IsNullOrEmpty(source.SpawnedFromId)
                || !IM.HasSpawnedID(source.SpawnedFromId)) return null;
            var entry = IM.GetSpawnerID(source.SpawnedFromId);
            return entry.MainObject != null && entry.MainObject.ItemID == source.ItemID ? entry : null;
        }

        internal bool Available(ItemSpawnerID entry) => GM.Rewards != null && GM.Rewards.RewardUnlocks.IsRewardUnlocked(entry)
            && (bool)isUnlocked.Invoke(null, new object[] { entry.ItemID });

        internal string BundlePath(string bundle)
        {
            var map = bundles.GetValue(null) as IDictionary;
            return map != null && map.Contains(bundle) ? map[bundle] as string : null;
        }

        internal List<FVRObject> SpawnSources(ItemSpawnerID entry)
        {
            var result = new List<FVRObject> { entry.MainObject };
            var map = (IDictionary)entries.GetValue(null);
            if (map.Contains(entry.ItemID))
            {
                var ids = spawnWith.GetValue(map[entry.ItemID]) as IEnumerable;
                if (ids != null) foreach (string id in ids)
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
