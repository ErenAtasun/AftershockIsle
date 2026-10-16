using AftershockIsle.Building;
using AftershockIsle.Core;
using UnityEngine;

namespace AftershockIsle.Characters
{
    public readonly struct SurvivorPanicked
    {
        public readonly Survivor Survivor;
        public SurvivorPanicked(Survivor survivor) => Survivor = survivor;
    }

    /// <summary>
    /// Stres ve açlık. Stres %100 olunca karakter panikler ve bir süre emir dinlemez.
    /// Açlık (0–3 kademe) gece karşılanmazsa artar; aç karakter "Tükenmiş"tir: hareket ve iş yarı hızda.
    /// </summary>
    [RequireComponent(typeof(Survivor))]
    public class SurvivorCondition : MonoBehaviour
    {
        public const int MaxHunger = 3;

        [SerializeField] float maxStress = 100f;
        [SerializeField] float panicDuration = 15f;
        [SerializeField] float stressAfterPanic = 60f;
        [Tooltip("Elektrik kesikken (karanlıkta) çalışırken saniyelik stres")]
        [SerializeField] float darkWorkStressPerSecond = 1f;
        [Tooltip("Işıkta boş dururken saniyelik stres azalması")]
        [SerializeField] float calmRecoveryPerSecond = 0.4f;
        [SerializeField, Range(0.1f, 1f)] float exhaustedSpeed = 0.5f;

        Survivor survivor;
        float panicRemaining;

        public float Stress { get; private set; }
        public float MaxStress => maxStress;
        public int Hunger { get; private set; }
        public bool IsExhausted => Hunger > 0;
        public bool IsPanicking => panicRemaining > 0f;
        public float PanicRemaining => panicRemaining;
        public float SpeedMultiplier => IsExhausted ? exhaustedSpeed : 1f;

        void Awake() => survivor = GetComponent<Survivor>();

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || survivor.IsDead) return;

            if (IsPanicking)
            {
                panicRemaining = Mathf.Max(0f, panicRemaining - dt);
                if (!IsPanicking) Stress = stressAfterPanic;
                return;
            }

            var tasks = survivor.Tasks;
            var mover = survivor.Mover;
            bool indoors = mover.CurrentFloor < mover.Building.FloorCount - 1;

            if (tasks.State == TaskState.Working && !PowerGrid.IsOn && indoors)
                AddStress(darkWorkStressPerSecond * dt);
            else if (tasks.State == TaskState.Idle && !mover.IsMoving && (PowerGrid.IsOn || !indoors))
                RelieveStress(calmRecoveryPerSecond * dt);
        }

        public void AddStress(float amount)
        {
            if (survivor.IsDead || IsPanicking || amount <= 0f) return;

            Stress = Mathf.Min(maxStress, Stress + amount);
            if (Stress >= maxStress) StartPanic();
        }

        public void RelieveStress(float amount) => Stress = Mathf.Max(0f, Stress - amount);

        /// <summary>Gece Yaşam Kiti karşılandı.</summary>
        public void Feed(float stressRelief)
        {
            Hunger = 0;
            RelieveStress(stressRelief);
        }

        /// <summary>Gece Yaşam Kiti karşılanamadı. Açlık zaten en üstteyse true döner.</summary>
        public bool Starve(float stress)
        {
            bool wasStarving = Hunger >= MaxHunger;
            Hunger = Mathf.Min(MaxHunger, Hunger + 1);
            AddStress(stress);
            return wasStarving;
        }

        void StartPanic()
        {
            panicRemaining = panicDuration;
            survivor.Tasks.CancelAll();
            survivor.Mover.Halt();
            EventBus.Publish(new SurvivorPanicked(survivor));
        }
    }
}
