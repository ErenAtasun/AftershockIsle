using System;
using AftershockIsle.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Çalışma anında uGUI arayüzü kurmak için yardımcılar: tuval, olay sistemi, plaka, yazı, buton, çubuk.
    /// Arayüz sahneye kaydedilmez, kod her açılışta kurar; sahneyi yeniden oluşturmak gerekmez.
    /// Görseller UiSkin'den gelir; eksikse kodda üretilen yuvarlak köşeli plakalara düşülür.
    /// Ölçüler 1920×1080 referansına göredir (CanvasScaler ekrana göre ölçekler).
    /// </summary>
    public static class UiFactory
    {
        public static readonly Vector2 Reference = new Vector2(1920f, 1080f);

        public static readonly Color Ink = new Color(0.07f, 0.07f, 0.08f, 1f);
        public static readonly Color Slate = new Color(0.16f, 0.17f, 0.2f, 0.94f);
        public static readonly Color SlateLight = new Color(0.27f, 0.29f, 0.33f, 0.97f);
        public static readonly Color Accent = new Color(1f, 0.8f, 0.22f, 1f);
        public static readonly Color Paper = new Color(0.92f, 0.86f, 0.68f, 1f);
        public static readonly Color Good = new Color(0.36f, 0.78f, 0.34f, 1f);
        public static readonly Color Danger = new Color(0.9f, 0.27f, 0.2f, 1f);
        public static readonly Color Text = new Color(0.96f, 0.95f, 0.9f, 1f);

        static Font font;
        static Sprite plate;
        static Sprite circle;

        public static Font Font => font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>Siyah kenarlı, yuvarlak köşeli beyaz plaka (Image.color ile boyanır, 9 dilimli).</summary>
        public static Sprite Plate => plate != null ? plate : plate = CreateRounded(48, 14, 4);

        /// <summary>Siyah kenarlı daire (kaydırıcı topuzu, nokta).</summary>
        public static Sprite Circle => circle != null ? circle : circle = CreateRounded(64, 32, 6);

        // ---------------------------------------------------------------- Kurulum

        /// <summary>
        /// Bütün tuvaller aynı ölçek kaynağını kullanır: ekran çözünürlüğü × oyuncunun arayüz boyutu ayarı (GameSettings.UiScale).
        /// designScale: yalnızca o tuvalin içeriğinin tasarım ölçüsü (menüler ve pencereler büyük tasarlandı, hepsi 0.9'a yakın tutulur).
        /// </summary>
        public static Canvas Canvas(string name, int sortingOrder, Transform parent = null, float designScale = 1f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            if (parent != null) go.transform.SetParent(parent, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<UiScaleSync>().Initialize(scaler, designScale);
            return canvas;
        }

        /// <summary>uGUI'nin fare, klavye ve gamepad girdisi için sahnede bir EventSystem olmalı (yeni Input System modülüyle).</summary>
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null || UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem));
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>anchor: ebeveyn içindeki bağlantı noktası (0–1); pivot da aynı nokta olur.</summary>
        public static RectTransform Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        public static RectTransform Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            return rect;
        }

        // ---------------------------------------------------------------- Parçalar

        /// <summary>sprite null ise kod plakası (dilimli) kullanılır.</summary>
        public static Image Image(Transform parent, string name, Sprite sprite, Color color, bool blocksClicks = false)
        {
            var rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite != null ? sprite : Plate;
            image.type = sprite != null ? UnityEngine.UI.Image.Type.Simple : UnityEngine.UI.Image.Type.Sliced;
            image.preserveAspect = sprite != null;
            image.color = color;
            image.raycastTarget = blocksClicks;
            return image;
        }

        public static Image Panel(Transform parent, string name, Color color) => Image(parent, name, null, color, true);

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter,
                                 FontStyle style = FontStyle.Bold, bool outline = true)
        {
            var rect = Rect("Yazı", parent);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = Font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.alignment = align;
            label.supportRichText = true;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;

            if (outline)
            {
                var effect = rect.gameObject.AddComponent<Outline>();
                effect.effectColor = new Color(0f, 0f, 0f, 0.85f);
                effect.effectDistance = new Vector2(Mathf.Max(1.5f, size / 18f), -Mathf.Max(1.5f, size / 18f));
            }
            return label;
        }

        /// <summary>Görsel buton (tasarım dosyasındaki hazır buton). Görsel yoksa yazılı plakaya düşer.</summary>
        public static Button SpriteButton(Transform parent, string name, Sprite sprite, Vector2 size, Action onClick, string fallbackLabel)
        {
            if (sprite == null) return TextButton(parent, fallbackLabel, size, onClick);

            var image = Image(parent, name, sprite, Color.white, true);
            image.rectTransform.sizeDelta = size;
            return MakeButton(image, onClick);
        }

        /// <summary>Taş plaka üstünde yazı.</summary>
        public static Button TextButton(Transform parent, string label, Vector2 size, Action onClick, Color? plateColor = null, int fontSize = 26)
        {
            var image = Image(parent, label, null, plateColor ?? SlateLight, true);
            image.rectTransform.sizeDelta = size;
            var text = Label(image.transform, label, fontSize, Text);
            Stretch(text.rectTransform, 6f);
            return MakeButton(image, onClick);
        }

        /// <summary>Görünmez tıklama alanı (görselin içine çizilmiş düğmeler için, ör. ayar panelindeki X).</summary>
        public static Button HitArea(Transform parent, string name, Vector2 size, Action onClick)
        {
            var image = Image(parent, name, null, new Color(1f, 1f, 1f, 0f), true);
            image.rectTransform.sizeDelta = size;
            var button = MakeButton(image, onClick);
            button.transition = Selectable.Transition.None;
            return button;
        }

        static Button MakeButton(Image image, Action onClick)
        {
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 0.85f);
            colors.selectedColor = new Color(1f, 1f, 0.8f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.7f);
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            image.gameObject.AddComponent<UiPress>();
            return button;
        }

        /// <summary>Arka plakalı doldurma çubuğu. Dönen Image'in fillAmount'u 0–1 ayarlanır.</summary>
        public static Image Bar(Transform parent, string name, Vector2 size, Color fill, out Text label, int fontSize = 20)
        {
            var back = Image(parent, name, null, new Color(0.08f, 0.08f, 0.1f, 0.92f));
            back.rectTransform.sizeDelta = size;

            var fillImage = Image(back.transform, "Dolgu", null, fill);
            Stretch(fillImage.rectTransform, 4f);
            fillImage.type = UnityEngine.UI.Image.Type.Filled;
            fillImage.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            fillImage.fillOrigin = 0;
            fillImage.preserveAspect = false;

            label = Label(back.transform, "", fontSize, Text);
            Stretch(label.rectTransform, 2f);
            return fillImage;
        }

        // ---------------------------------------------------------------- Kod içi görseller

        // Kenarı siyah, içi beyaz yuvarlak köşeli kare; Image rengi yalnızca içi boyar gibi görünür (siyah siyah kalır).
        static Sprite CreateRounded(int size, int radius, int border)
        {
            var texture = RoundedTexture(size, radius, border, Color.white, Color.black);
            float slice = radius + 2;
            return Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(slice, slice, slice, slice));
        }

        /// <summary>Kenarlı, yuvarlak köşeli doku (IMGUI teması da kullanır). Kenar ile iç arası yumuşatılır.</summary>
        public static Texture2D RoundedTexture(int size, int radius, int border, Color fill, Color edge)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "UiRounded",
                hideFlags = HideFlags.DontSave,
            };

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = RoundedDistance(x + 0.5f, y + 0.5f, size, radius);
                    float alpha = Mathf.Clamp01(0.5f - d);
                    float inner = Mathf.Clamp01(0.5f - (d + border));
                    var color = Color.Lerp(edge, fill, inner);
                    color.a *= alpha;
                    pixels[y * size + x] = color;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>
        /// Arayüzün tasarım biriminin (1920×1080 referansında bir birim) ekranda kaç piksel olduğu: çözünürlük × arayüz boyutu ayarı.
        /// uGUI göstergesi ve ölçekli IMGUI panelleri (UiTheme) bu değeri kullanır, böylece her şey aynı boyutta görünür.
        /// </summary>
        public static float CanvasScale =>
            Mathf.Sqrt(Screen.width / Reference.x * (Screen.height / Reference.y)) * GameSettings.UiScale;

        // Yuvarlak köşeli karenin kenarına işaretli uzaklık (içeride negatif).
        static float RoundedDistance(float x, float y, int size, int radius)
        {
            float half = size * 0.5f;
            float qx = Mathf.Abs(x - half) - (half - radius);
            float qy = Mathf.Abs(y - half) - (half - radius);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }
    }

    /// <summary>Butona canlılık: üstüne gelince ya da gamepad ile seçilince büyür, basınca küçülür (gerçek zamanla).</summary>
    public class UiPress : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        bool hovered;
        bool selected;
        bool pressed;
        Selectable selectable;

        void Awake() => selectable = GetComponent<Selectable>();

        public void OnPointerEnter(PointerEventData eventData) => hovered = true;
        public void OnPointerExit(PointerEventData eventData) => hovered = false;
        public void OnPointerDown(PointerEventData eventData) => pressed = true;
        public void OnPointerUp(PointerEventData eventData) => pressed = false;
        public void OnSelect(BaseEventData eventData) => selected = true;
        public void OnDeselect(BaseEventData eventData) => selected = false;

        void OnDisable()
        {
            hovered = selected = pressed = false;
            transform.localScale = Vector3.one;
        }

        void Update()
        {
            bool interactable = selectable == null || selectable.IsInteractable();
            float target = !interactable ? 1f : pressed ? 0.94f : hovered || selected ? 1.07f : 1f;
            float scale = Mathf.MoveTowards(transform.localScale.x, target, 1.6f * Time.unscaledDeltaTime);
            transform.localScale = new Vector3(scale, scale, 1f);
        }
    }

    /// <summary>Tuvalin referans çözünürlüğünü arayüz boyutu ayarına bağlar; ayar değişince anında günceller.</summary>
    public class UiScaleSync : MonoBehaviour
    {
        CanvasScaler scaler;
        float designScale = 1f;

        public void Initialize(CanvasScaler target, float design)
        {
            scaler = target;
            designScale = Mathf.Max(0.1f, design);
            Apply();
        }

        void OnEnable() => EventBus.Subscribe<SettingsChanged>(OnSettingsChanged);
        void OnDisable() => EventBus.Unsubscribe<SettingsChanged>(OnSettingsChanged);
        void OnSettingsChanged(SettingsChanged evt) => Apply();

        void Apply()
        {
            if (scaler != null) scaler.referenceResolution = UiFactory.Reference / (designScale * GameSettings.UiScale);
        }
    }
}
