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
        private static readonly FieldInfo SelectedTags = Field("m_selectedTags");
        private static readonly FieldInfo Working = Field("WorkingItemIDs");
        private static readonly FieldInfo SmallPosition = Field("m_curSmallPos");
        private static readonly MethodInfo Queue = Method("AddToSelectionQueue");
        private static readonly MethodInfo Select = Method("SetSelectedID");
        private static readonly MethodInfo Details = Method("RedrawDetailsCanvas");
        private static readonly MethodInfo CountGun = Method("IncrementSpawnedGuns");
        private readonly ItemSpawnerV2 spawner;
        private readonly HashSet<string> managedSelections = new HashSet<string>(StringComparer.Ordinal);

        internal SpawnerBridge(ItemSpawnerV2 spawner) { this.spawner = spawner; }
        private static FieldInfo Field(string name) => AccessTools.Field(typeof(ItemSpawnerV2), name)
            ?? throw new MissingFieldException(typeof(ItemSpawnerV2).FullName, name);
        private static MethodInfo Method(string name) => AccessTools.Method(typeof(ItemSpawnerV2), name)
            ?? throw new MissingMethodException(typeof(ItemSpawnerV2).FullName, name);
        internal static void Validate() { if (Page == null) throw new InvalidOperationException(); }

        internal ItemSpawnerV2.PageMode PageMode => (ItemSpawnerV2.PageMode)Page.GetValue(spawner);
        private ItemSpawnerV2.SearchMode SearchMode => (ItemSpawnerV2.SearchMode)Search.GetValue(spawner);
        internal bool IsTagMode => SearchMode == ItemSpawnerV2.SearchMode.Tag;
        internal string ScopeDescription => IsTagMode ? "matching current tags" : "of current section";
        internal bool IsBrowsingSection
        {
            get
            {
                var page = PageMode;
                var mode = SearchMode;
                return (mode == ItemSpawnerV2.SearchMode.Simple || mode == ItemSpawnerV2.SearchMode.Tag)
                    && page >= ItemSpawnerV2.PageMode.Firearms && page <= ItemSpawnerV2.PageMode.ToolsToys;
            }
        }

        private string ClassicContextKey
        {
            get
            {
                if (OtherLoaderBridge.Active) return OtherLoaderBridge.Path(spawner);
                var page = PageMode;
                var levels = (Dictionary<ItemSpawnerV2.PageMode, ItemSpawnerV2.SimpleDisplayLevel>)Levels.GetValue(spawner);
                var groups = (Dictionary<ItemSpawnerV2.PageMode, ItemSpawnerCategoryDefinitionsV2.SpawnerPage.SpawnerTagGroup>)Group.GetValue(spawner);
                bool overview = levels[page] == ItemSpawnerV2.SimpleDisplayLevel.Subcategory;
                var group = groups[page];
                return page + ":" + levels[page] + (overview || group == null ? "" : ":" + group.TagT + ":" + group.Tag);
            }
        }

        internal sealed class Context
        {
            internal ItemSpawnerV2.PageMode Page;
            internal ItemSpawnerV2.SearchMode Mode;
            internal string ClassicKey;
            internal TagSelectionSnapshot<TagType> Tags;
        }

        private Dictionary<TagType, List<string>> TagsForCurrentPage()
        {
            var pages = (Dictionary<ItemSpawnerV2.PageMode, Dictionary<TagType, List<string>>>)SelectedTags.GetValue(spawner);
            Dictionary<TagType, List<string>> tags;
            return pages != null && pages.TryGetValue(PageMode, out tags) ? tags : null;
        }

        internal Context CaptureContext() => new Context
        {
            Page = PageMode,
            Mode = SearchMode,
            ClassicKey = IsTagMode ? null : ClassicContextKey,
            Tags = IsTagMode ? new TagSelectionSnapshot<TagType>(TagsForCurrentPage()) : null
        };

        internal bool MatchesContext(Context context)
        {
            if (!IsBrowsingSection || context == null || context.Page != PageMode || context.Mode != SearchMode) return false;
            return IsTagMode ? context.Tags.Matches(TagsForCurrentPage()) : context.ClassicKey == ClassicContextKey;
        }

        internal List<ItemSpawnerID> CaptureSection()
        {
            var result = new List<ItemSpawnerID>();
            if (!IsBrowsingSection) return result;
            // Native filter/page callbacks already compute the complete result before pagination.
            // Reuse it without repeating native filtering, sorting and UI redraws on every roll.
            // The classic overview uses the page registry because WorkingItemIDs is stale there.
            bool tagMode = IsTagMode;
            var page = PageMode;
            bool categoryOverview = false;
            if (!tagMode)
            {
                var levels = (Dictionary<ItemSpawnerV2.PageMode, ItemSpawnerV2.SimpleDisplayLevel>)Levels.GetValue(spawner);
                categoryOverview = levels[page] == ItemSpawnerV2.SimpleDisplayLevel.Subcategory;
            }
            List<string> pageIds;
            if (!ManagerSingleton<IM>.Instance.PageItemLists.TryGetValue(page, out pageIds)) return result;
            var ids = OtherLoaderBridge.Active && !tagMode ? OtherLoaderBridge.ClassicIds(spawner)
                : SelectionPolicy.SectionIds(categoryOverview, pageIds, (List<string>)Working.GetValue(spawner));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ids)
            {
                var entry = OtherLoaderBridge.Resolve(id);
                if (IsAvailable(entry) && seen.Add(entry.MainObject.ItemID)) result.Add(entry);
            }
            return result;
        }

        internal static bool IsAvailable(ItemSpawnerID entry) => OtherLoaderBridge.Available(entry);

        internal void SelectEntry(ItemSpawnerID entry)
        {
            string id = OtherLoaderBridge.SelectionId(entry);
            Queue.Invoke(spawner, new object[] { id });
            Select.Invoke(spawner, new object[] { id });
            Details.Invoke(spawner, null);
            managedSelections.Add(id);
        }

        internal bool IsManagedSelection(string id) => id != null && managedSelections.Contains(id);
        internal string SelectedId => (string)Field("m_selectedID").GetValue(spawner);

        internal UnityEngine.Transform SpawnPoint(ItemSpawnerID entry)
        {
            if (entry.UsesHugeSpawnPad) return spawner.SpawnPoint_Huge;
            if (entry.UsesLargeSpawnPad) return spawner.SpawnPoint_Large;
            return SmallSpawnPoint();
        }

        internal UnityEngine.Transform SmallSpawnPoint()
        {
            if (spawner.SpawnPoints_Small == null || spawner.SpawnPoints_Small.Count == 0) return null;
            int index = (int)SmallPosition.GetValue(spawner);
            if (index < 0 || index >= spawner.SpawnPoints_Small.Count) index = 0;
            return spawner.SpawnPoints_Small[index];
        }

        internal void RecordSpawn(ItemSpawnerID entry, bool countFirearm = true)
        {
            int count = spawner.SpawnPoints_Small == null ? 0 : spawner.SpawnPoints_Small.Count;
            if (count > 0) SmallPosition.SetValue(spawner, ((int)SmallPosition.GetValue(spawner) + 1) % count);
            if (countFirearm && entry.MainObject.Category == FVRObject.ObjectCategory.Firearm) CountGun.Invoke(spawner, null);
        }
    }
}
