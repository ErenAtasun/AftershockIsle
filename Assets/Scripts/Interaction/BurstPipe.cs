using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Economy;
using UnityEngine;

namespace AftershockIsle.Interaction
{
    public readonly struct PipeBurst
    {
        public readonly BurstPipe Pipe;
        public PipeBurst(BurstPipe pipe) => Pipe = pipe;
    }

    /// <summary>Bodrumdaki şebeke borusu. Patlayınca bodruma su dolar; Boru &amp; Metal ile kaynaklanır.</summary>
    public class BurstPipe : Interactable
    {
        [SerializeField] ResourceAmount repairCost = new ResourceAmount(ResourceType.Metal, 1);
        [SerializeField] GameObject sprayVisual;

        public bool IsBurst { get; private set; }

        public override string ActionLabel => "Kaynakla";
        public override WorkCategory Category => WorkCategory.Repair;
        public override bool RequiresEmptyHands => true;
        public override string StatusText => IsBurst
            ? $"PATLAK — bodruma su doluyor. Tamir: {repairCost}"
            : "Sağlam";

        protected override void Awake()
        {
            base.Awake();
            RefreshVisual();
        }

        /// <summary>Boruyu patlatır. Zaten patlaksa false.</summary>
        public bool Burst()
        {
            if (IsBurst) return false;

            IsBurst = true;
            RefreshVisual();
            EventBus.Publish(new PipeBurst(this));
            return true;
        }

        public override bool CanStart(Survivor worker, out string reason)
        {
            if (!IsBurst)
            {
                reason = "Boru sağlam.";
                return false;
            }

            if (!ResourceStorage.Instance.Has(repairCost))
            {
                reason = $"Depoda yeterli malzeme yok: {repairCost} gerekli.";
                return false;
            }

            reason = null;
            return true;
        }

        public override bool CanContinue(Survivor worker) => IsBurst;

        public override void Complete(Survivor worker)
        {
            if (!ResourceStorage.Instance.TrySpend(repairCost)) return;

            IsBurst = false;
            RefreshVisual();
        }

        void RefreshVisual()
        {
            if (sprayVisual != null) sprayVisual.SetActive(IsBurst);
        }
    }
}
