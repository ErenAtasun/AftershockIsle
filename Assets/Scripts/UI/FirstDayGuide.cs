using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.CameraControl;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Economy;
using AftershockIsle.Expedition;
using AftershockIsle.Player;
using AftershockIsle.Save;
using UnityEngine;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// İlk oyunlar için görev listesi: sağ üstte sırayla yapılacak adımlar, tamamlananlar işaretlenir.
    /// Adımlar oyun olaylarından kendiliğinden tamamlanır; ilk görev bitince ya da dördüncü gün gelince panel kalkar.
    /// Aynı kontrol rehberi H ile her zaman açılır; bu panel yalnızca "ne yapmalıyım" sorusunu cevaplar.
    /// </summary>
    public class FirstDayGuide : MonoBehaviour
    {
        const int HideAfterDay = 4;
        const float MoveGoal = 2f;

        class Step
        {
            public string Title;
            public System.Func<string> Detail;
            public bool Done;
        }

        readonly List<Step> steps = new List<Step>();
        readonly int[] lastTotals = new int[ResourceTypes.Count];
        CommandController commands;
        DayCycle dayCycle;
        Vector3 startPosition;
        Survivor trackedSurvivor;
        bool hidden;

        // uGUI paneli: sağ üstte, kaynak rozetlerinin altında.
        GameObject panel;
        Text[] stepLabels;
        Text detailLabel;
        bool lastShown;

        void Awake()
        {
            steps.Add(new Step { Title = "Karakteri yürüt", Detail = () => $"{GameInput.Prompt(InputPrompt.Move)} ile karakteri gez. Seçili karakter yoksa {GameInput.Prompt(InputPrompt.SwitchCharacter)} ile seç." });
            steps.Add(new Step { Title = "Malzeme topla", Detail = () => $"Molozun yanında {GameInput.Prompt(InputPrompt.Interact)}: kaz. Bulunan malzeme doğrudan depoya eklenir." });
            steps.Add(new Step { Title = "Kırık kolonu yeniden dik", Detail = () => $"Kırık (Lv1) kolonun dibinde {GameInput.Prompt(InputPrompt.Interact)}: 4 Kalas & Hurda ile Lv2'ye çıkar." });
            steps.Add(new Step { Title = "Zamanı durdur", Detail = () => $"{GameInput.Prompt(InputPrompt.Pause)}: durdur, tekrar basınca devam. Planı durdurulmuş zamanda yap." });
            steps.Add(new Step { Title = "Tezgahta üret", Detail = () => $"Çalışma Tezgahı'nda {GameInput.Prompt(InputPrompt.Interact)}: bir tarif seç (ör. Mermi Paketi)." });
            steps.Add(new Step { Title = "Öğlen sefere çık", Detail = () => "Öğle vakti giriş katındaki dış kapıya git, haritadan lokasyon seç. İstersen yoldaş al." });
            steps.Add(new Step { Title = "İlk geceyi atlat", Detail = () => "Gece her kazazede 1 Yaşam Kiti yer. Depoda kit olsun." });
            BuildPanel();
        }

        void BuildPanel()
        {
            var canvas = UiFactory.Canvas("İlk Gün Rehberi", 11, transform);
            var rect = UiFactory.Panel(canvas.transform, "Rehber", UiFactory.Slate);
            rect.raycastTarget = false;
            panel = rect.gameObject;
            UiFactory.Place(rect.rectTransform, new Vector2(1f, 1f), new Vector2(-18f, -190f), new Vector2(440f, 0f));

            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 14, 16);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var title = UiFactory.Label(panel.transform, "İLK GÜN REHBERİ", 22, UiFactory.Accent, TextAnchor.MiddleLeft);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;

            stepLabels = new Text[steps.Count];
            for (int i = 0; i < steps.Count; i++)
            {
                var label = UiFactory.Label(panel.transform, "", 19, UiFactory.Text, TextAnchor.MiddleLeft, FontStyle.Normal, false);
                label.gameObject.AddComponent<LayoutElement>().preferredHeight = 27f;
                stepLabels[i] = label;
            }

            detailLabel = UiFactory.Label(panel.transform, "", 18, new Color(0.84f, 0.84f, 0.8f, 1f), TextAnchor.UpperLeft, FontStyle.Italic, false);
            panel.SetActive(false);
        }

        void OnEnable()
        {
            EventBus.Subscribe<ResourcesChanged>(OnResources);
            EventBus.Subscribe<ColumnStateChanged>(OnColumn);
            EventBus.Subscribe<TimeSpeedChanged>(OnSpeed);
            EventBus.Subscribe<RecipeCrafted>(OnCrafted);
            EventBus.Subscribe<ExpeditionStarted>(OnExpedition);
            EventBus.Subscribe<UpkeepResolved>(OnUpkeep);
            EventBus.Subscribe<GameLoaded>(OnGameLoaded);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<ResourcesChanged>(OnResources);
            EventBus.Unsubscribe<ColumnStateChanged>(OnColumn);
            EventBus.Unsubscribe<TimeSpeedChanged>(OnSpeed);
            EventBus.Unsubscribe<RecipeCrafted>(OnCrafted);
            EventBus.Unsubscribe<ExpeditionStarted>(OnExpedition);
            EventBus.Unsubscribe<UpkeepResolved>(OnUpkeep);
            EventBus.Unsubscribe<GameLoaded>(OnGameLoaded);
        }

        // Adımlar kayda yazılmaz: ilk günden sonraki bir kayıt yüklendiyse oyuncu temelleri zaten biliyor, rehber çıkmaz.
        void OnGameLoaded(GameLoaded evt)
        {
            if (dayCycle == null) dayCycle = FindFirstObjectByType<DayCycle>();
            if (evt.Success && dayCycle != null && dayCycle.Day > 1) hidden = true;
        }

        void Start()
        {
            commands = FindFirstObjectByType<CommandController>();
            dayCycle = FindFirstObjectByType<DayCycle>();
            var storage = ResourceStorage.Instance;
            if (storage != null)
                for (int i = 0; i < lastTotals.Length; i++) lastTotals[i] = storage.Get((ResourceType)i);
        }

        void Update()
        {
            if (!hidden)
            {
                if (dayCycle != null && dayCycle.Day >= HideAfterDay) hidden = true;
                if (steps.TrueForAll(step => step.Done)) hidden = true;
            }

            RefreshPanel();
            if (hidden || steps[0].Done || commands == null) return;

            // Seçili karakter yeterince yürüdüyse ilk adım biter.
            var selected = commands.Selected;
            if (selected == null) return;
            if (selected != trackedSurvivor)
            {
                trackedSurvivor = selected;
                startPosition = selected.transform.position;
            }
            if ((selected.transform.position - startPosition).sqrMagnitude >= MoveGoal * MoveGoal) steps[0].Done = true;
        }

        void OnResources(ResourcesChanged evt)
        {
            int index = (int)evt.Type;
            if (evt.Total > lastTotals[index]) steps[1].Done = true;
            lastTotals[index] = evt.Total;
        }

        void OnColumn(ColumnStateChanged evt)
        {
            if (evt.LeveledUp) steps[2].Done = true;
        }

        void OnSpeed(TimeSpeedChanged evt)
        {
            if (evt.Speed == TimeSpeed.Paused) steps[3].Done = true;
        }

        void OnCrafted(RecipeCrafted evt) => steps[4].Done = true;
        void OnExpedition(ExpeditionStarted evt) => steps[5].Done = true;
        void OnUpkeep(UpkeepResolved evt) => steps[6].Done = true;

        void RefreshPanel()
        {
            bool show = !hidden && !ModalMenu.IsOpen && !PauseMenu.IsOpen && !SideViewCamera.ViewingField;
            if (show != lastShown)
            {
                lastShown = show;
                panel.SetActive(show);
            }
            if (!show) return;

            // İlk yapılmayan adım ayrıntısıyla gösterilir, gerisi tek satır.
            Step next = steps.Find(step => !step.Done);
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var label = stepLabels[i];
                label.text = $"{(step.Done ? "[x]" : "[  ]")} {step.Title}";
                label.color = step.Done ? new Color(0.55f, 0.9f, 0.5f) : step == next ? UiFactory.Text : new Color(1f, 1f, 1f, 0.6f);
            }
            detailLabel.text = next != null ? next.Detail() : "";
        }
    }
}
