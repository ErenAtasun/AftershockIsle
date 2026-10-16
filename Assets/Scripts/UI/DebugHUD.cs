using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Economy;
using AftershockIsle.Player;
using UnityEngine;

namespace AftershockIsle.UI
{
    /// <summary>Prototip için geçici IMGUI göstergesi. Asıl arayüz gelince kaldırılacak.</summary>
    public class DebugHUD : MonoBehaviour
    {
        const float NoticeDuration = 3f;

        [SerializeField] TimeManager timeManager;
        [SerializeField] DayCycle dayCycle;
        [SerializeField] CommandController commands;
        [SerializeField] BuildingLayout building;
        [SerializeField] GameSession session;

        Camera cam;
        GUIStyle bannerStyle;
        GUIStyle centeredStyle;
        string notice;
        float noticeUntil;

        void Awake()
        {
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();
            if (dayCycle == null) dayCycle = FindFirstObjectByType<DayCycle>();
            if (commands == null) commands = FindFirstObjectByType<CommandController>();
            if (building == null) building = FindFirstObjectByType<BuildingLayout>();
            if (session == null) session = FindFirstObjectByType<GameSession>();
            cam = Camera.main;
        }

        void OnEnable() => EventBus.Subscribe<TaskNotice>(OnTaskNotice);
        void OnDisable() => EventBus.Unsubscribe<TaskNotice>(OnTaskNotice);

        void OnTaskNotice(TaskNotice evt)
        {
            notice = $"{evt.Survivor.DisplayName}: {evt.Message}";
            noticeUntil = Time.unscaledTime + NoticeDuration;
        }

        void OnGUI()
        {
            EnsureStyles();
            DrawInfoPanel();
            DrawTopBar();
            DrawWorkProgress();
            DrawHoverInfo();

            if (Time.unscaledTime < noticeUntil)
                GUI.Label(new Rect(0, Screen.height - 70, Screen.width, 24), notice, centeredStyle);

            if (session.Outcome != GameOutcome.None) DrawEndBanner();
            else if (timeManager.IsPaused) GUI.Label(new Rect(0, 70, Screen.width, 40), "DURAKLATILDI", bannerStyle);
        }

        void DrawInfoPanel()
        {
            GUILayout.BeginArea(new Rect(12, 12, 360, 260), GUI.skin.box);
            GUILayout.Label($"Gün {dayCycle.Day} — {dayCycle.Phase.ToDisplayName()} ({Mathf.CeilToInt(dayCycle.PhaseRemaining)} sn)");
            GUILayout.Label($"Hız: {SpeedLabel(timeManager.Speed)}");

            var selected = commands.Selected;
            if (selected != null)
            {
                GUILayout.Label($"Seçili: {selected.DisplayName} ({building.GetFloorName(selected.Mover.CurrentFloor)})");
                GUILayout.Label($"Görev: {TaskLabel(selected.Tasks)}");
                GUILayout.Label($"Elinde: {(selected.IsCarrying ? selected.Carried.ToString() : "—")}");
            }
            else
            {
                GUILayout.Label("Seçili: —");
            }

            GUILayout.Space(6);
            GUILayout.Label("Sol tık: seç   Sağ tık: git / iş ver   Shift+Sağ: sıraya ekle");
            GUILayout.Label("Space: duraklat   1/2: hız   WASD: kaydır   F: odakla");
            GUILayout.Label("Test: F9 hasar   F10 kolon çatlat   F11 +5 kaynak");
            GUILayout.EndArea();
        }

        void DrawTopBar()
        {
            var integrity = StructuralIntegrity.Instance;
            const float barWidth = 360f;
            float x = (Screen.width - barWidth) * 0.5f;

            GUI.Box(new Rect(x, 12, barWidth, 22), GUIContent.none);
            var previous = GUI.color;
            GUI.color = Color.Lerp(new Color(0.9f, 0.2f, 0.15f), new Color(0.3f, 0.85f, 0.35f), integrity.Normalized);
            GUI.DrawTexture(new Rect(x + 2, 14, (barWidth - 4) * integrity.Normalized, 18), Texture2D.whiteTexture);
            GUI.color = previous;

            float drain = integrity.TotalDrainPerSecond;
            string drainText = drain > 0f ? $"   (-%{drain:0.0}/sn)" : "";
            GUI.Label(new Rect(x, 13, barWidth, 22), $"Yapısal Bütünlük %{integrity.Current:0}{drainText}", centeredStyle);

            var storage = ResourceStorage.Instance;
            string resources = "";
            foreach (var type in ResourceTypes.All)
                resources += $"{type.ToDisplayName()}: {storage.Get(type)}     ";
            GUI.Label(new Rect(0, 38, Screen.width, 22), resources, centeredStyle);
        }

        void DrawWorkProgress()
        {
            foreach (var survivor in Survivor.All)
            {
                float progress = survivor.Tasks.Progress01;
                if (progress <= 0f) continue;

                Vector3 screen = cam.WorldToScreenPoint(survivor.transform.position + Vector3.up * 2.2f);
                if (screen.z < 0f) continue;

                var rect = new Rect(screen.x - 30, Screen.height - screen.y, 60, 8);
                GUI.Box(rect, GUIContent.none);
                GUI.DrawTexture(new Rect(rect.x + 1, rect.y + 1, (rect.width - 2) * progress, rect.height - 2), Texture2D.whiteTexture);
            }
        }

        void DrawHoverInfo()
        {
            var hovered = commands.Hovered;
            if (hovered == null) return;

            Vector2 mouse = GameInput.PointerPosition;
            string text = $"{hovered.DisplayName} — Sağ tık: {hovered.ActionLabel}";
            if (!string.IsNullOrEmpty(hovered.StatusText)) text += $"\n{hovered.StatusText}";

            var size = GUI.skin.box.CalcSize(new GUIContent(text));
            GUI.Box(new Rect(mouse.x + 16, Screen.height - mouse.y + 8, size.x + 8, size.y + 4), text);
        }

        void DrawEndBanner()
        {
            string title = session.Outcome == GameOutcome.Victory ? "TAHLİYE BAŞARILI" : "OYUN BİTTİ";
            GUI.Label(new Rect(0, Screen.height * 0.4f, Screen.width, 40), title, bannerStyle);
            GUI.Label(new Rect(0, Screen.height * 0.4f + 44, Screen.width, 24), session.EndReason, centeredStyle);
            GUI.Label(new Rect(0, Screen.height * 0.4f + 70, Screen.width, 24), "R: yeniden başla", centeredStyle);
        }

        void EnsureStyles()
        {
            bannerStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            centeredStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
        }

        static string TaskLabel(TaskRunner tasks)
        {
            if (tasks.Current == null) return tasks.QueuedCount > 0 ? $"sırada {tasks.QueuedCount}" : "boşta";

            string action = tasks.State == TaskState.Moving ? "yolda" : $"%{tasks.Progress01 * 100f:0}";
            string queued = tasks.QueuedCount > 0 ? $" (+{tasks.QueuedCount} sırada)" : "";
            return $"{tasks.Current.ActionLabel}: {tasks.Current.DisplayName} — {action}{queued}";
        }

        static string SpeedLabel(TimeSpeed speed) => speed switch
        {
            TimeSpeed.Paused => "Durdu",
            TimeSpeed.Fast => "2x",
            _ => "1x",
        };
    }
}
