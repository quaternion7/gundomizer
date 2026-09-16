using System;
using System.Collections.Generic;
using System.Reflection;
using FistVR;
using HarmonyLib;
using UnityEngine;

namespace Gundomizer
{
    // Ammo stations/T&H accept registered rounds even when their author supplies no
    // ItemSpawnerID. Give those exact objects a session-only native details selection.
    internal static class AmmoSpawnerEntries
    {
        private static readonly FieldInfo Registry = AccessTools.Field(typeof(IM), "SpawnerIDDic");
        private static readonly Dictionary<string, ItemSpawnerID> owned = new Dictionary<string, ItemSpawnerID>(StringComparer.Ordinal);

        internal static ItemSpawnerID Get(FVRObject source, string caliber, string variant)
        {
            if (source == null || string.IsNullOrEmpty(source.ItemID) || source.Category != FVRObject.ObjectCategory.Cartridge
                || IM.OD == null || !IM.OD.ContainsKey(source.ItemID) || Registry == null || ManagerSingleton<IM>.Instance == null) return null;
            string id = "Gundomizer.Ammo/" + source.ItemID;
            var registry = (Dictionary<string, ItemSpawnerID>)Registry.GetValue(ManagerSingleton<IM>.Instance);
            ItemSpawnerID entry, existing;
            if (registry.TryGetValue(id, out existing))
            {
                // Never replace another mod's registration or a different round.
                if (!owned.TryGetValue(id, out entry) || entry != existing) return null;
            }
            else
            {
                entry = ScriptableObject.CreateInstance<ItemSpawnerID>();
                entry.name = id; entry.ItemID = id;
                entry.Category = ItemSpawnerID.EItemCategory.Cartridge;
                entry.SubCategory = ItemSpawnerID.ESubCategory.None;
                entry.Secondaries = new ItemSpawnerID[0];
                entry.Secondaries_ByStringID = new List<string>();
                entry.TutorialBlocks = new List<string>();
                entry.IsDisplayedInMainEntry = false;
                owned[id] = entry; registry.Add(id, entry);
            }
            entry.MainObject = source;
            entry.DisplayName = caliber + " — " + variant;
            return entry;
        }

        internal static bool Owns(ItemSpawnerID entry)
        { ItemSpawnerID existing; return entry != null && owned.TryGetValue(entry.ItemID, out existing) && entry == existing; }

        internal static void Clear()
        {
            var manager = ManagerSingleton<IM>.Instance;
            var registry = manager == null || Registry == null ? null : Registry.GetValue(manager) as Dictionary<string, ItemSpawnerID>;
            foreach (var pair in owned)
            {
                ItemSpawnerID existing;
                if (registry != null && registry.TryGetValue(pair.Key, out existing) && existing == pair.Value) registry.Remove(pair.Key);
                UnityEngine.Object.Destroy(pair.Value);
            }
            owned.Clear();
        }
    }
}
