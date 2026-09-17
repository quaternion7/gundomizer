using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FistVR;
using UnityEngine;
using UnityEngine.UI;

namespace Gundomizer
{
    internal sealed class CompatiblePanel
    {
        internal const float ToggleWidth = 60f;
        private const float Width = 1200f, Height = 1050f;
        private const int PageSize = 4;
        private readonly RandomizerController owner;
        private readonly GameObject template;
        private readonly RectTransform popup;
        private readonly RandomizerButton toggle, section, all, previous, next;
        private readonly List<RandomizerButton> kinds = new List<RandomizerButton>(), rows = new List<RandomizerButton>();
        private readonly Text heldLabel, hint, connectorLabel, summary, pageLabel;
        private readonly List<KeyValuePair<CompatibilityKind, int>> connectors = new List<KeyValuePair<CompatibilityKind, int>>();
        private readonly Dictionary<Transform, Vector3> topPositions = new Dictionary<Transform, Vector3>();
        private List<CompatibilityKind> available = new List<CompatibilityKind>();
        private Compatibility.Query query;
        private FVRPhysicalObject held;
        private string signature;
        private string displayedScope;
        private int page, revision = -1;
        internal bool IsOpen => popup.gameObject.activeSelf;
        internal bool HasChoices { get; private set; }
        internal string ScopeDescription => CompatibleSelection.Shared.AllItems ? "across all items" : owner.ScopeDescription;
        internal string Tooltip => toggle.Hovered ? "Choose compatible item types and search scope for " + Compatibility.Name(held) : null;

