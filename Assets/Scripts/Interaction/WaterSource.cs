using System.Collections.Generic;
using AftershockIsle.Characters;
using AftershockIsle.Crisis;
using AftershockIsle.Economy;
using UnityEngine;

namespace AftershockIsle.Interaction
{
    /// <summary>
    /// Kova doldurma noktası. Bodrumdaki kaynak baskın suyunu kullanır (suyu azaltır);
    /// bidon gibi kaynakların sınırlı kova hakkı vardır.
    /// </summary>
    public class WaterSource : Interactable
    {
        [SerializeField] bool usesFloodWater;
        [Tooltip("Baskın suyu kullanılıyorsa her kovanın düşürdüğü su seviyesi (yüzde puanı)")]
        [SerializeField] float floodPerBucket = 6f;
        [Tooltip("Baskın suyu kullanmayan kaynakların kova hakkı")]
        [SerializeField] int charges = 3;

        static readonly List<WaterSource> all = new List<WaterSource>();

        public override string ActionLabel => "Kova Doldur";
        public override bool RequiresEmptyHands => true;
        public override string StatusText => usesFloodWater
            ? $"Bodrum suyundan kova doldurur (her kova -%{floodPerBucket:0} su)."
            : $"Kalan kova: {charges}";

        public bool HasWater => usesFloodWater
            ? FloodSystem.Instance != null && FloodSystem.Instance.Level >= floodPerBucket
            : charges > 0;

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        /// <summary>Suyu olan ve dumanla kapalı olmayan en yakın kaynak (kat farkı öncelikli).</summary>
        public static WaterSource FindNearest(Survivor worker)
        {
            WaterSource best = null;
            float bestScore = float.MaxValue;
            int floor = worker.Mover.CurrentFloor;
            float x = worker.transform.position.x;

            foreach (var source in all)
            {
                if (!source.HasWater || FireSpot.IsSmokeBlocked(source.Floor)) continue;

                float score = Mathf.Abs(source.Floor - floor) * 100f + Mathf.Abs(source.transform.position.x - x);
                if (score >= bestScore) continue;

                bestScore = score;
                best = source;
            }

            return best;
        }

        public override bool CanStart(Survivor worker, out string reason)
        {
            reason = HasWater ? null : usesFloodWater ? "Bodrumda kova dolduracak kadar su yok." : "Bidon boş.";
            return HasWater;
        }

        public override void Complete(Survivor worker)
        {
            if (!HasWater) return;

            if (usesFloodWater) FloodSystem.Instance.Drain(floodPerBucket);
            else charges--;

            worker.TryPickUp(new ResourceAmount(ResourceType.Water, 1));
        }

        public int Charges => charges;
        public bool UsesFloodWater => usesFloodWater;

        public void RestoreState(int chargesLeft)
        {
            charges = chargesLeft;
        }
    }
}
