using AftershockIsle.Building;
using AftershockIsle.Characters;
using UnityEngine;

namespace AftershockIsle.Interaction
{
    /// <summary>İşin türü; yetenekler ve durumlar çalışma hızını türe göre değiştirir.</summary>
    public enum WorkCategory { Dig, Repair, Dismantle, Search, Haul, Operate, Firefight }

    /// <summary>
    /// Bir karaktere iş olarak verilebilen nesne. Karakter çalışma noktasına yürür,
    /// WorkDuration kadar çalışır, sonra Complete çağrılır. Aynı anda tek karakter çalışabilir.
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        [SerializeField] string displayName = "Nesne";
        [SerializeField, Min(0.1f)] float workDuration = 3f;

        protected BuildingLayout Building { get; private set; }

        public string DisplayName => displayName;
        public int Floor { get; private set; }
        public Survivor ClaimedBy { get; private set; }
        public virtual float WorkDuration => workDuration;

        /// <summary>"Kaz", "Parçala" gibi sağ tık eylemi.</summary>
        public abstract string ActionLabel { get; }

        public virtual WorkCategory Category => WorkCategory.Operate;

        /// <summary>Üzerine gelince gösterilecek ek bilgi (durum, maliyet, kalan miktar).</summary>
        public virtual string StatusText => null;

        /// <summary>True ise karakter elindekini önce sandığa bırakır.</summary>
        public virtual bool RequiresEmptyHands => false;

        /// <summary>True ise iş bitince (ve gerekirse taşıma sonrası) karakter aynı işe geri döner.</summary>
        public virtual bool RepeatAfterComplete => false;

        /// <summary>True ise dumanla dolu katta da yapılabilir (ör. pencere kırmak).</summary>
        public virtual bool AllowedInSmoke => false;

        /// <summary>Bu işten önce yapılması gereken iş (ör. yangından önce kova doldurmak). Yoksa null.</summary>
        public virtual Interactable RequiredBefore(Survivor worker) => null;

        protected virtual void Awake()
        {
            Building = FindFirstObjectByType<BuildingLayout>();
            Floor = Building.FloorAtHeight(transform.position.y + 0.05f);
        }

        /// <summary>Karakterin duracağı X. Varsayılan: nesnenin önü.</summary>
        public virtual float GetWorkX(int fromFloor, float fromX) => transform.position.x;

        public virtual bool CanStart(Survivor worker, out string reason)
        {
            reason = null;
            return true;
        }

        public virtual bool CanContinue(Survivor worker) => true;

        public abstract void Complete(Survivor worker);

        public bool TryClaim(Survivor worker)
        {
            if (ClaimedBy != null && ClaimedBy != worker) return false;
            ClaimedBy = worker;
            return true;
        }

        public void Release(Survivor worker)
        {
            if (ClaimedBy == worker) ClaimedBy = null;
        }
    }
}
