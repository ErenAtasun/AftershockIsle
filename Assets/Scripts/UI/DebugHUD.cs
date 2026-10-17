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

            var rect = new Rect(16f, top, 360f, 284f);
            UIBlocker.Add(rect);
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("TEST PANELİ (F1)");
            GUILayout.Label("F3 gaz  F5 kamera  F6 yangın  F7 boru  F8 sarsıntı");
            GUILayout.Label("F9 hasar  F10 kolon  F11 kaynak+puan  F12 kazazede");

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
