using FistVR;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Gundomizer
{
    // Lifecycle and refresh follow the native menu; no scene-wide polling is needed.
    public sealed class WorkshopSlotControl : MonoBehaviour
    {
        private MonoBehaviour menu;
        private RandomizerButton button;
        private Texture2D rainbow;
        private Text tooltip;
        private bool ready;
        private string status;
        private float statusUntil;
        private Bounds? reservedLayout;

        internal void Refresh(MonoBehaviour source)
        {
            menu = source;
            bool visible = (bool)Read("_isShowingUI");
            if (!visible) { if (button != null) button.gameObject.SetActive(false); if (tooltip != null) tooltip.gameObject.SetActive(false); return; }
            var template = (GameObject)Read("HideButton");
            var nativeButton = template == null ? null : template.GetComponent<FVRPointableButton>();
            if (nativeButton == null || nativeButton.Image == null) return;
            var parent = template.transform.parent as RectTransform;
            if (parent == null) return;
            if (button == null)
            {
                rainbow = RandomizerController.MakeRainbowTexture();
                button = ButtonFactory.Create(template, parent, "Random modular slot", false, 0, 0, 1, rainbow);
                button.Ready = () => ready && menu != null && button.gameObject.activeInHierarchy && !ModularWorkshopBridge.IsBusy(menu);
                button.Handler = hand =>
                {
                    bool success = (bool)Read("_isShowingSkins")
                        ? ModularWorkshopBridge.TryRandomizeSkin(menu, out status)
                        : ModularWorkshopBridge.TryRandomizeSlot(menu, out status);
                    if (success) status = null;
                    statusUntil = success ? 0 : Time.unscaledTime + 2;
                    Refresh(menu);
                };
                var text = new GameObject("Gundomizer slot tooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                text.layer = template.layer; text.transform.SetParent(parent, false);
                text.GetComponent<Image>().color = new Color(.03f, .035f, .04f, .98f); text.GetComponent<Image>().raycastTarget = false;
                tooltip = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<Text>();
                tooltip.gameObject.layer = template.layer; tooltip.rectTransform.SetParent(text.transform, false);
                tooltip.rectTransform.anchorMin = Vector2.zero; tooltip.rectTransform.anchorMax = Vector2.one;
                tooltip.rectTransform.offsetMin = new Vector2(5, 3); tooltip.rectTransform.offsetMax = new Vector2(-5, -3);
                var nativeText = template.GetComponent<Text>() ?? template.GetComponentInChildren<Text>(true);
                tooltip.font = nativeText.font; tooltip.supportRichText = false;
                tooltip.resizeTextForBestFit = true;
                tooltip.alignment = TextAnchor.MiddleCenter; tooltip.color = Color.white; tooltip.raycastTarget = false;
            }
            if (!reservedLayout.HasValue)
            {
                var bounds = ReservedBounds(parent, template);
                float bottom = bounds.min.y;
                // Reserve hidden controls too: skins and pagination must not move the dice.
                foreach (string field in new[] { "ShowSkinsButton", "HideSkinsButton", "BackButton", "NextButton" })
                {
                    var obj = (GameObject)Read(field);
                    if (obj != null) bottom = Mathf.Min(bottom, ReservedBounds(parent, obj).min.y);
                }
                foreach (var obj in (GameObject[])Read("PartButtons"))
                    if (obj != null) bottom = Mathf.Min(bottom, ReservedBounds(parent, obj).min.y);
                reservedLayout = new Bounds(new Vector3(bounds.center.x, bottom, template.transform.localPosition.z), bounds.size);
            }
            float size = reservedLayout.Value.size.x;
            var rect = (RectTransform)button.transform;
            float oldHeight = rect.rect.height, oldWidth = rect.rect.width;
            rect.sizeDelta = new Vector2(size / Mathf.Abs(rect.localScale.x), size / Mathf.Abs(rect.localScale.y));
            rect.localPosition = reservedLayout.Value.center + Vector3.down * size * .65f;
            var collider = button.GetComponent<BoxCollider>();
            if (collider != null) collider.size = new Vector3(collider.size.x * rect.rect.width / oldWidth,
                collider.size.y * rect.rect.height / oldHeight, collider.size.z);
            ready = (bool)Read("_isShowingSkins") ? ModularWorkshopBridge.CanRandomizeSkin(menu) : ModularWorkshopBridge.CanRandomizeSlot(menu);
            button.gameObject.SetActive(true);
            var tipRect = (RectTransform)tooltip.transform.parent;
            tipRect.anchorMin = tipRect.anchorMax = new Vector2(.5f, .5f); tipRect.pivot = new Vector2(1, .5f);
            tipRect.sizeDelta = new Vector2(size * 4, size * .8f);
            tooltip.resizeTextMinSize = Mathf.Max(1, Mathf.RoundToInt(size * .18f));
            tooltip.resizeTextMaxSize = Mathf.Max(tooltip.resizeTextMinSize, Mathf.RoundToInt(size * .3f));
            tooltip.fontSize = tooltip.resizeTextMaxSize;
            tipRect.localPosition = rect.localPosition + new Vector3(-size * .65f, 0, -.02f);
            tipRect.gameObject.SetActive(false);
        }

        private object Read(string name) => ModularWorkshopBridge.Read(menu, name);
        private static Bounds ReservedBounds(RectTransform parent, GameObject control)
        {
            var bounds = new Bounds(parent.InverseTransformPoint(control.transform.position), Vector3.zero);
            var corners = new Vector3[4];
            foreach (var rect in control.GetComponentsInChildren<RectTransform>(true))
            {
                rect.GetWorldCorners(corners);
                foreach (var corner in corners) bounds.Encapsulate(parent.InverseTransformPoint(corner));
            }
            return bounds;
        }
        private void LateUpdate()
        {
            if (tooltip == null || button == null) return;
            bool show = button.gameObject.activeInHierarchy && button.Hovered;
            tooltip.transform.parent.gameObject.SetActive(show);
            if (show) tooltip.text = Time.unscaledTime < statusUntil ? status
                : "Randomize " + (((Text)Read("DisplayNameText"))?.text ?? (string)Read("ModularPartsGroupID")) + ((bool)Read("_isShowingSkins") ? " skin" : " part");
        }
        private void OnDestroy()
        {
            if (button != null) Object.Destroy(button.gameObject);
            if (tooltip != null) Object.Destroy(tooltip.transform.parent.gameObject);
            if (rainbow != null) Object.Destroy(rainbow);
        }
    }
}
