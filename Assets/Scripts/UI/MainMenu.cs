using AftershockIsle.Core;
using AftershockIsle.Save;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AftershockIsle.UI
{
    /// <summary>Başlangıç sahnesinin geçici IMGUI ana menüsü: devam et, yeni oyun, nasıl oynanır, çık. Fare, klavye ya da gamepad ile.</summary>
    public class MainMenu : MonoBehaviour
    {
        public const string GameScene = "Main";

        bool showHowTo;
        GUIStyle titleStyle;
        GUIStyle subtitleStyle;
        GUIStyle buttonStyle;
        GUIStyle bodyStyle;
        readonly MenuNav nav = new MenuNav();

        void Start()
        {
            Time.timeScale = 1f;
            nav.Reset();
        }

        void Update()
        {
            nav.Tick();
            if (GameInput.MenuBackPressed) showHowTo = false;
        }

        void OnGUI()
        {
            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 54,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            subtitleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
            };
            buttonStyle ??= new GUIStyle(GUI.skin.button) { fontSize = 20 };
            bodyStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };

            float cx = Screen.width * 0.5f;
            float y = Screen.height * 0.5f - 230f;

            GUI.Label(new Rect(0, y, Screen.width, 70), "AFTERSHOCK: ISLE", titleStyle);
            GUI.Label(new Rect(0, y + 70, Screen.width, 28), "Çöken binayı ayakta tut. Hayatta kal. Çatıdan tahliye ol.", subtitleStyle);
            y += 130f;

            const float width = 320f;
            nav.BeginDraw();

            bool hasSave = SaveManager.HasSave();
            if (hasSave)
            {
                Button(cx - width * 0.5f, ref y, width, "Devam Et", () =>
                {
                    SaveManager.PendingLoad = true;
                    SceneManager.LoadScene(GameScene);
                });
                Button(cx - width * 0.5f, ref y, width, "Yeni Oyun", () =>
                {
                    SaveManager.PendingLoad = false;
                    SceneManager.LoadScene(GameScene);
                });
            }
            else
            {
                Button(cx - width * 0.5f, ref y, width, "Oyuna Başla", () =>
                {
                    SaveManager.PendingLoad = false;
                    SceneManager.LoadScene(GameScene);
                });
            }

            Button(cx - width * 0.5f, ref y, width, showHowTo ? "Rehberi Gizle" : "Nasıl Oynanır", () => showHowTo = !showHowTo);
            Button(cx - width * 0.5f, ref y, width, "Çık", PauseMenu.QuitGame);
            nav.EndDraw();

            if (showHowTo) DrawHowTo(cx, y + 10f);
        }

        void Button(float x, ref float y, float width, string text, System.Action onActivate)
        {
            nav.Button(new Rect(x, y, width, 50), new GUIContent(text), onActivate, true, buttonStyle);
            y += 60f;
        }

        void DrawHowTo(float cx, float y)
        {
            const float width = 640f;
            var rect = new Rect(cx - width * 0.5f, y, width, 260f);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 14, rect.y + 10, rect.width - 28, rect.height - 20));
            GUILayout.Label(
                "Klavye: WASD karakteri yürütür (W / S merdivene çıkar / iner), F en yakın işi yapar, X durdurur, Tab karakter değiştirir.\n" +
                "Space zamanı durdurur, 1 / 2 hız. K yetenekler, H rehber. Fare de çalışır: sol tık seç, sağ tık iş ver.\n" +
                "Gamepad: sol çubuk yürü, A etkileş, B dur, LB / RB karakter, Y duraklat, X hız, sağ çubuk kamera, D-pad sağ yetenekler.\n\n" +
                "Üç tehdit binanın Yapısal Bütünlük barını eritir: artçı sarsıntı, su baskını ve yangın. Kolonları destekle, sığınaklara kaç, " +
                "şalteri yönet, suyu boşalt, yangını söndür.\n\n" +
                "Kazanmak için: tezgahı Kademe 3'e çıkar, çatıdaki telsizi tamir et, çatı molozunu temizle, çatı kolonlarını çelikle güçlendir " +
                "ve telsiz başında kesintisiz 60 saniye sinyal gönder.", bodyStyle);
            GUILayout.EndArea();
        }
    }
}
