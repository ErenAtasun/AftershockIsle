using UnityEngine;

namespace AftershockIsle.Core
{
    public enum DayPhase { Morning, Noon, Night }

    public static class DayPhaseNames
    {
        public static string ToDisplayName(this DayPhase phase) => phase switch
        {
            DayPhase.Morning => "Sabah",
            DayPhase.Noon => "Öğle",
            _ => "Gece",
        };
    }

    public readonly struct DayStarted
    {
        public readonly int Day;
        public DayStarted(int day) => Day = day;
    }

    public readonly struct DayPhaseChanged
    {
        public readonly int Day;
        public readonly DayPhase Phase;

        public DayPhaseChanged(int day, DayPhase phase)
        {
            Day = day;
            Phase = phase;
        }
    }

    /// <summary>Sabah → Öğle → Gece döngüsünü oyun zamanıyla (timeScale'li) ilerletir.</summary>
    public class DayCycle : MonoBehaviour
    {
        [SerializeField] DayCycleConfig config;

        public int Day { get; private set; }
        public DayPhase Phase { get; private set; }
        public float PhaseElapsed { get; private set; }
        public float PhaseDuration => config.GetDuration(Phase);
        public float PhaseRemaining => Mathf.Max(0f, PhaseDuration - PhaseElapsed);

        void Awake()
        {
            if (config == null) config = ScriptableObject.CreateInstance<DayCycleConfig>();
        }

        void Start()
        {
            if (Day == 0) StartDay(1);
        }

        void Update()
        {
            PhaseElapsed += Time.deltaTime;
            if (PhaseElapsed >= PhaseDuration) AdvancePhase();
        }

        void AdvancePhase()
        {
            if (Phase == DayPhase.Night) StartDay(Day + 1);
            else EnterPhase(Phase + 1);
        }

        void StartDay(int day)
        {
            Day = day;
            EventBus.Publish(new DayStarted(day));
            EnterPhase(DayPhase.Morning);
        }

        void EnterPhase(DayPhase phase)
        {
            Phase = phase;
            PhaseElapsed = 0f;
            EventBus.Publish(new DayPhaseChanged(Day, phase));
        }

        public void RestoreState(int day, DayPhase phase, float phaseElapsed)
        {
            Day = day;
            Phase = phase;
            PhaseElapsed = phaseElapsed;
            EventBus.Publish(new DayStarted(Day));
            EventBus.Publish(new DayPhaseChanged(Day, Phase));
        }
    }
}
