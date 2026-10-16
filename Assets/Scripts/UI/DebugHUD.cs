using AftershockIsle.Building;
using AftershockIsle.Core;
using AftershockIsle.Player;
using UnityEngine;

namespace AftershockIsle.UI
{
    /// <summary>Prototip için geçici IMGUI göstergesi. Asıl arayüz gelince kaldırılacak.</summary>
    public class DebugHUD : MonoBehaviour
    {
        [SerializeField] TimeManager timeManager;
        [SerializeField] DayCycle dayCycle;
        [SerializeField] CommandController commands;
        [SerializeField] BuildingLayout building;

        GUIStyle pausedStyle;

        void Awake()
        {
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();
            if (dayCycle == null) dayCycle = FindFirstObjectByType<DayCycle>();
            if (commands == null) commands = FindFirstObjectByType<CommandController>();
            if (building == null) building = FindFirstObjectByType<BuildingLayout>();
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12, 12, 340, 200), GUI.skin.box);
            GUILayout.Label($"Gün {dayCycle.Day} — {dayCycle.Phase.ToDisplayName()} ({Mathf.CeilToInt(dayCycle.PhaseRemaining)} sn)");
            GUILayout.Label($"Hız: {SpeedLabel(timeManager.Speed)}");

            var selected = commands.Selected;
            GUILayout.Label(selected != null
                ? $"Seçili: {selected.DisplayName} ({building.GetFloorName(selected.Mover.CurrentFloor)})"
                : "Seçili: —");

            GUILayout.Space(6);
            GUILayout.Label("Sol tık: seç   Sağ tık: git");
            GUILayout.Label("Space: duraklat   1/2: hız");
            GUILayout.Label("WASD: kaydır   F: odakla   Tekerlek: yakınlaş");
            GUILayout.EndArea();

            if (timeManager.IsPaused) DrawPausedBanner();
        }

        void DrawPausedBanner()
        {
            pausedStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            GUI.Label(new Rect(0, 16, Screen.width, 40), "DURAKLATILDI", pausedStyle);
        }

        static string SpeedLabel(TimeSpeed speed) => speed switch
        {
            TimeSpeed.Paused => "Durdu",
            TimeSpeed.Fast => "2x",
            _ => "1x",
        };
    }
}
