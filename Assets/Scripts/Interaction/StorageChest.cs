using AftershockIsle.Characters;
using AftershockIsle.Economy;

namespace AftershockIsle.Interaction
{
    /// <summary>
    /// Ortak Sandık: eldeki depolanabilir yük ya da sefer çantası buraya bırakılabilir (isteğe bağlı; malzeme artık kendiliğinden depoya girer).
    /// </summary>
    public class StorageChest : Interactable
    {
        static readonly System.Collections.Generic.List<StorageChest> all = new System.Collections.Generic.List<StorageChest>();

        public static StorageChest Main { get; private set; }
        public static System.Collections.Generic.IReadOnlyList<StorageChest> All => all;

        public override string ActionLabel => "Bırak";
        public override WorkCategory Category => WorkCategory.Haul;
        public override string StatusText => "Malzeme kendiliğinden depoya girer; elde kalmış yük ya da çanta buraya bırakılabilir.";

        protected override void Awake()
        {
            base.Awake();
            if (Main == null) Main = this;
        }

        void OnEnable() => all.Add(this);

        void OnDisable()
        {
            all.Remove(this);
            if (Main == this && all.Count > 0) Main = all[0];
        }

        void OnDestroy()
        {
            if (Main == this) Main = all.Count > 0 ? all[0] : null;
        }

        /// <summary>İşçiye en yakın (kat farkı öncelikli) aktif sandığı veya depo rafını bulur.</summary>
        public static StorageChest FindNearest(Survivor worker)
        {
            if (worker == null || all.Count == 0) return Main;
            StorageChest best = Main;
            float bestScore = float.MaxValue;
            int floor = worker.Mover.CurrentFloor;
            float x = worker.transform.position.x;

            foreach (var chest in all)
            {
                if (chest == null || !chest.isActiveAndEnabled) continue;
                float score = UnityEngine.Mathf.Abs(chest.Floor - floor) * 100f + UnityEngine.Mathf.Abs(chest.transform.position.x - x);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = chest;
                }
            }
            return best != null ? best : Main;
        }

        public override bool CanStart(Survivor worker, out string reason)
        {
            bool storableInHand = worker.IsCarrying && worker.Carried.type.IsStorable();
            if (storableInHand || worker.HasBag)
            {
                reason = null;
                return true;
            }

            reason = worker.IsCarrying
                ? $"{worker.Carried.type.ToDisplayName()} sandığa konmaz."
                : "Elinde taşıdığı bir şey yok.";
            return false;
        }

        public override void Complete(Survivor worker)
        {
            var storage = ResourceStorage.Instance;

            if (worker.IsCarrying && worker.Carried.type.IsStorable() && worker.TryTakeCarried(out var item))
                storage.Add(item);

            foreach (var loot in worker.EmptyBag())
                storage.Add(loot);
        }
    }
}
