using System;
using AftershockIsle.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// HUD öğelerinin (kaynak rozetleri, açlık / stres göstergesi, bütünlük çubuğu) üstüne gelince açılan ipucu balonu.
    /// Tek paylaşılan balon, imleci izler. Hedefte Graphic (raycast açık) olmalı; Attach bunu otomatik açar.
    /// </summary>
    public class UiTooltip : MonoBehaviour
    {
        const float Width = 380f;

        static UiTooltip instance;
        static RectTransform canvasRect;

        RectTransform panel;
        Text body;
        TooltipTarget current;

        /// <summary>target'a ipucu bağlar. text her gösterimde yeniden okunur (canlı değerler için).</summary>
        public static void Attach(GameObject target, Func<string> text)
        {
            if (target == null || text == null) return;
            foreach (var graphic in target.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = true;
            var hook = target.GetComponent<TooltipTarget>() ?? target.AddComponent<TooltipTarget>();
            hook.Text = text;
            Ensure();
        }

        static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("[İpucu Balonu]");
            instance = go.AddComponent<UiTooltip>();
        }

        void Awake()
        {
            var canvas = UiFactory.Canvas("[İpucu]", 70, transform);
            canvasRect = (RectTransform)canvas.transform;

            var plate = UiFactory.Image(canvas.transform, "Balon", null, new Color(0.07f, 0.08f, 0.11f, 0.96f));
            plate.raycastTarget = false;
            panel = plate.rectTransform;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0f, 1f);

            body = UiFactory.Label(panel, "", 17, UiFactory.Text, TextAnchor.UpperLeft, FontStyle.Normal, false);
            var rect = body.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(14f, -10f);
            rect.sizeDelta = new Vector2(Width - 28f, 100f);
            panel.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        internal void Enter(TooltipTarget target) => current = target;

        internal void Exit(TooltipTarget target)
        {
            if (current == target) current = null;
        }

        void LateUpdate()
        {
            if (panel == null) return;
            if (current == null || current.Text == null || ModalMenu.IsOpen)
            {
                if (panel.gameObject.activeSelf) panel.gameObject.SetActive(false);
                return;
            }

            body.text = current.Text();
            panel.sizeDelta = new Vector2(Width, body.preferredHeight + 22f);
            if (!panel.gameObject.activeSelf) panel.gameObject.SetActive(true);

            var half = canvasRect.rect.size * 0.5f;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, GameInput.PointerPosition, null, out var local);
            var size = panel.sizeDelta;
            var position = local + new Vector2(20f, -22f);
            if (position.x + size.x > half.x - 8f) position.x = local.x - size.x - 20f;
            if (position.y - size.y < -half.y + 8f) position.y = local.y + size.y + 22f;
            position.x = Mathf.Clamp(position.x, -half.x + 8f, half.x - size.x - 8f);
            position.y = Mathf.Clamp(position.y, -half.y + size.y + 8f, half.y - 8f);
            panel.anchoredPosition = position;
        }
    }

    public class TooltipTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Func<string> Text;

        public void OnPointerEnter(PointerEventData eventData) => Hook()?.Enter(this);
        public void OnPointerExit(PointerEventData eventData) => Hook()?.Exit(this);
        void OnDisable() => Hook()?.Exit(this);

        static UiTooltip Hook() => UnityEngine.Object.FindFirstObjectByType<UiTooltip>();
    }
}
