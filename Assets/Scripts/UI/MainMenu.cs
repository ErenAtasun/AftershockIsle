using AftershockIsle.Core;
using AftershockIsle.Save;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Başlangıç sahnesinin ana menüsü (uGUI, açılışta koddan kurulur): kayıt varsa Devam Et, Başla (yeni oyun), Nasıl Oynanır,
    /// ayarlar ve çıkış. Fare, klavye ya da gamepad ile gezilir; B / Esc açık paneli kapatır.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        public const string GameScene = "Main";
        const float MenuScale = 0.9f;

        GameObject howTo;
        SettingsPanel settings;
        GameObject firstButton;

        void Start()
        {
            Time.timeScale = 1f;
            UiFactory.EnsureEventSystem();
            Build();
            Select(firstButton);
        }

        void Update()
        {
            if (settings.ConsumedBack) return;

            if (howTo.activeSelf && (GameInput.MenuBackPressed || GameInput.EscapePressed))
            {
                howTo.SetActive(false);
                Select(firstButton);
            }

            // Gamepad'e geçilince odak kaybolmuşsa ilk butona dön.
            if (GameInput.UsingGamepad && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null)
                Select(howTo.activeSelf ? null : firstButton);
        }

        void Build()
        {
            var skin = GameAssets.Ui;
            var canvas = UiFactory.Canvas("Ana Menü", 0, transform, MenuScale).transform;

            var background = UiFactory.Panel(canvas, "Arka Plan", new Color(0.09f, 0.1f, 0.13f, 1f));
            UiFactory.Stretch(background.rectTransform);
            background.sprite = null;

            var title = UiFactory.Label(canvas, "AFTERSHOCK", 120, UiFactory.Accent);
            UiFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(1400f, 150f));
            var isle = UiFactory.Label(canvas, "I S L E", 54, UiFactory.Text);
            UiFactory.Place(isle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -205f), new Vector2(800f, 70f));
            var subtitle = UiFactory.Label(canvas, "Çöken binayı ayakta tut. Hayatta kal. Çatıdan tahliye ol.", 26, new Color(0.85f, 0.85f, 0.8f), outline: false);
            UiFactory.Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -275f), new Vector2(1200f, 40f));

            var column = UiFactory.Rect("Butonlar", canvas);
            UiFactory.Place(column, new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(600f, 600f));

            bool hasSave = SaveManager.HasSave();
            float y = hasSave ? 150f : 90f;
            if (hasSave)
            {
                var resume = UiFactory.SpriteButton(column, "Devam Et", skin != null ? skin.playButton : null, new Vector2(130f, 132f), Continue, "Devam Et");
                UiFactory.Place((RectTransform)resume.transform, new Vector2(0.5f, 0.5f), new Vector2(-150f, y + 40f), new Vector2(130f, 132f));
                var caption = UiFactory.Label(column, "DEVAM ET", 40, UiFactory.Text);
                UiFactory.Place(caption.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(70f, y + 40f), new Vector2(300f, 60f));
                firstButton = resume.gameObject;
                y -= 160f;
            }

            var start = UiFactory.SpriteButton(column, "Başla", skin != null ? skin.startButton : null, new Vector2(440f, 184f), NewGame,
                hasSave ? "Yeni Oyun" : "Başla");
            UiFactory.Place((RectTransform)start.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, y - 40f), new Vector2(440f, 184f));
            if (hasSave)
            {
                var hint = UiFactory.Label(column, "Yeni oyun başlatır", 20, new Color(0.8f, 0.8f, 0.75f), outline: false);
                UiFactory.Place(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, y - 145f), new Vector2(600f, 30f));
            }
            if (firstButton == null) firstButton = start.gameObject;

            var help = UiFactory.SpriteButton(column, "Nasıl Oynanır", skin != null ? skin.howToButton : null, new Vector2(400f, 208f), ShowHowTo, "Nasıl Oynanır");
            UiFactory.Place((RectTransform)help.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, y - 280f), new Vector2(400f, 208f));

            var corner = UiFactory.Rect("Köşe", canvas);
            UiFactory.Place(corner, new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(300f, 140f));
            var gear = UiFactory.SpriteButton(corner, "Ayarlar", skin != null ? skin.settingsButton : null, new Vector2(118f, 118f), OpenSettings, "Ayarlar");
            UiFactory.Place((RectTransform)gear.transform, new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(118f, 118f));
            var quit = UiFactory.SpriteButton(corner, "Çık", skin != null ? skin.closeButton : null, new Vector2(118f, 118f), PauseMenu.QuitGame, "Çık");
            UiFactory.Place((RectTransform)quit.transform, new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(118f, 118f));

            howTo = BuildHowTo(canvas, skin);
            settings = SettingsPanel.Create(canvas, () => Select(firstButton));
        }

        GameObject BuildHowTo(Transform canvas, UiSkin skin)
        {
            var root = UiFactory.Rect("Nasıl Oynanır Paneli", canvas);
            UiFactory.Stretch(root);
            var dim = UiFactory.Panel(root, "Karartma", new Color(0f, 0f, 0f, 0.6f));
            UiFactory.Stretch(dim.rectTransform);

            var panel = UiFactory.Panel(root, "Panel", UiFactory.Slate);
            UiFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180f, 700f));

            var heading = UiFactory.Label(panel.transform, "NASIL OYNANIR", 44, UiFactory.Accent);
            UiFactory.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(900f, 60f));

            var body = UiFactory.Label(panel.transform,
                "<b>Amaç:</b> Tezgahı Kademe 3'e çıkar, çatıdaki telsizi tamir et, çatı molozunu temizle, çatı kolonlarını çelikle " +
                "güçlendir ve telsiz başında kesintisiz 60 saniye sinyal gönder. Helikopter gelir, tahliye olursun.\n\n" +
                "<b>Tehditler:</b> Artçı sarsıntı, su baskını, yangın ve gaz kaçağı binanın Yapısal Bütünlük barını eritir. " +
                "Kolonları destekle, sarsıntıda yeşil Yaşam Üçgenlerine kaç, şalteri yönet, suyu pompala, yangını söndür.\n\n" +
                "<b>Gün:</b> Sabah üretim ve tamir, öğle dış sefer (yoldaş alabilirsin), gece tüketim. Her gece kişi başı 1 Yaşam Kiti.\n\n" +
                "<b>Klavye:</b> WASD yürü (W/S merdiven), F etkileş, X dur, Tab karakter, Space zamanı durdur, 1/2 hız, K yetenekler, " +
                "B yerleşim, H rehber, Esc menü. Fare: sol tık seç, sağ tık iş ver.\n" +
                "<b>Gamepad:</b> Sol çubuk yürü, A etkileş, B dur, LB/RB karakter, Y duraklat, X hız, sağ çubuk kamera, Start menü.",
                24, UiFactory.Text, TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(1060f, 520f));

            var close = UiFactory.SpriteButton(panel.transform, "Kapat", skin != null ? skin.closeButton : null, new Vector2(96f, 96f), () =>
            {
                root.gameObject.SetActive(false);
                Select(firstButton);
            }, "X");
            UiFactory.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(30f, 30f), new Vector2(96f, 96f));

            root.gameObject.SetActive(false);
            return root.gameObject;
        }

        void ShowHowTo()
        {
            howTo.SetActive(true);
            howTo.transform.SetAsLastSibling();
            Select(howTo.GetComponentInChildren<Button>().gameObject);
        }

        void OpenSettings() => settings.Open();

        static void Continue()
        {
            SaveManager.PendingLoad = true;
            SceneManager.LoadScene(GameScene);
        }

        static void NewGame()
        {
            SaveManager.PendingLoad = false;
            SceneManager.LoadScene(GameScene);
        }

        static void Select(GameObject target)
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(target);
        }
    }
}
