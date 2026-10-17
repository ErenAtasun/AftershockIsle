using System.Collections;
using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Economy;
using AftershockIsle.Interaction;
using AftershockIsle.Skills;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    /// <summary>Departing/Returning: kâşif kapı ile sokak arasında görünür halde yürüyor.</summary>
    public enum ExpeditionStage { None, ChoosingSite, Departing, Away, Returning }

    public readonly struct ExpeditionStarted
    {
        public readonly Survivor Explorer;
        public readonly ExpeditionSite Site;

        public ExpeditionStarted(Survivor explorer, ExpeditionSite site)
        {
            Explorer = explorer;
            Site = site;
        }
    }

    public readonly struct ExpeditionReturned
    {
        public readonly Survivor Explorer;
        public readonly IReadOnlyList<ResourceAmount> Loot;
        public readonly string HazardReport;
        public readonly Survivor Rescued;

        public ExpeditionReturned(Survivor explorer, IReadOnlyList<ResourceAmount> loot, string hazardReport, Survivor rescued)
        {
            Explorer = explorer;
            Loot = loot;
            HazardReport = hazardReport;
            Rescued = rescued;
        }
    }

    /// <summary>
    /// Dış sefer akışı: kapıda harita açılır (oyun durur) → lokasyon seçilir, kâşif binadan çıkar →
    /// oynanabilir dış sahaya geçilir (FieldExpedition: arama, dövüş, kurtarma) → kâşif çıkıştan dönüp
    /// çantayı sandığa götürür. Sefere günde bir kez, yalnızca öğle fazında çıkılır; sığınak sahadayken de işler.
    /// </summary>
    public class ExpeditionSystem : MonoBehaviour
    {
        [SerializeField] TimeManager timeManager;
        [SerializeField] DayCycle dayCycle;
        [SerializeField] int baseBagSlots = 3;
        [SerializeField] float rescueExtraction = 8f;
        [SerializeField] float timeUpStress = 20f;
        [SerializeField] ExpeditionSite[] sites = ExpeditionSite.Defaults();
        [Tooltip("Dış sahada kullanılan modeller (sahne kurucu doldurur). Boşsa saha kutularla kurulur.")]
        [SerializeField] FieldArtLibrary fieldArt;

        static readonly Dictionary<SkillBranch, string> rescueNames = new Dictionary<SkillBranch, string>
        {
            { SkillBranch.Construction, "Mert" },
            { SkillBranch.Exploration, "Deniz" },
            { SkillBranch.Engineering, "Arda" },
        };

        const int DoorFloor = 1;
        const float WalkSpeed = 2.5f;
        const float OutsideDistance = 3.5f;

        BuildingLayout building;
        FieldExpedition field;
        int lastExpeditionDay;

        public static ExpeditionSystem Instance { get; private set; }

        public IReadOnlyList<ExpeditionSite> Sites => sites;
        public ExpeditionStage Stage { get; private set; }
        public Survivor Explorer { get; private set; }
        public ExpeditionSite CurrentSite { get; private set; }
        public float Remaining => field != null ? field.Remaining : 0f;
        public bool IsRescuing => field != null && field.RescueSecured;
        public FieldExpedition Field => field;
        public int BagSlots => baseBagSlots + (SkillTree.Has(SkillId.WideBag) ? 1 : 0);

        /// <summary>Binanın dışında olan (sahnede pasif) kazazede sayısı.</summary>
        public static int AwayCount => AwayExplorer != null ? 1 : 0;

        /// <summary>Şu an binanın dışında olan kâşif (Survivor.All'da görünmez).</summary>
        public static Survivor AwayExplorer =>
            Instance != null && Instance.IsExplorerAway && !Instance.Explorer.IsDead ? Instance.Explorer : null;
        bool IsExplorerAway => Stage == ExpeditionStage.Away;

        float ExtractionTime => rescueExtraction * (SkillTree.Has(SkillId.LifeSaver) ? 0.5f : 1f);
        float OutsideX => building.MaxX + OutsideDistance;

        /// <summary>Kâşif kapıdan geçiyor (yürüyüşü bitene kadar emir almaz).</summary>
        public bool IsInTransit(Survivor survivor) =>
            survivor != null && survivor == TransitSurvivor &&
            (Stage == ExpeditionStage.Departing || Stage == ExpeditionStage.Returning);

        /// <summary>0–1 arası sefer ilerlemesi (panel için).</summary>
        public float Progress01 => CurrentSite == null ? 0f : Mathf.Clamp01(1f - Remaining / CurrentSite.duration);

        Survivor TransitSurvivor { get; set; }

        public static bool RescueVictimAvailable =>
            SurvivorRoster.Instance != null && !SurvivorRoster.Instance.IsFull && SurvivorRoster.FirstMissingBranch() != null;

        void Awake()
        {
            Instance = this;
            building = FindFirstObjectByType<BuildingLayout>();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();
            if (dayCycle == null) dayCycle = FindFirstObjectByType<DayCycle>();

            // Eski sahnelerde serileştirilmiş saha verisi olmayabilir.
            if (sites == null || sites.Length == 0 || sites[0].lootSpots <= 0) sites = ExpeditionSite.Defaults();
            field = gameObject.AddComponent<FieldExpedition>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public bool CanDepart(out string reason)
        {
            if (Stage != ExpeditionStage.None) reason = "Zaten bir sefer sürüyor.";
            else if (dayCycle.Phase != DayPhase.Noon) reason = "Sefere sadece öğle vakti çıkılabilir.";
            else if (lastExpeditionDay == dayCycle.Day) reason = "Bugün zaten sefere çıkıldı.";
            else reason = null;
            return reason == null;
        }

        public void OpenMap(Survivor explorer)
        {
            if (!CanDepart(out _)) return;

            Stage = ExpeditionStage.ChoosingSite;
            Explorer = explorer;
            timeManager.SetSpeed(TimeSpeed.Paused);
            Debug.Log($"[Sefer] Harita açıldı: {explorer.DisplayName}");
        }

        public void CancelMap()
        {
            if (Stage != ExpeditionStage.ChoosingSite) return;
            Stage = ExpeditionStage.None;
            Explorer = null;
        }

        public void Depart(ExpeditionSite site)
        {
            if (Stage != ExpeditionStage.ChoosingSite || Explorer == null || Explorer.IsDead) return;

            CurrentSite = site;
            lastExpeditionDay = dayCycle.Day;

            Explorer.Tasks.CancelAll();
            Stage = ExpeditionStage.Departing;
            timeManager.SetSpeed(TimeSpeed.Normal);
            EventBus.Publish(new ExpeditionStarted(Explorer, site));
            Debug.Log($"[Sefer] {Explorer.DisplayName} → {site.name} (saha süresi {site.duration:0} sn)");
            TransitSurvivor = Explorer;
            StartCoroutine(DepartRoutine(Explorer));
        }

        // Kâşif kapıdan sokağa yürür, sonra sahneden çıkar; sefer süresi o an başlar.
        IEnumerator DepartRoutine(Survivor explorer)
        {
            yield return WalkThroughDoor(explorer, building.MaxX, OutsideX);
            explorer.gameObject.SetActive(false);
            Stage = ExpeditionStage.Away;
            field.Begin(explorer, CurrentSite, building, BagSlots, ExtractionTime, RescueVictimAvailable, dayCycle.Day, fieldArt);
        }

        IEnumerator ReturnRoutine(Survivor explorer, IReadOnlyList<ResourceAmount> loot, string hazardReport, bool rescuing)
        {
            yield return WalkThroughDoor(explorer, OutsideX, building.MaxX);

            Survivor rescued = null;
            if (rescuing)
            {
                var branch = SurvivorRoster.FirstMissingBranch();
                if (branch != null)
                {
                    string name = $"{rescueNames[branch.Value]} ({branch.Value.ToDisplayName()})";
                    rescued = SurvivorRoster.Instance.Spawn(name, branch.Value, DoorFloor, building.MaxX - 0.8f);
                }
            }
            Stage = ExpeditionStage.None;
            TransitSurvivor = null;

            // Çantayı sandığa götürmesi otomatik; oyuncu isterse başka emir verebilir.
            if (!explorer.IsDead && StorageChest.Main != null) explorer.Tasks.Assign(StorageChest.Main, false);

            EventBus.Publish(new ExpeditionReturned(explorer, new List<ResourceAmount>(loot), hazardReport, rescued));
        }

        IEnumerator WalkThroughDoor(Survivor explorer, float fromX, float toX)
        {
            explorer.Mover.Halt();
            explorer.Mover.Frozen = true;
            explorer.transform.rotation = Quaternion.LookRotation(toX > fromX ? Vector3.right : Vector3.left);

            float distance = Mathf.Abs(toX - fromX);
            for (float walked = 0f; walked < distance; walked += WalkSpeed * Time.deltaTime)
            {
                explorer.transform.position = building.Point(DoorFloor, Mathf.MoveTowards(fromX, toX, walked));
                yield return null;
            }

            explorer.transform.position = building.Point(DoorFloor, toX);
            explorer.Mover.Frozen = explorer.IsDead || explorer.IsTrapped;
        }

        void Update()
        {
            if (Stage != ExpeditionStage.Away || field.Outcome == FieldOutcome.Active) return;

            var outcome = field.Outcome;
            var explorer = Explorer;

            if (outcome == FieldOutcome.Died || explorer.IsDead)
            {
                Debug.Log($"[Sefer] {explorer.DisplayName} sahada öldü.");
                field.End();
                Stage = ExpeditionStage.None;
                Explorer = null;
                CurrentSite = null;
                TransitSurvivor = null;
                return;
            }

            Return(explorer, outcome == FieldOutcome.TimeUp);
        }

        void Return(Survivor explorer, bool timeUp)
        {
            string report = field.Report;
            if (timeUp)
            {
                // Karanlık basınca kaçarak döner; çantanın yarısı yolda düşer.
                field.DropHalfBag();
                explorer.Condition.AddStress(timeUpStress);
                report = (report != null ? report + " " : "") + "Süre doldu, kaçarken çantanın yarısı düştü.";
            }

            var loot = new List<ResourceAmount>(field.Bag);
            bool rescuing = field.RescueSecured;
            field.End();

            Stage = ExpeditionStage.Returning;
            Explorer = null;
            CurrentSite = null;

            explorer.transform.position = building.Point(DoorFloor, OutsideX);
            explorer.gameObject.SetActive(true);
            explorer.FillBag(loot);

            Debug.Log($"[Sefer] {explorer.DisplayName} döndü: {loot.Count} çeşit ganimet{(report != null ? $", {report}" : "")}");
            TransitSurvivor = explorer;
            StartCoroutine(ReturnRoutine(explorer, loot, report, rescuing));
        }
    }
}
