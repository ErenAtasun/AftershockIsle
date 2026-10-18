using AftershockIsle.Building;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Crisis;
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
            int floor = mover.CurrentFloor;
            bool indoors = floor < mover.Building.FloorCount - 1;
            bool hasHeater = PlaceableStructure.HasOnFloor(RecipeId.ShelterHeater, floor);
            bool isLit = PowerGrid.IsOn || hasHeater || !indoors;

            if (survivor.HasTrait(TraitId.Claustrophobic) && floor == 0 && tasks.State == TaskState.Working)
            {
                AddStress(0.35f * dt);
            }

            if (tasks.State == TaskState.Working && !isLit)
            {
                float coldFactor = hasHeater ? 1f : WeatherSystem.ColdDarkStressMultiplier;
                float claustroFactor = survivor.HasTrait(TraitId.Claustrophobic) ? 1.8f : 1f;
                float lampFactor = ToolKit.HasHeadLamp ? ToolKit.HeadLampDarkStress : 1f;
                AddStress(darkWorkStressPerSecond * coldFactor * claustroFactor * lampFactor * dt);
            }
            else if (tasks.State == TaskState.Idle && !mover.IsMoving && isLit)
            {
                float comfortBonus = PlaceableStructure.HasOnFloor(RecipeId.ComfortCorner, floor) ? 2.5f : 1f;
                float optimistBonus = survivor.HasTrait(TraitId.Optimist) ? 1.35f : 1f;
                RelieveStress(calmRecoveryPerSecond * comfortBonus * optimistBonus * dt);
            }
        }

        public void AddStress(float amount)
        {
            if (survivor.IsDead || IsPanicking || amount <= 0f) return;

            if (survivor.HasTrait(TraitId.Optimist)) amount *= 0.75f;
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

        public void RestoreState(float stress, int hunger, float panic)
        {
            Stress = Mathf.Clamp(stress, 0f, maxStress);
            Hunger = Mathf.Clamp(hunger, 0, MaxHunger);
            panicRemaining = Mathf.Max(0f, panic);
        }
    }
}
