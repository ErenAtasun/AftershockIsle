using System.Collections.Generic;
using System.Text;
using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Crisis;
using AftershockIsle.Economy;
using AftershockIsle.Endgame;
using AftershockIsle.Expedition;
using AftershockIsle.Interaction;
using AftershockIsle.Save;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Sabah ekranı:
    /// - Normal günlerde: Tam ekran pencere açılmaz; üst kısımda kısa bir "GÜN N" ve özet satırı
    ///   (yemek, keşif sonucu, depo değişimi) belirir. Zaman akmaya devam eder, oyuncuyu bölmez.
    /// - Kritik durumlarda (İlk gün veya ölüm / ağır kayıp olduğunda): Büyük kart açılır, zaman durur
    ///   ve oyuncunun "Devam" onayı beklenir.
    /// </summary>
    public class DayCard : MonoBehaviour
    {
        const float CompactBannerDuration = 8.5f;

        UiWindow window;
        Button continueButton;
        bool open;
        int pendingDay;
        int pendingFrames;

        // Kompakt Sabah Başlığı (Normal Günler)
        Canvas compactCanvas;
        GameObject compactRoot;
        Text compactTitle;
        Text compactSummary;
        float compactCloseAt;
        bool compactVisible;

        // Önceki gün başından beri biriken veriler
        readonly int[] startResources = new int[ResourceTypes.Count];
        int fed;
        int hungry;
        int skipped;
        readonly List<string> scoutReports = new List<string>();
        string raidText;
        readonly List<string> deaths = new List<string>();
        int rubbleCleared;
        int structuresPlaced;
        int recipesCrafted;
        int expeditions;
        int lootItems;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindFirstObjectByType<BuildingLayout>() == null || FindFirstObjectByType<DayCard>() != null) return;
            new GameObject("[Gün Kartı]").AddComponent<DayCard>();
        }

        void OnEnable()
        {
            EventBus.Subscribe<DayStarted>(OnDayStarted);
            EventBus.Subscribe<UpkeepResolved>(OnUpkeep);
            EventBus.Subscribe<NightRaidResolved>(OnRaid);
            EventBus.Subscribe<SurvivorDied>(OnDied);
            EventBus.Subscribe<RubbleCleared>(OnRubble);
            EventBus.Subscribe<StructurePlaced>(OnPlaced);
            EventBus.Subscribe<RecipeCrafted>(OnCrafted);
            EventBus.Subscribe<ExpeditionReturned>(OnExpedition);
            EventBus.Subscribe<GameLoaded>(OnLoaded);
            EventBus.Subscribe<NightScavengeReturned>(OnScoutReturned);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<DayStarted>(OnDayStarted);
            EventBus.Unsubscribe<UpkeepResolved>(OnUpkeep);
            EventBus.Unsubscribe<NightRaidResolved>(OnRaid);
            EventBus.Unsubscribe<SurvivorDied>(OnDied);
            EventBus.Unsubscribe<RubbleCleared>(OnRubble);
            EventBus.Unsubscribe<StructurePlaced>(OnPlaced);
            EventBus.Unsubscribe<RecipeCrafted>(OnCrafted);
            EventBus.Unsubscribe<ExpeditionReturned>(OnExpedition);
            EventBus.Unsubscribe<GameLoaded>(OnLoaded);
            EventBus.Unsubscribe<NightScavengeReturned>(OnScoutReturned);
            TimeManager.Release(this);
        }

        // ---------------------------------------------------------------- Veri Toplama

        void OnUpkeep(UpkeepResolved evt)
        {
            fed = evt.Fed;
            hungry = evt.Hungry;
            skipped = evt.Skipped;
        }

        void OnScoutReturned(NightScavengeReturned evt) => scoutReports.Add(evt.Report);

        void OnRaid(NightRaidResolved evt)
        {
            if (!evt.Happened) raidText = "Gece sakin geçti.";
            else raidText = evt.Repelled ? "<color=#9be37a>Yağmacı baskını püskürtüldü.</color>" : "<color=#ff8f7a>Yağmacılar sığınağı talan etti!</color>";
        }

        void OnDied(SurvivorDied evt) => deaths.Add($"{evt.Survivor.DisplayName} ({evt.Cause})");
        void OnRubble(RubbleCleared evt) => rubbleCleared++;
        void OnPlaced(StructurePlaced evt) => structuresPlaced++;
        void OnCrafted(RecipeCrafted evt) => recipesCrafted++;

        void OnExpedition(ExpeditionReturned evt)
        {
            expeditions++;
            if (evt.Loot != null) foreach (var item in evt.Loot) lootItems += item.amount;
        }

        void OnLoaded(GameLoaded evt)
        {
            pendingDay = 0;
            Dismiss();
            DismissCompact();
            TakeSnapshot();
        }

        // ---------------------------------------------------------------- Gün Başı

        void OnDayStarted(DayStarted evt)
        {
            if (evt.Day < 1 || SaveManager.PendingLoad || SaveManager.IsApplying) return;
            pendingDay = evt.Day;
            pendingFrames = 3;
        }

        void Update()
        {
            if (pendingDay > 0)
            {
                if (DayTransition.Busy) return;
                if (--pendingFrames <= 0)
                {
                    int day = pendingDay;
                    pendingDay = 0;
                    Show(day);
                }
                return;
            }

            if (compactVisible && Time.unscaledTime >= compactCloseAt)
            {
                DismissCompact();
            }

            if (!open) return;

            TimeManager.Hold(this);
            window.Tick(continueButton != null ? continueButton.gameObject : null);
            if (GameInput.MenuConfirmPressed) Dismiss();
        }

        void Show(int day)
        {
            var session = FindFirstObjectByType<GameSession>();
            if (session != null && session.Outcome != GameOutcome.None) return;

            // Kritik durum kontrolü: İlk gün başlangıcı VEYA ölüm/kayıp varsa büyük modal açılır.
            bool isCritical = day <= 1 || deaths.Count > 0 || scoutReports.Exists(r => r.Contains("öldü") || r.Contains("kayıp") || r.Contains("ağır yaralı"));

            if (isCritical)
            {
                DismissCompact();
                ShowCriticalModal(day);
            }
            else
            {
                Dismiss();
                ShowCompactBanner(day);
            }

            TakeSnapshot();
        }

        // ---------------------------------------------------------------- Kompakt Sabah Başlığı

        void ShowCompactBanner(int day)
        {
            EnsureCompactBanner();

            var weather = WeatherSystem.Current;
            compactTitle.text = $"GÜN {day} BAŞLADI   <size=15><color=#cfcfc8>• Hava: {WeatherSystem.ToDisplayName(weather)}</color></size>";
            compactSummary.text = BuildCompactSummaryText();

            compactRoot.SetActive(true);
            compactVisible = true;
            compactCloseAt = Time.unscaledTime + CompactBannerDuration;

            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.DayPhaseChange, 0.6f);
        }

        string BuildCompactSummaryText()
        {
            var parts = new List<string>();

            // 1) Yemek & Gece
            if (fed + hungry > 0)
            {
                if (hungry == 0) parts.Add($"<color=#9be37a>{fed} kişi doydu</color>");
                else parts.Add($"{fed} doydu, <color=#ff8f7a>{hungry} aç kaldı</color>");
            }
            if (!string.IsNullOrEmpty(raidText))
            {
                parts.Add(raidText);
            }

            // 2) Keşif Sonucu
            if (scoutReports.Count > 0)
            {
                parts.Add($"Keşif: {string.Join(" • ", scoutReports)}");
            }

            // 3) Depo Değişimi
            var storage = ResourceStorage.Instance;
            if (storage != null)
            {
                var changes = new List<string>();
                foreach (var type in ResourceTypes.All)
                {
                    if (!type.IsStorable()) continue;
                    int delta = storage.Get(type) - startResources[(int)type];
                    if (delta == 0) continue;
                    string color = delta > 0 ? "#9be37a" : "#ff8f7a";
                    changes.Add($"<color={color}>{delta:+#;-#} {type.ToDisplayName()}</color>");
                }
                if (changes.Count > 0) parts.Add($"Depo: {string.Join(" ", changes)}");
            }

            return parts.Count > 0 ? string.Join("   |   ", parts) : "Gece olaysız geçti.";
        }

        void EnsureCompactBanner()
        {
            if (compactRoot != null) return;

            compactCanvas = UiFactory.Canvas("[Sabah Özeti]", 45);
            compactRoot = UiFactory.Panel(compactCanvas.transform, "CompactCard", new Color(0.10f, 0.12f, 0.16f, 0.96f)).gameObject;
            var rect = compactRoot.GetComponent<RectTransform>();
            UiFactory.Place(rect, new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(960f, 68f));

            // Kenarlık / Vurgu çizgisi
            var accentLine = UiFactory.Panel(compactRoot.transform, "AccentLine", UiFactory.Accent);
            UiFactory.Place(accentLine.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(960f, 3f));

            compactTitle = UiFactory.Label(compactRoot.transform, "", 17, UiFactory.Accent, TextAnchor.UpperLeft);
            UiFactory.Place(compactTitle.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -8f), new Vector2(860f, 22f));

            compactSummary = UiFactory.Label(compactRoot.transform, "", 14, Color.white, TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(compactSummary.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -32f), new Vector2(920f, 28f));

            // Kapatma düğmesi
            var closeBtn = UiFactory.TextButton(compactRoot.transform, "✕", new Vector2(32f, 32f), DismissCompact, null, 15);
            UiFactory.Place((RectTransform)closeBtn.transform, new Vector2(1f, 1f), new Vector2(-6f, -6f), new Vector2(32f, 32f));

            // Tıklayınca hemen kapanması için tıklama alanı
            var clickArea = compactRoot.AddComponent<Button>();
            clickArea.onClick.AddListener(DismissCompact);

            compactRoot.SetActive(false);
        }

        void DismissCompact()
        {
            compactVisible = false;
            if (compactRoot != null) compactRoot.SetActive(false);
        }

        // ---------------------------------------------------------------- Büyük Kritik Modal

        void ShowCriticalModal(int day)
        {
            if (window == null) window = new UiWindow("Gün Kartı", new Vector2(1000f, 760f), null, true, false);
            window.ClearContent();
            window.ClearFooter();

            window.Title.text = day <= 1 ? "GÜN 1 — SIĞINAK" : $"GÜN {day - 1} ÖZETİ   →   GÜN {day}";

            if (day > 1) BuildSummary();
            BuildNewDay(day);

            continueButton = window.FooterButton($"Devam ({GameInput.Prompt(InputPrompt.Confirm)})", Dismiss, UiFactory.Good, 260f);

            open = true;
            window.Show(true);
            TimeManager.Hold(this);
            if (GameInput.UsingGamepad && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(continueButton.gameObject);
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.DayPhaseChange, 0.8f);
        }

        void Dismiss()
        {
            open = false;
            if (window != null) window.Show(false);
            TimeManager.Release(this);
        }

        void TakeSnapshot()
        {
            var storage = ResourceStorage.Instance;
            for (int i = 0; i < startResources.Length; i++)
                startResources[i] = storage != null ? storage.Get((ResourceType)i) : 0;
            fed = 0;
            hungry = 0;
            skipped = 0;
            scoutReports.Clear();
            raidText = null;
            deaths.Clear();
            rubbleCleared = 0;
            structuresPlaced = 0;
            recipesCrafted = 0;
            expeditions = 0;
            lootItems = 0;
        }

        void BuildSummary()
        {
            window.Heading("Geçen gün");

            var night = new StringBuilder();
            if (fed + hungry > 0)
                night.Append(hungry == 0 ? $"<color=#9be37a>{fed} kazazede yemek yedi.</color>"
                    : $"{fed} kazazede yedi, <color=#ff8f7a>{hungry} kazazede aç kaldı</color>" +
                      (skipped >= hungry ? " (senin kararınla)." : skipped > 0 ? $" ({skipped} kişi senin kararınla, kalanına kit yetmedi)." : " (Yaşam Kiti yetmedi)."));
            if (raidText != null) night.Append(night.Length > 0 ? "   " : "").Append(raidText);
            if (night.Length > 0) Line("Gece: " + night, UiFactory.Text);
            foreach (var report in scoutReports) Line("Keşif: " + report, UiFactory.Text);

            Line(deaths.Count == 0 ? "Kayıp yok." : "<color=#ff8f7a>Kayıp: " + string.Join(", ", deaths) + "</color>",
                deaths.Count == 0 ? new Color(0.78f, 0.78f, 0.74f) : UiFactory.Text);

            var done = new List<string>();
            if (rubbleCleared > 0) done.Add($"{rubbleCleared} moloz yığını temizlendi");
            if (recipesCrafted > 0) done.Add($"{recipesCrafted} üretim");
            if (structuresPlaced > 0) done.Add($"{structuresPlaced} yapı kuruldu");
            if (expeditions > 0) done.Add($"{expeditions} sefer ({lootItems} ganimet)");
            if (done.Count > 0) Line("Yapılanlar: " + string.Join(" · ", done), UiFactory.Text);

            var storage = ResourceStorage.Instance;
            if (storage != null)
            {
                var changes = new List<string>();
                foreach (var type in ResourceTypes.All)
                {
                    if (!type.IsStorable()) continue;
                    int delta = storage.Get(type) - startResources[(int)type];
                    if (delta == 0) continue;
                    string color = delta > 0 ? "#9be37a" : "#ff8f7a";
                    changes.Add($"<color={color}>{delta:+#;-#} {type.ToDisplayName()}</color>");
                }
                Line("Depo: " + (changes.Count > 0 ? string.Join("  ", changes) : "değişmedi"), UiFactory.Text);
            }
        }

        void BuildNewDay(int day)
        {
            window.Heading(day <= 1 ? "Başlangıç" : $"Gün {day}");

            if (day <= 1)
            {
                Line(GameMode.IsEndless
                    ? "SONSUZ MOD: Tahliye yok. Binayı ayakta tut, kazazedeleri doyur ve olabildiğince uzun dayan; telsiz birkaç günde bir erzak yardımı çağırır. Afetler gün geçtikçe sertleşir."
                    : $"Deprem sığınağın ayakta kalmasını sana bıraktı. Binayı ayakta tut, kazazedeleri doyur, {GameMode.CampaignTargetDay}. günden önce çatıdan helikopterle tahliye ol.",
                    UiFactory.Text);
                Line("İlk görevler: kırık kolonu onar, molozu kaz, zamanı durdurup planını yap.", new Color(0.84f, 0.84f, 0.8f));
                Line("Çalışma Tezgahı (2. kat): aletler (Kürek, Levye...), silah, mermi, ekipman ve yükseltmeler.   İnşaat Masası (giriş katı): eve kurulan yapılar.   Sandalyeler: dinlenmek için.", new Color(0.84f, 0.84f, 0.8f));
                Line("Her akşam Şehir Haritası açılır: kim sığınakta kalacak, kim nöbet tutacak, kim gece keşfine çıkacak ve kim yiyecek, sen seçersin.", new Color(0.84f, 0.84f, 0.8f));
            }

            var weather = WeatherSystem.Current;
            Line($"Hava: <b>{WeatherSystem.ToDisplayName(weather)}</b> — {WeatherSystem.ToEffectSummary(weather)}", UiFactory.Text);

            int alive = Survivor.AliveCount + ExpeditionSystem.AwayCount;
            int kits = ResourceStorage.Instance != null ? ResourceStorage.Instance.Get(ResourceType.Supplies) : 0;
            string kitColor = kits >= alive ? "#9be37a" : "#ff8f7a";
            Line($"Gece için {alive} Yaşam Kiti gerekir; depoda <color={kitColor}>{kits}</color> var.", UiFactory.Text);

            int stressed = 0;
            int injured = 0;
            int starving = 0;
            foreach (var survivor in Survivor.All)
            {
                if (survivor == null || survivor.IsDead) continue;
                if (survivor.Condition.Stress >= 60f) stressed++;
                if (survivor.Health < survivor.MaxHealth * 0.5f) injured++;
                if (survivor.Condition.Hunger > 0) starving++;
            }
            if (stressed + injured + starving > 0)
                Line($"Dikkat: <color=#ff8f7a>{(stressed > 0 ? $"{stressed} gergin  " : "")}{(injured > 0 ? $"{injured} yaralı  " : "")}{(starving > 0 ? $"{starving} aç" : "")}</color>".TrimEnd(),
                    UiFactory.Text);

            var building = FindFirstObjectByType<BuildingLayout>();
            if (building != null)
            {
                var conditions = EvacuationConditions.Evaluate(building, RadioStation.Instance);
                string goal = conditions.BlockingReason;
                if (GameMode.IsEndless)
                {
                    var radio = RadioStation.Instance;
                    string help = radio != null && radio.SignalComplete
                        ? $"Telsiz Gün {EvacuationSystem.NextDropDay}'de yeniden yardım çağırabilir."
                        : goal != null ? $"Erzak yardımı için: {goal}" : "<color=#9be37a>Telsizden erzak yardımı çağırabilirsin!</color>";
                    string best = GameMode.EndlessBest > 0 ? $"   ·   rekor: {GameMode.EndlessBest}. gün" : "";
                    Line(help + best, new Color(0.95f, 0.88f, 0.6f));
                }
                else
                {
                    string deadline = day < GameMode.CampaignTargetDay ? $"   ·   hedefe {GameMode.CampaignTargetDay - day} gün" :
                        day == GameMode.CampaignTargetDay ? "   ·   <color=#ff8f7a>hedef gün bugün!</color>" : "   ·   <color=#ff8f7a>hedef gün geçti</color>";
                    Line((goal != null ? $"Tahliye hedefi: {goal}" : "<color=#9be37a>Tahliye hazır: telsizden sinyal gönderebilirsin!</color>") + deadline,
                        new Color(0.95f, 0.88f, 0.6f));
                }
            }
        }

        void Line(string text, Color color)
        {
            var label = UiFactory.Label(window.Content, text, 21, color, TextAnchor.UpperLeft, FontStyle.Normal, false);
            label.gameObject.AddComponent<LayoutElement>().minHeight = 30f;
        }
    }
}
