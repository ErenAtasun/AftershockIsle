using AftershockIsle.Core;
using UnityEngine;

namespace AftershockIsle.Economy
{
    public readonly struct ResourcesChanged
    {
        public readonly ResourceType Type;
        public readonly int Total;

        public ResourcesChanged(ResourceType type, int total)
        {
            Type = type;
            Total = total;
        }
    }

    /// <summary>Ortak Sandık'a bırakılan malzemelerin global sayacı. Üretim ve tamir buradan harcar.</summary>
    public class ResourceStorage : MonoBehaviour
    {
        [SerializeField] ResourceAmount[] startingAmounts =
        {
            new ResourceAmount(ResourceType.Scrap, 2),
            new ResourceAmount(ResourceType.Metal, 1),
            new ResourceAmount(ResourceType.Supplies, 3),
        };

        readonly int[] counts = new int[System.Enum.GetValues(typeof(ResourceType)).Length];

        public static ResourceStorage Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            foreach (var start in startingAmounts)
            {
                int idx = (int)start.type;
                if (idx >= 0 && idx < counts.Length)
                    counts[idx] += start.amount;
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public int Get(ResourceType type)
        {
            int idx = (int)type;
            return (idx >= 0 && idx < counts.Length) ? counts[idx] : 0;
        }

        public bool Has(ResourceAmount cost) => Get(cost.type) >= cost.amount;

        public bool TrySpend(ResourceAmount cost)
        {
            if (!Has(cost)) return false;
            Change(cost.type, -cost.amount);
            return true;
        }

        public bool HasAll(ResourceAmount[] costs)
        {
            foreach (var cost in costs)
                if (!Has(cost)) return false;
            return true;
        }

        /// <summary>Hepsi karşılanıyorsa hepsini birden harcar; biri eksikse hiçbirini harcamaz.</summary>
        public bool TrySpendAll(ResourceAmount[] costs)
        {
            if (!HasAll(costs)) return false;
            foreach (var cost in costs) Change(cost.type, -cost.amount);
            return true;
        }

        public static string FormatCosts(ResourceAmount[] costs)
        {
            var parts = new string[costs.Length];
            for (int i = 0; i < costs.Length; i++) parts[i] = costs[i].ToString();
            return string.Join(", ", parts);
        }

        public void Add(ResourceAmount amount)
        {
            if (amount.type.IsStorable()) Change(amount.type, amount.amount);
        }

        void Change(ResourceType type, int delta)
        {
            int idx = (int)type;
            if (idx < 0 || idx >= counts.Length) return;
            counts[idx] = Mathf.Max(0, counts[idx] + delta);
            EventBus.Publish(new ResourcesChanged(type, counts[idx]));
        }

        public void RestoreState(int scrap, int metal, int electronics, int supplies)
        {
            counts[(int)ResourceType.Scrap] = scrap;
            counts[(int)ResourceType.Metal] = metal;
            counts[(int)ResourceType.Electronics] = electronics;
            counts[(int)ResourceType.Supplies] = supplies;
            foreach (var type in ResourceTypes.All)
                EventBus.Publish(new ResourcesChanged(type, counts[(int)type]));
        }
    }
}
