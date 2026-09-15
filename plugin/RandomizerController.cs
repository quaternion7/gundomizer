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
        private readonly List<KeyValuePair<Transform, Vector3>> pagerOffsets = new List<KeyValuePair<Transform, Vector3>>();
        private GameObject tooltip;
        private Text tooltipText;
        private Texture2D rainbowTexture;
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
                return;
            }
            if (Time.unscaledTime >= nextHeldItemCheck) RefreshHeldItem();
            if (ammoPanel != null) ammoPanel.Update();
            string message = null;
            if (busy || Time.unscaledTime < statusUntil) message = status;
            else if (compatibleButton.Hovered)
                message = "Random COMPATIBLE item of held item [" +
                    (cachedHeldItem == null ? "none" : cachedHeldName) + "] (" + bridge.ScopeDescription + ")";
            else if (randomButton.Hovered) message = "Random Item (" + bridge.ScopeDescription + ")";
            else if (ammoPanel != null) message = ammoPanel.Tooltip;
            if (ammoPanel != null && ammoPanel.IsOpen && !busy) message = null;
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
            // A fresh click can accept a newly picked-up object before the next background poll.
            if (!CanClick(false)) return;
            if (compatible) RefreshHeldItem(pointingHand);
            var held = compatible ? cachedHeldItem : null;
            if (compatible && held == null) return;
            busy = true;
            status = compatible ? "Finding a compatible item..." : "Choosing an item...";
            nextClick = Time.unscaledTime + 0.3f;
            // Run on this panel's component so scene destruction cancels pending work.
            // Capture the setting now so a pending roll cannot change from selection to spawning.
            StartCoroutine(GuardedRoll(compatible, held, pointingHand, Plugin.SpawnItemInstantly.Value));
        }

        internal void ClickAmmo(FVRViveHand hand)
        {
            if (!CanClick(false)) return;
            RefreshHeldItem(hand);
            var variants = ammoPanel.EnabledVariants();
            if (cachedHeldItem == null || variants.Count == 0) return;
            busy = true;
            status = "Choosing compatible ammo...";
            nextClick = Time.unscaledTime + 0.3f;
            StartCoroutine(GuardedRoll(true, cachedHeldItem, hand, Plugin.SpawnItemInstantly.Value,
                variants, AmmoSelection.Shared.Revision));
        }

        private IEnumerator GuardedRoll(bool compatible, FVRPhysicalObject held, FVRViveHand hand, bool spawnInstantly,
            List<AmmoCatalog.Variant> ammo = null, int ammoRevision = -1)
        {
            var metrics = new RollMetrics { Kind = ammo == null ? "Compatible" : "Ammo" };
            var roll = Roll(compatible, held, hand, spawnInstantly, metrics, ammo, ammoRevision);
            try
            {
                while (true)
                {
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
                        Message("Could not complete the roll. See the BepInEx log.");
                        break;
                    }
                    yield return current;
                }
            }
            finally
            {
                (roll as IDisposable)?.Dispose();
                busy = false;
                nextClick = Time.unscaledTime + 0.25f;
                if (compatible) metrics.Log();
            }
        }

        private IEnumerator Roll(bool compatible, FVRPhysicalObject held, FVRViveHand hand, bool spawnInstantly,
            RollMetrics metrics, List<AmmoCatalog.Variant> ammo, int ammoRevision)
        {
            var context = ammo == null ? bridge.CaptureContext() : null;
            var variants = new Dictionary<string, AmmoCatalog.Variant>(StringComparer.Ordinal);
            var candidates = ammo == null ? bridge.CaptureSection() : new List<ItemSpawnerID>();
            if (ammo != null) foreach (var variant in ammo)
                if (!variants.ContainsKey(variant.Entry.ItemID)) { variants.Add(variant.Entry.ItemID, variant); candidates.Add(variant.Entry); }
            metrics.SectionCount = candidates.Count;
            // Rule out unrelated feed connectors and unsupported relationships using metadata
            // before loading prefabs. Native component checks remain authoritative after loading.
            var query = compatible && ammo == null ? Compatibility.Capture(held) : null;
            if (query != null) query.Prefilter(candidates);
            metrics.FilteredCount = candidates.Count;
            SelectionPolicy.Shuffle(candidates, random);
            int inspected = 0;
            // The first match in a random permutation is uniform over matching entries. Prefabs
            // load lazily, so a click does not force every modded object into memory up front.
            foreach (var entry in candidates)
            {
                if (!StillValid(context, compatible, held, hand, false, ammoRevision)) yield break;
                if (!SpawnerBridge.IsAvailable(entry)) continue;
                AnvilCallback<GameObject> request = null;
                metrics.BeginRequest();
                try { request = entry.MainObject.GetGameObjectAsync(); }
                catch (Exception ex) { LogSkipped(entry, ex); }
                if (request == null) continue;
                bool waiting = true;
                bool failed = false;
                float deadline = Time.realtimeSinceStartup + 30f;
                while (waiting)
                {
                    if (!StillValid(context, compatible, held, hand, false, ammoRevision)) yield break;
                    try { waiting = request.keepWaiting; }
                    catch (Exception ex) { LogSkipped(entry, ex); failed = true; break; }
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        LogSkipped(entry, new TimeoutException("Prefab load exceeded 30 seconds."));
                        failed = true; break;
                    }
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
                    prefab = entry.MainObject.GetGameObject();
                    ++metrics.CheckedPrefabs;
                    match = prefab != null && prefab.GetComponent<FVRPhysicalObject>() != null
                        && (ammo != null ? AmmoCatalog.Matches(held, prefab, variants[entry.ItemID]) : query == null || query.Matches(prefab));
                    if (match)
                    {
                        string problem = PrefabGuard.Problem(prefab);
                        if (problem != null)
                        {
                            LogSkipped(entry, new InvalidOperationException(problem));
                            match = false;
                        }
                    }
                }
                catch (Exception ex) { LogSkipped(entry, ex); }
                if (match)
                {
                    // Recheck live hands and range once at commit, even between background polls.
                    if (!StillValid(context, compatible, held, hand, true, ammoRevision) || !SpawnerBridge.IsAvailable(entry)) yield break;
                    // Recheck actual mounts after loading; no yields occur before selecting or spawning.
                    if (ammo != null ? !AmmoCatalog.Matches(held, prefab, variants[entry.ItemID]) : compatible && !Compatibility.Matches(held, prefab)) continue;
                    if (!spawnInstantly)
                    {
                        bridge.SelectEntry(entry);
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
                    var spawned = Object.Instantiate(prefab, position, point.rotation);
                    spawned.GetComponent<FVRPhysicalObject>().IDSpawnedFrom = entry;
                    spawned.SetActive(true);
                    bridge.RecordSpawn(entry);
                    bridge.SelectEntry(entry);
                    metrics.Outcome = "spawned";
                    spawner.Boop(1);
                    Plugin.Log.LogInfo("Spawned " + entry.ItemID + (compatible ? " compatible with " + Compatibility.Name(held) : ""));
                    Message("Spawned " + entry.DisplayName);
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
            string scope = bridge.IsTagMode ? "matching these tags" : "in this section";
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
                left + diceWidth + gap + compatibleWidth * 0.5f, y, compatibleWidth);
            float ammoLeft = left + diceWidth + gap + compatibleWidth + 16f;
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
            var background = backgroundObject.AddComponent<RawImage>();
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
