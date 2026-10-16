using AftershockIsle.Characters;
using AftershockIsle.Economy;

namespace AftershockIsle.Interaction
{
    /// <summary>Ortak Sandık: elde taşınan malzeme buraya bırakılınca global sayaca eklenir.</summary>
    public class StorageChest : Interactable
    {
        public static StorageChest Main { get; private set; }

        public override string ActionLabel => "Bırak";
        public override WorkCategory Category => WorkCategory.Haul;
        public override string StatusText => "Taşınan malzeme buraya bırakılınca depoya eklenir.";

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
            if (!worker.IsCarrying)
            {
                reason = "Elinde taşıdığı bir şey yok.";
                return false;
            }

            if (!worker.Carried.type.IsStorable())
            {
                reason = $"{worker.Carried.type.ToDisplayName()} sandığa konmaz.";
                return false;
            }

            reason = null;
            return true;
        }

        public override void Complete(Survivor worker)
        {
            if (worker.TryTakeCarried(out var item))
                ResourceStorage.Instance.Add(item);
        }
    }
}
