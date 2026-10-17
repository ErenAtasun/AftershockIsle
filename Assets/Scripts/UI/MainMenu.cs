using UnityEngine;
using UnityEngine.SceneManagement;

namespace AftershockIsle.UI
{
    /// <summary>Başlangıç sahnesinin geçici IMGUI ana menüsü: oyuna başla, nasıl oynanır, çık.</summary>
    public class MainMenu : MonoBehaviour
    {
        public const string GameScene = "Main";

        bool showHowTo;
        GUIStyle titleStyle;
        GUIStyle subtitleStyle;
        GUIStyle buttonStyle;
        GUIStyle bodyStyle;

        void Start() => Time.timeScale = 1f;

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
            y += 140f;

            const float width = 320f;
            if (Button(cx - width * 0.5f, ref y, width, "Oyuna Başla")) SceneManager.LoadScene(GameScene);
            if (Button(cx - width * 0.5f, ref y, width, showHowTo ? "Rehberi Gizle" : "Nasıl Oynanır")) showHowTo = !showHowTo;
            if (Button(cx - width * 0.5f, ref y, width, "Çık")) PauseMenu.QuitGame();

            if (showHowTo) DrawHowTo(cx, y + 10f);
        }

        bool Button(float x, ref float y, float width, string text)
        {
            bool clicked = GUI.Button(new Rect(x, y, width, 50), text, buttonStyle) && Event.current.button == 0;
            y += 60f;
            return clicked;
        }

        void DrawHowTo(float cx, float y)
        {
            const float width = 640f;
            var rect = new Rect(cx - width * 0.5f, y, width, 230f);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 14, rect.y + 10, rect.width - 28, rect.height - 20));
            GUILayout.Label(
                "Sol tık: karakter seç.   Sağ tık: boşluğa yürü / nesneye iş ver (Shift: sıraya ekle).\n" +
                "Space: zamanı durdur (duraklatmada emir verilebilir).   1 / 2: normal / hızlı.   WASD: kamera.   K: yetenekler.   H: kontrol rehberi.\n\n" +
                "Üç tehdit binanın Yapısal Bütünlük barını eritir: artçı sarsıntı, su baskını ve yangın. Kolonları destekle, sığınaklara kaç, " +
                "şalteri yönet, suyu boşalt, yangını söndür.\n\n" +
                "Kazanmak için: tezgahı Kademe 3'e çıkar, çatıdaki telsizi tamir et, çatı molozunu temizle, çatı kolonlarını çelikle güçlendir " +
                "ve telsiz başında kesintisiz 60 saniye sinyal gönder.", bodyStyle);
            GUILayout.EndArea();
        }
    }
}
