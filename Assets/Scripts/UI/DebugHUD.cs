using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using AftershockIsle.Economy;
using AftershockIsle.Interaction;
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
        ManualPump[] pumps;
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
            pumps = FindObjectsByType<ManualPump>(FindObjectsSortMode.None);
        }

        void OnEnable()
        {
            EventBus.Subscribe<TaskNotice>(OnTaskNotice);
            EventBus.Subscribe<AftershockImpact>(OnAftershockImpact);
            EventBus.Subscribe<SurvivorDied>(OnSurvivorDied);
            EventBus.Subscribe<PipeBurst>(OnPipeBurst);
            EventBus.Subscribe<ElectrocutionStarted>(OnElectrocution);
            EventBus.Subscribe<PowerChanged>(OnPowerChanged);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<TaskNotice>(OnTaskNotice);
            EventBus.Unsubscribe<AftershockImpact>(OnAftershockImpact);
            EventBus.Unsubscribe<SurvivorDied>(OnSurvivorDied);
            EventBus.Unsubscribe<PipeBurst>(OnPipeBurst);
            EventBus.Unsubscribe<ElectrocutionStarted>(OnElectrocution);
            EventBus.Unsubscribe<PowerChanged>(OnPowerChanged);
        }

        void OnPipeBurst(PipeBurst evt) => ShowNotice("Bodrumdaki şebeke borusu patladı! Su dolmaya başladı.");
        void OnElectrocution(ElectrocutionStarted evt) => ShowNotice($"{evt.Survivor.DisplayName} elektrik çarpıyor! Sudan çıkar ya da şalteri indir.");
        void OnPowerChanged(PowerChanged evt) => ShowNotice(evt.IsOn ? "Şalter kaldırıldı, elektrik geldi." : "Şalter indirildi, bina karanlıkta.");

        void OnTaskNotice(TaskNotice evt) => ShowNotice($"{evt.Survivor.DisplayName}: {evt.Message}");

        void OnAftershockImpact(AftershockImpact evt)
        {
            string columns = evt.BrokenColumns > 0 ? $", {evt.BrokenColumns} kırık kolon" : "";
            string exposed = evt.ExposedSurvivors > 0 ? $", {evt.ExposedSurvivors} kişi açıkta yakalandı" : "";
            ShowNotice($"Artçı sarsıntı! Bina -%{evt.Damage:0.#}{columns}{exposed}");
        }

        void OnSurvivorDied(SurvivorDied evt) => ShowNotice($"{evt.Survivor.DisplayName} öldü. {evt.Cause}");

        void ShowNotice(string message)
        {
            notice = message;
            noticeUntil = Time.unscaledTime + NoticeDuration;
        }

        void OnGUI()
        {
            EnsureStyles();
            DrawInfoPanel();
            DrawTopBar();
            DrawSurvivorOverlays();
            DrawHoverInfo();

            if (Time.unscaledTime < noticeUntil)
                GUI.Label(new Rect(0, Screen.height - 70, Screen.width, 24), notice, centeredStyle);

            if (session.Outcome != GameOutcome.None) DrawEndBanner();
            else
            {
                if (timeManager.IsPaused) GUI.Label(new Rect(0, 80, Screen.width, 40), "DURAKLATILDI", bannerStyle);
                DrawQuakeWarning();
                DrawFloodWarnings();
            }
        }

        void DrawQuakeWarning()
        {
            var quake = AftershockSystem.Instance;
            if (quake == null || quake.Phase != QuakePhase.Warning) return;

            var previous = GUI.color;
            GUI.color = new Color(1f, 0.35f, 0.25f);
            GUI.Label(new Rect(0, 110, Screen.width, 40),
                $"ARTÇI SARSINTI GELİYOR: {Mathf.CeilToInt(quake.PhaseRemaining)}", bannerStyle);
            GUI.color = previous;
            GUI.Label(new Rect(0, 148, Screen.width, 24),
                "Space ile durdur, karakterleri yeşil Yaşam Üçgeni alanlarına gönder.", centeredStyle);
        }

        void DrawInfoPanel()
        {
            GUILayout.BeginArea(new Rect(12, 12, 360, 260), GUI.skin.box);
            GUILayout.Label($"Gün {dayCycle.Day} — {dayCycle.Phase.ToDisplayName()} ({Mathf.CeilToInt(dayCycle.PhaseRemaining)} sn)");
            GUILayout.Label($"Hız: {SpeedLabel(timeManager.Speed)}");

            var selected = commands.Selected;
            if (selected != null)
            {
                GUILayout.Label($"Seçili: {selected.DisplayName} ({building.GetFloorName(selected.Mover.CurrentFloor)})  HP {selected.Health:0}/{selected.MaxHealth:0}");
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
            GUILayout.Label("Test: F7 boru  F8 sarsıntı  F9 hasar  F10 kolon  F11 +5 kaynak");
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
            resources += $"Elektrik: {(PowerGrid.IsOn ? "Açık" : "Kapalı")}";
            GUI.Label(new Rect(0, 38, Screen.width, 22), resources, centeredStyle);

            var flood = FloodSystem.Instance;
            if (flood != null && (flood.Level > 0.01f || flood.IsPipeBurst))
            {
                string pipe = flood.IsPipeBurst ? "  — boru patlak!" : "";
                GUI.Label(new Rect(0, 58, Screen.width, 22), $"Bodrum suyu: %{flood.Level:0}{pipe}", centeredStyle);
            }
        }

        void DrawFloodWarnings()
        {
            var flood = FloodSystem.Instance;
            if (flood == null) return;

            var previous = GUI.color;
            float y = 190;
            if (flood.IsFull)
            {
                GUI.color = new Color(1f, 0.35f, 0.25f);
                GUI.Label(new Rect(0, y, Screen.width, 40),
                    $"TEMEL KAYIYOR: {Mathf.CeilToInt(flood.CollapseCountdown)} sn — suyu pompala!", bannerStyle);
                y += 40;
            }

            if (flood.IsElectrified)
            {
                GUI.color = new Color(1f, 0.9f, 0.3f);
                GUI.Label(new Rect(0, y, Screen.width, 24),
                    "ELEKTRİK KAÇAĞI: bodrumdaki su elektrikli. Suya girmeden önce şalteri indir.", centeredStyle);
            }
            GUI.color = previous;

            foreach (var pump in pumps)
            {
                if (pump == null || !pump.IsBeingOperated) continue;
                GUI.Label(new Rect(0, Screen.height - 100, Screen.width, 24),
                    "Pompalanıyor — Q / E tuşlarına dönüşümlü bas!", centeredStyle);
                break;
            }
        }

        // Her kazazedenin başının üstünde: can barı, iş ilerlemesi, enkaz altında uyarısı.
        void DrawSurvivorOverlays()
        {
            foreach (var survivor in Survivor.All)
            {
                if (survivor.IsDead) continue;

                Vector3 screen = cam.WorldToScreenPoint(survivor.transform.position + Vector3.up * 2.2f);
                if (screen.z < 0f) continue;

                float x = screen.x - 30;
                float y = Screen.height - screen.y;

                float health = survivor.Health / survivor.MaxHealth;
                DrawBar(new Rect(x, y, 60, 6), health, Color.Lerp(Color.red, Color.green, health));

                float progress = survivor.Tasks.Progress01;
                if (progress > 0f) DrawBar(new Rect(x, y + 8, 60, 6), progress, Color.white);

                if (survivor.IsTrapped)
                    GUI.Label(new Rect(x - 40, y - 22, 140, 20),
                        $"ENKAZ ALTINDA {Mathf.CeilToInt(survivor.TrappedRemaining)} sn", centeredStyle);
            }
        }

        static void DrawBar(Rect rect, float fill, Color color)
        {
            GUI.Box(rect, GUIContent.none);
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x + 1, rect.y + 1, (rect.width - 2) * Mathf.Clamp01(fill), rect.height - 2), Texture2D.whiteTexture);
            GUI.color = previous;
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
