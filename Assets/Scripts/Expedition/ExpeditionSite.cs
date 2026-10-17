using System;
using AftershockIsle.Economy;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    public enum ExpeditionHazard { None, Collapse, Fire }

    [Serializable]
    public struct LootEntry
    {
        public ResourceType type;
        [Min(1)] public int min;
        [Min(1)] public int max;
        [Min(0f)] public float weight;

        public LootEntry(ResourceType type, int min, int max, float weight)
        {
            this.type = type;
            this.min = min;
            this.max = max;
            this.weight = weight;
        }
    }

    /// <summary>Nodlu şehir haritasındaki bir lokasyon: ganimet tablosu, risk ve süre.</summary>
    [Serializable]
    public class ExpeditionSite
    {
        public string name;
        [TextArea] public string description;
        [Tooltip("Sahada kalınabilecek süre (oyun saniyesi); dolunca kâşif kaçarak döner")]
        public float duration = 100f;
        [Tooltip("Aranabilir nokta sayısı (çanta slotundan fazla: seçim yapılır)")]
        public int lootSpots = 4;
        public int dogs = 1;
        public int raiders = 1;
        public float fieldLength = 36f;
        public LootEntry[] loot;
        [Tooltip("Aranan noktada tuzak/göçük çıkma türü ve ihtimali")]
        public ExpeditionHazard hazard;
        [Range(0f, 1f)] public float hazardChance;
        public float hazardDamage;
        [Tooltip("Enkaz Alanı: mahsur kalan kazazede kurtarma kararı")]
        public bool isRescueSite;
        [Tooltip("Harita penceresindeki düğüm konumu (0–1)")]
        public Vector2 mapPosition;

        public static ExpeditionSite[] Defaults() => new[]
        {
            new ExpeditionSite
            {
                name = "İnşaat Sahası",
                description = "Bol kalas ve hurda, az tıbbi malzeme. Göçük riski.",
                duration = 100f, lootSpots = 4, dogs = 2, raiders = 1,
                loot = new[]
                {
                    new LootEntry(ResourceType.Scrap, 2, 3, 60f),
                    new LootEntry(ResourceType.Metal, 1, 1, 25f),
                    new LootEntry(ResourceType.Supplies, 1, 1, 15f),
                },
                hazard = ExpeditionHazard.Collapse, hazardChance = 0.15f, hazardDamage = 20f,
                mapPosition = new Vector2(0.18f, 0.25f),
            },
            new ExpeditionSite
            {
                name = "Yıkık Klinik",
                description = "Bol ilaç ve bandaj (Yaşam Kiti). Yangın ve duman riski.",
                duration = 100f, lootSpots = 4, dogs = 1, raiders = 2,
                loot = new[]
                {
                    new LootEntry(ResourceType.Supplies, 1, 2, 70f),
                    new LootEntry(ResourceType.Scrap, 1, 2, 20f),
                    new LootEntry(ResourceType.Electronics, 1, 1, 10f),
                },
                hazard = ExpeditionHazard.Fire, hazardChance = 0.3f, hazardDamage = 25f,
                mapPosition = new Vector2(0.78f, 0.2f),
            },
            new ExpeditionSite
            {
                name = "Terk Edilmiş Atölye",
                description = "Elektronik çip, akü ve telsiz parçaları. Çürük çatı riski.",
                duration = 110f, lootSpots = 4, dogs = 0, raiders = 3,
                loot = new[]
                {
                    new LootEntry(ResourceType.Electronics, 1, 1, 50f),
                    new LootEntry(ResourceType.Metal, 1, 2, 40f),
                    new LootEntry(ResourceType.Scrap, 1, 2, 10f),
                },
                hazard = ExpeditionHazard.Collapse, hazardChance = 0.2f, hazardDamage = 15f,
                mapPosition = new Vector2(0.2f, 0.78f),
            },
            new ExpeditionSite
            {
                name = "Enkaz Alanı",
                description = "Enkaz altında mahsur kalanlar var. Mahsur kalanı çıkarmak uzun sürer; kurtarılan kişi +1 iş gücü ve yeni yetenek dalı getirir, ama günlük tüketimi de artırır.",
                duration = 100f, lootSpots = 2, dogs = 2, raiders = 1,
                loot = new[] { new LootEntry(ResourceType.Scrap, 1, 2, 100f) },
                hazard = ExpeditionHazard.Collapse, hazardChance = 0.1f, hazardDamage = 15f,
                isRescueSite = true,
                mapPosition = new Vector2(0.8f, 0.75f),
            },
        };
    }
}
