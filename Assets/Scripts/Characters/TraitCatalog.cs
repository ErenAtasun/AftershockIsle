using System.Collections.Generic;
using AftershockIsle.Interaction;
using UnityEngine;

namespace AftershockIsle.Characters
{
    public enum TraitId
    {
        // Pozitif Özellikler (Avantajlar)
        Carpenter = 0,
        Miner = 1,
        Firefighter = 2,
        Sprinter = 3,
        Chef = 4,
        Sharpshooter = 5,
        Optimist = 6,

        // Negatif Özellikler (Kusurlar & Fobiler)
        Claustrophobic = 10,
        Pyrophobic = 11,
        BigAppetite = 12,
        Frail = 13,
        Clumsy = 14
    }

    public readonly struct TraitInfo
    {
        public readonly TraitId Id;
        public readonly string Name;
        public readonly string Description;
        public readonly bool IsPositive;

        public TraitInfo(TraitId id, string name, string description, bool isPositive)
        {
            Id = id;
            Name = name;
            Description = description;
            IsPositive = isPositive;
        }
    }

    /// <summary>
    /// Kazazedelerin kişilik ve uzmanlık özellikleri (Traits &amp; Perks).
    /// Her kazazede 1 pozitif avantaj ve 1 negatif kusur ile doğar.
    /// </summary>
    public static class TraitCatalog
    {
        public static readonly TraitId[] PositivePool =
        {
            TraitId.Carpenter,
            TraitId.Miner,
            TraitId.Firefighter,
            TraitId.Sprinter,
            TraitId.Chef,
            TraitId.Sharpshooter,
            TraitId.Optimist
        };

        public static readonly TraitId[] NegativePool =
        {
            TraitId.Claustrophobic,
            TraitId.Pyrophobic,
            TraitId.BigAppetite,
            TraitId.Frail,
            TraitId.Clumsy
        };

        static readonly Dictionary<TraitId, TraitInfo> catalog = new Dictionary<TraitId, TraitInfo>
        {
            { TraitId.Carpenter, new TraitInfo(TraitId.Carpenter, "Marangoz", "Tamir, tezgahta üretim ve kolon güçlendirme hızı +%35.", true) },
            { TraitId.Miner, new TraitInfo(TraitId.Miner, "Enkazcı", "Moloz kazma hızı +%35, düşen enkazda sıkışma süresi -%50.", true) },
            { TraitId.Firefighter, new TraitInfo(TraitId.Firefighter, "İtfaiyeci", "Yangın söndürme hızı +%40, duman hasar ve stresinden %50 az etkilenir.", true) },
            { TraitId.Sprinter, new TraitInfo(TraitId.Sprinter, "Atlet", "Yürüme hızı +%20, ağır yük taşıma yavaşlaması yarıya iner.", true) },
            { TraitId.Chef, new TraitInfo(TraitId.Chef, "İdareli Aşçı", "Gece bakımında %35 ihtimalle Yaşam Kiti harcamaz ve takıma -5 stres verir.", true) },
            { TraitId.Sharpshooter, new TraitInfo(TraitId.Sharpshooter, "Keskin Nişancı", "Dış seferde ateşli silah hasarı +%25 ve %25 ihtimalle mermi harcamaz.", true) },
            { TraitId.Optimist, new TraitInfo(TraitId.Optimist, "İyimser", "Stres kazanımı -%25, dinlenirken stres atma hızı +%35.", true) },

            { TraitId.Claustrophobic, new TraitInfo(TraitId.Claustrophobic, "Klostrofobik", "Bodrum katında veya karanlıkta çalışırken stres 1.8x hızlı artar.", false) },
            { TraitId.Pyrophobic, new TraitInfo(TraitId.Pyrophobic, "Ateş Fobisi", "Bulunduğu katta yangın varken ekstra stres alır ve iş hızı -%20 düşer.", false) },
            { TraitId.BigAppetite, new TraitInfo(TraitId.BigAppetite, "Obur", "Gece aç kaldığında aldığı açlık stresi ve hasarı +%40 daha fazladır.", false) },
            { TraitId.Frail, new TraitInfo(TraitId.Frail, "Narin Bünye", "Maksimum sağlığı 100 yerine 85 HP'dir.", false) },
            { TraitId.Clumsy, new TraitInfo(TraitId.Clumsy, "Sakar", "Tamir işlerinde -%15 daha yavaş çalışır.", false) },
        };

        public static TraitInfo Get(TraitId id) =>
            catalog.TryGetValue(id, out var info) ? info : new TraitInfo(id, id.ToString(), "", true);

        /// <summary>Birbiriyle çelişmeyen (örn. Marangoz + Sakar olmayan) 1 Pozitif ve 1 Negatif özellik seçer.</summary>
        public static void RollBalancedTraits(List<TraitId> target, int seedOffset = 0)
        {
            target.Clear();
            var pos = PositivePool[Random.Range(0, PositivePool.Length)];
            target.Add(pos);

            for (int attempt = 0; attempt < 10; attempt++)
            {
                var neg = NegativePool[Random.Range(0, NegativePool.Length)];
                if (pos == TraitId.Carpenter && neg == TraitId.Clumsy) continue;
                if (pos == TraitId.Firefighter && neg == TraitId.Pyrophobic) continue;
                target.Add(neg);
                return;
            }
            target.Add(TraitId.Claustrophobic);
        }

        public static float GetWorkSpeedMultiplier(Survivor survivor, WorkCategory category)
        {
            if (survivor == null) return 1f;
            float mult = 1f;

            if (category == WorkCategory.Repair && survivor.HasTrait(TraitId.Carpenter))
                mult *= 1.35f;
            if (category == WorkCategory.Repair && survivor.HasTrait(TraitId.Clumsy))
                mult *= 0.85f;
            if (category == WorkCategory.Dig && survivor.HasTrait(TraitId.Miner))
                mult *= 1.35f;
            if (category == WorkCategory.Firefight && survivor.HasTrait(TraitId.Firefighter))
                mult *= 1.40f;

            return mult;
        }
    }
}
