using System.Collections.Generic;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Expedition;
using AftershockIsle.Interaction;
using UnityEngine;

namespace AftershockIsle.Skills
{
    public readonly struct SkillUnlocked
    {
        public readonly SkillId Id;
        public SkillUnlocked(SkillId id) => Id = id;
    }

    public readonly struct SkillPointsGained
    {
        public readonly int Total;
        public SkillPointsGained(int total) => Total = total;
    }

    /// <summary>
    /// Ortak yetenek puanı havuzu. Her yeni gün +1 puan gelir; bir dalın yetenekleri sırayla açılır
    /// ve dal ancak o dalın uzmanı yaşayan bir kazazede varken kullanılabilir. Etkiler takıma geneldir.
    /// </summary>
    public class SkillTree : MonoBehaviour
    {
        [SerializeField] int startingPoints;
        [SerializeField] int pointsPerDay = 1;

        readonly HashSet<SkillId> unlocked = new HashSet<SkillId>();

        public static SkillTree Instance { get; private set; }
        public int Points { get; private set; }

        public static bool Has(SkillId id) => Instance != null && Instance.unlocked.Contains(id);

        // ---- Etki yardımcıları
        public static float WorkSpeedBonus(WorkCategory category) =>
            (category == WorkCategory.Dig || category == WorkCategory.Repair) && Has(SkillId.QuickHammer) ? 1.3f : 1f;

        public static float MoveSpeedBonus => Has(SkillId.NimbleStep) ? 1.25f : 1f;
        public static int SupportDurability => Has(SkillId.SteelBracing) ? 2 : 1;
        public static bool QuakeImmunity => Has(SkillId.LifeTriangleArchitect);
        public static int ApplyRecycling(int scrap) => Has(SkillId.Recycling) ? Mathf.CeilToInt(scrap * 1.5f) : scrap;

        void Awake()
        {
            Instance = this;
            Points = startingPoints;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable() => EventBus.Subscribe<DayStarted>(OnDayStarted);
        void OnDisable() => EventBus.Unsubscribe<DayStarted>(OnDayStarted);

        void OnDayStarted(DayStarted evt)
        {
            if (evt.Day > 1) AddPoints(pointsPerDay);
        }

        public void AddPoints(int amount)
        {
            Points += amount;
            EventBus.Publish(new SkillPointsGained(Points));
        }

        public bool IsUnlocked(SkillId id) => unlocked.Contains(id);

        public static bool IsBranchAvailable(SkillBranch branch)
        {
            foreach (var survivor in Survivor.All)
                if (!survivor.IsDead && survivor.Branch == branch) return true;

            // Seferdeki kâşif sahnede pasiftir ama hâlâ takımdadır.
            var away = ExpeditionSystem.AwayExplorer;
            return away != null && away.Branch == branch;
        }

        public bool CanUnlock(SkillId id, out string reason)
        {
            var info = SkillCatalog.Get(id);
            if (unlocked.Contains(id)) reason = "Zaten açık.";
            else if (!IsBranchAvailable(info.Branch)) reason = $"{info.Branch.ToDisplayName()} uzmanı bir kazazede gerekli.";
            else if (info.Tier > 1 && !unlocked.Contains(id - 1)) reason = "Önce dalın önceki yeteneğini aç.";
            else if (Points < 1) reason = "Yetenek puanı yok.";
            else reason = null;

            return reason == null;
        }

        public bool TryUnlock(SkillId id)
        {
            if (!CanUnlock(id, out _)) return false;

            Points--;
            unlocked.Add(id);
            EventBus.Publish(new SkillUnlocked(id));
            return true;
        }
    }
}
