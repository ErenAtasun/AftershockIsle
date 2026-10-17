using AftershockIsle.Characters;
using AftershockIsle.Economy;

namespace AftershockIsle.Interaction
{
    /// <summary>
    /// Ortak Sandık: elde taşınan malzeme ve sefer çantası buraya bırakılınca global sayaca eklenir.
    /// </summary>
    public class StorageChest : Interactable
    {
        public static StorageChest Main { get; private set; }

        public override string ActionLabel => "Bırak";
        public override WorkCategory Category => WorkCategory.Haul;
        public override string StatusText => "Taşınan malzeme ve sefer çantası buraya bırakılınca depoya eklenir.";

        protected override void Awake()
        {
            base.Awake();
            Main = this;
        }

        void OnDestroy()
        {
            if (Main == this) Main = null;
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
