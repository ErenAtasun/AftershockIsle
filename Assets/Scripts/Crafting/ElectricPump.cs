using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using AftershockIsle.Economy;
using AftershockIsle.Interaction;
using AftershockIsle.Skills;
using UnityEngine;

namespace AftershockIsle.Crafting
{
    public readonly struct PumpBroke
    {
        public readonly ElectricPump Pump;
        public PumpBroke(ElectricPump pump) => Pump = pump;
    }

    /// <summary>
    /// Kademe 2 elektrikli pompa: kurulduktan sonra elektrik açıkken bodrum suyunu insan gücü olmadan çeker.
    /// Çalıştıkça yıpranır; ömrü dolunca bozulur ve Boru &amp; Metal ile tamir edilir.
    /// </summary>
    [RequireComponent(typeof(InstallableVisual))]
    public class ElectricPump : Interactable
    {
        [SerializeField] float drainPerSecond = 2f;
        [Tooltip("Bozulmadan önceki çalışma süresi (sn); Otomasyon Ustası ile %50 uzar")]
        [SerializeField] float lifetime = 60f;
        [SerializeField] ResourceAmount repairCost = new ResourceAmount(ResourceType.Metal, 1);

        InstallableVisual installable;
        float wear;

        public bool IsBroken { get; private set; }
        public bool IsRunning => installable.IsInstalled && !IsBroken && PowerGrid.IsOn &&
                                 FloodSystem.Instance != null && FloodSystem.Instance.Level > 0.01f;
        float Lifetime => lifetime * (SkillTree.Has(SkillId.AutomationMaster) ? 1.5f : 1f);

        public override string ActionLabel => "Tamir Et";
        public override WorkCategory Category => WorkCategory.Repair;
        public override string StatusText => IsBroken
            ? $"BOZUK — tamir: {repairCost}"
            : $"{(IsRunning ? "Çalışıyor" : PowerGrid.IsOn ? "Beklemede" : "Elektrik yok")}  — yıpranma %{wear / Lifetime * 100f:0}";

        protected override void Awake()
        {
            base.Awake();
            installable = GetComponent<InstallableVisual>();
        }

        void Update()
        {
            if (!IsRunning || Time.deltaTime <= 0f) return;

            FloodSystem.Instance.Drain(drainPerSecond * Time.deltaTime);
            wear += Time.deltaTime;
            if (wear < Lifetime) return;

            IsBroken = true;
            EventBus.Publish(new PumpBroke(this));
        }

        public override bool CanStart(Survivor worker, out string reason)
        {
            if (!IsBroken) reason = "Pompa çalışır durumda.";
            else if (!ResourceStorage.Instance.Has(repairCost)) reason = $"Depoda yeterli malzeme yok: {repairCost} gerekli.";
            else reason = null;
            return reason == null;
        }

        public override bool CanContinue(Survivor worker) => IsBroken;

        public override void Complete(Survivor worker)
        {
            if (!ResourceStorage.Instance.TrySpend(repairCost)) return;
            IsBroken = false;
            wear = 0f;
        }
    }
}
