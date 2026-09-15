using System;
using System.Collections.Generic;
using System.Reflection;
using FistVR;
using HarmonyLib;

namespace Gundomizer
{
    internal sealed class SpawnerBridge
    {
        private static readonly FieldInfo Page = Field("PMode");
        private static readonly FieldInfo Search = Field("SMode");
        private static readonly FieldInfo Levels = Field("m_displayLevel");
        private static readonly FieldInfo Group = Field("m_curTagGroup");
        private static readonly FieldInfo Working = Field("WorkingItemIDs");
        private static readonly FieldInfo SmallPosition = Field("m_curSmallPos");
        private static readonly MethodInfo Redraw = Method("RedrawSimpleCanvas");
        private static readonly MethodInfo Queue = Method("AddToSelectionQueue");
        private static readonly MethodInfo Select = Method("SetSelectedID");
        private static readonly MethodInfo Details = Method("RedrawDetailsCanvas");
        private static readonly MethodInfo CountGun = Method("IncrementSpawnedGuns");
        private readonly ItemSpawnerV2 spawner;

        internal SpawnerBridge(ItemSpawnerV2 spawner) { this.spawner = spawner; }
        private static FieldInfo Field(string name) => AccessTools.Field(typeof(ItemSpawnerV2), name)
            ?? throw new MissingFieldException(typeof(ItemSpawnerV2).FullName, name);
        private static MethodInfo Method(string name) => AccessTools.Method(typeof(ItemSpawnerV2), name)
            ?? throw new MissingMethodException(typeof(ItemSpawnerV2).FullName, name);
        internal static void Validate() { if (Page == null) throw new InvalidOperationException(); }

        internal ItemSpawnerV2.PageMode PageMode => (ItemSpawnerV2.PageMode)Page.GetValue(spawner);
        internal bool IsClassicSection
        {
            get
            {
                var page = PageMode;
                return (ItemSpawnerV2.SearchMode)Search.GetValue(spawner) == ItemSpawnerV2.SearchMode.Simple
                    && page >= ItemSpawnerV2.PageMode.Firearms && page <= ItemSpawnerV2.PageMode.ToolsToys;
            }
        }

        internal string ContextKey
        {
            get
            {
                if (!IsClassicSection) return string.Empty;
                var page = PageMode;
                var levels = (Dictionary<ItemSpawnerV2.PageMode, ItemSpawnerV2.SimpleDisplayLevel>)Levels.GetValue(spawner);
                var groups = (Dictionary<ItemSpawnerV2.PageMode, ItemSpawnerCategoryDefinitionsV2.SpawnerPage.SpawnerTagGroup>)Group.GetValue(spawner);
                bool overview = levels[page] == ItemSpawnerV2.SimpleDisplayLevel.Subcategory;
                var group = groups[page];
                return page + ":" + levels[page] + (overview || group == null ? "" : ":" + group.TagT + ":" + group.Tag);
            }
        }

        internal List<ItemSpawnerID> CaptureSection()
        {
            var result = new List<ItemSpawnerID>();
            if (!IsClassicSection) return result;
            // Native redraw computes the current subcategory across ALL pages. Overview leaves
            // WorkingItemIDs stale, so use the page registry directly at that level.
            Redraw.Invoke(spawner, null);
            var page = PageMode;
            var levels = (Dictionary<ItemSpawnerV2.PageMode, ItemSpawnerV2.SimpleDisplayLevel>)Levels.GetValue(spawner);
            List<string> pageIds;
            if (!ManagerSingleton<IM>.Instance.PageItemLists.TryGetValue(page, out pageIds)) return result;
            var ids = SelectionPolicy.SectionIds(levels[page] == ItemSpawnerV2.SimpleDisplayLevel.Subcategory,
                pageIds, (List<string>)Working.GetValue(spawner));
            foreach (var id in ids)
            {
                if (!IM.HasSpawnedID(id)) continue;
                var entry = IM.GetSpawnerID(id);
                if (IsAvailable(entry)) result.Add(entry);
            }
            return result;
        }

        internal static bool IsAvailable(ItemSpawnerID entry) => entry != null && entry.MainObject != null
            && GM.Rewards != null && GM.Rewards.RewardUnlocks.IsRewardUnlocked(entry);

        internal void SelectEntry(ItemSpawnerID entry)
        {
            Queue.Invoke(spawner, new object[] { entry.ItemID });
            Select.Invoke(spawner, new object[] { entry.ItemID });
            Details.Invoke(spawner, null);
        }

        internal UnityEngine.Transform SpawnPoint(ItemSpawnerID entry)
        {
            if (entry.UsesHugeSpawnPad) return spawner.SpawnPoint_Huge;
            if (entry.UsesLargeSpawnPad) return spawner.SpawnPoint_Large;
            if (spawner.SpawnPoints_Small == null || spawner.SpawnPoints_Small.Count == 0) return null;
            int index = (int)SmallPosition.GetValue(spawner);
            if (index < 0 || index >= spawner.SpawnPoints_Small.Count) index = 0;
            return spawner.SpawnPoints_Small[index];
        }

        internal void RecordSpawn(ItemSpawnerID entry)
        {
            int count = spawner.SpawnPoints_Small == null ? 0 : spawner.SpawnPoints_Small.Count;
            if (count > 0) SmallPosition.SetValue(spawner, ((int)SmallPosition.GetValue(spawner) + 1) % count);
            if (entry.MainObject.Category == FVRObject.ObjectCategory.Firearm) CountGun.Invoke(spawner, null);
        }
    }
}
