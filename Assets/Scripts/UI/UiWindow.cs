using System;
using AftershockIsle.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Oyun içi pencerelerin (tezgah, harita, yetenek ağacı) ortak iskeleti: karartma, ortada panel, başlık, kapat X'i,
    /// kaydırılabilir içerik ve alt buton satırı. Pencereler ortak bir tuvalde (HUD'un üstünde, duraklatma menüsünün altında) durur.
    /// Gamepad ile seçilen öğe kaydırma alanının dışındaysa görünür olana kadar kaydırılır.
    /// </summary>
    public class UiWindow
    {
        const float HeaderHeight = 58f;
        const float FooterHeight = 66f;
        const float WindowScale = 0.9f;

        static Canvas shared;

        public readonly GameObject Root;
        public readonly RectTransform Panel;
        public readonly RectTransform Content;
        public readonly RectTransform Footer;
        public readonly Text Title;
        readonly ScrollRect scroll;

        public bool IsOpen => Root.activeSelf;

        /// <summary>
        /// scrolling false ise içerik kaydırma alanına değil doğrudan panele konur (sabit yerleşimli pencereler).
        /// closable false ise kapat X'i yoktur (seçim yapılmadan kapanmayan olay pencereleri).
        /// </summary>
        public UiWindow(string name, Vector2 size, Action onClose, bool scrolling = true, bool closable = true)
        {
            if (shared == null)
            {
                UiFactory.EnsureEventSystem();
                shared = UiFactory.Canvas("[Pencereler]", 50, null, WindowScale);
            }

            Root = UiFactory.Rect(name, shared.transform).gameObject;
            UiFactory.Stretch((RectTransform)Root.transform);
            var dim = UiFactory.Panel(Root.transform, "Karartma", new Color(0f, 0f, 0f, 0.5f));
            UiFactory.Stretch(dim.rectTransform);

            var panel = UiFactory.Panel(Root.transform, "Panel", new Color(0.13f, 0.14f, 0.17f, 0.98f));
            Panel = UiFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, size);

            Title = UiFactory.Label(Panel, "", 24, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.Place(Title.rectTransform, new Vector2(0f, 1f), new Vector2(22f, -10f), new Vector2(size.x - 110f, 40f));

            if (closable)
            {
                var skin = GameAssets.Ui;
                var close = UiFactory.SpriteButton(Panel, "Kapat", skin != null ? skin.closeButton : null, new Vector2(54f, 54f), () => onClose?.Invoke(), "X");
                UiFactory.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(-10f, -6f), new Vector2(54f, 54f));
            }

            var footer = UiFactory.Rect("Alt Satır", Panel);
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.pivot = new Vector2(0.5f, 0f);
            footer.offsetMin = new Vector2(18f, 12f);
            footer.offsetMax = new Vector2(-18f, 12f + FooterHeight - 18f);
            var row = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 12f;
            row.childAlignment = TextAnchor.MiddleRight;
            row.childControlWidth = false;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            Footer = footer;

            if (scrolling)
            {
                var viewport = UiFactory.Rect("Görünüm", Panel);
                viewport.anchorMin = Vector2.zero;
                viewport.anchorMax = Vector2.one;
                viewport.offsetMin = new Vector2(16f, FooterHeight);
                viewport.offsetMax = new Vector2(-16f, -HeaderHeight);
                viewport.gameObject.AddComponent<RectMask2D>();
                var hit = viewport.gameObject.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0f);

                Content = UiFactory.Rect("İçerik", viewport);
                Content.anchorMin = new Vector2(0f, 1f);
                Content.anchorMax = new Vector2(1f, 1f);
                Content.pivot = new Vector2(0.5f, 1f);
                Content.offsetMin = Vector2.zero;
                Content.offsetMax = Vector2.zero;
                var layout = Content.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 8f;
                layout.padding = new RectOffset(4, 4, 4, 4);
                layout.childControlHeight = true;
                layout.childControlWidth = true;
                layout.childForceExpandHeight = false;
                Content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                scroll = viewport.gameObject.AddComponent<ScrollRect>();
                scroll.viewport = viewport;
                scroll.content = Content;
                scroll.horizontal = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 30f;
            }
            else
            {
                Content = UiFactory.Rect("İçerik", Panel);
                Content.anchorMin = Vector2.zero;
                Content.anchorMax = Vector2.one;
                Content.offsetMin = new Vector2(16f, FooterHeight);
                Content.offsetMax = new Vector2(-16f, -HeaderHeight);
            }

            // Her açılışta hafifçe büyüyerek belirir.
            Root.AddComponent<CanvasGroup>();
            Root.AddComponent<UiPop>().Target = Panel;

            Root.SetActive(false);
        }

        public void Show(bool visible)
        {
            if (Root.activeSelf == visible) return;
            Root.SetActive(visible);
            if (visible)
            {
                Root.transform.SetAsLastSibling();
                if (scroll != null) scroll.verticalNormalizedPosition = 1f;
            }
        }

        /// <summary>Açıkken her kare çağrılır: dünya girdisini kapatır, gamepad odağını korur ve görünür tutar.</summary>
        public void Tick(GameObject fallbackSelection)
        {
            ModalMenu.MarkOpen();
            var events = EventSystem.current;
            if (events == null) return;

            var selected = events.currentSelectedGameObject;
            if ((selected == null || !selected.activeInHierarchy) && GameInput.UsingGamepad && fallbackSelection != null)
                events.SetSelectedGameObject(fallbackSelection);

            if (scroll != null && selected != null && selected.transform.IsChildOf(Content)) KeepVisible((RectTransform)selected.transform);
        }

        void KeepVisible(RectTransform target)
        {
            var viewport = scroll.viewport;
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            float top = viewport.InverseTransformPoint(corners[1]).y;
            float bottom = viewport.InverseTransformPoint(corners[0]).y;
            float viewTop = viewport.rect.yMax;
            float viewBottom = viewport.rect.yMin;

            float shift = top > viewTop ? top - viewTop : bottom < viewBottom ? bottom - viewBottom : 0f;
            if (Mathf.Abs(shift) < 0.5f) return;
            Content.anchoredPosition -= new Vector2(0f, shift);
        }

        public void ClearContent()
        {
            for (int i = Content.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(Content.GetChild(i).gameObject);
        }

        public void ClearFooter()
        {
            for (int i = Footer.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(Footer.GetChild(i).gameObject);
        }

        /// <summary>İçeriğe sabit yükseklikte bir satır ekler (kaydırmalı pencerede düzen yerleştirir).</summary>
        public RectTransform Row(float height, Color? background = null)
        {
            var image = UiFactory.Image(Content, "Satır", null, background ?? new Color(1f, 1f, 1f, 0.05f));
            var element = image.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
            return image.rectTransform;
        }

        public Text Heading(string text)
        {
            var row = UiFactory.Rect("Başlık", Content);
            var element = row.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 34f;
            var label = UiFactory.Label(row, text, 19, UiFactory.Accent, TextAnchor.LowerLeft);
            UiFactory.Stretch(label.rectTransform, 4f);
            return label;
        }

        public Button FooterButton(string text, Action onClick, Color? color = null, float width = 220f)
        {
            return UiFactory.TextButton(Footer, text, new Vector2(width, 46f), onClick, color, 18);
        }
    }
}
