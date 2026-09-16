using System;
using FistVR;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Gundomizer
{
    public sealed class RandomizerButton : FVRPointable, IPointerEnterHandler, IPointerExitHandler
    {
        internal RandomizerController Owner;
        internal bool Compatible;
        internal ButtonSurface Background;
        internal Texture2D RainbowTexture;
        internal ButtonIcon Icon;
        internal Button UiButton;
        internal Text Caption;
        internal Action<FVRViveHand> Handler;
        internal Func<bool> Ready;
        internal bool Rainbow = true;
        internal bool NeedsHeldContext;
        private float nextActivation;
        internal void Activate(FVRViveHand hand)
        {
            if (Time.unscaledTime < nextActivation || Owner == null) return;
            if (Owner.CanCancel(this))
            {
                nextActivation = Time.unscaledTime + .15f;
                Owner.CancelRoll();
                return;
            }
            if (!Owner.CanClick(false)) return;
            if (Compatible || NeedsHeldContext) Owner.RefreshHeldItem(hand);
            if (!(Ready == null ? Owner.CanClick(Compatible) : Ready())) return;
            nextActivation = Time.unscaledTime + 0.15f;
            if (Handler != null) Handler(hand); else Owner.Click(Compatible, hand);
        }
        private bool mouseHover;
        internal bool Hovered => m_isBeingPointedAt || mouseHover;

        public override void OnPoint(FVRViveHand hand)
        {
            base.OnPoint(hand);
            if (Owner != null && hand.CurrentInteractable == null && hand.Input.TriggerDown)
                Activate(hand);
        }

        public void OnPointerEnter(PointerEventData eventData) { mouseHover = true; }
        public void OnPointerExit(PointerEventData eventData) { mouseHover = false; }

        private void LateUpdate()
        {
            if (Owner == null) return;
            bool ready = Owner.CanCancel(this) || (Ready == null ? Owner.CanClick(Compatible) : Ready());
            // Native pointables invoke callbacks directly, so readiness is checked again by Click.
            UiButton.interactable = ready;
            if (Background != null)
            {
                Background.texture = ready && Rainbow ? RainbowTexture : Texture2D.whiteTexture;
                Background.uvRect = new Rect(ready && Rainbow ? Mathf.Repeat(-Time.unscaledTime * 0.13f, 1f) : 0f, 0f, 1f, 1f);
                var color = ready ? new Color(Hovered ? 0.8f : 0.53f, Hovered ? 0.8f : 0.53f, Hovered ? 0.8f : 0.53f)
                    : new Color(0.19f, 0.19f, 0.19f);
                color.a = 0.95f;
                if (!Rainbow && ready) color = Hovered ? new Color(0.3f, 0.35f, 0.4f, 1f) : new Color(0.13f, 0.16f, 0.19f, 1f);
                Background.color = color;
            }
            if (Icon != null) Icon.color = ready ? Color.white : new Color(0.52f, 0.52f, 0.52f);
        }

        private void OnDisable()
        {
            mouseHover = false;
            PointingHands.Clear();
            m_isBeingPointedAt = false;
        }
    }
}
