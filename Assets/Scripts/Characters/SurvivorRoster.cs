using AftershockIsle.Building;
using AftershockIsle.Core;
using AftershockIsle.Expedition;
using AftershockIsle.Skills;
using UnityEngine;

namespace AftershockIsle.Characters
{
    public readonly struct SurvivorJoined
    {
        public readonly Survivor Survivor;
        public SurvivorJoined(Survivor survivor) => Survivor = survivor;
    }

    /// <summary>Sığınak nüfusu. Kurtarılan kazazedeler buradan sahneye eklenir (jam kapsamı: en fazla 3 kişi).</summary>
    public class SurvivorRoster : MonoBehaviour
    {
        [SerializeField] Survivor survivorPrefab;
        [SerializeField] int maxPopulation = 3;

        BuildingLayout building;

        public static SurvivorRoster Instance { get; private set; }
        public int MaxPopulation => maxPopulation;
        public bool IsFull => Survivor.AliveCount + ExpeditionSystem.AwayCount >= maxPopulation;

        void Awake()
        {
            Instance = this;
            building = FindFirstObjectByType<BuildingLayout>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public Survivor Spawn(string displayName, SkillBranch branch, int floor, float x)
        {
            if (IsFull || survivorPrefab == null) return null;

            var survivor = Instantiate(survivorPrefab, building.Point(floor, x), Quaternion.Euler(0f, 180f, 0f));
            survivor.name = displayName;
            survivor.Initialize(displayName, branch);
            EventBus.Publish(new SurvivorJoined(survivor));
            return survivor;
        }

        /// <summary>Henüz sığınakta uzmanı olmayan ilk dal (yoksa null).</summary>
        public static SkillBranch? FirstMissingBranch()
        {
            foreach (SkillBranch branch in System.Enum.GetValues(typeof(SkillBranch)))
                if (!SkillTree.IsBranchAvailable(branch)) return branch;
            return null;
        }
    }
}
