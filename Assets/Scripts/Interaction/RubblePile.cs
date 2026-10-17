using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Economy;
using UnityEngine;

namespace AftershockIsle.Interaction
{
    public readonly struct RubbleCleared
    {
        public readonly RubblePile Pile;
        public RubbleCleared(RubblePile pile) => Pile = pile;
    }

    /// <summary>
    /// Kazıldıkça Kalas/Hurda veren moloz yığını. Her kazı tek taşımalık malzeme verir;
    /// karakter sandığa taşıyıp geri gelir. blocksPath açıksa temizlenene kadar kat çizgisini kapatır.
    /// </summary>
    public class RubblePile : Interactable
    {
        [SerializeField, Min(1)] int digCount = 3;
        [SerializeField] ResourceAmount yieldPerDig = new ResourceAmount(ResourceType.Scrap, 1);
        [SerializeField] bool blocksPath;

        static readonly System.Collections.Generic.List<RubblePile> all = new System.Collections.Generic.List<RubblePile>();

        int remaining;
        Vector3 fullScale;
        float baseY;

        public static System.Collections.Generic.IReadOnlyList<RubblePile> All => all;

        public static int CountOnFloor(int floor)
        {
            int count = 0;
            foreach (var pile in all)
                if (pile.Floor == floor && pile.remaining > 0) count++;
            return count;
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public bool BlocksPath => blocksPath;
        public float HalfWidth => fullScale.x * 0.5f;

        public override string ActionLabel => "Kaz";
        public override WorkCategory Category => WorkCategory.Dig;
        public override bool RequiresEmptyHands => true;
        public override bool RepeatAfterComplete => remaining > 0;
        public override string StatusText =>
            $"Kalan kazı: {remaining}/{digCount}  (+{yieldPerDig}){(blocksPath ? "  — yolu kapatıyor" : "")}";

        protected override void Awake()
        {
            base.Awake();
            remaining = digCount;
            fullScale = transform.localScale;
            baseY = transform.position.y - transform.lossyScale.y * 0.5f;

            if (blocksPath)
            {
                float x = transform.position.x;
                Building.AddBlocker(this, Floor, x - HalfWidth, x + HalfWidth);
            }
        }

        void OnDestroy()
        {
            if (blocksPath && Building != null) Building.RemoveBlocker(this);
        }

        public override float GetWorkX(int fromFloor, float fromX)
        {
            if (!blocksPath) return transform.position.x;

            // Yolu kapatan yığın içinden kazılamaz; karakterin geldiği taraftaki kenarda durulur.
            float x = transform.position.x;
            float arrival = Building.ArrivalX(fromFloor, fromX, Floor);
            float side = arrival < x ? -1f : 1f;
            return x + side * (HalfWidth + BuildingLayout.BlockerStandOff);
        }

        public override bool CanStart(Survivor worker, out string reason)
        {
            reason = remaining > 0 ? null : "Moloz zaten temizlendi.";
            return remaining > 0;
        }

        public override void Complete(Survivor worker)
        {
            worker.TryPickUp(yieldPerDig);
            remaining--;

            if (remaining <= 0)
            {
                if (blocksPath) Building.RemoveBlocker(this);
                EventBus.Publish(new RubbleCleared(this));
                Destroy(gameObject);
                return;
            }

            // Yığın her kazıda alçalır ama zemine oturmaya devam eder.
            float t = Mathf.Lerp(0.35f, 1f, remaining / (float)digCount);
            var scale = fullScale;
            scale.y *= t;
            transform.localScale = scale;

            var position = transform.position;
            position.y = baseY + transform.lossyScale.y * 0.5f;
            transform.position = position;
        }
    }
}
