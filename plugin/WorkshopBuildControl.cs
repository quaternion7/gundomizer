using System;
using System.Collections;
using System.Collections.Generic;
using FistVR;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Gundomizer
{
    // One small control per native platform. All part changes still go through MW's menus.
    public sealed class WorkshopBuildControl : MonoBehaviour
    {
        private MonoBehaviour platform;
        private FVRQuickBeltSlot slot;
        private Coroutine roll;
        private bool cancel;
        private RandomizerButton button;
        private GameObject canvasObject;
        private Texture2D rainbow;
        private Text tooltip;
        private bool attempted;
        private float statusUntil;
        internal bool Busy { get; private set; }
        internal string Status { get; private set; }
        internal int Changed { get; private set; }

        internal void Refresh(MonoBehaviour source)
        {
            if (platform == null)
            {
                platform = source;
                slot = (FVRQuickBeltSlot)ModularWorkshopBridge.Read(platform, "QuickBeltSlot");
            }
            if (attempted) return;
            var screens = (IDictionary)ModularWorkshopBridge.Read(platform, "_UIScreens");
            foreach (GameObject screen in screens.Values)
            {
                if (screen == null) continue;
                var menu = screen.GetComponent(platform.GetType().Assembly.GetType("ModularWorkshop.ModularWorkshopUI"));
                var template = (GameObject)ModularWorkshopBridge.Read(menu, "HideButton");
                if (template == null) continue;
                attempted = true;
                try { CreateButton(template); }
                catch (Exception ex) { Plugin.Log.LogWarning("Could not create modular build control: " + ex); }
                break;
            }
        }

        private void CreateButton(GameObject template)
        {
            // Wolfie's platform exposes its cog as a SimpleSwitch sprite. Locate it by
            // component contract, without referencing the platform or AFCL assemblies.
            SpriteRenderer cog = null;
            var body = platform.GetComponentInParent<FVRPhysicalObject>();
            if (body == null) return;
            foreach (var component in body.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null || component.GetType().FullName != "AndrewFTW.SimpleSwitch") continue;
                cog = component.GetComponentInChildren<SpriteRenderer>(true);
                if (cog != null) break;
            }
            if (cog == null || cog.sprite == null) return;
            canvasObject = new GameObject("Gundomizer build control", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvasObject.layer = template.layer;
            var root = (RectTransform)canvasObject.transform;
            // The switch hides its instruction sprite until touched. Follow the switch
            // itself so the dice remains visible when that sprite is inactive.
            root.SetParent(cog.transform.parent, false);
            float width = cog.sprite.bounds.size.x;
            root.localPosition = cog.transform.localPosition + cog.transform.localRotation * Vector3.Scale(cog.transform.localScale, new Vector3(-width * 1.05f, 0, width * .3f));
            root.localRotation = cog.transform.localRotation * Quaternion.Euler(0, 180, 0);
            root.localScale = cog.transform.localScale * (width * .8f / 100);
            root.sizeDelta = new Vector2(100, 100);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            rainbow = RandomizerController.MakeRainbowTexture();
            button = ButtonFactory.Create(template, root, "Randomize build", false, 0, 0, 100, rainbow);
            var rect = (RectTransform)button.transform;
            rect.localScale = Vector3.one; rect.sizeDelta = new Vector2(100, 100);
            rect.localPosition = Vector3.zero;
            var box = button.GetComponent<BoxCollider>() ?? button.gameObject.AddComponent<BoxCollider>();
            box.center = Vector3.zero; box.size = new Vector3(100, 100, 8);
            button.Ready = () => isActiveAndEnabled && (Busy || HasModularTarget());
            button.Handler = hand => Activate();
            var tip = new GameObject("Build tooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            tip.layer = template.layer; tip.transform.SetParent(root, false);
            tip.GetComponent<Image>().color = new Color(.03f, .035f, .04f, .98f); tip.GetComponent<Image>().raycastTarget = false;
            var tipRect = (RectTransform)tip.transform; tipRect.sizeDelta = new Vector2(550, 100);
            tipRect.localPosition = new Vector3(0, 115, -5);
            tooltip = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<Text>();
            tooltip.gameObject.layer = template.layer; tooltip.rectTransform.SetParent(tip.transform, false);
            tooltip.rectTransform.anchorMin = Vector2.zero; tooltip.rectTransform.anchorMax = Vector2.one;
            tooltip.rectTransform.offsetMin = new Vector2(8, 5); tooltip.rectTransform.offsetMax = new Vector2(-8, -5);
            var nativeText = template.GetComponent<Text>() ?? template.GetComponentInChildren<Text>(true);
            tooltip.font = nativeText.font; tooltip.supportRichText = false; tooltip.fontSize = 34;
            tooltip.resizeTextForBestFit = true; tooltip.resizeTextMinSize = 24; tooltip.resizeTextMaxSize = 34;
            tooltip.alignment = TextAnchor.MiddleCenter; tooltip.color = Color.white; tooltip.raycastTarget = false;
            tip.SetActive(false);
        }

        private void LateUpdate()
        {
            if (tooltip == null || button == null) return;
            bool show = button.Hovered;
            tooltip.transform.parent.gameObject.SetActive(show);
            if (show) tooltip.text = Busy ? Status : "Randomize build" + (Time.unscaledTime < statusUntil ? "\n" + Status : "");
        }

        internal void Activate()
        {
            if (Busy) { cancel = true; return; }
            if (slot == null || slot.HeldObject == null || !HasModularTarget()) return;
            cancel = false; Busy = true; Changed = 0;
            Status = "Randomizing build… Click again to cancel.";
            roll = StartCoroutine(Randomize(slot.HeldObject));
        }

        private bool HasModularTarget()
        {
            if (slot == null || slot.HeldObject == null) return false;
            var target = (ModularWorkshopBridge.Read(platform, "ModularWeapon")
                ?? ModularWorkshopBridge.Read(platform, "ModularPhysicalObject")) as Component;
            return target != null && target.GetComponentInParent<FVRPhysicalObject>() == slot.HeldObject;
        }

        private IEnumerator Randomize(FVRInteractiveObject original)
        {
            // Track point instances, rather than menu objects: swapping a parent can replace
            // both the child point and its menu. Re-read the native graph after every change.
            var visited = new HashSet<object>();
            try
            {
                yield return null;
                while (!cancel && slot != null && slot.HeldObject == original && original != null)
                {
                    MonoBehaviour next;
                    object point;
                    try { next = Next(visited, out point); }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning("Could not inspect modular build: " + ex);
                        Status = "Could not inspect this build. Check logs.";
                        yield break;
                    }
                    if (next == null) { Status = "Randomized " + Changed + " slots."; yield break; }
                    // Bound repeated changes across snapshots of an unusual author graph.
                    if (visited.Count >= 256) { Status = "Build has too many changing slots. Roll stopped."; yield break; }
                    visited.Add(point);
                    string message;
                    if (!ModularWorkshopBridge.TryRandomizeBuildSlot(next, out message))
                    { Status = message; yield break; }
                    ++Changed;
                    Status = "Randomizing build… " + Changed + " slots. Click again to cancel.";
                    // Native destruction/menu creation settles before the next graph snapshot.
                    yield return null;
                }
                Status = "Build randomization cancelled.";
            }
            finally { Busy = false; roll = null; statusUntil = Time.unscaledTime + 3; }
        }

        private MonoBehaviour Next(HashSet<object> visited, out object selectedPoint)
        {
            MonoBehaviour result = null; selectedPoint = null; int nearest = int.MaxValue;
            var screens = (IDictionary)ModularWorkshopBridge.Read(platform, "_UIScreens");
            foreach (GameObject screen in screens.Values)
            {
                if (screen == null) continue;
                var menu = screen.GetComponent(platform.GetType().Assembly.GetType("ModularWorkshop.ModularWorkshopUI")) as MonoBehaviour;
                if (menu == null || !ModularWorkshopBridge.CanRandomizeSlot(menu, true)) continue;
                object target = ModularWorkshopBridge.Read(menu, "ModularWeapon") ?? ModularWorkshopBridge.Read(menu, "ModularPhysicalObject");
                if (target == null) continue;
                var points = (IDictionary)AccessTools.Property(target.GetType(), "AllAttachmentPoints").GetValue(target, null);
                string group = (string)ModularWorkshopBridge.Read(menu, "ModularPartsGroupID");
                if (!points.Contains(group)) continue;
                var point = points[group];
                if (visited.Contains(point) || (bool)ModularWorkshopBridge.Read(point, "IsPointDisabled")
                    || (bool)ModularWorkshopBridge.Read(point, "DisallowTakeAndHoldRandomization")) continue;
                // A child's actual mount is below its parent part in the native transform graph.
                int depth = 0;
                var mount = (Transform)ModularWorkshopBridge.Read(point, "ModularPartPoint");
                if (mount == null) continue;
                if (ModularWorkshopBridge.Read(menu, "PartType").ToString() == "SubAttachmentPoint")
                    for (var ancestor = mount; ancestor != null; ancestor = ancestor.parent) ++depth;
                if (depth < nearest) { nearest = depth; result = menu; selectedPoint = point; }
            }
            return result;
        }

        private void OnDisable()
        {
            if (roll != null) StopCoroutine(roll);
            roll = null; Busy = false;
            if (canvasObject != null) canvasObject.SetActive(false);
        }

        private void OnEnable() { if (canvasObject != null) canvasObject.SetActive(true); }

        private void OnDestroy()
        {
            if (canvasObject != null) Destroy(canvasObject);
            if (rainbow != null) Destroy(rainbow);
        }
    }
}
