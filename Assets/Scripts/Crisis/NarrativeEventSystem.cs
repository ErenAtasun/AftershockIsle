using System;
using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.CameraControl;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Economy;
using AftershockIsle.Skills;
using AftershockIsle.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace AftershockIsle.Crisis
{
    public readonly struct NarrativeNotice
    {
        public readonly string Message;
        public NarrativeNotice(string message) => Message = message;
    }

    /// <summary>
    /// This War of Mine tarzı Rastgele Kapı Karşılaşmaları, Ahlaki Seçimler ve Gece Sığınak İçi Olaylar.
    /// </summary>
    public class NarrativeEventSystem : MonoBehaviour
    {
        public static NarrativeEventSystem Instance { get; private set; }
        public static bool IsModalOpen => Instance != null && Instance.activeEvent != null;

        struct ChoiceOption
        {
            public string Label;
            public bool Enabled;
            public System.Action OnSelect;
        }

        class ActiveEncounter
        {
            public string Title;
            public string Body;
            public readonly List<ChoiceOption> Choices = new List<ChoiceOption>();
        }

        readonly HashSet<int> triggeredDays = new HashSet<int>();
        int motherGiftDueDay = -1;
        ActiveEncounter activeEvent;
        // Kapı karşılaşmaları: her gün değil (2. gün hep, sonra ~%65), sabahın farklı bir anında; aynı karşılaşma
        // dördü de görülmeden tekrar gelmez. Günün kararı gün numarasından türetilir, böylece kayıt yüklenince değişmez.
        const int EncounterCount = 4;
        const int EncounterChancePercent = 65;
        readonly List<int> encounterDeck = new List<int>();
        int lastEncounter = -1;

        DayCycle dayCycle;

        // Olay penceresi (uGUI). Açıkken zaman TimeManager.Hold ile durur; seçim yapılmadan kapanmaz.
        UiWindow window;
        ActiveEncounter shownEvent;
        GameObject firstChoice;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstance()
        {
            if (Instance == null)
            {
                var existing = FindFirstObjectByType<NarrativeEventSystem>();
                if (existing != null)
                {
                    Instance = existing;
                    return;
                }

                var systems = GameObject.Find("[Systems]");
                if (systems != null)
                {
                    systems.AddComponent<NarrativeEventSystem>();
                }
            }
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            TimeManager.Release(this);
        }

        void OnEnable()
        {
            EventBus.Subscribe<DayStarted>(OnDayStarted);
            EventBus.Subscribe<DayPhaseChanged>(OnPhaseChanged);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<DayStarted>(OnDayStarted);
            EventBus.Unsubscribe<DayPhaseChanged>(OnPhaseChanged);
        }

        void OnDayStarted(DayStarted evt)
        {
            // Yardım edilen annenin 2 gün sonraki teşekkür hediyesi
            if (motherGiftDueDay > 0 && evt.Day >= motherGiftDueDay)
            {
                motherGiftDueDay = -1;
                if (ResourceStorage.Instance != null)
                {
                    ResourceStorage.Instance.Add(new ResourceAmount(ResourceType.Electronics, 2));
                    ResourceStorage.Instance.Add(new ResourceAmount(ResourceType.Metal, 2));
                }
                RelieveAllStress(15f);
                EventBus.Publish(new NarrativeNotice("Kapıya bir paket bırakılmış! Yardım ettiğiniz anne 2 Elektronik ve 2 Metal göndermiş."));
            }
        }

        void OnPhaseChanged(DayPhaseChanged evt)
        {
            if (evt.Phase == DayPhase.Night)
            {
                ResolveNightShelterTension();
            }
        }

        void Update()
        {
            if (activeEvent != null)
            {
                // Seçim yapılana kadar zaman durur (tutma tekrar edilse de tek sayılır).
                TimeManager.Hold(this);
                ShowActiveEvent();
                window.Tick(firstChoice);
                return;
            }
            if (window != null && window.IsOpen) window.Show(false);

            if (SideViewCamera.ViewingField || PlacementController.IsPlacementActive) return;

            if (dayCycle == null) dayCycle = FindFirstObjectByType<DayCycle>();
            var cycle = dayCycle;
            if (cycle == null || cycle.Day < 2) return;

            if (cycle.Phase == DayPhase.Morning &&
                cycle.PhaseElapsed >= TriggerSecond(cycle.Day) &&
                !triggeredDays.Contains(cycle.Day))
            {
                triggeredDays.Add(cycle.Day);
                if (HasEncounter(cycle.Day)) TriggerEncounter(DrawEncounter());
            }
        }

        static int DayHash(int day) => (int)(((uint)day * 2654435761u) >> 8);

        static bool HasEncounter(int day) => day == 2 || DayHash(day) % 100 < EncounterChancePercent;

        static float TriggerSecond(int day) => 8f + DayHash(day) % 30;

        // Desteden çek: hepsi görülene kadar tekrar yok, deste yenilenince son gelen başa düşmez.
        int DrawEncounter()
        {
            if (encounterDeck.Count == 0)
                for (int i = 0; i < EncounterCount; i++)
                    if (i != lastEncounter || EncounterCount == 1) encounterDeck.Add(i);

            int pick = encounterDeck[Random.Range(0, encounterDeck.Count)];
            encounterDeck.Remove(pick);
            lastEncounter = pick;
            return pick;
        }

        int debugEncounterCursor;

        /// <summary>Test: karşılaşmaları sırayla açar.</summary>
        public void TriggerNextDoorEncounter() => TriggerEncounter(debugEncounterCursor++ % EncounterCount);

        void TriggerEncounter(int encounterIndex)
        {
            var storage = ResourceStorage.Instance;
            if (storage == null) return;

            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.DoorKnock, 0.9f);
            var encounter = new ActiveEncounter();

            switch (encounterIndex)
            {
                case 0: // Çaresiz Anne
                {
                    encounter.Title = "🚪 Sığınak Kapısı: Çaresiz Anne";
                    encounter.Body =
                        "Kapının dışından titreyen bir kadın sesi geliyor:\n\n" +
                        "\"Lütfen kapıyı açın! Yıkılan yan apartmandan çıktık, çocuğum ateşler içinde yanıyor. " +
                        "Sadece 1 Yaşam Kiti ya da ilacınız varsa hayatımızı kurtarırsınız... Söz veriyorum karşılıksız bırakmam!\"";

                    bool hasKit = storage.Has(new ResourceAmount(ResourceType.Supplies, 1));
                    encounter.Choices.Add(new ChoiceOption
                    {
                        Label = hasKit
                            ? "Yardım Et (-1 Yaşam Kiti) — Herkese -25 Stres, gelecekte sürpriz ödül"
                            : "Yardım Et (Yeterli Yaşam Kiti Yok)",
                        Enabled = hasKit,
                        OnSelect = () =>
                        {
                            if (storage.TrySpend(new ResourceAmount(ResourceType.Supplies, 1)))
                            {
                                RelieveAllStress(25f);
                                var cycle = FindFirstObjectByType<DayCycle>();
                                motherGiftDueDay = (cycle != null ? cycle.Day : 2) + 2;
                                PublishNotice("Kadın gözyaşları içinde teşekkür etti. Sığınaktaki herkesin morali yükseldi (-25 Stres).");
                            }
                            CloseModal();
                        }
                    });
                    encounter.Choices.Add(new ChoiceOption
                    {
                        Label = "Kapıyı Açma (Erzak Korunur, Vicdan Azabı: +15 Stres)",
                        Enabled = true,
                        OnSelect = () =>
                        {
                            AddAllStress(15f);
                            PublishNotice("Kadın ağlayarak uzaklaştı. Sığınaktakiler ağır bir sessizliğe gömüldü (+15 Stres).");
                            CloseModal();
                        }
                    });
                    break;
                }

                case 1: // Gezgin Tüccar
                {
                    encounter.Title = "🎒 Sığınak Kapısı: Gezgin Hurdacı";
                    encounter.Body =
                        "Sırtında devasa bir çantayla gezen yaşlı bir hurdacı kapıyı tıklattı:\n\n" +
                        "\"Selam komşular! Bende konserve erzak ve sağlam aküler var. Fazla inşaat hurdanız veya borunuz varsa takas yapalım mı?\"";

                    bool canTradeSupplies = storage.Has(new ResourceAmount(ResourceType.Scrap, 3));
                    bool canTradeElec = storage.Has(new ResourceAmount(ResourceType.Metal, 2));

                    encounter.Choices.Add(new ChoiceOption
                    {
                        Label = canTradeSupplies
                            ? "Takas: 3 Kalas & Hurda Ver → +2 Yaşam Kiti Al"
                            : "Takas: 3 Kalas & Hurda Gerekli (Yetersiz)",
                        Enabled = canTradeSupplies,
                        OnSelect = () =>
                        {
                            if (storage.TrySpend(new ResourceAmount(ResourceType.Scrap, 3)))
                            {
                                storage.Add(new ResourceAmount(ResourceType.Supplies, 2));
                                PublishNotice("Hurdacıyla takas yapıldı: +2 Yaşam Kiti depoya eklendi.");
                            }
                            CloseModal();
                        }
                    });
                    encounter.Choices.Add(new ChoiceOption
                    {
                        Label = canTradeElec
                            ? "Takas: 2 Boru & Metal Ver → +1 Elektronik & Akü Al"
                            : "Takas: 2 Boru & Metal Gerekli (Yetersiz)",
                        Enabled = canTradeElec,
                        OnSelect = () =>
                        {
                            if (storage.TrySpend(new ResourceAmount(ResourceType.Metal, 2)))
                            {
                                storage.Add(new ResourceAmount(ResourceType.Electronics, 1));
                                PublishNotice("Hurdacıyla takas yapıldı: +1 Elektronik & Akü depoya eklendi.");
                            }
                            CloseModal();
                        }
                    });
                    encounter.Choices.Add(new ChoiceOption
                    {
                        Label = "Teşekkür Edip Gönder (Takas Yapma)",
                        Enabled = true,
                        OnSelect = () =>
                        {
                            PublishNotice("Hurdacı iyi şanslar dileyip yoluna devam etti.");
                            CloseModal();
                        }
                    });
                    break;
                }

                case 2: // Yaralı Mühendis
                {
                    encounter.Title = "🩺 Sığınak Kapısı: Yaralı Teknisyen";
                    encounter.Body =
                        "Kolundan yaralanmış eski bir belediye teknisyeni kapıya yaslanmış durumda:\n\n" +
                        "\"Yağmacılardan kaçarken vuruldum... Yaramı saracak 1 Yaşam Kitiniz varsa size bina statiği ve telsiz frekansları konusunda bildiğim tüm püf noktalarını öğretirim!\"";

                    bool hasKit = storage.Has(new ResourceAmount(ResourceType.Supplies, 1));
                    encounter.Choices.Add(new ChoiceOption
                    {
                        Label = hasKit
                            ? "Yarasını Sar (-1 Yaşam Kiti) → +1 Yetenek Puanı & -15 Stres"
                            : "Yarasını Sar (1 Yaşam Kiti Gerekli)",
                        Enabled = hasKit,
                        OnSelect = () =>
                        {
                            if (storage.TrySpend(new ResourceAmount(ResourceType.Supplies, 1)))
                            {
                                if (SkillTree.Instance != null) SkillTree.Instance.AddPoints(1);
                                RelieveAllStress(15f);
                                PublishNotice("Teknisyen tedavi edildi! Karşılığında kritik teknik bilgiler paylaştı (+1 Yetenek Puanı).");
                            }
                            CloseModal();
                        }
                    });
                    encounter.Choices.Add(new ChoiceOption
                    {
                        Label = "İçeri Alma (Risk Alma)",
                        Enabled = true,
                        OnSelect = () =>
                        {
                            AddAllStress(8f);
                            PublishNotice("Yaralı teknisyen karanlık sokağa doğru uzaklaştı.");
                            CloseModal();
                        }
                    });
                    break;
                }

                default: // Şüpheli Yabancı / Yağmacı Gözcüsü
                {
                    encounter.Title = "⚠ Sığınak Kapısı: Şüpheli Yabancı";
                    encounter.Body =
                        "Kapüşonlu bir yabancı kapıyı yumrukluyor:\n\n" +
                        "\"Dışarıda fırtına ve yağmacılar var! Birkaç saatliğine giriş katında ısınmama izin verin, elimdeki hurda metalleri size bırakırım.\"";

                    encounter.Choices.Add(new ChoiceOption
                    {
                        Label = "İçeri Al (%65 İhtimalle +2 Metal & +2 Hurda Bırakır, %35 Hırsızlık Riski)",
                        Enabled = true,
                        OnSelect = () =>
                        {
                            if (Random.value < 0.65f)
                            {
                                storage.Add(new ResourceAmount(ResourceType.Metal, 2));
                                storage.Add(new ResourceAmount(ResourceType.Scrap, 2));
                                PublishNotice("Yabancı sözünü tuttu! Isındıktan sonra +2 Metal ve +2 Hurda bırakıp ayrıldı.");
                            }
                            else
                            {
                                storage.TrySpend(new ResourceAmount(ResourceType.Supplies, 1));
                                AddAllStress(12f);
                                PublishNotice("Yabancı fırsatını bulup depodan erzak aşırarak kaçtı! (+12 Stres)");
                            }
                            CloseModal();
                        }
                    });
                    encounter.Choices.Add(new ChoiceOption
                    {
                        Label = "Kapıdan Uzaklaştır (Güvenliği Koru)",
                        Enabled = true,
                        OnSelect = () =>
                        {
                            NightDefenseSystem.RaiseAttention(25);
                            PublishNotice("Yabancı tehditler savurarak kapıdan uzaklaştı. Sığınağın yerini artık yağmacılar biliyor olabilir...");
                            CloseModal();
                        }
                    });
                    break;
                }
            }

            Open(encounter);
        }

        void ResolveNightShelterTension()
        {
            // Gece sığınak içi moral veya gerilim olayı
            bool hasOptimist = false;
            Survivor starvingSurvivor = null;

            foreach (var s in Survivor.All)
            {
                if (s == null || s.IsDead) continue;
                if (s.HasTrait(TraitId.Optimist)) hasOptimist = true;
                if (s.Condition.Hunger >= 2) starvingSurvivor = s;
            }

            if (hasOptimist || PlaceableStructure.HasAny(RecipeId.ComfortCorner))
            {
                RelieveAllStress(12f);
                PublishNotice("Gece: Sığınakta yapılan moral sohbeti herkesin stresini azalttı (-12 Stres).");
            }
            else if (starvingSurvivor != null)
            {
                AddAllStress(8f);
                PublishNotice($"Gece Gerilimi: {starvingSurvivor.DisplayName} açlık yüzünden gece boyu huzursuzlandı (+8 Stres).");
            }
        }

        void RelieveAllStress(float amount)
        {
            foreach (var s in Survivor.All)
                if (s != null && !s.IsDead) s.Condition.RelieveStress(amount);
        }

        void AddAllStress(float amount)
        {
            foreach (var s in Survivor.All)
                if (s != null && !s.IsDead) s.Condition.AddStress(amount);
        }

        void PublishNotice(string msg) => EventBus.Publish(new NarrativeNotice(msg));

        /// <summary>Başka sistemlerin (ör. gece savunması) aynı seçim penceresini kullanması için seçenek.</summary>
        public struct ExternalChoice
        {
            public string Label;
            public bool Enabled;
            public System.Action OnSelect;

            public ExternalChoice(string label, System.Action onSelect, bool enabled = true)
            {
                Label = label;
                OnSelect = onSelect;
                Enabled = enabled;
            }
        }

        /// <summary>Seçim penceresi açar; başka bir olay açıksa sıraya alınır. Seçim yapılınca pencere kendiliğinden kapanır.</summary>
        public void ShowExternal(string title, string body, IList<ExternalChoice> choices)
        {
            var encounter = new ActiveEncounter { Title = title, Body = body };
            foreach (var choice in choices)
            {
                var callback = choice.OnSelect;
                encounter.Choices.Add(new ChoiceOption
                {
                    Label = choice.Label,
                    Enabled = choice.Enabled,
                    OnSelect = () =>
                    {
                        callback?.Invoke();
                        CloseModal();
                    }
                });
            }

            Open(encounter);
        }

        readonly Queue<ActiveEncounter> pendingEvents = new Queue<ActiveEncounter>();

        /// <summary>Olayı açar; başka bir olay açıksa sıraya alır.</summary>
        void Open(ActiveEncounter encounter)
        {
            if (activeEvent != null)
            {
                pendingEvents.Enqueue(encounter);
                return;
            }
            activeEvent = encounter;
            TimeManager.Hold(this);
        }

        void CloseModal()
        {
            activeEvent = null;
            if (pendingEvents.Count > 0)
            {
                activeEvent = pendingEvents.Dequeue();
                return;
            }
            TimeManager.Release(this);
            if (window != null) window.Show(false);
        }

        // Seçenek hata verse bile pencere kapanır ve zaman bırakılır: oyun bir olayda asla kilitli kalmaz.
        void Choose(ActiveEncounter encounter, ChoiceOption choice)
        {
            if (activeEvent != encounter || !choice.Enabled) return;
            try
            {
                choice.OnSelect?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                if (activeEvent == encounter) CloseModal();
            }
        }

        // ---------------------------------------------------------------- Pencere

        void ShowActiveEvent()
        {
            if (window == null) BuildWindow();
            if (shownEvent == activeEvent && window.IsOpen) return;

            shownEvent = activeEvent;
            var encounter = activeEvent;
            window.Title.text = UiFactory.StripSymbols(encounter.Title);
            window.ClearContent();

            var body = UiFactory.Label(window.Content, UiFactory.StripSymbols(encounter.Body), 20, UiFactory.Text, TextAnchor.UpperLeft, FontStyle.Normal, false);
            body.lineSpacing = 1.1f;
            var gap = UiFactory.Rect("Boşluk", window.Content).gameObject.AddComponent<LayoutElement>();
            gap.preferredHeight = 10f;

            firstChoice = null;
            foreach (var choice in encounter.Choices)
            {
                var option = choice;
                var button = UiFactory.TextButton(window.Content, UiFactory.StripSymbols(option.Label), new Vector2(0f, 58f),
                    () => Choose(encounter, option), option.Enabled ? (Color?)null : new Color(0.25f, 0.25f, 0.27f, 1f), 19);
                var element = button.gameObject.AddComponent<LayoutElement>();
                element.preferredHeight = 58f;
                element.minHeight = 58f;
                button.interactable = option.Enabled;
                if (option.Enabled && firstChoice == null) firstChoice = button.gameObject;
            }

            window.Show(true);
            if (GameInput.UsingGamepad && EventSystem.current != null && firstChoice != null)
                EventSystem.current.SetSelectedGameObject(firstChoice);
        }

        void BuildWindow()
        {
            window = new UiWindow("Olay Penceresi", new Vector2(880f, 620f), null, true, false);
            var hint = UiFactory.Label(window.Footer, "Zaman durdu — devam etmek için bir seçim yapın.", 17,
                new Color(0.78f, 0.78f, 0.74f, 1f), TextAnchor.MiddleRight, FontStyle.Italic, false);
            hint.rectTransform.sizeDelta = new Vector2(760f, 40f);
        }

        public IEnumerable<int> GetTriggeredDays() => triggeredDays;
        public IReadOnlyList<int> EncounterDeck => encounterDeck;
        public int LastEncounter => lastEncounter;

        /// <summary>Kayıttan: kalan karşılaşma destesi ve en son gelen karşılaşma.</summary>
        public void RestoreDeck(IEnumerable<int> deck, int last)
        {
            encounterDeck.Clear();
            if (deck != null)
                foreach (int index in deck)
                    if (index >= 0 && index < EncounterCount && !encounterDeck.Contains(index)) encounterDeck.Add(index);
            lastEncounter = last >= 0 && last < EncounterCount ? last : -1;
        }
        public int MotherGiftDueDay => motherGiftDueDay;

        public void RestoreState(IEnumerable<int> savedDays, int savedMotherGiftDay)
        {
            triggeredDays.Clear();
            if (savedDays != null)
                foreach (var d in savedDays) triggeredDays.Add(d);
            motherGiftDueDay = savedMotherGiftDay;
            activeEvent = null;
            pendingEvents.Clear();
            TimeManager.Release(this);
            if (window != null) window.Show(false);
        }
    }
}
