using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Crisis;
using AftershockIsle.Economy;
using AftershockIsle.Endgame;
using AftershockIsle.Expedition;
using AftershockIsle.Interaction;
using AftershockIsle.Player;
using AftershockIsle.Save;
using AftershockIsle.Skills;
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
            EventBus.Subscribe<FireStarted>(OnFireStarted);
            EventBus.Subscribe<FireExtinguished>(OnFireExtinguished);
            EventBus.Subscribe<CeilingCollapsed>(OnCeilingCollapsed);
            EventBus.Subscribe<SmokeEvacuation>(OnSmokeEvacuation);
            EventBus.Subscribe<GasLeakStarted>(OnGasLeak);
            EventBus.Subscribe<GasLeakStopped>(OnGasStopped);
            EventBus.Subscribe<GasExploded>(OnGasExploded);
            EventBus.Subscribe<UpkeepResolved>(OnUpkeep);
            EventBus.Subscribe<SurvivorPanicked>(OnPanicked);
            EventBus.Subscribe<SurvivorJoined>(OnJoined);
            EventBus.Subscribe<SkillPointsGained>(OnSkillPoints);
            EventBus.Subscribe<SkillUnlocked>(OnSkillUnlocked);
            EventBus.Subscribe<ExpeditionStarted>(OnExpeditionStarted);
            EventBus.Subscribe<ExpeditionReturned>(OnExpeditionReturned);
            EventBus.Subscribe<RecipeCrafted>(OnRecipeCrafted);
            EventBus.Subscribe<PumpBroke>(OnPumpBroke);
            EventBus.Subscribe<RadioRepaired>(OnRadioRepaired);
            EventBus.Subscribe<SignalInterrupted>(OnSignalInterrupted);
            EventBus.Subscribe<HelicopterInbound>(OnHelicopterInbound);
            EventBus.Subscribe<SaveCompleted>(OnSaveCompleted);
            EventBus.Subscribe<GameLoaded>(OnGameLoaded);
            EventBus.Subscribe<StructurePlaced>(OnStructurePlaced);
        }

        void OnStructurePlaced(StructurePlaced evt)
        {
            if (evt.Structure != null)
                ShowNotice($"✓ {evt.Structure.Info.Name} {building.GetFloorName(evt.Structure.GridFloor)} katına yerleştirildi.");
        }

        void OnSaveCompleted(SaveCompleted evt)
        {
            if (evt.Success) ShowNotice(evt.SlotName == SaveManager.AutoSaveFile ? "Otomatik Kayıt Alındı." : "Oyun Kaydedildi.");
        }

        void OnGameLoaded(GameLoaded evt)
        {
            if (evt.Success) ShowNotice("Kayıtlı Oyun Yüklendi.");
        }

        void OnRecipeCrafted(RecipeCrafted evt)
        {
            var info = RecipeCatalog.Get(evt.Id);
            ShowNotice(info.IsPlaceable
                ? $"Tezgah: {info.Name} tamamlandı! Sığınak ızgarasında istediğin hücreye yerleştir."
                : $"Tezgah: {info.Name} tamamlandı.");
        }
        void OnPumpBroke(PumpBroke evt) => ShowNotice("Elektrikli pompa bozuldu! Tamir için 1 Boru & Metal gerekli.");
        void OnRadioRepaired(RadioRepaired evt) => ShowNotice("Telsiz tamir edildi. Şartlar sağlanınca sinyal gönderilebilir.");
        void OnSignalInterrupted(SignalInterrupted evt) => ShowNotice(evt.Reason, 5f);
        void OnHelicopterInbound(HelicopterInbound evt) => ShowNotice("SİNYAL ULAŞTI! Tahliye helikopteri yolda.", 6f);

        void OnExpeditionStarted(ExpeditionStarted evt) =>
            ShowNotice($"{evt.Explorer.DisplayName} {evt.Site.name} için yola çıktı. Sahada WASD / sol çubukla yürü, F / A ile ara.");

        void OnExpeditionReturned(ExpeditionReturned evt)
        {
            string message = $"{evt.Explorer.DisplayName} döndü. Çanta: {LootText(evt.Loot)}.";
            if (evt.HazardReport != null) message += $" {evt.HazardReport}";
            if (evt.Rescued != null) message += $" {evt.Rescued.DisplayName} kurtarıldı!";
            ShowNotice(message, 6f);
        }

        void OnUpkeep(UpkeepResolved evt) => ShowNotice(evt.Hungry == 0
            ? $"Gece: {evt.Fed} kişi Yaşam Kiti ile doydu."
            : $"Gece: {evt.Fed} kişi doydu, {evt.Hungry} kişi aç kaldı (Tükenmiş: yarı hız).");
        void OnPanicked(SurvivorPanicked evt) => ShowNotice($"{evt.Survivor.DisplayName} PANİKLEDİ! Bir süre emir dinlemeyecek.");
        void OnJoined(SurvivorJoined evt) => ShowNotice($"{evt.Survivor.DisplayName} sığınağa katıldı ({evt.Survivor.Branch.ToDisplayName()} uzmanı).");
        void OnSkillPoints(SkillPointsGained evt) => ShowNotice($"Yeni gün: +1 yetenek puanı (toplam {evt.Total}). K ile yetenek ağacını aç.");
        void OnSkillUnlocked(SkillUnlocked evt) => ShowNotice($"Yetenek açıldı: {SkillCatalog.Get(evt.Id).Name}");

        void OnFireStarted(FireStarted evt) => ShowNotice($"YANGIN çıktı: {building.GetFloorName(evt.Fire.Floor)}! Kova suyuyla söndür.");
        void OnFireExtinguished(FireExtinguished evt) => ShowNotice($"{building.GetFloorName(evt.Fire.Floor)} yangını söndürüldü.");
        void OnCeilingCollapsed(CeilingCollapsed evt) => ShowNotice($"{building.GetFloorName(evt.Fire.Floor)} tavanı yangından çöktü!");
        void OnGasLeak(GasLeakStarted evt) => ShowNotice(
            $"GAZ KAÇAĞI: {building.GetFloorName(evt.Leak.Floor)}! Şalteri indir (kıvılcım patlatır), pencereyi kır, vanayı kapat.", 7f);
        void OnGasStopped(GasLeakStopped evt) => ShowNotice("Gaz vanası kapatıldı. Kalan gaz dağılıyor.");
        void OnGasExploded(GasExploded evt) => ShowNotice($"GAZ PATLADI: {building.GetFloorName(evt.Leak.Floor)} alev aldı!", 6f);
        void OnSmokeEvacuation(SmokeEvacuation evt) => ShowNotice($"{evt.Survivor.DisplayName} öksürerek dumandan kaçıyor.");

        void OnDisable()
        {
            EventBus.Unsubscribe<TaskNotice>(OnTaskNotice);
            EventBus.Unsubscribe<AftershockImpact>(OnAftershockImpact);
            EventBus.Unsubscribe<SurvivorDied>(OnSurvivorDied);
            EventBus.Unsubscribe<PipeBurst>(OnPipeBurst);
            EventBus.Unsubscribe<ElectrocutionStarted>(OnElectrocution);
            EventBus.Unsubscribe<PowerChanged>(OnPowerChanged);
            EventBus.Unsubscribe<FireStarted>(OnFireStarted);
            EventBus.Unsubscribe<FireExtinguished>(OnFireExtinguished);
            EventBus.Unsubscribe<CeilingCollapsed>(OnCeilingCollapsed);
            EventBus.Unsubscribe<SmokeEvacuation>(OnSmokeEvacuation);
            EventBus.Unsubscribe<GasLeakStarted>(OnGasLeak);
            EventBus.Unsubscribe<GasLeakStopped>(OnGasStopped);
            EventBus.Unsubscribe<GasExploded>(OnGasExploded);
            EventBus.Unsubscribe<UpkeepResolved>(OnUpkeep);
            EventBus.Unsubscribe<SurvivorPanicked>(OnPanicked);
            EventBus.Unsubscribe<SurvivorJoined>(OnJoined);
            EventBus.Unsubscribe<SkillPointsGained>(OnSkillPoints);
            EventBus.Unsubscribe<SkillUnlocked>(OnSkillUnlocked);
            EventBus.Unsubscribe<ExpeditionStarted>(OnExpeditionStarted);
            EventBus.Unsubscribe<ExpeditionReturned>(OnExpeditionReturned);
            EventBus.Unsubscribe<RecipeCrafted>(OnRecipeCrafted);
            EventBus.Unsubscribe<PumpBroke>(OnPumpBroke);
            EventBus.Unsubscribe<RadioRepaired>(OnRadioRepaired);
            EventBus.Unsubscribe<SignalInterrupted>(OnSignalInterrupted);
            EventBus.Unsubscribe<HelicopterInbound>(OnHelicopterInbound);
            EventBus.Unsubscribe<SaveCompleted>(OnSaveCompleted);
            EventBus.Unsubscribe<GameLoaded>(OnGameLoaded);
            EventBus.Unsubscribe<StructurePlaced>(OnStructurePlaced);
        }

        // Paneller alanlarını OnGUI'de yeniden bildirir; LateUpdate OnGUI'den önce çalışır.
        void LateUpdate() => UIBlocker.Clear();

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

        void ShowNotice(string message, float duration = NoticeDuration)
        {
            notice = message;
            noticeUntil = Time.unscaledTime + duration;
        }

        void OnGUI()
        {
            EnsureStyles();
            DrawInfoPanel();
            DrawTopBar();
            DrawSurvivorOverlays();
            DrawEvacuationChecklist();
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
            var rect = new Rect(12, 12, 400, 355);
            UIBlocker.Add(rect);
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label($"Gün {dayCycle.Day} — {dayCycle.Phase.ToDisplayName()} ({Mathf.CeilToInt(dayCycle.PhaseRemaining)} sn)");
            GUILayout.Label($"Hız: {SpeedLabel(timeManager.Speed)}   Nüfus: {Survivor.AliveCount + ExpeditionSystem.AwayCount}");

            var away = ExpeditionSystem.AwayExplorer;
            if (away != null)
            {
                var expeditions = ExpeditionSystem.Instance;
                string rescue = expeditions.IsRescuing ? ", kazazede kurtarıldı" : "";
                GUILayout.Label($"Seferde: {away.DisplayName} → {expeditions.CurrentSite.name} (kalan {Mathf.CeilToInt(expeditions.Remaining)} sn{rescue}, Can {away.Health:0}) — {GameInput.Prompt(InputPrompt.SwitchCharacter)}: sahaya geç");
            }

            var selected = commands.Selected;
            if (selected != null)
            {
                var condition = selected.Condition;
                GUILayout.Label($"Seçili: {selected.DisplayName} [{selected.Branch.ToDisplayName()}] ({building.GetFloorName(selected.Mover.CurrentFloor)})");
                GUILayout.Label($"HP {selected.Health:0}/{selected.MaxHealth:0}   Stres %{condition.Stress:0}   Açlık {condition.Hunger}/{SurvivorCondition.MaxHunger}{(condition.IsExhausted ? "  (Tükenmiş)" : "")}");
                GUILayout.Label($"Görev: {TaskLabel(selected.Tasks)}");
                string bag = selected.HasBag ? $"   Çanta: {LootText(selected.ExpeditionBag)}" : "";
                GUILayout.Label($"Elinde: {(selected.IsCarrying ? selected.Carried.ToString() : "—")}{bag}");
            }
            else
            {
                GUILayout.Label("Seçili: —");
            }

            GUILayout.Space(4);
            GUILayout.Label("WASD / sol çubuk: yürü   F / A: etkileş   Tab / LB-RB: karakter");
            GUILayout.Label("Fare: sol tık seç, sağ tık iş ver   [B]: Ev Yerleşimi (Grid)   K / H");
            GUILayout.Label("Test: F6 yangın F7 boru F8 sarsıntı F9 hasar F10 kolon");
            GUILayout.Label("      F11 kaynak+puan  F12 kazazede ekle");
            if (GUILayout.Button(PlacementController.IsPlacementActive ? "🏗 Yerleşim Modunu Kapat [B]" : "🏗 Ev Yerleşim & Grid Modu [B]", GUILayout.Height(24)))
            {
                if (PlacementController.Instance != null)
                    PlacementController.Instance.ToggleBuildMode();
            }
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

            foreach (var leak in GasLeak.All)
            {
                if (!leak.IsLeaking && leak.Concentration < 1f) continue;

                string floorName = building.GetFloorName(leak.Floor);
                string danger = leak.IsExplosive ? (PowerGrid.IsOn ? " — PATLAMA RİSKİ: şalteri indir!" : " — patlayıcı yoğunlukta")
                    : leak.IsBlockingHere ? " — kata girilemiyor, pencereyi kır" : "";
                GUI.color = new Color(0.65f, 0.9f, 0.35f);
                string state = leak.IsLeaking ? "GAZ KAÇAĞI" : "GAZ";
                GUI.Label(new Rect(0, y + 26, Screen.width, 24), $"{state}: {floorName} %{leak.Concentration:0}{danger}", centeredStyle);
                y += 24;
            }

            foreach (var fire in FireSpot.All)
            {
                if (!fire.IsBurning && !fire.IsSmokeBlockedHere) continue;

                string floorName = building.GetFloorName(fire.Floor);
                string collapse = fire.IsAtFullBurn ? $" — TAVAN ÇÖKÜYOR: {Mathf.CeilToInt(fire.CollapseCountdown)} sn" : "";
                string smoke = fire.IsSmokeBlockedHere ? " — dumandan girilemiyor, pencereyi kır" : "";
                GUI.color = new Color(1f, 0.55f, 0.2f);
                string label = fire.IsBurning ? $"YANGIN: {floorName} %{fire.Intensity:0}" : $"DUMAN: {floorName}";
                GUI.Label(new Rect(0, y + 26, Screen.width, 24), $"{label}{collapse}{smoke}", centeredStyle);
                y += 24;
            }
            GUI.color = previous;

            foreach (var pump in pumps)
            {
                if (pump == null || !pump.IsBeingOperated) continue;
                GUI.Label(new Rect(0, Screen.height - 100, Screen.width, 24),
                    $"Pompalanıyor — {GameInput.Prompt(InputPrompt.Pump)} tuşlarına dönüşümlü bas!", centeredStyle);
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

                var condition = survivor.Condition;
                DrawBar(new Rect(x, y + 7, 60, 4), condition.Stress / condition.MaxStress, new Color(0.75f, 0.35f, 0.9f));

                float progress = survivor.Tasks.Progress01;
                if (progress > 0f) DrawBar(new Rect(x, y + 13, 60, 6), progress, Color.white);

                string status = survivor.IsTrapped ? $"ENKAZ ALTINDA {Mathf.CeilToInt(survivor.TrappedRemaining)} sn"
                    : condition.IsPanicking ? $"PANİK {Mathf.CeilToInt(condition.PanicRemaining)} sn"
                    : condition.IsExhausted ? "Tükenmiş"
                    : null;
                if (status != null) GUI.Label(new Rect(x - 40, y - 22, 140, 20), status, centeredStyle);
            }
        }

        // Sağ altta: GDD 10.1 tahliye şartları ve sinyal ilerlemesi.
        void DrawEvacuationChecklist()
        {
            var radio = RadioStation.Instance;
            if (radio == null) return;

            var conditions = EvacuationConditions.Evaluate(building, radio);
            int tier = Workbench.Instance != null ? Workbench.Instance.Tier : 1;

            var rect = new Rect(Screen.width - 292, Screen.height - 190, 280, 150);
            UIBlocker.Add(rect);
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("TAHLİYE ŞARTLARI");
            GUILayout.Label($"{Check(conditions.WorkbenchTier3)} Tezgah Kademe 3 (şu an {tier})");
            GUILayout.Label($"{Check(conditions.RadioRepaired)} Çatıdaki telsiz tamir edildi");
            GUILayout.Label($"{Check(conditions.RoofRubbleLeft == 0)} Çatı molozu temiz ({conditions.RoofRubbleLeft} yığın kaldı)");
            GUILayout.Label($"{Check(conditions.RoofColumnsTotal > 0 && conditions.RoofColumnsReinforced == conditions.RoofColumnsTotal)} Çatı kolonları çelik ({conditions.RoofColumnsReinforced}/{conditions.RoofColumnsTotal})");
            GUILayout.Label($"{Check(conditions.PowerOn)} Ana şalter açık");

            string signal = radio.SignalComplete ? "ULAŞTI" : $"%{radio.Signal01 * 100f:0}";
            GUILayout.Label($"Kesintisiz sinyal: {signal}");
            GUILayout.EndArea();
        }

        static string Check(bool done) => done ? "[x]" : "[  ]";

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

        static string LootText(System.Collections.Generic.IReadOnlyList<ResourceAmount> items)
        {
            if (items.Count == 0) return "boş";
            var parts = new string[items.Count];
            for (int i = 0; i < items.Count; i++) parts[i] = items[i].ToString();
            return string.Join(", ", parts);
        }

        static string SpeedLabel(TimeSpeed speed) => speed switch
        {
            TimeSpeed.Paused => "Durdu",
            TimeSpeed.Fast => "2x",
            _ => "1x",
        };
    }
}
