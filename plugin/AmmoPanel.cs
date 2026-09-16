using System;
using System.Collections.Generic;
using FistVR;
using UnityEngine;
using UnityEngine.UI;

namespace Gundomizer
{
    internal sealed class AmmoPanel
    {
        internal const float GroupWidth = 200f;
        private const int PageSize = 7;
        private const float Width = 1200f;
        private const float Height = 850f;
        private readonly RandomizerController owner;
        private readonly GameObject template;
        private readonly RectTransform popup;
        private readonly RandomizerButton rollButton;
        private readonly RandomizerButton toggleButton;
        private readonly List<RandomizerButton> rows = new List<RandomizerButton>();
        private readonly Text title;
        private readonly Text hint;
        private readonly Text pageText;
        private readonly RandomizerButton previous;
        private readonly RandomizerButton next;
        private List<AmmoCatalog.Variant> variants = new List<AmmoCatalog.Variant>();
        private HashSet<FireArmRoundType> types = new HashSet<FireArmRoundType>();
        private FVRPhysicalObject held;
        private int page;
        private int revision;
        private int enabledCount;
        internal bool IsOpen => popup.gameObject.activeSelf;
        internal RandomizerButton RollButton => rollButton;

        internal AmmoPanel(RandomizerController owner, ItemSpawnerV2 spawner, RectTransform root,
            GameObject template, float left, float y)
        {
            this.owner = owner;
            this.template = template;
            rollButton = owner.CloneButton(template, root, "Ammo", false, left + 60f, y, 120f);
            rollButton.Icon.Ammo = true; rollButton.Icon.SetVerticesDirty();
            rollButton.NeedsHeldContext = true;
            rollButton.Ready = () => owner.CanClick(true) && enabledCount > 0;
            rollButton.Handler = owner.ClickAmmo;
            toggleButton = owner.CloneButton(template, root, "Ammo choices", false, left + 160f, y, 80f);
            toggleButton.Icon.Dropdown = true; toggleButton.Icon.SetVerticesDirty();
            toggleButton.Rainbow = false;
            toggleButton.Background.texture = Texture2D.whiteTexture;
            toggleButton.Background.color = new Color(0.13f, 0.16f, 0.19f, 1f);
            // The two hit targets meet at one straight seam; only the outside corners are rounded.
            rollButton.Background.RoundRight = false;
            toggleButton.Background.RoundLeft = false;
            var rollBackground = rollButton.Background.rectTransform;
            rollBackground.offsetMax = new Vector2(0f, rollBackground.offsetMax.y);
            var toggleBackground = toggleButton.Background.rectTransform;
            toggleBackground.offsetMin = new Vector2(0f, toggleBackground.offsetMin.y);
            var separator = new GameObject("Group separator", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            separator.gameObject.layer = template.layer;
            separator.rectTransform.SetParent(toggleButton.transform, false);
            separator.rectTransform.anchorMin = new Vector2(0, 0);
            separator.rectTransform.anchorMax = new Vector2(0, 1);
            separator.rectTransform.offsetMin = new Vector2(0, 4);
            separator.rectTransform.offsetMax = new Vector2(0.75f, -4);
            separator.rectTransform.localPosition += new Vector3(0, 0, -0.015f);
            separator.color = new Color(0.65f, 0.68f, 0.7f, 0.6f);
            separator.raycastTarget = false;
            toggleButton.NeedsHeldContext = true;
            toggleButton.Ready = () => owner.CanClick(true) && variants.Count > 0;
            toggleButton.Handler = hand => { if (IsOpen) Hide(); else { Refresh(held, true); Redraw(); SetOpen(true); } };

            popup = (RectTransform)new GameObject("Gundomizer Ammo Choices", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(BoxCollider), typeof(FVRPointable)).transform;
            popup.gameObject.layer = template.layer;
            popup.SetParent(root, false);
            popup.anchorMin = popup.anchorMax = new Vector2(0.5f, 0.5f);
            popup.pivot = Vector2.zero;
            popup.sizeDelta = new Vector2(Width, Height);
            popup.localPosition = new Vector3(left, y + 95f, -8f);
            var background = popup.GetComponent<Image>();
            background.color = new Color(0.025f, 0.03f, 0.04f, 1f);
            background.raycastTarget = true;
            // Intercept VR rays over the popup instead of clicking the native UI behind it.
            var blocker = popup.GetComponent<BoxCollider>();
            blocker.center = new Vector3(Width * 0.5f, Height * 0.5f, 2f);
            blocker.size = new Vector3(Width, Height, 1f);
            popup.GetComponent<FVRPointable>().MaxPointingRange = rollButton.MaxPointingRange;
            title = Label("Title", 32, Height - 60, Width - 190, 76, 46);
            hint = Label("Description", 32, Height - 132, Width - 64, 64, 32);
            TextButton("Close", "X", Width - 75, Height - 60, 90, hand => Hide());
            for (int i = 0; i < PageSize; ++i)
            {
                int slot = i;
                var row = TextButton("Variant " + i, "", Width * 0.5f, Height - 210f - i * 84f, Width - 64f,
                    hand => Toggle(slot));
                row.Caption.alignment = TextAnchor.MiddleLeft;
                rows.Add(row);
            }
            TextButton("All", "All", 130, 48, 180, hand => SetAll(true));
            TextButton("None", "None", 335, 48, 180, hand => SetAll(false));
            previous = TextButton("Previous", "<", 780, 48, 140, hand => { --page; Redraw(); });
            next = TextButton("Next", ">", 1090, 48, 140, hand => { ++page; Redraw(); });
            pageText = Label("Page", 860, 48, 160, 70, 40);
            pageText.alignment = TextAnchor.MiddleCenter;
            popup.gameObject.SetActive(false);
        }

        private Text Label(string name, float left, float y, float width, float height, int size)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<Text>();
            text.gameObject.layer = template.layer;
            text.rectTransform.SetParent(popup, false);
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = Vector2.zero;
            text.rectTransform.pivot = new Vector2(0f, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(width, height);
            text.rectTransform.localPosition = new Vector3(left, y, -1f);
            text.font = template.GetComponent<Text>().font;
            text.fontSize = size;
            text.supportRichText = false;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private RandomizerButton TextButton(string name, string caption, float x, float y, float width, Action<FVRViveHand> handler)
        {
            var button = owner.CloneButton(template, popup, "Ammo " + name, false, x, y, width);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.localPosition = new Vector3(x, y, -1f);
            // Keep row hit targets consistent while using the native pointable button component.
            var collider = button.GetComponent<BoxCollider>();
            collider.size = new Vector3(collider.size.x, collider.size.y * 0.55f, 0.25f);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, rect.sizeDelta.y * 0.55f);
            button.Icon.gameObject.SetActive(false);
            button.Rainbow = false;
            button.Background.texture = Texture2D.whiteTexture;
            button.Background.color = new Color(0.13f, 0.16f, 0.19f, 1f);
            button.Ready = () => owner.CanClick(false);
            button.Handler = handler;
            // Native text lives on the root, before our replacement background in canvas order.
            // Put the caption last so the opaque background cannot cover it.
            var text = new GameObject("Caption", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<Text>();
            text.gameObject.layer = template.layer;
            text.rectTransform.SetParent(rect, false);
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(12, 0); text.rectTransform.offsetMax = new Vector2(-12, 0);
            text.rectTransform.localPosition += new Vector3(0, 0, -0.02f);
            text.font = template.GetComponent<Text>().font;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            button.Caption = text;
            text.text = caption; text.fontSize = 22;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return button;
        }

        internal void Refresh(FVRPhysicalObject target, bool force = false)
        {
            var current = AmmoCatalog.Types(target);
            bool changed = target != held || !current.SetEquals(types);
            if (!force && !changed) return;
            if (target != held) Hide();
            held = target; types = current;
            variants = AmmoCatalog.Read(types);
            if (changed) page = 0;
            Redraw();
            if (held == null || variants.Count == 0) Hide();
        }

        internal List<AmmoCatalog.Variant> EnabledVariants()
        {
            Refresh(held, true);
            var result = new List<AmmoCatalog.Variant>();
            foreach (var variant in variants) if (AmmoSelection.Shared.Includes(variant.Key)) result.Add(variant);
            return result;
        }

        internal string Tooltip => rollButton.Hovered
            ? (enabledCount == 0 ? "Hold an ammo-using item and enable at least one ammo variant."
                : "Random compatible ammo for " + Compatibility.Name(held) + " (" + enabledCount + " variants enabled)")
            : toggleButton.Hovered ? "Choose ammo variants for " + Compatibility.Name(held) : null;

        private void SetOpen(bool open)
        {
            popup.gameObject.SetActive(open);
            toggleButton.Icon.rectTransform.localRotation = Quaternion.Euler(0, 0, open ? 180f : 0f);
        }

        internal void Hide() { SetOpen(false); }

        internal void Update()
        {
            if (revision != AmmoSelection.Shared.Revision) Redraw();
            if (!IsOpen) return;
            string description = "Choose which ammo variants to include.";
            for (int i = 0; i < rows.Count; ++i)
            {
                int index = page * PageSize + i;
                if (index < variants.Count && rows[i].Hovered && !string.IsNullOrEmpty(variants[index].Properties))
                    description = variants[index].Name + ": " + variants[index].Properties;
            }
            hint.text = description;
        }

        private void Toggle(int slot)
        {
            int index = page * PageSize + slot;
            if (index < 0 || index >= variants.Count) return;
            var variant = variants[index];
            AmmoSelection.Shared.Set(variant.Key, !AmmoSelection.Shared.Includes(variant.Key));
            Redraw();
        }

        private void SetAll(bool include)
        {
            foreach (var variant in variants) AmmoSelection.Shared.Set(variant.Key, include);
            Redraw();
        }

        private void Redraw()
        {
            revision = AmmoSelection.Shared.Revision;
            enabledCount = 0;
            foreach (var variant in variants) if (AmmoSelection.Shared.Includes(variant.Key)) ++enabledCount;
            int pageCount = Math.Max(1, (variants.Count + PageSize - 1) / PageSize);
            page = Math.Max(0, Math.Min(page, pageCount - 1));
            title.text = (types.Count == 1 && variants.Count > 0 ? variants[0].Caliber : "Compatible ammo")
                + "  (" + enabledCount + "/" + variants.Count + ")";
            hint.text = "Choose which ammo variants to include.";
            for (int i = 0; i < rows.Count; ++i)
            {
                int index = page * PageSize + i;
                rows[i].gameObject.SetActive(index < variants.Count);
                if (index >= variants.Count) continue;
                var variant = variants[index];
                rows[i].Caption.text = (AmmoSelection.Shared.Includes(variant.Key) ? " [x] " : " [ ] ")
                    + (types.Count > 1 ? variant.Caliber + " - " : "") + variant.Name;
            }
            previous.gameObject.SetActive(page > 0);
            next.gameObject.SetActive(page + 1 < pageCount);
            pageText.text = (page + 1) + " / " + pageCount;
        }
    }
}
