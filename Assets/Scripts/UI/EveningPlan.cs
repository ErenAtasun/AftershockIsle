using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.CameraControl;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using AftershockIsle.Economy;
using AftershockIsle.Expedition;
using AftershockIsle.Save;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Akşam planı: gece başlarken (zaman durur) her kazazede için gece görevi ve yemek seçilir.
    /// Görev: sığınakta kal (dinlenir, sabah stresi azalır) / kapıda nöbet tut (tek kişi) / gece keşfine çık (üç yer;
    /// sabah ganimetle ya da yaralı döner). Yemek: yesin / aç kalsın (kit yetmiyorsa kimin yiyeceğini sen seçersin).
    /// "Planı Onayla" ile yemek dağıtılır, nöbetçi kapıya gider, keşifçiler kapıdan çıkar.
    /// </summary>
    public class EveningPlan : MonoBehaviour
    {
        enum Duty { Stay, Guard, Scout }

        class Entry
        {
            public Survivor Survivor;
            public Duty Duty;
            public NightSite Site;
            public bool Eat = true;
            public Button DutyButton;
            public Text DutyLabel;
            public Button FoodButton;
            public Text FoodLabel;
            public Text Effect;
            public Image DutyPlate;
            public Image FoodPlate;
        }

        const float StayStressRelief = 6f;

        static EveningPlan instance;
        static bool planDone = true;

        UiWindow window;
        readonly List<Entry> entries = new List<Entry>();
        readonly List<Survivor> stayers = new List<Survivor>();
        Text foodLabel;
        Button confirmButton;
        bool pending;
        bool open;
        int planDay;
        DayCycle dayCycle;

        /// <summary>Bu gecenin planı yapıldı mı (kayıt için).</summary>
        public static bool PlanDone => instance == null || planDone;

        public static bool IsOpen => instance != null && instance.open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindFirstObjectByType<BuildingLayout>() == null || FindFirstObjectByType<EveningPlan>() != null) return;
            new GameObject("[Akşam Planı]").AddComponent<EveningPlan>();
        }

        /// <summary>Kayıttan: gece yüklendi ve plan yapılmamışsa pencere yeniden açılır.</summary>
        public static void Restore(bool done, bool night)
        {
            if (instance == null) return;
            instance.Close();
            planDone = done || !night;
            instance.pending = night && !done;
            instance.stayers.Clear();
        }

        void Awake()
        {
            instance = this;
            planDone = true;
        }

        void OnEnable()
        {
            UpkeepSystem.HandledByPlan = true;
            NightDefenseSystem.GuardChosenByPlan = true;
            EventBus.Subscribe<DayPhaseChanged>(OnPhaseChanged);
            EventBus.Subscribe<DayStarted>(OnDayStarted);
        }

        void OnDisable()
        {
            UpkeepSystem.HandledByPlan = false;
            NightDefenseSystem.GuardChosenByPlan = false;
            EventBus.Unsubscribe<DayPhaseChanged>(OnPhaseChanged);
            EventBus.Unsubscribe<DayStarted>(OnDayStarted);
            TimeManager.Release(this);
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void OnPhaseChanged(DayPhaseChanged evt)
        {
            if (evt.Phase != DayPhase.Night || SaveManager.IsApplying) return;
            planDay = evt.Day;
            planDone = false;
            pending = true;
        }

        // Sığınakta kalıp uyuyanlar sabah biraz dinlenmiş uyanır.
        void OnDayStarted(DayStarted evt)
        {
            if (SaveManager.IsApplying) return;

            // Plan hiç açılamadan (ör. sefer sürerken) sabah olduysa yemek yine dağıtılsın.
            if (pending || open)
            {
                Close();
                pending = false;
                if (!planDone) FindFirstObjectByType<UpkeepSystem>()?.Resolve();
                planDone = true;
            }

            foreach (var survivor in stayers)
                if (survivor != null && !survivor.IsDead) survivor.Condition.RelieveStress(StayStressRelief);
            stayers.Clear();
        }

        void Update()
        {
            if (pending)
            {
                if (dayCycle == null) dayCycle = FindFirstObjectByType<DayCycle>();
                bool blocked = NarrativeEventSystem.IsModalOpen || SideViewCamera.ViewingField || PlacementController.IsPlacementActive;
                var session = FindFirstObjectByType<GameSession>();
                if (session != null && session.Outcome != GameOutcome.None)
                {
                    pending = false;
                    return;
                }
                if (!blocked && dayCycle != null && dayCycle.Phase == DayPhase.Night)
                {
                    pending = false;
                    Open();
                }
                return;
            }

            if (!open) return;

            TimeManager.Hold(this);
            window.Tick(entries.Count > 0 ? entries[0].DutyButton.gameObject : confirmButton.gameObject);

            // Klavyede Enter onaylar; gamepad'de A seçili düğmeye basar (onay düğmesi alt satırda).
            if (!GameInput.UsingGamepad && GameInput.MenuConfirmPressed) Confirm();
        }

        // ---------------------------------------------------------------- Pencere

        void Open()
        {
            entries.Clear();
            foreach (var survivor in Survivor.All)
                if (survivor != null && !survivor.IsDead && survivor.gameObject.activeInHierarchy)
                    entries.Add(new Entry { Survivor = survivor });

            if (entries.Count == 0)
            {
                // Sığınakta kimse yoksa (herkes seferde) yemek doğrudan dağıtılır.
                FindFirstObjectByType<UpkeepSystem>()?.Resolve();
                planDone = true;
                return;
            }

            if (dayCycle == null) dayCycle = FindFirstObjectByType<DayCycle>();
            if (planDay <= 0) planDay = dayCycle != null ? dayCycle.Day : 1;

            if (window == null) window = new UiWindow("Akşam Planı", new Vector2(1240f, 800f), null, true, false);
            window.ClearContent();
            window.ClearFooter();
            window.Title.text = $"GÜN {planDay} BİTİYOR — AKŞAM PLANI";

            BuildHeader();
            foreach (var entry in entries) BuildRow(entry);

            var hint = UiFactory.Label(window.Footer, "Düğmelere basarak seçenekleri değiştir.", 15, new Color(0.8f, 0.8f, 0.75f),
                TextAnchor.MiddleLeft, FontStyle.Normal, false);
            hint.rectTransform.sizeDelta = new Vector2(520f, 46f);
            confirmButton = window.FooterButton(GameInput.UsingGamepad ? "Planı Onayla" : $"Planı Onayla ({GameInput.Prompt(InputPrompt.Confirm)})",
                Confirm, UiFactory.Good, 280f);

            ApplyDefaultRations();
            RefreshAll();

            open = true;
            window.Show(true);
            TimeManager.Hold(this);
            if (GameInput.UsingGamepad && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(entries[0].DutyButton.gameObject);
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.DayPhaseChange, 0.7f);
        }

        void Close()
        {
            open = false;
            if (window != null) window.Show(false);
            TimeManager.Release(this);
        }

        void BuildHeader()
        {
            var defense = NightDefenseSystem.Instance;
            var weather = WeatherSystem.Current;
            string threat = defense != null ? NightDefenseSystem.ThreatLabel(defense.RaidChance(planDay)) : "?";

            Line("Gece çöküyor. Kim sığınakta kalacak, kim kapıyı bekleyecek, kim karanlıkta dışarı çıkacak?", UiFactory.Text, 20);
            Line($"Hava: <b>{WeatherSystem.ToDisplayName(weather)}</b>   •   Baskın tehdidi: {threat}   •   Silah: {NightDefenseSystem.WeaponSummary()}   •   " +
                 $"Barikat: {(NightDefenseSystem.HasBarricade ? "Çelik Kapı var" : "yok")}", new Color(0.84f, 0.84f, 0.8f), 17);
            Line("Kal: dinlenir (sabah -6 stres).   Nöbet: baskına karşı savunur, uyumaz (+10 stres).   Keşif: sabah ganimetle döner, uyumaz (+12 stres), risklidir.",
                new Color(0.84f, 0.84f, 0.8f), 16);
            foodLabel = Line("", UiFactory.Text, 19);
        }

        Text Line(string text, Color color, int size)
        {
            var label = UiFactory.Label(window.Content, text, size, color, TextAnchor.UpperLeft, FontStyle.Normal, false);
            label.gameObject.AddComponent<LayoutElement>().minHeight = size + 10f;
            return label;
        }

        void BuildRow(Entry entry)
        {
            var survivor = entry.Survivor;
            var row = window.Row(92f);

            var name = UiFactory.Label(row, survivor.DisplayName, 19, UiFactory.Text, TextAnchor.UpperLeft);
            UiFactory.Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -8f), new Vector2(330f, 26f));

            string hunger = survivor.Condition.Hunger > 0 ? $"   <color=#ff8f7a>Açlık {survivor.Condition.Hunger}</color>" : "";
            string healthColor = survivor.Health < survivor.MaxHealth * 0.5f ? "#ff8f7a" : "#cfcfc8";
            string stressColor = survivor.Condition.Stress >= 60f ? "#ff8f7a" : "#cfcfc8";
            var stats = UiFactory.Label(row,
                $"<color={healthColor}>Can {survivor.Health:0}/{survivor.MaxHealth:0}</color>   <color={stressColor}>Stres {survivor.Condition.Stress:0}</color>{hunger}",
                15, UiFactory.Text, TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(stats.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -38f), new Vector2(330f, 22f));

            var traits = UiFactory.Label(row, survivor.TraitsSummary(), 13, new Color(0.7f, 0.7f, 0.66f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(traits.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -62f), new Vector2(330f, 22f));

            entry.DutyButton = UiFactory.TextButton(row, "", new Vector2(320f, 52f), () => CycleDuty(entry), null, 18);
            UiFactory.Place((RectTransform)entry.DutyButton.transform, new Vector2(0f, 0.5f), new Vector2(356f, 0f), new Vector2(320f, 52f));
            entry.DutyLabel = entry.DutyButton.GetComponentInChildren<Text>();
            entry.DutyPlate = entry.DutyButton.GetComponent<Image>();

            entry.FoodButton = UiFactory.TextButton(row, "", new Vector2(160f, 52f), () => ToggleFood(entry), null, 18);
            UiFactory.Place((RectTransform)entry.FoodButton.transform, new Vector2(0f, 0.5f), new Vector2(690f, 0f), new Vector2(160f, 52f));
            entry.FoodLabel = entry.FoodButton.GetComponentInChildren<Text>();
            entry.FoodPlate = entry.FoodButton.GetComponent<Image>();

            entry.Effect = UiFactory.Label(row, "", 14, new Color(0.84f, 0.84f, 0.8f), TextAnchor.MiddleLeft, FontStyle.Normal, false);
            UiFactory.Place(entry.Effect.rectTransform, new Vector2(0f, 0.5f), new Vector2(866f, 0f), new Vector2(320f, 80f));
        }

        // ---------------------------------------------------------------- Seçimler

        void CycleDuty(Entry entry)
        {
            var options = DutyOptions(entry.Survivor);
            int index = options.FindIndex(o => o.duty == entry.Duty && (entry.Duty != Duty.Scout || o.site == entry.Site));
            var next = options[(index + 1) % options.Count];

            if (next.duty == Duty.Guard)
                foreach (var other in entries)
                    if (other != entry && other.Duty == Duty.Guard) other.Duty = Duty.Stay;

            entry.Duty = next.duty;
            entry.Site = next.site;
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.UiClick, 0.6f);
            RefreshAll();
        }

        List<(Duty duty, NightSite site)> DutyOptions(Survivor survivor)
        {
            var options = new List<(Duty, NightSite)> { (Duty.Stay, NightSite.Ruins) };
            if (survivor.CanAct) options.Add((Duty.Guard, NightSite.Ruins));
            if (NightScavenge.CanScout(survivor, out _))
                foreach (var site in NightScavenge.Sites) options.Add((Duty.Scout, site));
            return options;
        }

        void ToggleFood(Entry entry)
        {
            entry.Eat = !entry.Eat;
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.UiClick, 0.6f);
            RefreshAll();
        }

        int Kits => ResourceStorage.Instance != null ? ResourceStorage.Instance.Get(ResourceType.Supplies) : 0;

        // Kit yetmiyorsa varsayılan olarak en sağlıklı ve tok olanlar aç kalır (en zayıflar önce yer).
        void ApplyDefaultRations()
        {
            int available = Kits - ExpeditionSystem.AwayCount;
            var order = new List<Entry>(entries);
            order.Sort((a, b) => Need(b.Survivor).CompareTo(Need(a.Survivor)));
            foreach (var entry in order)
            {
                entry.Eat = available > 0;
                available--;
            }
        }

        static float Need(Survivor survivor) =>
            survivor.Condition.Hunger * 100f + (survivor.MaxHealth - survivor.Health) + survivor.Condition.Stress * 0.2f;

        int PartnersAt(Entry entry)
        {
            int count = 0;
            foreach (var other in entries)
                if (other != entry && other.Duty == Duty.Scout && other.Site == entry.Site) count++;
            return count;
        }

        void RefreshAll()
        {
            int eaters = ExpeditionSystem.AwayCount;
            foreach (var entry in entries) if (entry.Eat) eaters++;
            int kits = Kits;
            string away = ExpeditionSystem.AwayCount > 0 ? $" (seferdeki {ExpeditionSystem.AwayCount} kişi dahil)" : "";
            string warn = eaters > kits ? $"   <color=#ff8f7a>{eaters - kits} kişiye kit yetmeyecek!</color>" : "";
            foodLabel.text = $"Yiyecek: depoda <b>{kits}</b> Yaşam Kiti   •   bu gece yiyecek: <b>{eaters}</b> kişi{away}{warn}";

            foreach (var entry in entries)
            {
                var survivor = entry.Survivor;
                switch (entry.Duty)
                {
                    case Duty.Guard:
                        entry.DutyLabel.text = "Nöbet tut";
                        entry.DutyPlate.color = new Color(0.55f, 0.42f, 0.16f, 0.97f);
                        entry.Effect.text = $"Savunma gücü <b>{Mathf.RoundToInt(NightDefenseSystem.DefenseScore(survivor))}</b>\nSabah +10 stres";
                        break;
                    case Duty.Scout:
                        float risk = NightScavenge.Risk(survivor, entry.Site, planDay, PartnersAt(entry));
                        string riskColor = risk < 0.2f ? "#9be37a" : risk < 0.35f ? "#ffd75a" : "#ff7a5a";
                        entry.DutyLabel.text = $"Keşif: {NightScavenge.SiteName(entry.Site)}";
                        entry.DutyPlate.color = new Color(0.2f, 0.33f, 0.52f, 0.97f);
                        entry.Effect.text = $"Risk <color={riskColor}><b>%{Mathf.RoundToInt(risk * 100f)}</b></color>  •  {NightScavenge.SiteLoot(entry.Site)}\nSabah döner, +12 stres";
                        break;
                    default:
                        NightScavenge.CanScout(survivor, out string why);
                        entry.DutyLabel.text = "Sığınakta kal";
                        entry.DutyPlate.color = UiFactory.SlateLight;
                        entry.Effect.text = why != null ? $"Dinlenir (sabah -6 stres)\n<color=#a8a8a0>Keşfe çıkamaz: {why}</color>" : "Dinlenir (sabah -6 stres)";
                        break;
                }

                entry.FoodLabel.text = entry.Eat ? "Yesin" : "Aç kalsın";
                entry.FoodPlate.color = entry.Eat ? new Color(0.24f, 0.5f, 0.24f, 0.97f) : new Color(0.55f, 0.2f, 0.16f, 0.97f);
            }
        }

        // ---------------------------------------------------------------- Onay

        void Confirm()
        {
            if (!open) return;

            // 1) Yemek: aç bırakılanlar yemez. Keşifçiler çıkmadan yer (seferdekiler stoktan yemeye devam eder).
            var skip = new HashSet<Survivor>();
            foreach (var entry in entries) if (!entry.Eat) skip.Add(entry.Survivor);
            FindFirstObjectByType<UpkeepSystem>()?.Resolve(skip);

            // 2) Nöbet.
            Survivor guard = null;
            foreach (var entry in entries)
                if (entry.Duty == Duty.Guard && entry.Survivor != null && !entry.Survivor.IsDead) guard = entry.Survivor;
            NightDefenseSystem.Instance?.SetGuard(guard);

            // 3) Keşif ve dinlenme.
            stayers.Clear();
            var scoutNames = new List<string>();
            foreach (var entry in entries)
            {
                var survivor = entry.Survivor;
                if (survivor == null || survivor.IsDead) continue;
                if (entry.Duty == Duty.Scout && NightScavenge.Instance != null && NightScavenge.CanScout(survivor, out _))
                {
                    NightScavenge.Instance.Send(survivor, entry.Site);
                    scoutNames.Add(survivor.DisplayName);
                }
                else if (entry.Duty == Duty.Stay) stayers.Add(survivor);
            }
            if (scoutNames.Count > 0)
                EventBus.Publish(new NarrativeNotice($"{string.Join(", ", scoutNames)} gece keşfine çıkıyor; sabah dönecek."));

            planDone = true;
            Close();
        }
    }
}
