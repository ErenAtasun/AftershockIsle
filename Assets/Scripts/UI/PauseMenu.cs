using AftershockIsle.Core;
using AftershockIsle.Save;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Esc / Start ile açılan duraklatma menüsü: devam, kaydet, yeniden başla, ana menü, çıkış. Gamepad ile gezilir.
    /// Açıkken oyun donar ve klavye kısayolları (Space/1/2) çalışmaz; oyun bitmişse zamana dokunmaz.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        public const string MainMenuScene = "MainMenu";

        [SerializeField] TimeManager timeManager;
        [SerializeField] GameSession session;

        bool open;
        TimeSpeed resumeSpeed;
        GUIStyle titleStyle;
        GUIStyle buttonStyle;
        GUIStyle noticeStyle;
        readonly MenuNav nav = new MenuNav();
        float saveFeedbackTimer;

        void Awake()
        {
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();
            if (session == null) session = FindFirstObjectByType<GameSession>();
        }

        void Update()
        {
            if (saveFeedbackTimer > 0f) saveFeedbackTimer -= Time.unscaledDeltaTime;
            if (GameInput.EscapePressed) SetOpen(!open);
            if (!open) return;

            nav.Tick();
            if (GameInput.MenuBackPressed && session.Outcome == GameOutcome.None) SetOpen(false);
        }

        void OnDisable()
        {
            if (open) SetOpen(false);
        }

        void SetOpen(bool value)
        {
            if (open == value) return;
            open = value;
            if (open) nav.Reset();

            if (session.Outcome != GameOutcome.None) return;

            if (open)
            {
                resumeSpeed = timeManager.Speed;
                timeManager.SetSpeed(TimeSpeed.Paused);
                timeManager.enabled = false;
            }
            else
            {
                timeManager.enabled = true;
                timeManager.SetSpeed(resumeSpeed);
            }
        }

        void OnGUI()
        {
            if (!open) return;

            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            buttonStyle ??= new GUIStyle(GUI.skin.button) { fontSize = 18 };
            noticeStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            // Menü açıkken arkadaki dünyaya tıklanmasın.
            UIBlocker.Add(new Rect(0, 0, Screen.width, Screen.height));

            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;

            const float width = 320f;
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height * 0.5f - 180f;

            GUI.Label(new Rect(x, y, width, 50), "DURAKLATILDI", titleStyle);
            y += 60f;

            if (saveFeedbackTimer > 0f)
            {
                var prevColor = GUI.color;
                GUI.color = new Color(0.4f, 1f, 0.4f);
                GUI.Label(new Rect(x, y, width, 30), "✓ Oyun Başarıyla Kaydedildi!", noticeStyle);
                GUI.color = prevColor;
                y += 35f;
            }

            nav.BeginDraw();
            bool ended = session.Outcome != GameOutcome.None;
            if (!ended)
            {
                Button(ref y, x, width, "Devam Et", () => SetOpen(false));
                Button(ref y, x, width, "Oyunu Kaydet", () =>
                {
                    if (SaveManager.Instance != null && SaveManager.Instance.SaveGame())
                        saveFeedbackTimer = 3f;
                });
            }
            Button(ref y, x, width, "Yeniden Başla", () => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex));
            Button(ref y, x, width, "Ana Menü", () => SceneManager.LoadScene(MainMenuScene));
            Button(ref y, x, width, "Çık", QuitGame);
            nav.EndDraw();
        }

        void Button(ref float y, float x, float width, string text, System.Action onActivate)
        {
            nav.Button(new Rect(x, y, width, 46), new GUIContent(text), onActivate, true, buttonStyle);
            y += 56f;
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
