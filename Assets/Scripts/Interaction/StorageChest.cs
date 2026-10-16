using AftershockIsle.Characters;
using AftershockIsle.Economy;

namespace AftershockIsle.Interaction
{
    /// <summary>Ortak Sandık: elde taşınan malzeme buraya bırakılınca global sayaca eklenir.</summary>
    public class StorageChest : Interactable
    {
        public static StorageChest Main { get; private set; }

        public override string ActionLabel => "Bırak";
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
            reason = worker.IsCarrying ? null : "Elinde taşıdığı bir şey yok.";
            return worker.IsCarrying;
        }

        public override void Complete(Survivor worker)
        {
            if (worker.TryTakeCarried(out var item))
                ResourceStorage.Instance.Add(item);
        }
    }
}
