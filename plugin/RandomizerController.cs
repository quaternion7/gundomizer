using System;
using System.Collections;
using System.Collections.Generic;
using FistVR;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Gundomizer
{
    public sealed class RandomizerController : MonoBehaviour
    {
        private ItemSpawnerV2 spawner;
        private SpawnerBridge bridge;
        private RandomizerButton randomButton;
        private RandomizerButton compatibleButton;
        private RectTransform uiRoot;
        private AmmoPanel ammoPanel;
        private CompatiblePanel compatiblePanel;
        private int compatibleRevision = -1;
        private readonly List<KeyValuePair<Transform, Vector3>> pagerOffsets = new List<KeyValuePair<Transform, Vector3>>();
        private GameObject tooltip;
        private Text tooltipText;
        private Texture2D rainbowTexture;
        private PreviewFallback previewFallback;
        private bool busy;
        private float nextClick;
        private string status;
        private float statusUntil;
        private const float HeldItemCheckInterval = 1f;
        private const float HeldItemRangeSquared = 8f * 8f;
        private float nextHeldItemCheck;
        private bool playerNearby;
        private FVRPhysicalObject cachedHeldItem;
        private string cachedHeldName = "none";
        private readonly System.Random random = new System.Random();
        private readonly HashSet<string> loggedFailures = new HashSet<string>();
        private readonly HashSet<string> ammoSelections = new HashSet<string>();
        private RandomizerButton activeButton;
        private bool cancelRequested;

        internal bool CanCancel(RandomizerButton button) => busy && !cancelRequested && Visible
            && activeButton == button && Time.unscaledTime >= nextClick;

        internal void CancelRoll() { if (busy) cancelRequested = true; }

        private void Progress(string message) { status = message + "\nClick again to cancel."; }

        internal void Initialize(ItemSpawnerV2 owner)
        {
            spawner = owner;
            bridge = new SpawnerBridge(owner);
            try
            {
                BuildUi();
                Plugin.Log.LogInfo("Added classic/tag-viewer randomizer buttons to " + owner.name);
            }
            catch
            {
                DestroyUi();
                enabled = false;
                throw;
            }
        }

        private bool Visible => spawner != null && bridge != null && bridge.IsBrowsingSection;
        internal bool IsTagMode => bridge.IsTagMode;
        internal string ScopeDescription => bridge.ScopeDescription;
        internal void CloseChoices(bool openingAmmo)
        { if (openingAmmo) compatiblePanel?.Hide(); else ammoPanel?.Hide(); }

        internal void RefreshPreview(string selectedId)
        {
            if (spawner == null || spawner.IM_Detail == null) return;
            bool selected = OtherLoaderBridge.Resolve(selectedId) != null;
            if (previewFallback == null)
            {
                if (!selected || spawner.IM_Detail.sprite != null) return;
                var obj = new GameObject("Gundomizer Missing Preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(PreviewFallback));
                obj.layer = spawner.IM_Detail.gameObject.layer;
                var rect = (RectTransform)obj.transform;
                rect.SetParent(spawner.IM_Detail.transform, false);
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                previewFallback = obj.GetComponent<PreviewFallback>();
                previewFallback.Initialize(spawner.IM_Detail);
            }
            previewFallback.Refresh(selected);
        }

        internal bool CanClick(bool compatible)
        {
            return Visible && !busy && Time.unscaledTime >= nextClick
                && (!compatible || (playerNearby && cachedHeldItem != null));
        }

        internal void RefreshHeldItem(FVRViveHand pointingHand = null)
        {
            nextHeldItemCheck = Time.unscaledTime + HeldItemCheckInterval;
            var body = GM.CurrentPlayerBody;
            playerNearby = body != null && body.Head != null
                && (body.Head.position - spawner.transform.position).sqrMagnitude <= HeldItemRangeSquared;
            // No hand/component lookups for distant panels. Measure from the VR head to THIS spawner.
            cachedHeldItem = playerNearby ? Compatibility.HeldItem(pointingHand) : null;
            cachedHeldName = Compatibility.Name(cachedHeldItem);
            if (ammoPanel != null) ammoPanel.Refresh(cachedHeldItem);
            if (compatiblePanel != null) compatiblePanel.Refresh(cachedHeldItem);
        }

        private void Update()
        {
            if (randomButton == null || compatibleButton == null || uiRoot == null || spawner == null) return;
            bool visible = Visible;
            if (uiRoot.gameObject.activeSelf != visible) uiRoot.gameObject.SetActive(visible);
            if (!visible)
            {
                cachedHeldItem = null;
                cachedHeldName = "none";
                playerNearby = false;
                tooltip.SetActive(false);
                if (ammoPanel != null) ammoPanel.Hide();
                if (compatiblePanel != null) compatiblePanel.Hide();
                return;
            }
            if (Time.unscaledTime >= nextHeldItemCheck) RefreshHeldItem();
            if (ammoPanel != null) ammoPanel.Update();
            if (compatiblePanel != null) compatiblePanel.Update();
            string message = null;
            if (busy || Time.unscaledTime < statusUntil) message = status;
            else if (compatibleButton.Hovered)
                message = "Random COMPATIBLE item of held item [" +
                    (cachedHeldItem == null ? "none" : cachedHeldName) + "] (" + compatiblePanel.ScopeDescription + ")";
            else if (randomButton.Hovered) message = "Random Item (" + bridge.ScopeDescription + ")";
            else message = compatiblePanel?.Tooltip ?? ammoPanel?.Tooltip;
            if (ammoPanel != null && ammoPanel.IsOpen && !busy) message = null;
            if (compatiblePanel != null && compatiblePanel.IsOpen && !busy) message = null;
            tooltip.SetActive(!string.IsNullOrEmpty(message));
            if (message != null && tooltipText.text != message)
            {
                tooltipText.text = message;
                // Keep the larger type readable even with a long modded item's display name.
                var rect = (RectTransform)tooltip.transform;
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, Mathf.Max(240f, tooltipText.preferredHeight + 48f));
            }
        }

        internal void Click(bool compatible, FVRViveHand pointingHand)
        {
            var button = compatible ? compatibleButton : randomButton;
            if (CanCancel(button)) { CancelRoll(); return; }
            // A fresh click can accept a newly picked-up object before the next background poll.
            if (!CanClick(false)) return;
            if (compatible) RefreshHeldItem(pointingHand);
            var held = compatible ? cachedHeldItem : null;
            if (compatible && held == null) return;
            if (compatible && !compatiblePanel.HasChoices) return;
            if (compatible) compatiblePanel.Hide();
            busy = true;
            activeButton = button;
            cancelRequested = false;
            Progress(compatible ? "Finding a compatible item..." : "Choosing an item...");
            nextClick = Time.unscaledTime + 0.3f;
            // Run on this panel's component so scene destruction cancels pending work.
            // Capture the setting now so a pending roll cannot change from selection to spawning.
            StartCoroutine(GuardedRoll(compatible, held, pointingHand, Plugin.SpawnItemInstantly.Value));
        }

        internal void ClickAmmo(FVRViveHand hand)
        {
            if (CanCancel(ammoPanel.RollButton)) { CancelRoll(); return; }
            if (!CanClick(false)) return;
            RefreshHeldItem(hand);
            var variants = ammoPanel.EnabledVariants();
            if (cachedHeldItem == null || variants.Count == 0) return;
            ammoPanel.Hide();
            busy = true;
            activeButton = ammoPanel.RollButton;
            cancelRequested = false;
            Progress("Choosing compatible ammo...");
            nextClick = Time.unscaledTime + 0.3f;
            StartCoroutine(GuardedRoll(true, cachedHeldItem, hand, Plugin.SpawnItemInstantly.Value,
                variants, AmmoSelection.Shared.Revision, Plugin.AutoFillHeldItem.Value));
        }

        private IEnumerator GuardedRoll(bool compatible, FVRPhysicalObject held, FVRViveHand hand, bool spawnInstantly,
            List<AmmoCatalog.Variant> ammo = null, int ammoRevision = -1, bool autoFill = false)
        {
            var metrics = new RollMetrics { Kind = ammo != null ? "Ammo" : compatible ? "Compatible" : "Random" };
            compatibleRevision = compatible && ammo == null ? CompatibleSelection.Shared.Revision : -1;
            return Guarded(Roll(compatible, held, hand, spawnInstantly, metrics, ammo, ammoRevision, autoFill), metrics);
        }

        private IEnumerator Guarded(IEnumerator roll, RollMetrics metrics)
        {
            try
            {
                while (true)
                {
                    if (cancelRequested)
                    {
                        metrics.Outcome = "cancelled";
                        Message("Randomizer cancelled.");
                        break;
                    }
                    object current;
                    try
                    {
                        if (!roll.MoveNext()) break;
                        current = roll.Current;
                    }
                    catch (Exception ex)
                    {
                        metrics.Outcome = "failed";
                        Plugin.Log.LogError("Randomizer request failed: " + ex);
                        Message("Could not complete the roll. Check logs.");
                        break;
                    }
                    yield return current;
                }
            }
            finally
            {
                (roll as IDisposable)?.Dispose();
                busy = false;
                activeButton = null;
                compatibleRevision = -1;
                cancelRequested = false;
                nextClick = Time.unscaledTime + 0.25f;
                metrics.Log();
            }
        }

        internal bool SpawnManagedSelection() => SpawnManagedSelection(bridge.SelectedId);

        internal bool SpawnManagedSelection(string id) => RequestSelected(id, null);

        internal bool SpawnManagedSelectionAtPoint(Vector3 point) => RequestSelected(bridge.SelectedId, point);

        private bool RequestSelected(string id, Vector3? targetPoint)
        {
            if (!Visible || !bridge.IsManagedSelection(id)) return false;
            if (busy)
            {
                if (CanCancel(null)) CancelRoll();
                return true;
            }
            var entry = OtherLoaderBridge.Resolve(id);
            if (!SpawnerBridge.IsAvailable(entry)) return false;
            busy = true;
            activeButton = null;
            cancelRequested = false;
            nextClick = Time.unscaledTime + .3f;
            Progress("Loading " + entry.DisplayName + "...");
            var metrics = new RollMetrics { Kind = "Native selection" };
            FVRPhysicalObject fillTarget = null;
            if (Plugin.AutoFillHeldItem.Value && ammoSelections.Contains(id))
            {
                RefreshHeldItem();
                fillTarget = cachedHeldItem;
            }
            StartCoroutine(Guarded(SpawnSelected(entry, metrics, fillTarget, targetPoint), metrics));
            return true;
        }

        private IEnumerator SpawnSelected(ItemSpawnerID entry, RollMetrics metrics, FVRPhysicalObject fillTarget, Vector3? targetPoint)
        {
            // Accepting a roll with native Spawn retains its main + SecondObject behavior.
            // Gather the requested prefabs first, so cancellation cannot leave half a native set.
            var sources = OtherLoaderBridge.SpawnSources(entry);
            var prefabs = new List<GameObject>();
            foreach (var source in sources)
            {
                AnvilCallback<GameObject> request = null;
                bool started = false;
                while (request == null)
                {
                    if (AssetAccess.TryRequest(source, out request, out started)) break;
                    Progress("Waiting for item loading...");
                    yield return null;
                }
                metrics.BeginRequest();
                Progress("Loading " + source.DisplayName + "... (" + (prefabs.Count + 1) + "/" + sources.Count + ")");
                if (started) { ++metrics.NewLoads; yield return null; }
                while (request != null && request.keepWaiting)
                {
                    metrics.NotePending();
                    yield return null;
                }
                metrics.EndRequest();
                var prefab = request == null ? null : request.Result;
                if (prefab == null || prefab.GetComponent<FVRPhysicalObject>() == null)
                { Message("Could not load " + entry.DisplayName + ". Check logs."); yield break; }
                ConnectorIndex.Observe(source, prefab);
                prefabs.Add(prefab);
            }
            string fillMessage = "";
            for (int i = 0; i < prefabs.Count; ++i)
            {
                var point = i == 0 ? bridge.SpawnPoint(entry) : bridge.SmallSpawnPoint();
                if (!targetPoint.HasValue && point == null) { Message("This spawner has no spawn pad for this item."); yield break; }
                // The toolbox tablet accepts selected items at the stylus's ray hit, with
                // native vertical offsets for bundled items, rather than advancing its pads.
                var position = targetPoint.HasValue ? targetPoint.Value + (i == 0 ? Vector3.zero : Vector3.up * (i + 1)) : point.position;
                var rotation = targetPoint.HasValue ? Quaternion.identity : point.rotation;
                if (i == 0 && (entry.UsesLargeSpawnPad || entry.UsesHugeSpawnPad)) position += Vector3.up * .2f;
                GameObject spawned;
                string problem;
                if (!SpawnTransaction.TrySpawn(prefabs[i], position, rotation, entry, out spawned, out problem))
                {
                    metrics.Outcome = "initialization failed";
                    Plugin.Log.LogError("Could not initialize " + sources[i].ItemID + "; removed the failed instance. " + problem);
                    Message(i == 0 ? "Could not initialize " + entry.DisplayName + ". Failed instance removed; Check logs."
                        : "Main item spawned; a bundled item failed and was removed. Check logs.");
                    yield break;
                }
                bridge.RecordSpawn(entry, i == 0, !targetPoint.HasValue);
                if (i == 0) fillMessage = FillSpawnedAmmo(spawned, fillTarget, null);
            }
            metrics.Outcome = "spawned";
            spawner.Boop(1);
            Message("Spawned " + entry.DisplayName + fillMessage);
        }

        private IEnumerator Roll(bool compatible, FVRPhysicalObject held, FVRViveHand hand, bool spawnInstantly,
            RollMetrics metrics, List<AmmoCatalog.Variant> ammo, int ammoRevision, bool autoFill)
        {
            var selection = compatible && ammo == null ? CompatibleSelection.Shared.Snapshot() : null;
            var context = ammo == null && (selection == null || !selection.AllItems) ? bridge.CaptureContext() : null;
            var variants = new Dictionary<string, AmmoCatalog.Variant>(StringComparer.Ordinal);
            if (ammo != null) foreach (var variant in ammo)
                if (!variants.ContainsKey(variant.Entry.ItemID)) variants.Add(variant.Entry.ItemID, variant);
            var query = selection == null ? null : Compatibility.CaptureFiltered(held, selection);
            var candidates = ammo == null && (selection == null || !selection.AllItems) ? bridge.CaptureSection() : new List<ItemSpawnerID>();
            if (selection != null && selection.AllItems)
            {
                Progress("Collecting compatible choices across all items...");
                var catalog = bridge.CollectAll(candidates);
                try
                {
                    while (catalog.MoveNext())
                    {
                        yield return catalog.Current;
                        if (!StillValid(context, compatible, held, hand, false, ammoRevision)) yield break;
                    }
                }
                finally { (catalog as IDisposable)?.Dispose(); }
            }
            if (ammo != null) foreach (var variant in variants.Values) candidates.Add(variant.Entry);
            metrics.SectionCount = candidates.Count;
            {
                // Compact in linear time and yield during large catalog work. Unknown connector
                // data stays eligible; actual loaded components can reject unrelated connectors.
                float slice = Time.realtimeSinceStartup;
                int write = 0;
                for (int i = 0; i < candidates.Count; ++i)
                {
                    var entry = candidates[i];
                    if (query == null || query.CouldMatch(entry, true)) candidates[write++] = entry;
                    else if (query.CouldMatch(entry, false)) ++metrics.IndexRejected;
                    if (i % 64 == 0 && Time.realtimeSinceStartup - slice > 0.0015f)
                    {
                        yield return null;
                        if (!StillValid(context, compatible, held, hand, false, ammoRevision)) yield break;
                        slice = Time.realtimeSinceStartup;
                    }
                }
                candidates.RemoveRange(write, candidates.Count - write);
                for (int i = candidates.Count - 1; i > 0; --i)
                {
                    int j = random.Next(i + 1);
                    var swap = candidates[i]; candidates[i] = candidates[j]; candidates[j] = swap;
                    if (i % 256 == 0 && Time.realtimeSinceStartup - slice > 0.0015f)
                    { yield return null; slice = Time.realtimeSinceStartup; }
                }
            }
            metrics.FilteredCount = candidates.Count;
            int inspected = 0;
            float inspectionSlice = Time.realtimeSinceStartup;
            // The first match in a random permutation is uniform over matching entries. Prefabs
            // load lazily, so a click does not force every modded object into memory up front.
            for (int candidateIndex = 0; candidateIndex < candidates.Count; ++candidateIndex)
            {
                if (candidateIndex % 64 == 0 && Time.realtimeSinceStartup - inspectionSlice > 0.0015f)
                { yield return null; inspectionSlice = Time.realtimeSinceStartup; }
                var entry = candidates[candidateIndex];
                if (!StillValid(context, compatible, held, hand, false, ammoRevision)) yield break;
                if (!SpawnerBridge.IsAvailable(entry)) continue;
                if (query != null && !query.CouldMatch(entry, true)) { ++metrics.IndexRejected; continue; }
                // Plain selection needs only spawner metadata. Avoid loading a potentially huge
                // weapon just to put its existing artwork/name into the details panel.
                if (!compatible && !spawnInstantly)
                {
                    SelectEntry(entry, false);
                    metrics.Outcome = "selected";
                    spawner.Boop(0);
                    Message("Selected " + entry.DisplayName + ".");
                    yield break;
                }
                AnvilCallback<GameObject> request = null;
                bool started = false;
                metrics.BeginRequest();
                while (request == null)
                {
                    if (!StillValid(context, compatible, held, hand, false, ammoRevision)) yield break;
                    try { AssetAccess.TryRequest(entry.MainObject, out request, out started); }
                    catch (Exception ex)
                    {
                        LogSkipped(entry, ex);
                        metrics.Outcome = "load failed";
                        Message("Could not load " + entry.DisplayName + ". Check logs.");
                        yield break;
                    }
                    if (request == null)
                    {
                        Progress("Waiting for item loading...");
                        metrics.NotePending();
                        yield return null;
                    }
                }
                Progress("Loading " + entry.DisplayName + "... (" + (candidateIndex + 1) + "/" + candidates.Count + ")");
                // Pace speculative loads even when an asset completes synchronously. Keep
                // only one native request outstanding across all panels, without click budgets.
                if (started) { ++metrics.NewLoads; yield return null; }
                bool waiting = true;
                bool failed = false;
                while (waiting)
                {
                    if (!StillValid(context, compatible, held, hand, false, ammoRevision)) yield break;
                    try { waiting = request.keepWaiting; }
                    catch (Exception ex) { LogSkipped(entry, ex); failed = true; break; }
                    if (waiting)
                    {
                        metrics.NotePending();
                        yield return null;
                    }
                }
                metrics.EndRequest();
                if (failed) continue;
                GameObject prefab = null;
                bool match = false;
                try
                {
                    prefab = request.Result; // The completed request, never a second synchronous lookup.
                    ConnectorIndex.Observe(entry.MainObject, prefab);
                    ++metrics.CheckedPrefabs;
                    match = prefab != null && prefab.GetComponent<FVRPhysicalObject>() != null
                        && (ammo != null ? AmmoCatalog.Matches(held, prefab, variants[entry.ItemID]) : query == null || query.Matches(prefab));
                }
                catch (Exception ex) { LogSkipped(entry, ex); }
                if (match)
                {
                    // Recheck live hands and range once at commit, even between background polls.
                    if (!StillValid(context, compatible, held, hand, true, ammoRevision) || !SpawnerBridge.IsAvailable(entry)) yield break;
                    // Recheck actual mounts after loading; no yields occur before selecting or spawning.
                    if (ammo != null ? !AmmoCatalog.Matches(held, prefab, variants[entry.ItemID])
                        : compatible && !Compatibility.CaptureFiltered(held, selection).Matches(prefab)) continue;
                    if (!spawnInstantly)
                    {
                        SelectEntry(entry, ammo != null);
                        metrics.Outcome = "selected";
                        spawner.Boop(0);
                        Plugin.Log.LogInfo("Selected " + entry.ItemID + (compatible ? " compatible with " + Compatibility.Name(held) : ""));
                        Message("Selected " + entry.DisplayName + ".");
                        yield break;
                    }
                    var point = bridge.SpawnPoint(entry);
                    if (point == null)
                    {
                        Message("This spawner has no spawn pad for this item.");
                        yield break;
                    }
                    var position = point.position;
                    if (entry.UsesLargeSpawnPad || entry.UsesHugeSpawnPad) position += Vector3.up * 0.2f;
                    SelectEntry(entry, ammo != null);
                    GameObject spawned;
                    string problem;
                    if (!SpawnTransaction.TrySpawn(prefab, position, point.rotation, entry, out spawned, out problem))
                    {
                        metrics.Outcome = "initialization failed";
                        Plugin.Log.LogError("Could not initialize " + entry.ItemID + "; removed the failed instance. " + problem);
                        Message("Could not initialize " + entry.DisplayName + ". Failed instance removed; Check logs.");
                        yield break;
                    }
                    bridge.RecordSpawn(entry);
                    metrics.Outcome = "spawned";
                    spawner.Boop(1);
                    Plugin.Log.LogInfo("Spawned " + entry.ItemID + (compatible ? " compatible with " + Compatibility.Name(held) : ""));
                    Message("Spawned " + entry.DisplayName + FillSpawnedAmmo(spawned, autoFill ? held : null, hand));
                    yield break;
                }
                if (++inspected % 8 == 0) yield return null;
            }
            metrics.Outcome = "no matches";
            if (ammo != null)
            {
                Message("No spawnable enabled ammo variants for " + Compatibility.Name(held) + ".");
                yield break;
            }
            string scope = selection != null && selection.AllItems ? "among enabled types across all items"
                : (bridge.IsTagMode ? "matching these tags" : "in this section") + (selection == null ? "" : " and enabled types");
            Message(compatible ? "No compatible items " + scope + " for " + Compatibility.Name(held) + "."
                : "No spawnable items " + scope + ".");
        }

        private bool StillValid(SpawnerBridge.Context context, bool compatible, FVRPhysicalObject held, FVRViveHand hand,
            bool checkLiveHand = false, int ammoRevision = -1)
        {
            if (!Visible || (context != null && !bridge.MatchesContext(context)))
            {
                Message("Section or filters changed. Randomizer cancelled."); return false;
            }
            if (ammoRevision >= 0 && ammoRevision != AmmoSelection.Shared.Revision)
            {
                Message("Ammo choices changed. Randomizer cancelled."); return false;
            }
            if (compatible && ammoRevision < 0 && compatibleRevision >= 0 && compatibleRevision != CompatibleSelection.Shared.Revision)
            { Message("Compatible choices changed. Randomizer cancelled."); return false; }
            // Loading uses the 1 Hz cache; click and commit are the only on-demand hand reads.
            if (compatible && checkLiveHand) RefreshHeldItem(hand);
            if (compatible && !playerNearby)
            {
                Message("Moved away from this spawner. Randomizer cancelled."); return false;
            }
            if (compatible && (held == null || cachedHeldItem != held))
            {
                Message("Held item changed. Randomizer cancelled."); return false;
            }
            return true;
        }

        private void LogSkipped(ItemSpawnerID entry, Exception ex)
        {
            if (loggedFailures.Add(entry.ItemID)) Plugin.Log.LogWarning("Skipped " + entry.ItemID + ": " + ex.Message);
        }

        private void SelectEntry(ItemSpawnerID entry, bool ammo)
        {
            bridge.SelectEntry(entry);
            if (ammo) ammoSelections.Add(bridge.SelectedId);
            else ammoSelections.Remove(bridge.SelectedId);
        }

        private string FillSpawnedAmmo(GameObject spawned, FVRPhysicalObject target, FVRViveHand hand)
        {
            if (target == null || spawned == null) return "";
            var round = spawned.GetComponent<FVRFireArmRound>();
            if (round == null) return "";
            RefreshHeldItem(hand);
            if (!playerNearby || cachedHeldItem != target) return "";
            var fill = AmmoFill.Apply(target, round);
            if (fill.Failed > 0) return ". Fill error (details in log).";
            return fill.Filled > 0 ? ". Filled " + Compatibility.Name(target) + "." : "";
        }

        private void Message(string message)
        {
            status = message;
            statusUntil = Time.unscaledTime + 3.5f;
        }

        private void BuildUi()
        {
            var template = spawner.GO_SimpleTiles_NextPage;
            var source = template == null ? null : template.GetComponent<RectTransform>();
            var previous = spawner.GO_SimpleTiles_PrevPage == null ? null
                : spawner.GO_SimpleTiles_PrevPage.GetComponent<RectTransform>();
            if (source == null || previous == null || spawner.IMG_SimpleTiles == null || spawner.IMG_SimpleTiles.Count == 0)
                throw new InvalidOperationException("Missing native classic-viewer layout references.");
            var parent = source.parent as RectTransform;
            if (parent == null) throw new InvalidOperationException("Expected a native RectTransform parent.");
            var canvas = parent.parent as RectTransform;
            if (canvas == null || canvas.GetComponent<Canvas>() == null)
                throw new InvalidOperationException("Expected the shared native spawner canvas.");
            var tile = spawner.IMG_SimpleTiles[0].rectTransform;
            var tileBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, tile);
            var prevBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, previous);
            float left = tileBounds.min.x;
            float right = prevBounds.min.x - 35f;
            float gap = 20f;
            float height = source.rect.height * Mathf.Abs(source.localScale.y);
            float diceWidth = height;
            float compatibleWidth = height * 1.75f;
            if (diceWidth + compatibleWidth + gap > right - left)
                throw new InvalidOperationException("Insufficient free space beside native paging controls.");
            float y = source.localPosition.y;
            // A sibling of the native mode panels stays visible in both modes. Copy the classic
            // coordinate space exactly, so button positions, scale, and collider sizes stay unchanged.
            uiRoot = (RectTransform)new GameObject("Gundomizer Controls", typeof(RectTransform)).transform;
            uiRoot.gameObject.layer = template.layer;
            uiRoot.SetParent(canvas, false);
            uiRoot.anchorMin = parent.anchorMin; uiRoot.anchorMax = parent.anchorMax;
            uiRoot.pivot = parent.pivot; uiRoot.sizeDelta = parent.sizeDelta;
            uiRoot.anchoredPosition3D = parent.anchoredPosition3D;
            uiRoot.localRotation = parent.localRotation; uiRoot.localScale = parent.localScale;
            uiRoot.gameObject.SetActive(false);
            parent = uiRoot;
            rainbowTexture = MakeRainbowTexture();
            randomButton = CloneButton(template, parent, "Randomizer", false, left + diceWidth * 0.5f, y, diceWidth);
            compatibleButton = CloneButton(template, parent, "Compatible", true,
                left + diceWidth + gap + (compatibleWidth - CompatiblePanel.ToggleWidth) * 0.5f, y, compatibleWidth - CompatiblePanel.ToggleWidth);
            float compatibleRight = left + diceWidth + gap + compatibleWidth - CompatiblePanel.ToggleWidth;
            compatiblePanel = new CompatiblePanel(this, parent, template, compatibleButton, compatibleRight, y);
            float ammoLeft = compatibleRight + CompatiblePanel.ToggleWidth + 16f;
            ammoPanel = new AmmoPanel(this, spawner, parent, template, ammoLeft, y);
            // Make room beside the native tag pager without shrinking its text or hit targets.
            var tagPrev = (RectTransform)spawner.BTN_TagPagePrev.transform;
            var tagNext = (RectTransform)spawner.BTN_TagPageNext.transform;
            float shift = Mathf.Max(0, ammoLeft + AmmoPanel.GroupWidth + 30f
                - RectTransformUtility.CalculateRelativeRectTransformBounds(parent, tagPrev).min.x);
            float nextRight = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, tagNext).max.x;
            float listLeft = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, spawner.BTN_ListPagePrev.transform).min.x;
            if (nextRight + shift + 20f > listLeft) throw new InvalidOperationException("Insufficient space beside tag paging controls.");
            foreach (var pager in new[] { tagPrev, tagNext, spawner.TXT_TagPage.rectTransform })
            {
                var delta = pager.parent.InverseTransformVector(parent.TransformVector(Vector3.right * shift));
                pager.localPosition += delta;
                pagerOffsets.Add(new KeyValuePair<Transform, Vector3>(pager, delta));
            }

            tooltip = new GameObject("Gundomizer Tooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            tooltip.layer = template.layer;
            var rect = (RectTransform)tooltip.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(1350f, 240f);
            rect.localPosition = new Vector3(left, y + source.rect.height * source.localScale.y * 0.55f + 10f, -2f);
            var background = tooltip.GetComponent<Image>();
            background.color = new Color(0.025f, 0.025f, 0.025f, 0.97f);
            background.raycastTarget = false;
            var label = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            label.layer = template.layer;
            label.transform.SetParent(rect, false);
            tooltipText = label.GetComponent<Text>();
            tooltipText.font = template.GetComponent<Text>().font;
            tooltipText.fontSize = 52;
            tooltipText.supportRichText = false;
            tooltipText.alignment = TextAnchor.MiddleLeft;
            tooltipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tooltipText.verticalOverflow = VerticalWrapMode.Truncate;
            tooltipText.color = Color.white;
            tooltipText.raycastTarget = false;
            var textRect = tooltipText.rectTransform;
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(28f, 24f); textRect.offsetMax = new Vector2(-28f, -24f);
            tooltip.SetActive(false);
            uiRoot.gameObject.SetActive(Visible);
        }

        internal RandomizerButton CloneButton(GameObject template, RectTransform parent, string text,
            bool compatible, float x, float y, float width)
        {
            var clone = Object.Instantiate(template);
            clone.name = "Gundomizer " + text;
            clone.SetActive(false);
            var rect = clone.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = template.transform.localScale;
            float oldWidth = rect.rect.width;
            rect.sizeDelta = new Vector2(width / Mathf.Abs(rect.localScale.x), rect.rect.height);
            rect.localPosition = new Vector3(x, y, 0f);
            // Replace only components on OUR clone, including serialized native click targets.
            var native = clone.GetComponent<FVRPointableButton>();
            if (native == null) { Object.Destroy(clone); throw new InvalidOperationException("Native pointable button missing."); }
            var nativeBackground = native.Image;
            float range = native.MaxPointingRange;
            Object.DestroyImmediate(native);
            if (nativeBackground == null) { Object.Destroy(clone); throw new InvalidOperationException("Native background missing."); }
            var backgroundObject = nativeBackground.gameObject;
            Object.DestroyImmediate(nativeBackground);
            var background = backgroundObject.AddComponent<ButtonSurface>();
            background.texture = rainbowTexture;
            background.raycastTarget = true;
            if (background.transform != rect)
            {
                // Stock background is a fixed-size, scaled child, not a stretch anchor.
                // Resize it with our wider label and collider instead of leaving a narrow color patch.
                var bg = background.rectTransform;
                bg.localScale = Vector3.one;
                bg.localRotation = Quaternion.identity;
                bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
                bg.offsetMin = new Vector2(4f, 2f); bg.offsetMax = new Vector2(-4f, -2f);
            }
            var ui = clone.GetComponent<Button>();
            ui.onClick = new Button.ButtonClickedEvent();
            ui.transition = Selectable.Transition.None;
            ui.interactable = true;
            ui.targetGraphic = background;
            var label = clone.GetComponent<Text>() ?? clone.GetComponentInChildren<Text>(true);
            label.text = string.Empty;
            label.raycastTarget = false;
            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(ButtonIcon));
            iconObject.layer = clone.layer;
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.SetParent(rect, false);
            iconRect.anchorMin = Vector2.zero; iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(10f, 9f); iconRect.offsetMax = new Vector2(-10f, -9f);
            // Place in front of the native background without changing collider depth.
            iconRect.localPosition += new Vector3(0f, 0f, -0.01f);
            var icon = iconObject.GetComponent<ButtonIcon>();
            icon.Compatible = compatible;
            icon.raycastTarget = false;
            icon.SetVerticesDirty();
            var collider = clone.GetComponent<BoxCollider>();
            if (collider != null)
            {
                var size = collider.size;
                size.x *= rect.rect.width / oldWidth;
                collider.size = size;
            }
            var button = clone.AddComponent<RandomizerButton>();
            button.Owner = this; button.Compatible = compatible; button.MaxPointingRange = range;
            button.Icon = icon; button.Background = background; button.RainbowTexture = rainbowTexture; button.UiButton = ui;
            ui.onClick.AddListener(() => button.Activate(null));
            clone.SetActive(true); // Mode visibility belongs to the shared root.
            return button;
        }

        private void DestroyUi()
        {
            cancelRequested = true;
            if (previewFallback != null) Object.Destroy(previewFallback.gameObject);
            foreach (var pager in pagerOffsets) if (pager.Key != null) pager.Key.localPosition -= pager.Value;
            pagerOffsets.Clear();
            if (uiRoot != null) Object.Destroy(uiRoot.gameObject);
            if (rainbowTexture != null) Object.Destroy(rainbowTexture);
        }

        private static Texture2D MakeRainbowTexture()
        {
            const int width = 256;
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false);
            texture.name = "Gundomizer Sliding Rainbow";
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color[width];
            for (int i = 0; i < width; ++i) pixels[i] = Color.HSVToRGB((float)i / width, 0.85f, 1f);
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void OnDestroy() { DestroyUi(); }
    }
}
