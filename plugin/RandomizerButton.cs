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
        internal RawImage Background;
        internal Texture2D RainbowTexture;
        internal ButtonIcon Icon;
        internal Button UiButton;
        private bool mouseHover;
        internal bool Hovered => m_isBeingPointedAt || mouseHover;

        public override void OnPoint(FVRViveHand hand)
        {
            base.OnPoint(hand);
            if (Owner != null && hand.CurrentInteractable == null && hand.Input.TriggerDown)
                Owner.Click(Compatible, hand);
        }

        public void OnPointerEnter(PointerEventData eventData) { mouseHover = true; }
        public void OnPointerExit(PointerEventData eventData) { mouseHover = false; }

        private void LateUpdate()
        {
            if (Owner == null) return;
            bool ready = Owner.CanClick(Compatible);
            // Native pointables invoke callbacks directly, so readiness is checked again by Click.
            UiButton.interactable = ready;
            if (Background != null)
            {
                Background.texture = ready ? RainbowTexture : Texture2D.whiteTexture;
                Background.uvRect = new Rect(Mathf.Repeat(-Time.unscaledTime * 0.13f, 1f), 0f, 1f, 1f);
                var color = ready ? new Color(Hovered ? 0.8f : 0.53f, Hovered ? 0.8f : 0.53f, Hovered ? 0.8f : 0.53f)
                    : new Color(0.19f, 0.19f, 0.19f);
                color.a = 0.95f;
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
