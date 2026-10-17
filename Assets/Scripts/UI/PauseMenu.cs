using AftershockIsle.Core;
using AftershockIsle.Save;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Esc / Start ile açılan duraklatma menüsü (uGUI, koddan kurulur): devam, ayarlar, ana menü, kaydet, yeniden başla, çıkış.
    /// Açıkken oyun donar ve klavye kısayolları (Space/1/2) çalışmaz (TimeManager.Hold).
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        public const string MainMenuScene = "MainMenu";
        const float MenuScale = 0.9f;

        [SerializeField] GameSession session;

        bool open;
        float saveFeedbackUntil;
        string saveFeedback;
        bool saveFeedbackOk;

        GameObject root;
        SettingsPanel settings;
        GameObject resumeButton;
        GameObject saveButton;
        GameObject firstButton;
        Text feedback;

        public static bool IsOpen { get; private set; }

        void Awake()
        {
            if (session == null) session = FindFirstObjectByType<GameSession>();
        }

        void Start()
        {
            UiFactory.EnsureEventSystem();
            Build();
            root.SetActive(false);
        }

        void Update()
        {
            if (root == null) return;

            bool ended = session.Outcome != GameOutcome.None;
            feedback.text = Time.unscaledTime < saveFeedbackUntil ? saveFeedback : "";
            feedback.color = saveFeedbackOk ? UiFactory.Good : UiFactory.Danger;
            resumeButton.SetActive(!ended);
            saveButton.SetActive(!ended);

            if (settings.ConsumedBack) return;
            if (GameInput.EscapePressed) SetOpen(!open);
            if (!open) return;

            ModalMenu.MarkOpen();
            if (GameInput.MenuBackPressed && !ended) SetOpen(false);

            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null && GameInput.UsingGamepad)
                Select(FirstVisible());
        }

        void OnDisable()
        {
            if (open) SetOpen(false);
        }

        void OnDestroy() => IsOpen = false;

        void SetOpen(bool value)
        {
            if (open == value) return;
            open = value;
            IsOpen = value;
            root.SetActive(value);
            if (open) Select(FirstVisible());
            else settings.Close();

            if (open) TimeManager.Hold(this);
            else TimeManager.Release(this);
        }

        GameObject FirstVisible() => resumeButton.activeSelf ? resumeButton : firstButton;

        void Build()
        {
            var skin = GameAssets.Ui;
            var canvas = UiFactory.Canvas("Duraklatma Menüsü", 100, transform, MenuScale).transform;
            root = UiFactory.Rect("Menü", canvas).gameObject;
            UiFactory.Stretch((RectTransform)root.transform);

            var dim = UiFactory.Panel(root.transform, "Karartma", new Color(0f, 0f, 0f, 0.65f));
            UiFactory.Stretch(dim.rectTransform);

            if (skin != null && skin.pausedTitle != null)
            {
                var title = UiFactory.Image(root.transform, "Başlık", skin.pausedTitle, Color.white);
                UiFactory.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 300f), new Vector2(680f, 130f));
            }
            else
            {
                var title = UiFactory.Label(root.transform, "DURAKLATILDI", 84, UiFactory.Text);
                UiFactory.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 300f), new Vector2(900f, 120f));
            }

            // Yuvarlak butonlar: devam, ayarlar, ana menü.
            resumeButton = Round(skin != null ? skin.playButton : null, "Devam", -220f, () => SetOpen(false));
            var gear = Round(skin != null ? skin.settingsButton : null, "Ayarlar", 0f, () => settings.Open());
            Round(skin != null ? skin.closeButton : null, "Ana Menü", 220f, () => SceneManager.LoadScene(MainMenuScene));
            firstButton = gear;

            saveButton = Wide("Oyunu Kaydet", -110f, () =>
            {
                saveFeedbackOk = SaveManager.CanSave(out string reason) && SaveManager.Instance != null && SaveManager.Instance.SaveGame();
                saveFeedback = saveFeedbackOk ? "Oyun başarıyla kaydedildi." : reason ?? "Kayıt alınamadı.";
                saveFeedbackUntil = Time.unscaledTime + 3f;
            }, UiFactory.Good);
            Wide("Yeniden Başla", -195f, () => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex), null);
            Wide("Oyundan Çık", -280f, QuitGame, UiFactory.Danger);

            feedback = UiFactory.Label(root.transform, "", 28, UiFactory.Good);
            UiFactory.Place(feedback.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -360f), new Vector2(800f, 50f));

            settings = SettingsPanel.Create(canvas, () => Select(firstButton));
        }

        GameObject Round(Sprite sprite, string caption, float x, System.Action onClick)
        {
            var button = UiFactory.SpriteButton(root.transform, caption, sprite, new Vector2(140f, 140f), onClick, caption);
            UiFactory.Place((RectTransform)button.transform, new Vector2(0.5f, 0.5f), new Vector2(x, 120f), new Vector2(140f, 140f));
            var label = UiFactory.Label(root.transform, caption, 28, UiFactory.Text);
            UiFactory.Place(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(x, 25f), new Vector2(220f, 40f));
            return button.gameObject;
        }

        GameObject Wide(string text, float y, System.Action onClick, Color? color)
        {
            var button = UiFactory.TextButton(root.transform, text, new Vector2(400f, 70f), onClick, color);
            UiFactory.Place((RectTransform)button.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(400f, 70f));
            return button.gameObject;
        }

        static void Select(GameObject target)
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(target);
        }

        public static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