        internal CompatiblePanel(RandomizerController owner, RectTransform root, GameObject template,
            RandomizerButton roll, float left, float y)
        {
            this.owner = owner; this.template = template;
            toggle = owner.CloneButton(template, root, "Compatible choices", false, left + ToggleWidth * .5f, y, ToggleWidth);
            toggle.Icon.Dropdown = true; toggle.Icon.SetVerticesDirty(); toggle.Rainbow = false;
            toggle.NeedsHeldContext = true;
            toggle.Ready = () => owner.CanClick(true);
            toggle.Handler = hand => { if (IsOpen) Hide(); else { owner.CloseChoices(false); SetOpen(true); Redraw(); } };
            roll.Ready = () => owner.CanClick(true) && HasChoices;
            roll.Background.RoundRight = false; toggle.Background.RoundLeft = false;
            var bg = roll.Background.rectTransform; bg.offsetMax = new Vector2(0, bg.offsetMax.y);
            bg = toggle.Background.rectTransform; bg.offsetMin = new Vector2(0, bg.offsetMin.y);
            var seam = new GameObject("Group separator", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            seam.gameObject.layer = template.layer; seam.rectTransform.SetParent(toggle.transform, false);
            seam.rectTransform.anchorMin = Vector2.zero; seam.rectTransform.anchorMax = new Vector2(0, 1);
            seam.rectTransform.offsetMin = new Vector2(0, 4); seam.rectTransform.offsetMax = new Vector2(.75f, -4);
            seam.rectTransform.localPosition += new Vector3(0, 0, -.015f);
            seam.color = new Color(.65f, .68f, .7f, .6f); seam.raycastTarget = false;
            popup = (RectTransform)new GameObject("Gundomizer Compatible Choices", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(BoxCollider), typeof(FVRPointable)).transform;
            popup.gameObject.layer = template.layer; popup.SetParent(root, false);
            popup.anchorMin = popup.anchorMax = new Vector2(.5f, .5f); popup.pivot = Vector2.zero;
            popup.sizeDelta = new Vector2(Width, Height);
            popup.localPosition = new Vector3(left - ((RectTransform)roll.transform).rect.width * Mathf.Abs(roll.transform.localScale.x), y + 95f, -8f);
            popup.GetComponent<Image>().color = new Color(.025f, .03f, .04f, 1);
            var blocker = popup.GetComponent<BoxCollider>(); blocker.center = new Vector3(Width / 2, Height / 2, 2);
            blocker.size = new Vector3(Width, Height, 1);
            popup.GetComponent<FVRPointable>().MaxPointingRange = roll.MaxPointingRange;
            Label("Title", "Compatible choices", 32, 990, 1000, 70, 46);
            Button("Close", "X", 1125, 990, 90, hand => Hide());
            heldLabel = Label("Held", "", 32, 930, 1136, 60, 34);
            section = Button("Section", "Current section", 305, 850, 545, hand => SetScope(false));
            all = Button("All items", "All items", 895, 850, 545, hand => SetScope(true));
            hint = Label("Scope hint", "", 32, 790, 1136, 65, 30);
            Label("Include", "Include", 32, 735, 1136, 50, 34);
            for (int i = 0; i < 5; ++i)
            {
                int slot = i;
                kinds.Add(Button("Kind " + i, "", 305 + (i % 2) * 590, 675 - (i / 2) * 78, 545, hand => ToggleKind(slot)));
            }
            connectorLabel = Label("Connectors", "Connector choices", 32, 430, 1136, 50, 34);
            for (int i = 0; i < PageSize; ++i)
            {
                int slot = i;
                var row = Button("Connector " + i, "", Width / 2, 360 - i * 75, 1136, hand => ToggleConnector(slot));
                row.Caption.alignment = TextAnchor.MiddleLeft; rows.Add(row);
            }
            previous = Button("Previous", "<", 780, 95, 140, hand => { --page; Redraw(); });
            next = Button("Next", ">", 1090, 95, 140, hand => { ++page; Redraw(); });
            pageLabel = Label("Page", "", 860, 95, 160, 60, 32); pageLabel.alignment = TextAnchor.MiddleCenter;
            summary = Label("Summary", "", 32, 38, 1136, 62, 29);
            foreach (Transform child in popup)
                if (child != summary.transform && child != previous.transform && child != next.transform && child != pageLabel.transform)
                    topPositions.Add(child, child.localPosition);
            Hide();
        }

        internal void Refresh(FVRPhysicalObject target)
        {
            // This is called by the existing nearby, one-Hz hand poll, never every frame.
            var current = target == null ? null : Compatibility.Capture(target);
            if (target == held && signature == (current == null ? null : current.Signature)) return;
            if (target != held) Hide();
            held = target; query = current; signature = current == null ? null : current.Signature; page = 0;
            available = query == null ? new List<CompatibilityKind>() : query.AvailableKinds();
            Redraw();
        }
        internal void Update()
        {
            if (revision != CompatibleSelection.Shared.Revision || displayedScope != ScopeDescription) Redraw();
            section.Caption.text = (CompatibleSelection.Shared.AllItems ? "[ ] " : "[x] ") + (owner.IsTagMode ? "Current tags" : "Current section");
        }
        internal void Hide() { SetOpen(false); }
        private void SetOpen(bool open)
        { popup.gameObject.SetActive(open); toggle.Icon.rectTransform.localRotation = Quaternion.Euler(0, 0, open ? 180 : 0); }
        private void SetScope(bool value) { CompatibleSelection.Shared.SetScope(value); Redraw(); }
        private void ToggleKind(int slot)
        {
            if (slot >= available.Count) return;
            var kind = available[slot]; var choices = CompatibleSelection.Shared;
            choices.Set(kind, null, !choices.Includes(kind)); page = 0; Redraw();
        }
        private void ToggleConnector(int slot)
        {
            int index = page * PageSize + slot; if (index >= connectors.Count) return;
            var c = connectors[index]; var choices = CompatibleSelection.Shared;
            choices.Set(c.Key, c.Value, !choices.Includes(c.Key, c.Value)); Redraw();
        }
        private static string KindName(CompatibilityKind kind)
        { return kind == CompatibilityKind.Speedloader ? "Speedloaders" : kind == CompatibilityKind.Firearm ? "Firearms" : kind + "s"; }
        private static string ConnectorName(CompatibilityKind kind, int value)
        {
            var type = kind == CompatibilityKind.Attachment ? typeof(FVRFireArmAttachment).GetField("Type").FieldType
                : kind == CompatibilityKind.Magazine ? typeof(FVRFireArmMagazine).GetField("MagazineType").FieldType
                : typeof(FVRFireArmClip).GetField("ClipType").FieldType;
            string name = Enum.GetName(type, value);
            if (name == null) return "Custom connector " + value;
            if (name.StartsWith("m", StringComparison.Ordinal) && name.Length > 1 && char.IsUpper(name[1])) name = name.Substring(1);
            return Regex.Replace(name.Replace('_', ' '), "([a-z])([A-Z])", "$1 $2");
        }
        private void Redraw()
        {
            var choices = CompatibleSelection.Shared; revision = choices.Revision;
            displayedScope = ScopeDescription;
            heldLabel.text = "Held: " + Compatibility.Name(held);
            hint.text = choices.AllItems ? "All categories, including modular magazines under Firearms." : "Use the spawner's current section or selected tags.";
            all.Caption.text = (choices.AllItems ? "[x] " : "[ ] ") + "All items";
            connectors.Clear(); HasChoices = false;
            for (int i = 0; i < kinds.Count; ++i)
            {
                kinds[i].gameObject.SetActive(i < available.Count);
                if (i >= available.Count) continue;
                var kind = available[i]; bool include = choices.Includes(kind);
                kinds[i].Caption.text = (include ? "[x] " : "[ ] ") + KindName(kind);
                if (!include) continue;
                var values = query.Connectors(kind);
                if (values.Count == 0) HasChoices = true;
                foreach (int value in values)
                { connectors.Add(new KeyValuePair<CompatibilityKind, int>(kind, value)); if (choices.Includes(kind, value)) HasChoices = true; }
            }
            int pages = Math.Max(1, (connectors.Count + PageSize - 1) / PageSize); page = Math.Max(0, Math.Min(page, pages - 1));
            int rowCount = Math.Min(PageSize, Math.Max(0, connectors.Count - page * PageSize));
            float trim = (3 - (available.Count + 1) / 2) * 78 + (PageSize - rowCount) * 75;
            popup.sizeDelta = new Vector2(Width, Height - trim);
            var blocker = popup.GetComponent<BoxCollider>(); blocker.center = new Vector3(Width / 2, (Height - trim) / 2, 2);
            blocker.size = new Vector3(Width, Height - trim, 1);
            foreach (var position in topPositions) position.Key.localPosition = position.Value - Vector3.up * trim;
            float connectorY = 675 - ((available.Count + 1) / 2) * 78 - trim;
            connectorLabel.rectTransform.localPosition = new Vector3(32, connectorY, -1);
            connectorLabel.gameObject.SetActive(connectors.Count > 0);
            for (int i = 0; i < rows.Count; ++i)
            {
                int index = page * PageSize + i; rows[i].gameObject.SetActive(index < connectors.Count);
                if (index >= connectors.Count) continue;
                var c = connectors[index]; rows[i].transform.localPosition = new Vector3(Width / 2, connectorY - 65 - i * 75, -1);
                rows[i].Caption.text = (choices.Includes(c.Key, c.Value) ? " [x] " : " [ ] ") + c.Key + ": " + ConnectorName(c.Key, c.Value);
            }
            previous.gameObject.SetActive(page > 0); next.gameObject.SetActive(page + 1 < pages);
            pageLabel.text = pages > 1 ? (page + 1) + " / " + pages : "";
            summary.text = HasChoices ? "Next roll: compatible items " + ScopeDescription + "."
                : held == null ? "Hold an item to see its compatible types." : "No types selected or available. Enable a type to roll.";
        }

        private Text Label(string name, string value, float x, float y, float width, float height, int size)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<Text>();
            text.gameObject.layer = template.layer; text.rectTransform.SetParent(popup, false);
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = Vector2.zero; text.rectTransform.pivot = new Vector2(0, .5f);
            text.rectTransform.sizeDelta = new Vector2(width, height); text.rectTransform.localPosition = new Vector3(x, y, -1);
            text.font = template.GetComponent<Text>().font; text.fontSize = size; text.text = value; text.supportRichText = false;
            text.color = Color.white; text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; return text;
        }
        private RandomizerButton Button(string name, string value, float x, float y, float width, Action<FVRViveHand> handler)
        {
            var button = owner.CloneButton(template, popup, "Compatible " + name, false, x, y, width);
            var rect = (RectTransform)button.transform; rect.anchorMin = rect.anchorMax = Vector2.zero; rect.localPosition = new Vector3(x, y, -1);
            var collider = button.GetComponent<BoxCollider>(); collider.size = new Vector3(collider.size.x, collider.size.y * .55f, .25f);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, rect.sizeDelta.y * .55f);
            button.Icon.gameObject.SetActive(false); button.Rainbow = false; button.Ready = () => owner.CanClick(false); button.Handler = handler;
            var text = new GameObject("Caption", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<Text>();
            text.gameObject.layer = template.layer; text.rectTransform.SetParent(rect, false);
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(12, 0); text.rectTransform.offsetMax = new Vector2(-12, 0);
            text.rectTransform.localPosition += new Vector3(0, 0, -.02f);
            text.font = template.GetComponent<Text>().font; text.fontSize = 22; text.text = value; text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter; text.supportRichText = false; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; button.Caption = text; return button;
        }
    }
}
