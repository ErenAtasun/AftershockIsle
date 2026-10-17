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
        GUIStyle titleStyle;
        GUIStyle detailStyle;

        void Awake()
        {
            steps.Add(new Step { Title = "Karakteri yürüt", Detail = () => $"{GameInput.Prompt(InputPrompt.Move)} ile karakteri gez. Seçili karakter yoksa {GameInput.Prompt(InputPrompt.SwitchCharacter)} ile seç." });
            steps.Add(new Step { Title = "Malzeme topla", Detail = () => $"Molozun yanında {GameInput.Prompt(InputPrompt.Interact)}: kaz. Karakter bulduğunu Ortak Sandık'a götürür." });
            steps.Add(new Step { Title = "Kırık kolonu yeniden dik", Detail = () => $"Kırık (Lv1) kolonun dibinde {GameInput.Prompt(InputPrompt.Interact)}: 4 Kalas & Hurda ile Lv2'ye çıkar." });
            steps.Add(new Step { Title = "Zamanı durdur", Detail = () => $"{GameInput.Prompt(InputPrompt.Pause)}: durdur, tekrar basınca devam. Planı durdurulmuş zamanda yap." });
            steps.Add(new Step { Title = "Tezgahta üret", Detail = () => $"Çalışma Tezgahı'nda {GameInput.Prompt(InputPrompt.Interact)}: bir tarif seç (ör. Mermi Paketi)." });
            steps.Add(new Step { Title = "Öğlen sefere çık", Detail = () => "Öğle vakti giriş katındaki dış kapıya git, haritadan lokasyon seç. İstersen yoldaş al." });
            steps.Add(new Step { Title = "İlk geceyi atlat", Detail = () => "Gece her kazazede 1 Yaşam Kiti yer. Depoda kit olsun." });
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
            if (hidden) return;

            if (dayCycle != null && dayCycle.Day >= HideAfterDay) hidden = true;
            if (steps.TrueForAll(step => step.Done)) hidden = true;
            if (steps[0].Done || commands == null) return;

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

        void OnGUI()
        {
            UiTheme.Apply();
            if (hidden || ModalMenu.IsOpen || SideViewCamera.ViewingField) return;

            titleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            detailStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };

            // İlk yapılmayan adım ayrıntısıyla gösterilir, gerisi tek satır.
            Step next = steps.Find(step => !step.Done);
            float height = 30f + steps.Count * 20f + 52f;
            // Kaynak rozetleri ve sefer etiketinin (GameHud) altında.
            var rect = new Rect(UiTheme.Width - 312, 150f, 300, height);
            UIBlocker.Add(rect);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12));
            GUILayout.Label("İLK GÜN REHBERİ", titleStyle);
            foreach (var step in steps)
            {
                var previous = GUI.color;
                GUI.color = step.Done ? new Color(0.5f, 0.9f, 0.5f) : step == next ? Color.white : new Color(1f, 1f, 1f, 0.6f);
                GUILayout.Label($"{(step.Done ? "[x]" : "[ ]")} {step.Title}", detailStyle);
                GUI.color = previous;
            }
            if (next != null) GUILayout.Label(next.Detail(), detailStyle);
            GUILayout.EndArea();
        }
    }
}
