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
    /// Kazıldıkça Kalas/Hurda veren moloz yığını. Her kazının malzemesi doğrudan depoya girer. blocksPath açıksa temizlenene kadar kat çizgisini kapatır.
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

        public float HalfWidth => fullScale.x * 0.5f;

        public override string ActionLabel => "Kaz";
        public override WorkCategory Category => WorkCategory.Dig;
        public override bool RequiresEmptyHands => true;
        public override bool RepeatAfterComplete => remaining > 0;
        public override string StatusText =>
            $"Kalan kazı: {remaining}/{digCount}  (+{yieldPerDig}){(BlocksWholeFloor ? "  — yolu kapatıyor" : "")}";

        protected override void Awake()
        {
            base.Awake();
            remaining = digCount;
            fullScale = transform.localScale;
            baseY = transform.position.y - transform.lossyScale.y * 0.5f;
            RegisterBlocker();
        }

        // Yığının kapladığı alan katın yürünebilir derinliğini bu kadar payla kaplıyorsa yolu tamamen keser.
        const float FullDepthTolerance = 0.35f;
        bool spansDepth;

        /// <summary>
        /// Katın bütün derinliğini (önden arkaya) kaplayan yığın yolu tamamen keser: temizlenmeden geçilmez. Kaplamıyorsa
        /// mobilya gibi yalnızca kendi alanı engeldir, açık kalan yerinden etrafından dolaşılır.
        /// </summary>
        public bool BlocksWholeFloor => blocksPath && spansDepth;

        void RegisterBlocker()
        {
            spansDepth = false;
            if (!blocksPath || Building == null) return;

            float minX, maxX;
            if (TryGetFootprint(out var bounds))
            {
                spansDepth = bounds.min.z <= Building.MinZ + FullDepthTolerance &&
                             bounds.max.z >= Building.MaxZ - FullDepthTolerance;
                minX = bounds.min.x;
                maxX = bounds.max.x;
            }
            else
            {
                // Collider'ı yoksa eski davranış: yolu keser.
                spansDepth = true;
                minX = transform.position.x - HalfWidth;
                maxX = transform.position.x + HalfWidth;
            }

            if (spansDepth) Building.AddBlocker(this, Floor, minX, maxX);
        }

        void OnDestroy()
        {
            if (blocksPath && Building != null) Building.RemoveBlocker(this);
        }

        public override float GetWorkX(int fromFloor, float fromX)
        {
            if (!BlocksWholeFloor) return transform.position.x;

            // Yolu kapatan yığın içinden kazılamaz; karakterin geldiği taraftaki kenarda durulur.
            float x = transform.position.x;
            float arrival = Building.ArrivalX(fromFloor, fromX, Floor);
            float side = arrival < x ? -1f : 1f;
            return x + side * (HalfWidth + BuildingLayout.BlockerStandOff);
        }

        // Yolu kapatan yığının yanında, yürüme çizgisinde durulur.
        public override float GetWorkZ() => BlocksWholeFloor ? Building.WalkPlaneZ : base.GetWorkZ();

        public override bool CanStart(Survivor worker, out string reason)
        {
            reason = remaining > 0 ? null : "Moloz zaten temizlendi.";
            return remaining > 0;
        }

        public override void Complete(Survivor worker)
        {
            worker.Collect(yieldPerDig);
            // Kazma demir donatıyı da söker.
            if (AftershockIsle.Crafting.ToolKit.HasPickaxe && Random.value < AftershockIsle.Crafting.ToolKit.PickaxeMetalChance)
                worker.Collect(new ResourceAmount(ResourceType.Metal, 1));
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

        /// <summary>Çalışma anında oluşturulan yığın için (pasif nesneye, Awake'ten önce).</summary>
        public void SetupRuntime(int digs, bool blocks)
        {
            digCount = Mathf.Max(1, digs);
            blocksPath = blocks;
        }

        public int Remaining => remaining;
        public int DigCount => digCount;
        public bool BlocksPath => blocksPath;

        public void RestoreState(int remainingDigs)
        {
            remaining = remainingDigs;
            if (remaining <= 0)
            {
                if (blocksPath && Building != null) Building.RemoveBlocker(this);
                Destroy(gameObject);
                return;
            }

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
