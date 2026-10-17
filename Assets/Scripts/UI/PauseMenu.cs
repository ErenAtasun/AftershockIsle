using AftershockIsle.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Esc ile açılan duraklatma menüsü: devam, yeniden başla, ana menü, çıkış.
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

        void Awake()
        {
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();
            if (session == null) session = FindFirstObjectByType<GameSession>();
        }

        void Update()
        {
            if (GameInput.EscapePressed) SetOpen(!open);
        }

        void OnDisable()
        {
            if (open) SetOpen(false);
        }

        void SetOpen(bool value)
        {
            if (open == value) return;
            open = value;

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

            // Menü açıkken arkadaki dünyaya tıklanmasın.
            UIBlocker.Add(new Rect(0, 0, Screen.width, Screen.height));

            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;

            const float width = 320f;
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height * 0.5f - 160f;

            GUI.Label(new Rect(x, y, width, 50), "DURAKLATILDI", titleStyle);
            y += 70f;

            bool ended = session.Outcome != GameOutcome.None;
            if (!ended && Button(ref y, x, width, "Devam Et")) SetOpen(false);
            if (Button(ref y, x, width, "Yeniden Başla")) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            if (Button(ref y, x, width, "Ana Menü")) SceneManager.LoadScene(MainMenuScene);
            if (Button(ref y, x, width, "Çık")) QuitGame();
        }

        bool Button(ref float y, float x, float width, string text)
        {
            bool clicked = GUI.Button(new Rect(x, y, width, 46), text, buttonStyle) && Event.current.button == 0;
            y += 56f;
            return clicked;
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
