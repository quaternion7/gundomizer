using System;
using FistVR;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Gundomizer
{
    internal static class ButtonFactory
    {
        internal const float DropdownWidth = 60f;

        internal static void LayoutFooter(RandomizerButton button, float width, float height)
        {
            var rect = (RectTransform)button.transform;
            var old = rect.rect.size;
            rect.sizeDelta = new Vector2(width / Mathf.Abs(rect.localScale.x), height / Mathf.Abs(rect.localScale.y));
            var box = button.GetComponent<BoxCollider>();
            if (box != null) box.size = new Vector3(box.size.x * rect.rect.width / old.x, box.size.y * rect.rect.height / old.y, box.size.z);
            if (button.Background.transform != rect)
            {
                button.Background.rectTransform.offsetMin = new Vector2(2, 2);
                button.Background.rectTransform.offsetMax = new Vector2(-2, -2);
            }
            if (button.Icon.Dropdown)
            {
                var icon = button.Icon.rectTransform;
                icon.anchorMin = icon.anchorMax = new Vector2(.5f, .5f);
                icon.sizeDelta = new Vector2(height * .36f / Mathf.Abs(rect.localScale.x), height * .36f / Mathf.Abs(rect.localScale.y));
                icon.anchoredPosition3D = new Vector3(0, 0, -.01f);
            }
        }

        internal static RandomizerButton Create(GameObject template, RectTransform parent, string text,
            bool compatible, float x, float y, float width, Texture2D rainbowTexture, RandomizerController owner = null)
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
            var ui = clone.GetComponent<Button>() ?? clone.AddComponent<Button>();
            ui.onClick = new Button.ButtonClickedEvent();
            ui.transition = Selectable.Transition.None;
            ui.interactable = true;
            ui.targetGraphic = background;
            var label = clone.GetComponent<Text>() ?? clone.GetComponentInChildren<Text>(true);
            if (label != null) { label.text = string.Empty; label.raycastTarget = false; }
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
            button.Owner = owner; button.Compatible = compatible; button.MaxPointingRange = range;
            button.Icon = icon; button.Background = background; button.RainbowTexture = rainbowTexture; button.UiButton = ui;
            ui.onClick.AddListener(() => button.Activate(null));
            clone.SetActive(true); // Mode visibility belongs to the shared root.
            return button;
        }
    }
}
