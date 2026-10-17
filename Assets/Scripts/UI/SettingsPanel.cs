using System;
using AftershockIsle.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Ayarlar (parşömen panel): ses seviyesi, kamera açısı, tam ekran. Ana menü ve duraklatma menüsü aynı paneli kullanır.
    /// Ses çubuğu ve kapat X'i görselin içine çizili; kaydırıcı ve tıklama alanı tam onların üstüne oturtulur.
    /// Değerler GameSettings'e yazılır (PlayerPrefs + SettingsChanged olayı).
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        // Parşömen görselinin piksel ölçüsü ve içine çizili öğelerin yerleri (tasarım dosyasındaki konumlar).
        static readonly Vector2 Design = new Vector2(823f, 629f);
        const float SliderLeft = 222f;
        const float SliderRight = 728f;
        const float SliderY = 180f;
        static readonly Vector2 CloseCenter = new Vector2(756f, 60f);
        const float Scale = 0.9f;

        Action onClosed;
        RectTransform paper;
        Slider volume;
        Text volumeLabel;
        Text pitchValue;
        Text uiScaleValue;
        Text fullscreenValue;

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>Panelin kapandığı kare: aynı Esc/B basışı arkadaki menüyü de kapatmasın diye.</summary>
        public int ClosedFrame { get; private set; } = -1;

        /// <summary>Açık ya da bu karede kapandı (sahibi Esc/B'yi bu karede yok saymalı).</summary>
        public bool ConsumedBack => IsOpen || ClosedFrame == Time.frameCount;

        public static SettingsPanel Create(Transform canvas, Action onClosed)
        {
            var root = UiFactory.Rect("Ayarlar", canvas);
            UiFactory.Stretch(root);
            var panel = root.gameObject.AddComponent<SettingsPanel>();
            panel.onClosed = onClosed;
            panel.Build();
            root.gameObject.SetActive(false);
            return panel;
        }

        public void Open()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Refresh();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(volume.gameObject);
        }

        public void Close()
        {
            if (!IsOpen) return;
            ClosedFrame = Time.frameCount;
            gameObject.SetActive(false);
            onClosed?.Invoke();
        }

        void Update()
        {
            ModalMenu.MarkOpen();
            if (GameInput.MenuBackPressed || GameInput.EscapePressed) Close();
        }

        void Build()
        {
            var dim = UiFactory.Panel(transform, "Karartma", new Color(0f, 0f, 0f, 0.55f));
            UiFactory.Stretch(dim.rectTransform);

            var skin = GameAssets.Ui;
            var paperSprite = skin != null ? skin.settingsPanel : null;
            var paperImage = UiFactory.Image(transform, "Parşömen", paperSprite, paperSprite != null ? Color.white : UiFactory.Paper, true);
            paper = UiFactory.Place(paperImage.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, Design * Scale);

            // Ses çubuğu: çizili çizginin üstüne şeffaf kaydırıcı ve koyu topuz.
            float width = (SliderRight - SliderLeft) * Scale;
            volume = CreateSlider(paper, At(new Vector2((SliderLeft + SliderRight) * 0.5f, SliderY)), width);
            volume.onValueChanged.AddListener(value =>
            {
                GameSettings.Volume = value;
                RefreshLabels();
            });
            if (paperSprite == null) DrawSliderLine(width);

            volumeLabel = Ink(paper, At(new Vector2(475f, 245f)), new Vector2(520f, 40f), 26, TextAnchor.MiddleCenter);

            Ink(paper, At(new Vector2(250f, 310f)), new Vector2(300f, 50f), 30, TextAnchor.MiddleLeft).text = "Kamera Açısı";
            Small(paper, At(new Vector2(530f, 310f)), "-", () => StepPitch(-5f));
            pitchValue = Ink(paper, At(new Vector2(620f, 310f)), new Vector2(110f, 50f), 30, TextAnchor.MiddleCenter);
            Small(paper, At(new Vector2(710f, 310f)), "+", () => StepPitch(5f));

            Ink(paper, At(new Vector2(250f, 390f)), new Vector2(300f, 50f), 30, TextAnchor.MiddleLeft).text = "Arayüz Boyutu";
            Small(paper, At(new Vector2(530f, 390f)), "-", () => StepUiScale(-0.1f));
            uiScaleValue = Ink(paper, At(new Vector2(620f, 390f)), new Vector2(110f, 50f), 30, TextAnchor.MiddleCenter);
            Small(paper, At(new Vector2(710f, 390f)), "+", () => StepUiScale(0.1f));

            Ink(paper, At(new Vector2(250f, 470f)), new Vector2(300f, 50f), 30, TextAnchor.MiddleLeft).text = "Tam Ekran";
            var toggle = UiFactory.TextButton(paper, "", new Vector2(190f, 58f), ToggleFullscreen);
            UiFactory.Place((RectTransform)toggle.transform, new Vector2(0.5f, 0.5f), At(new Vector2(620f, 470f)), new Vector2(190f, 58f));
            fullscreenValue = toggle.GetComponentInChildren<Text>();

            var done = UiFactory.TextButton(paper, "Tamam", new Vector2(220f, 62f), Close, UiFactory.Good);
            UiFactory.Place((RectTransform)done.transform, new Vector2(0.5f, 0.5f), At(new Vector2(411f, 556f)), new Vector2(220f, 62f));

            // Görseldeki X; görsel yoksa görünür bir X butonu.
            if (paperSprite != null)
            {
                var close = UiFactory.HitArea(paper, "Kapat", new Vector2(90f, 90f), Close);
                UiFactory.Place((RectTransform)close.transform, new Vector2(0.5f, 0.5f), At(CloseCenter), new Vector2(90f, 90f));
            }
            else
            {
                var close = UiFactory.TextButton(paper, "X", new Vector2(64f, 64f), Close, UiFactory.Danger);
                UiFactory.Place((RectTransform)close.transform, new Vector2(0.5f, 0.5f), At(CloseCenter), new Vector2(64f, 64f));
            }

        }

        // Tasarım pikseli (sol üst köşe orijin) → parşömen merkezine göre yerel konum.
        static Vector2 At(Vector2 design) => new Vector2((design.x - Design.x * 0.5f) * Scale, (Design.y * 0.5f - design.y) * Scale);

        Slider CreateSlider(RectTransform parent, Vector2 position, float width)
        {
            var area = UiFactory.Image(parent, "Ses", null, new Color(1f, 1f, 1f, 0f), true);
            UiFactory.Place(area.rectTransform, new Vector2(0.5f, 0.5f), position, new Vector2(width, 54f));

            var handleArea = UiFactory.Rect("Topuz Alanı", area.transform);
            UiFactory.Stretch(handleArea);
            var handle = UiFactory.Image(handleArea, "Topuz", UiFactory.Circle, UiFactory.Accent, true);
            handle.type = Image.Type.Simple;
            handle.rectTransform.sizeDelta = new Vector2(44f, 44f);

            var slider = area.gameObject.AddComponent<Slider>();
            slider.targetGraphic = handle;
            slider.handleRect = handle.rectTransform;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            area.gameObject.AddComponent<UiPress>();
            return slider;
        }

        void DrawSliderLine(float width)
        {
            var line = UiFactory.Image(paper, "Çizgi", null, UiFactory.Ink);
            UiFactory.Place(line.rectTransform, new Vector2(0.5f, 0.5f), At(new Vector2((SliderLeft + SliderRight) * 0.5f, SliderY)), new Vector2(width, 10f));
            line.transform.SetSiblingIndex(volume.transform.GetSiblingIndex());
        }

        static Text Ink(RectTransform parent, Vector2 position, Vector2 size, int fontSize, TextAnchor align)
        {
            var label = UiFactory.Label(parent, "", fontSize, UiFactory.Ink, align, FontStyle.Bold, false);
            UiFactory.Place(label.rectTransform, new Vector2(0.5f, 0.5f), position, size);
            return label;
        }

        static void Small(RectTransform parent, Vector2 position, string text, Action onClick)
        {
            var button = UiFactory.TextButton(parent, text, new Vector2(64f, 58f), onClick, UiFactory.SlateLight, 30);
            UiFactory.Place((RectTransform)button.transform, new Vector2(0.5f, 0.5f), position, new Vector2(64f, 58f));
        }

        void StepUiScale(float delta)
        {
            GameSettings.UiScale += delta;
            RefreshLabels();
        }

        void StepPitch(float delta)
        {
            GameSettings.CameraPitch += delta;
            RefreshLabels();
        }

        void ToggleFullscreen()
        {
            GameSettings.Fullscreen = !GameSettings.Fullscreen;
            RefreshLabels();
        }

        void Refresh()
        {
            volume.SetValueWithoutNotify(GameSettings.Volume);
            RefreshLabels();
        }

        void RefreshLabels()
        {
            volumeLabel.text = $"Ses: %{Mathf.RoundToInt(GameSettings.Volume * 100f)}";
            pitchValue.text = $"{GameSettings.CameraPitch:0}°";
            uiScaleValue.text = $"%{Mathf.RoundToInt(GameSettings.UiScale * 100f)}";
            fullscreenValue.text = GameSettings.Fullscreen ? "Açık" : "Kapalı";
        }
    }
}
