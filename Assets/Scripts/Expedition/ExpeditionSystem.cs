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
    public enum ExpeditionStage { None, ChoosingSite, Departing, Away, RescueDecision, Returning }

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
    /// süre dolunca çantayla kapıdan döner ve çantayı sandığa götürür. Enkaz Alanı'nda yolun yarısında
    /// kurtarma kararı sorulur. Sefere günde bir kez, yalnızca öğle fazında çıkılır.
    /// </summary>
    public class ExpeditionSystem : MonoBehaviour
    {
        [SerializeField] TimeManager timeManager;
        [SerializeField] DayCycle dayCycle;
        [SerializeField] int baseBagSlots = 3;
        [SerializeField] float rescueExtraction = 20f;
        [SerializeField] float hazardStress = 20f;
        [SerializeField] ExpeditionSite[] sites = ExpeditionSite.Defaults();

        static readonly Dictionary<SkillBranch, string> rescueNames = new Dictionary<SkillBranch, string>
        {
            { SkillBranch.Construction, "Mert" },
            { SkillBranch.Exploration, "Deniz" },
            { SkillBranch.Engineering, "Arda" },
        };

        const int DoorFloor = 1;
        const float WalkSpeed = 2.5f;
        const float OutsideDistance = 3.5f;

        readonly List<ResourceAmount> loot = new List<ResourceAmount>();
        BuildingLayout building;
        int lastExpeditionDay;
        bool rescueDecided;
        bool rescuing;

        public static ExpeditionSystem Instance { get; private set; }

        public IReadOnlyList<ExpeditionSite> Sites => sites;
        public ExpeditionStage Stage { get; private set; }
        public Survivor Explorer { get; private set; }
        public ExpeditionSite CurrentSite { get; private set; }
        public float Remaining { get; private set; }
        public bool IsRescuing => rescuing;
        public int BagSlots => baseBagSlots + (SkillTree.Has(SkillId.WideBag) ? 1 : 0);

        /// <summary>Binanın dışında olan (sahnede pasif) kazazede sayısı.</summary>
        public static int AwayCount => AwayExplorer != null ? 1 : 0;

        /// <summary>Şu an binanın dışında olan kâşif (Survivor.All'da görünmez).</summary>
        public static Survivor AwayExplorer =>
            Instance != null && Instance.IsExplorerAway && !Instance.Explorer.IsDead ? Instance.Explorer : null;
        bool IsExplorerAway => Stage == ExpeditionStage.Away || Stage == ExpeditionStage.RescueDecision;

        float ExtractionTime => rescueExtraction * (SkillTree.Has(SkillId.LifeSaver) ? 0.5f : 1f);
        float OutsideX => building.MaxX + OutsideDistance;

        /// <summary>Kâşif kapıdan geçiyor (yürüyüşü bitene kadar emir almaz).</summary>
        public bool IsInTransit(Survivor survivor) =>
            survivor != null && survivor == TransitSurvivor &&
            (Stage == ExpeditionStage.Departing || Stage == ExpeditionStage.Returning);

        /// <summary>0–1 arası sefer ilerlemesi (panel için).</summary>
        public float Progress01 => CurrentSite == null ? 0f
            : Mathf.Clamp01(1f - Remaining / (CurrentSite.duration + (IsRescuing ? ExtractionTime : 0f)));

        Survivor TransitSurvivor { get; set; }

        public static bool RescueVictimAvailable =>
            SurvivorRoster.Instance != null && !SurvivorRoster.Instance.IsFull && SurvivorRoster.FirstMissingBranch() != null;

        void Awake()
        {
            Instance = this;
            building = FindFirstObjectByType<BuildingLayout>();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();
            if (dayCycle == null) dayCycle = FindFirstObjectByType<DayCycle>();
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
            Remaining = site.duration;
            lastExpeditionDay = dayCycle.Day;
            rescueDecided = !site.isRescueSite;
            rescuing = false;

            Explorer.Tasks.CancelAll();
            Stage = ExpeditionStage.Departing;
            timeManager.SetSpeed(TimeSpeed.Normal);
            EventBus.Publish(new ExpeditionStarted(Explorer, site));
            Debug.Log($"[Sefer] {Explorer.DisplayName} → {site.name} ({site.duration:0} sn)");
            TransitSurvivor = Explorer;
            StartCoroutine(DepartRoutine(Explorer));
        }

        // Kâşif kapıdan sokağa yürür, sonra sahneden çıkar; sefer süresi o an başlar.
        IEnumerator DepartRoutine(Survivor explorer)
        {
            yield return WalkThroughDoor(explorer, building.MaxX, OutsideX);
            explorer.gameObject.SetActive(false);
            Stage = ExpeditionStage.Away;
        }

        IEnumerator ReturnRoutine(Survivor explorer, string hazardReport)
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
            rescuing = false;
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

        public void DecideRescue(bool rescue)
        {
            if (Stage != ExpeditionStage.RescueDecision) return;

            rescueDecided = true;
            rescuing = rescue && RescueVictimAvailable;
            if (rescuing) Remaining += ExtractionTime;
            Stage = ExpeditionStage.Away;
            timeManager.SetSpeed(TimeSpeed.Normal);
        }

        void Update()
        {
            if (Stage != ExpeditionStage.Away) return;

            Remaining -= Time.deltaTime;

            if (!rescueDecided && Remaining <= CurrentSite.duration * 0.5f)
            {
                if (RescueVictimAvailable)
                {
                    Stage = ExpeditionStage.RescueDecision;
                    timeManager.SetSpeed(TimeSpeed.Paused);
                    return;
                }
                rescueDecided = true;
            }

            if (Remaining <= 0f) Return();
        }

        void Return()
        {
            var explorer = Explorer;
            var site = CurrentSite;
            Stage = ExpeditionStage.Returning;
            Explorer = null;
            CurrentSite = null;

            explorer.transform.position = building.Point(DoorFloor, OutsideX);
            explorer.gameObject.SetActive(true);

            RollLoot(site);
            explorer.FillBag(loot);
            string hazardReport = ApplyHazard(explorer, site);

            Debug.Log($"[Sefer] {explorer.DisplayName} döndü: {loot.Count} çeşit ganimet{(hazardReport != null ? $", {hazardReport}" : "")}");
            TransitSurvivor = explorer;
            StartCoroutine(ReturnRoutine(explorer, hazardReport));
        }

        void RollLoot(ExpeditionSite site)
        {
            loot.Clear();
            float totalWeight = 0f;
            foreach (var entry in site.loot) totalWeight += entry.weight;
            if (totalWeight <= 0f) return;

            for (int slot = 0; slot < BagSlots; slot++)
            {
                float roll = Random.value * totalWeight;
                foreach (var entry in site.loot)
                {
                    roll -= entry.weight;
                    if (roll > 0f) continue;

                    int amount = Random.Range(entry.min, Mathf.Max(entry.min, entry.max) + 1);
                    AddLoot(entry.type, amount);
                    break;
                }
            }
        }

        void AddLoot(ResourceType type, int amount)
        {
            for (int i = 0; i < loot.Count; i++)
            {
                if (loot[i].type != type) continue;
                loot[i] = new ResourceAmount(type, loot[i].amount + amount);
                return;
            }
            loot.Add(new ResourceAmount(type, amount));
        }

        string ApplyHazard(Survivor explorer, ExpeditionSite site)
        {
            if (site.hazard == ExpeditionHazard.None || Random.value >= site.hazardChance) return null;

            string report = site.hazard == ExpeditionHazard.Fire
                ? "Klinikte yangın ve dumana yakalandı."
                : "Göçük altında kaldı, yaralı kurtuldu.";

            explorer.Condition.AddStress(hazardStress);
            explorer.TakeDamage(site.hazardDamage, $"Seferde: {report}");
            return report;
        }
    }
}
