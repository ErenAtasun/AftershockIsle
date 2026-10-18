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
    /// Akşam planı: Gece olunca tam ekran şehir haritası açılır (CityMapView).
    /// Sol panelde kazazede listesi (görev, yemek) ve alt özet (tehdit, silah, yiyecek stoğu);
    /// harita üzerinde gece keşif bölgeleri (Yıkıntılar, Market, Hastane) ve oraya gidenlerin rozetleri yer alır.
    /// "Devam Et" ile yemek dağıtılır, nöbetçi atanır, keşifçiler gönderilir ve gün sonu başlar.
    /// </summary>
    public class EveningPlan : MonoBehaviour
    {
        enum Duty { Stay, Guard, Scout }

        class Entry
        {
            public Survivor Survivor;
            public Duty Duty;
            public NightSite Site = NightSite.Ruins;
            public bool Eat = true;

            public RectTransform Row;
            public Image RowPlate;
            public Text NameLabel;
            public Text StatsLabel;
            public Button DutyButton;
            public Text DutyLabel;
            public Image DutyPlate;
            public Button FoodButton;
            public Text FoodLabel;
            public Image FoodPlate;
            public Text EffectLabel;
        }

        class MapNodeView
        {
            public NightSite Site;
            public Button Button;
            public Image Highlight;
            public Text Label;
            public Text BadgeText;
            public GameObject BadgePlate;
        }

        const float StayStressRelief = 6f;
        const float SidePanelWidth = 530f;

        static EveningPlan instance;
        static bool planDone = true;

        CityMapView view;
        readonly List<Entry> entries = new List<Entry>();
        readonly List<Survivor> stayers = new List<Survivor>();
        readonly List<MapNodeView> mapNodes = new List<MapNodeView>();

        Button senButton;
        Image senHighlight;
        Text senBadgeText;
        GameObject senBadgePlate;

        Entry activeScoutEntry;
        NightSite selectedNightSite = NightSite.Ruins;
        bool selectedSen;

        Text summaryThreat;
        Text summaryDefense;
        Text summaryFood;
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

        void OnDayStarted(DayStarted evt)
        {
            if (SaveManager.IsApplying) return;

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
            view.Window.Tick(entries.Count > 0 ? entries[0].DutyButton.gameObject : confirmButton.gameObject);

            if (!GameInput.UsingGamepad && GameInput.MenuConfirmPressed) Confirm();

            RefreshDynamic();
        }

        // ---------------------------------------------------------------- Pencere Kurulumu

        void Open()
        {
            entries.Clear();
            foreach (var survivor in Survivor.All)
                if (survivor != null && !survivor.IsDead && survivor.gameObject.activeInHierarchy)
                    entries.Add(new Entry { Survivor = survivor });

            if (entries.Count == 0)
            {
                FindFirstObjectByType<UpkeepSystem>()?.Resolve();
                planDone = true;
                return;
            }

            if (dayCycle == null) dayCycle = FindFirstObjectByType<DayCycle>();
            if (planDay <= 0) planDay = dayCycle != null ? dayCycle.Day : 1;

            if (view == null) Build();

            view.Window.Title.text = $"GÜN {planDay} BİTİYOR — GECE PLANI & HARİTA";

            // Sol paneldeki listeyi baştan kur
            BuildRosterRows();

            ApplyDefaultRations();
            activeScoutEntry = entries.Find(e => e.Duty == Duty.Scout);
            selectedSen = false;
            selectedNightSite = NightSite.Ruins;

            RefreshAll();

            open = true;
            view.Open();
            TimeManager.Hold(this);
            if (GameInput.UsingGamepad && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(entries[0].DutyButton.gameObject);
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.DayPhaseChange, 0.7f);
        }

        void Close()
        {
            open = false;
            if (view != null) view.Close();
            TimeManager.Release(this);
        }

        void Build()
        {
            view = new CityMapView("Akşam Planı", null, false);
            view.SetSidePanel(true, SidePanelWidth);

            BuildMapNodes();
            BuildSidePanelSkeleton();

            // Footer
            var hint = UiFactory.Label(view.Window.Footer, "Kazazedelerin görevlerini ve yemeğini seç; keşif yerlerini haritadan belirle.", 15,
                new Color(0.85f, 0.85f, 0.8f), TextAnchor.MiddleLeft, FontStyle.Normal, false);
            hint.rectTransform.sizeDelta = new Vector2(620f, 44f);

            confirmButton = view.Window.FooterButton(
                GameInput.UsingGamepad ? "Planı Onayla & Devam Et" : $"Planı Onayla ({GameInput.Prompt(InputPrompt.Confirm)})",
                Confirm, UiFactory.Good, 320f);
        }

        void BuildMapNodes()
        {
            mapNodes.Clear();

            // Sığınak
            senButton = view.AddNode("Sığınak", CityMapView.ShelterRect, null, out senHighlight, out var senLabel);
            senLabel.text = "";
            senBadgeText = CityMapView.AddBadge(senButton, out senBadgePlate);
            senButton.onClick.AddListener(() =>
            {
                selectedSen = true;
                AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.UiClick, 0.4f);
                RefreshAll();
            });

            // Gece keşfi yapılabilen 3 bölge:
            // 1) Yakın Yıkıntılar (Enkaz Alanı düğümü)
            AddNightNode(NightSite.Ruins, "Yakın Yıkıntılar");
            // 2) Market Harabesi (Market düğümü)
            AddNightNode(NightSite.Market, "Market Harabesi");
            // 3) Hastane (Yıkık Klinik düğümü)
            AddNightNode(NightSite.Hospital, "Hastane");
        }

        void AddNightNode(NightSite site, string defaultName)
        {
            var norm = CityMapView.NightLayout(site, out var plate);
            var btn = view.AddNode(defaultName, norm, plate, out var hl, out var lbl);
            lbl.text = plate.HasValue ? defaultName : "";
            var badgeText = CityMapView.AddBadge(btn, out var badgePlate);

            btn.onClick.AddListener(() =>
            {
                OnSelectMapNode(site);
            });

            mapNodes.Add(new MapNodeView
            {
                Site = site,
                Button = btn,
                Highlight = hl,
                Label = lbl,
                BadgeText = badgeText,
                BadgePlate = badgePlate
            });
        }

        void OnSelectMapNode(NightSite site)
        {
            selectedNightSite = site;
            selectedSen = false;

            // Eğer şu an keşfe atanmış aktif bir kazazede seçiliyse, hedefini bu bölge yap
            if (activeScoutEntry != null && activeScoutEntry.Duty == Duty.Scout)
            {
                activeScoutEntry.Site = site;
                AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.CraftSuccess, 0.5f);
            }
            else
            {
                // Keşifte olan ilk kazazedeyi bu bölgeye yönlendir ya da ses ver
                AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.UiClick, 0.4f);
            }

            RefreshAll();
        }

        void BuildSidePanelSkeleton()
        {
            var side = view.SidePanel;

            // Üst Açıklama
            var header = UiFactory.Label(side, "KAZAZEDE GÖREV & YEMEK DAĞILIMI", 18, UiFactory.Accent, TextAnchor.UpperLeft);
            UiFactory.Place(header.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -12f), new Vector2(SidePanelWidth - 32f, 26f));

            var subHeader = UiFactory.Label(side, "Kal: dinlenir (-6 stres) • Nöbet: savunur (+10 stres) • Keşif: ganimet (+12 stres)", 12,
                new Color(0.8f, 0.8f, 0.76f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(subHeader.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -38f), new Vector2(SidePanelWidth - 32f, 20f));

            // Orta Liste Alanı (Scroll View)
            var scrollObj = UiFactory.Rect("RosterScroll", side);
            scrollObj.anchorMin = new Vector2(0f, 0f);
            scrollObj.anchorMax = new Vector2(1f, 1f);
            scrollObj.offsetMin = new Vector2(10f, 126f); // alttaki özet panelinin üstüne kadar
            scrollObj.offsetMax = new Vector2(-10f, -62f);

            var mask = scrollObj.gameObject.AddComponent<RectMask2D>();
            var scrollHit = scrollObj.gameObject.AddComponent<Image>();
            scrollHit.color = new Color(0f, 0f, 0f, 0f);

            var content = UiFactory.Rect("RosterContent", scrollObj);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var csf = content.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollRect = scrollObj.gameObject.AddComponent<ScrollRect>();
            scrollRect.viewport = scrollObj;
            scrollRect.content = content;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 25f;

            // Alt Özet Paneli (Baskın tehdidi, silah, barikat, yiyecek)
            var summaryCard = UiFactory.Panel(side, "SummaryCard", new Color(0.06f, 0.07f, 0.09f, 0.98f));
            UiFactory.Place(summaryCard.rectTransform, new Vector2(0f, 0f), new Vector2(10f, 10f), new Vector2(SidePanelWidth - 20f, 110f));

            var sumTitle = UiFactory.Label(summaryCard.transform, "GECE TEHLİKE & ERZAK DURUMU", 13, UiFactory.Accent, TextAnchor.UpperLeft);
            UiFactory.Place(sumTitle.rectTransform, new Vector2(0f, 1f), new Vector2(12f, -8f), new Vector2(SidePanelWidth - 44f, 18f));

            summaryThreat = UiFactory.Label(summaryCard.transform, "", 13, Color.white, TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(summaryThreat.rectTransform, new Vector2(0f, 1f), new Vector2(12f, -28f), new Vector2(SidePanelWidth - 44f, 20f));

            summaryDefense = UiFactory.Label(summaryCard.transform, "", 13, new Color(0.85f, 0.85f, 0.8f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(summaryDefense.rectTransform, new Vector2(0f, 1f), new Vector2(12f, -50f), new Vector2(SidePanelWidth - 44f, 24f));

            summaryFood = UiFactory.Label(summaryCard.transform, "", 13, Color.white, TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(summaryFood.rectTransform, new Vector2(0f, 1f), new Vector2(12f, -76f), new Vector2(SidePanelWidth - 44f, 28f));
        }

        void BuildRosterRows()
        {
            var content = view.SidePanel.Find("RosterScroll/RosterContent");
            if (content == null) return;

            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);

            foreach (var entry in entries)
            {
                var survivor = entry.Survivor;
                var rowObj = new GameObject($"Row_{survivor.DisplayName}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                rowObj.transform.SetParent(content, false);

                var le = rowObj.GetComponent<LayoutElement>();
                le.preferredHeight = 88f;
                le.minHeight = 88f;

                var plate = rowObj.GetComponent<Image>();
                plate.sprite = CityMapView.FrameSprite;
                plate.type = Image.Type.Sliced;
                plate.color = new Color(0.14f, 0.15f, 0.19f, 0.95f);

                entry.Row = rowObj.GetComponent<RectTransform>();
                entry.RowPlate = plate;

                // Tıklanarak aktif kâşif seçimi yapılabilir
                var rowBtn = rowObj.AddComponent<Button>();
                rowBtn.targetGraphic = plate;
                var currentEntry = entry;
                rowBtn.onClick.AddListener(() =>
                {
                    activeScoutEntry = currentEntry;
                    if (currentEntry.Duty == Duty.Scout)
                        selectedNightSite = currentEntry.Site;
                    AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.UiClick, 0.4f);
                    RefreshAll();
                });

                // İsim
                entry.NameLabel = UiFactory.Label(rowObj.transform, survivor.DisplayName, 17, UiFactory.Text, TextAnchor.UpperLeft);
                UiFactory.Place(entry.NameLabel.rectTransform, new Vector2(0f, 1f), new Vector2(12f, -6f), new Vector2(240f, 24f));

                // Can, Stres, Açlık
                entry.StatsLabel = UiFactory.Label(rowObj.transform, "", 13, Color.white, TextAnchor.UpperLeft, FontStyle.Normal, false);
                UiFactory.Place(entry.StatsLabel.rectTransform, new Vector2(0f, 1f), new Vector2(12f, -30f), new Vector2(260f, 22f));

                // Açıklama / Etki
                entry.EffectLabel = UiFactory.Label(rowObj.transform, "", 12, new Color(0.85f, 0.85f, 0.8f), TextAnchor.UpperLeft, FontStyle.Normal, false);
                UiFactory.Place(entry.EffectLabel.rectTransform, new Vector2(0f, 1f), new Vector2(12f, -54f), new Vector2(260f, 28f));

                // Görev Butonu
                entry.DutyButton = UiFactory.TextButton(rowObj.transform, "", new Vector2(130f, 40f), () => CycleDuty(currentEntry), null, 14);
                UiFactory.Place((RectTransform)entry.DutyButton.transform, new Vector2(1f, 0.5f), new Vector2(-114f, 0f), new Vector2(126f, 44f));
                entry.DutyLabel = entry.DutyButton.GetComponentInChildren<Text>();
                entry.DutyPlate = entry.DutyButton.GetComponent<Image>();

                // Yemek Butonu
                entry.FoodButton = UiFactory.TextButton(rowObj.transform, "", new Vector2(94f, 40f), () => ToggleFood(currentEntry), null, 14);
                UiFactory.Place((RectTransform)entry.FoodButton.transform, new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(94f, 44f));
                entry.FoodLabel = entry.FoodButton.GetComponentInChildren<Text>();
                entry.FoodPlate = entry.FoodButton.GetComponent<Image>();
            }
        }

        // ---------------------------------------------------------------- Mantık & Seçimler

        void CycleDuty(Entry entry)
        {
            var options = DutyOptions(entry.Survivor);
            int index = options.FindIndex(o => o.duty == entry.Duty && (entry.Duty != Duty.Scout || o.site == entry.Site));
            if (index < 0) index = 0;
            var next = options[(index + 1) % options.Count];

            if (next.duty == Duty.Guard)
            {
                foreach (var other in entries)
                    if (other != entry && other.Duty == Duty.Guard) other.Duty = Duty.Stay;
            }

            entry.Duty = next.duty;
            entry.Site = next.site;

            if (entry.Duty == Duty.Scout)
            {
                activeScoutEntry = entry;
                selectedNightSite = entry.Site;
            }

            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.UiClick, 0.6f);
            RefreshAll();
        }

        List<(Duty duty, NightSite site)> DutyOptions(Survivor survivor)
        {
            var options = new List<(Duty, NightSite)> { (Duty.Stay, NightSite.Ruins) };
            if (survivor.CanAct) options.Add((Duty.Guard, NightSite.Ruins));
            if (NightScavenge.CanScout(survivor, out _))
            {
                foreach (var site in NightScavenge.Sites) options.Add((Duty.Scout, site));
            }
            return options;
        }

        void ToggleFood(Entry entry)
        {
            entry.Eat = !entry.Eat;
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.UiClick, 0.6f);
            RefreshAll();
        }

        int Kits => ResourceStorage.Instance != null ? ResourceStorage.Instance.Get(ResourceType.Supplies) : 0;

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

        // ---------------------------------------------------------------- Görsel Yenileme

        void RefreshDynamic()
        {
            float pulse = CityMapView.Pulse;

            // Vurgu yalnızca seçili ya da üstüne gelinen düğümde (haritada sürekli kutu görünmesin).
            if (senHighlight != null)
            {
                var hover = senButton != null ? senButton.GetComponent<UiHoverFlag>() : null;
                senHighlight.color = selectedSen ? new Color(0.45f, 0.75f, 1f, pulse)
                    : hover != null && hover.Hovered ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0f);
            }

            foreach (var node in mapNodes)
            {
                bool isSel = !selectedSen && node.Site == selectedNightSite;
                var hover = node.Button != null ? node.Button.GetComponent<UiHoverFlag>() : null;
                node.Highlight.color = isSel ? new Color(1f, 0.85f, 0.25f, pulse)
                    : hover != null && hover.Hovered ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0f);
            }
        }

        void RefreshAll()
        {
            // 1. Roster Satırları
            foreach (var entry in entries)
            {
                var s = entry.Survivor;
                bool isFocused = entry == activeScoutEntry;

                entry.RowPlate.color = isFocused
                    ? new Color(0.24f, 0.26f, 0.35f, 0.98f)
                    : new Color(0.13f, 0.14f, 0.18f, 0.95f);

                string hungerStr = s.Condition.Hunger > 0 ? $"  •  <color=#ff8f7a>Açlık {s.Condition.Hunger}</color>" : "";
                string hpColor = s.Health < s.MaxHealth * 0.5f ? "#ff8f7a" : "#cfcfc8";
                string stColor = s.Condition.Stress >= 60f ? "#ff8f7a" : "#cfcfc8";
                entry.StatsLabel.text = $"<color={hpColor}>Can {s.Health:0}/{s.MaxHealth:0}</color>  •  <color={stColor}>Stres {s.Condition.Stress:0}</color>{hungerStr}";

                switch (entry.Duty)
                {
                    case Duty.Guard:
                        entry.DutyLabel.text = "Nöbet Tut";
                        entry.DutyPlate.color = new Color(0.6f, 0.44f, 0.15f, 0.98f);
                        entry.EffectLabel.text = $"Savunma gücü: <b>{Mathf.RoundToInt(NightDefenseSystem.DefenseScore(s))}</b> • Sabah +10 stres";
                        break;

                    case Duty.Scout:
                        float risk = NightScavenge.Risk(s, entry.Site, planDay, PartnersAt(entry));
                        string riskColor = risk < 0.2f ? "#9be37a" : risk < 0.35f ? "#ffd75a" : "#ff7a5a";
                        entry.DutyLabel.text = $"Keşif: {NightScavenge.SiteName(entry.Site)}";
                        entry.DutyPlate.color = new Color(0.2f, 0.35f, 0.6f, 0.98f);
                        entry.EffectLabel.text = $"Risk <color={riskColor}><b>%{Mathf.RoundToInt(risk * 100f)}</b></color> • {NightScavenge.SiteLoot(entry.Site)}";
                        break;

                    default:
                        NightScavenge.CanScout(s, out string why);
                        entry.DutyLabel.text = "Sığınakta Kal";
                        entry.DutyPlate.color = UiFactory.SlateLight;
                        entry.EffectLabel.text = why != null ? $"Dinlenir (-6 stres) • <color=#a8a8a0>Keşif: {why}</color>" : "Dinlenir (sabah -6 stres)";
                        break;
                }

                entry.FoodLabel.text = entry.Eat ? "Yesin" : "Aç kalsın";
                entry.FoodPlate.color = entry.Eat ? new Color(0.22f, 0.52f, 0.24f, 0.98f) : new Color(0.58f, 0.18f, 0.16f, 0.98f);
            }

            // 2. Harita Rozetleri (Hangi bölgeye kimler gidiyor)
            var stayNames = new List<string>();
            var guardNames = new List<string>();
            var siteScouts = new Dictionary<NightSite, List<string>>
            {
                { NightSite.Ruins, new List<string>() },
                { NightSite.Market, new List<string>() },
                { NightSite.Hospital, new List<string>() },
            };

            foreach (var e in entries)
            {
                if (e.Duty == Duty.Stay) stayNames.Add(e.Survivor.DisplayName);
                else if (e.Duty == Duty.Guard) guardNames.Add(e.Survivor.DisplayName);
                else if (e.Duty == Duty.Scout) siteScouts[e.Site].Add(e.Survivor.DisplayName);
            }

            // Sığınak Rozeti
            if (senBadgePlate != null)
            {
                int staying = stayNames.Count;
                string guardStr = guardNames.Count > 0 ? $" [Nöbet: {guardNames[0]}]" : "";
                senBadgePlate.SetActive(staying > 0 || guardNames.Count > 0);
                senBadgeText.text = $"{staying} kişi sığınakta{guardStr}";
            }

            // Gece Bölgeleri Rozetleri
            foreach (var node in mapNodes)
            {
                var scouts = siteScouts[node.Site];
                if (scouts.Count > 0)
                {
                    node.BadgePlate.SetActive(true);
                    node.BadgeText.text = $"Keşif: {string.Join(", ", scouts)}";
                }
                else
                {
                    node.BadgePlate.SetActive(false);
                    node.BadgeText.text = "";
                }
            }

            // 3. Sağ Detay Paneli
            if (selectedSen)
            {
                view.ShowDetail(true);
                view.DetailTitle.text = "SIĞINAK MERKEZİ";
                view.DetailBadge.text = "GÜVENLİ BÖLGE · GECE DİNLENME";
                var guardName = guardNames.Count > 0 ? guardNames[0] : "Kimse nöbette değil!";
                view.DetailText.text =
                    $"• Sığınakta Kalanlar: <b>{stayNames.Count} kişi</b>\n" +
                    $"• Kapı Nöbetçisi: <b>{guardName}</b>\n\n" +
                    "Sığınakta uyuyan kazazedeler sabah -6 stres ile dinlenmiş uyanır. Nöbetçi sabaha kadar kapıyı bekler ve olası yağmacı baskınını püskürtür.";
                view.SetDetailAction(null, null);
                view.SetBanner(null);
            }
            else
            {
                view.ShowDetail(true);
                string sName = NightScavenge.SiteName(selectedNightSite);
                view.DetailTitle.text = sName.ToUpperInvariant();
                view.DetailBadge.text = "GECE KEŞİF BÖLGESİ";

                var assigned = siteScouts[selectedNightSite];
                string who = assigned.Count > 0 ? string.Join(", ", assigned) : "Henüz kimse atanmadı";

                view.DetailText.text =
                    $"• Olası Ganimet: <b>{NightScavenge.SiteLoot(selectedNightSite)}</b>\n" +
                    $"• Atanan Kâşifler: <b>{who}</b>\n\n" +
                    "Gece keşfine çıkan kazazedeler sabah ganimetle döner ancak uyumadıkları için +12 stres alırlar. " +
                    "Aynı yere birden fazla kâşif göndermek riski azaltır.\n\n" +
                    "<color=#ffcc33>Soldan bir kazazedeyi 'Keşif' seçip haritadaki bu noktaya tıklayarak hedefini belirleyebilirsin.</color>";

                if (activeScoutEntry != null && activeScoutEntry.Duty == Duty.Scout)
                {
                    view.SetDetailAction($"{activeScoutEntry.Survivor.DisplayName} → Bu Bölgeye Ata", () =>
                    {
                        activeScoutEntry.Site = selectedNightSite;
                        AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.CraftSuccess, 0.5f);
                        RefreshAll();
                    });
                    view.SetBanner($"<b>{activeScoutEntry.Survivor.DisplayName}</b> için keşif hedefi: Haritadaki bir bölgeye tıkla");
                }
                else
                {
                    view.SetDetailAction(null, null);
                    view.SetBanner(null);
                }
            }

            // 4. Alt Sol Özet Kutusu
            var defense = NightDefenseSystem.Instance;
            var weather = WeatherSystem.Current;
            string threatLabel = defense != null ? NightDefenseSystem.ThreatLabel(defense.RaidChance(planDay)) : "?";
            summaryThreat.text = $"• Baskın Tehdidi: <b>{threatLabel}</b>  •  Hava: <b>{WeatherSystem.ToDisplayName(weather)}</b>";
            summaryDefense.text = $"• Silah: <b>{NightDefenseSystem.WeaponSummary()}</b>  •  Barikat: <b>{(NightDefenseSystem.HasBarricade ? "Çelik Kapı var" : "Yok")}</b>";

            int eaters = ExpeditionSystem.AwayCount;
            foreach (var entry in entries) if (entry.Eat) eaters++;
            int kits = Kits;
            string awayStr = ExpeditionSystem.AwayCount > 0 ? $" (seferdeki {ExpeditionSystem.AwayCount} dahil)" : "";
            string warnStr = eaters > kits ? $"   <color=#ff8f7a><b>{eaters - kits} kişiye kit yetmeyecek!</b></color>" : "";
            summaryFood.text = $"• Yiyecek: Depoda <b>{kits}</b> Yaşam Kiti  •  Bu gece: <b>{eaters}</b> kişi yiyecek{awayStr}{warnStr}";
        }

        // ---------------------------------------------------------------- Onay

        void Confirm()
        {
            if (!open) return;

            // 1) Yemek: aç bırakılanlar yemez.
            var skip = new HashSet<Survivor>();
            foreach (var entry in entries) if (!entry.Eat) skip.Add(entry.Survivor);
            FindFirstObjectByType<UpkeepSystem>()?.Resolve(skip);

            // 2) Nöbet
            Survivor guard = null;
            foreach (var entry in entries)
                if (entry.Duty == Duty.Guard && entry.Survivor != null && !entry.Survivor.IsDead) guard = entry.Survivor;
            NightDefenseSystem.Instance?.SetGuard(guard);

            // 3) Keşif ve dinlenme
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
