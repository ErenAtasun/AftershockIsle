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

        /// <summary>Oyuncu sağ tıkladığında. Varsayılan: işi karakterin kuyruğuna ekler; tezgah gibi nesneler menü açar.</summary>
        public virtual void HandleCommand(Survivor worker, bool append) => worker.Tasks.Assign(this, append);

        protected virtual void Awake()
        {
            Building = FindFirstObjectByType<BuildingLayout>();
            if (Building != null) Floor = Building.FloorAtHeight(transform.position.y + 0.05f);
            // Oyun sırasında doğan nesne (düşen enkaz) yürüyüş engellerine hemen girsin.
            if (BlocksMovement) FloorObstacles.Invalidate();
        }

        public void ConfigureRuntime(string name, float duration)
        {
            displayName = name;
            workDuration = Mathf.Max(0.1f, duration);
            RefreshFloorFromPosition();
        }

        public void RefreshFloorFromPosition()
        {
            if (Building == null) Building = FindFirstObjectByType<BuildingLayout>();
            if (Building != null) Floor = Building.FloorAtHeight(transform.position.y + 0.05f);
            if (BlocksMovement) FloorObstacles.Invalidate();
        }

        /// <summary>Karakterin duracağı X. Varsayılan: nesnenin önü.</summary>
        public virtual float GetWorkX(int fromFloor, float fromX) => transform.position.x;

        /// <summary>Karakterin duracağı Z (odanın derinliğinde). Varsayılan: nesnenin kameraya bakan yüzünün önü.</summary>
        public virtual float GetWorkZ()
        {
            return TryGetFootprint(out var bounds)
                ? Building.ClampZ(bounds.min.z - WorkStandOff)
                : Building.WalkPlaneZ;
        }

        /// <summary>Kat içinde serbest yürürken bu nesnenin içinden geçilemez mi (mobilya, tezgah, kolon...).</summary>
        public virtual bool BlocksMovement => true;

        const float WorkStandOff = 0.55f;

        /// <summary>Nesnenin etkin collider'larının dünya sınırı (model giydirilmişse modelin collider'ı).</summary>
        public bool TryGetFootprint(out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (var collider in GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger) continue;
                if (found) bounds.Encapsulate(collider.bounds);
                else bounds = collider.bounds;
                found = true;
            }
            return found;
        }

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
