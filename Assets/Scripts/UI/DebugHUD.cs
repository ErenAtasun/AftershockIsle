using AftershockIsle.Building;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Geliştirici test paneli: yalnızca editörde ve development build'de var, oyuncunun build'ine girmez.
    /// F1 ile açılır/kapanır. Oyuncunun gördüğü bilgiler GameHud'dadır.
    /// </summary>
    public class DebugHUD : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        bool open;
        DayCycle dayCycle;

        void JumpToPhase(DayPhase phase)
        {
            if (dayCycle == null) dayCycle = FindFirstObjectByType<DayCycle>();
            if (dayCycle != null) dayCycle.JumpToPhase(phase);
        }

        static void DebugButton(string label, DebugAction action)
        {
            if (GUILayout.Button(label, GUILayout.Height(26f))) GameInput.TriggerDebug(action);
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame) open = !open;
        }

        void OnGUI()
        {
            UiTheme.Apply();
            float top = 200f;

            if (!open)
            {
                GUI.Label(new Rect(16f, top, 220f, 22f), "F1: test paneli");
                return;
            }

            var rect = new Rect(16f, top, 360f, 452f);
            UIBlocker.Add(rect);
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("TEST PANELİ (F1)");
            GUILayout.Label($"Zaman: {Time.timeScale:0.##}x — {Core.TimeManager.Describe()}");

            // Afetler
            GUILayout.BeginHorizontal();
            DebugButton("Artçı Sarsıntı", DebugAction.Quake);
            DebugButton("Yangın", DebugAction.Fire);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DebugButton("Gaz Kaçağı", DebugAction.Gas);
            DebugButton("Boru Patlat", DebugAction.Flood);
            GUILayout.EndHorizontal();

            // Bina
            GUILayout.BeginHorizontal();
            DebugButton("Bütünlük -10", DebugAction.Damage);
            DebugButton("Kolon Çatlat", DebugAction.Crack);
            GUILayout.EndHorizontal();

            // Kaynak, kazazede, kamera
            GUILayout.BeginHorizontal();
            DebugButton("Kaynak +5, Puan +1", DebugAction.Resources);
            DebugButton("Kazazede Ekle", DebugAction.SpawnSurvivor);
            GUILayout.EndHorizontal();
            DebugButton("Kamera Eğimini Değiştir", DebugAction.CameraPitch);

            if (GUILayout.Button(PlacementController.IsPlacementActive ? "Yerleşim Modunu Kapat [B]" : "Yerleşim / Izgara Modu [B]", GUILayout.Height(26f))
                && PlacementController.Instance != null)
                PlacementController.Instance.ToggleBuildMode();

            if (GUILayout.Button("Kapı Olayını Tetikle", GUILayout.Height(26f)) && NarrativeEventSystem.Instance != null)
                NarrativeEventSystem.Instance.TriggerNextDoorEncounter();

            if (GUILayout.Button("Havayı Değiştir", GUILayout.Height(26f)) && WeatherSystem.Instance != null)
                WeatherSystem.Instance.SetWeather((WeatherType)(((int)WeatherSystem.Current + 1) % 5));

            // Gün değişmeden faza atlar: gece-gündüz göstergesi ve gece olayları birlikte değişir.
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Öğlene Al", GUILayout.Height(26f))) JumpToPhase(DayPhase.Noon);
            if (GUILayout.Button("Akşama Al", GUILayout.Height(26f))) JumpToPhase(DayPhase.Night);
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
#endif
    }
}
