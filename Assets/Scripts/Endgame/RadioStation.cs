using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Crisis;
using AftershockIsle.Interaction;
using AftershockIsle.Skills;
using UnityEngine;

namespace AftershockIsle.Endgame
{
    public readonly struct RadioRepaired { }

    public readonly struct SignalInterrupted
    {
        public readonly string Reason;
        public SignalInterrupted(string reason) => Reason = reason;
    }

    public readonly struct SignalCompleted { }

    /// <summary>
    /// Çatıdaki telsiz istasyonu. Önce Kademe 3 tezgahta üretilen modülle tamir edilir; sonra bir karakter
    /// başında durup kesintisiz sinyal gönderir. Şartlar bozulursa, sarsıntı olursa ya da operatör ayrılırsa
    /// sinyal sıfırlanır.
    /// </summary>
    public class RadioStation : Interactable
    {
        [SerializeField] float signalDuration = 60f;
        [SerializeField] float repairDuration = 8f;
        [Tooltip("Operatör işler arası bu kadar kısa ayrılırsa sinyal kesilmiş sayılmaz")]
        [SerializeField] float interruptGrace = 1.5f;
        [SerializeField] Renderer statusLight;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        MaterialPropertyBlock block;
        float idleTimer;

        public static RadioStation Instance { get; private set; }

        public bool IsRepaired { get; private set; }
        public float Signal01 { get; private set; }
        public bool SignalComplete { get; private set; }

        float SignalSpeed => SkillTree.Has(SkillId.FrequencyLock) ? 2f : 1f;
        EvacuationConditions Conditions => EvacuationConditions.Evaluate(Building, this);

        public override string ActionLabel => IsRepaired ? "Sinyal Gönder" : "Tamir Et";
        public override WorkCategory Category => IsRepaired ? WorkCategory.Operate : WorkCategory.Repair;
        public override float WorkDuration => IsRepaired ? 1f : repairDuration;
        public override bool RepeatAfterComplete => IsRepaired && !SignalComplete;

        public override string StatusText
        {
            get
            {
                if (!IsRepaired) return Workbench.Has(RecipeId.RadioModule)
                    ? "Bozuk. Telsiz modülü hazır, tamir edilebilir."
                    : "Bozuk. Tamir için tezgahta Telsiz Modülü üret (Kademe 3).";

                string reason = Conditions.BlockingReason;
                return reason == null
                    ? $"Sinyal %{Signal01 * 100f:0} — başında kesintisiz {signalDuration / SignalSpeed:0} sn kalınmalı."
                    : $"Sinyal gönderilemez: {reason}";
            }
        }

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
            RefreshLight();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable() => EventBus.Subscribe<AftershockImpact>(OnAftershock);
        void OnDisable() => EventBus.Unsubscribe<AftershockImpact>(OnAftershock);

        void OnAftershock(AftershockImpact evt) => Interrupt("Sarsıntı sinyali kesti.");

        public override bool CanStart(Survivor worker, out string reason)
        {
            if (SignalComplete) reason = "Sinyal ulaştı, helikopter yolda.";
            else if (!IsRepaired) reason = Workbench.Has(RecipeId.RadioModule) ? null : "Tamir için tezgahta Telsiz Modülü üret (Kademe 3).";
            else reason = Conditions.BlockingReason;
            return reason == null;
        }

        public override bool CanContinue(Survivor worker) => !SignalComplete && (!IsRepaired || Conditions.ReadyToSignal);

        public override void Complete(Survivor worker)
        {
            if (IsRepaired) return; // Sinyal ilerlemesi Update'te, operatör başındayken birikir.

            IsRepaired = true;
            RefreshLight();
            EventBus.Publish(new RadioRepaired());
        }

        void Update()
        {
            if (!IsRepaired || SignalComplete || Time.deltaTime <= 0f) return;

            var operatorSurvivor = ClaimedBy;
            bool operating = operatorSurvivor != null && operatorSurvivor.Tasks.Current == this &&
                             operatorSurvivor.Tasks.State == TaskState.Working;

            if (Signal01 <= 0f && !operating) return;

            var conditions = Conditions;
            if (!conditions.ReadyToSignal)
            {
                Interrupt($"Sinyal kesildi: {conditions.BlockingReason}");
                return;
            }

            if (operating)
            {
                idleTimer = 0f;
                Signal01 = Mathf.Min(1f, Signal01 + Time.deltaTime * SignalSpeed / signalDuration);
                if (Signal01 >= 1f) CompleteSignal();
                return;
            }

            idleTimer += Time.deltaTime;
            if (idleTimer > interruptGrace) Interrupt("Sinyal kesildi: telsizin başında kimse yok.");
        }

        void Interrupt(string reason)
        {
            if (Signal01 <= 0f || SignalComplete) return;

            Signal01 = 0f;
            idleTimer = 0f;
            EventBus.Publish(new SignalInterrupted(reason));
        }

        void CompleteSignal()
        {
            SignalComplete = true;
            RefreshLight();
            EventBus.Publish(new SignalCompleted());
        }

        void RefreshLight()
        {
            if (statusLight == null) return;
            block ??= new MaterialPropertyBlock();
            statusLight.GetPropertyBlock(block);
            block.SetColor(BaseColorId, SignalComplete ? Color.green : IsRepaired ? new Color(1f, 0.8f, 0.2f) : new Color(0.8f, 0.15f, 0.1f));
            statusLight.SetPropertyBlock(block);
        }
    }
}
