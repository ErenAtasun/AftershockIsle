using UnityEngine;

namespace AftershockIsle.Core
{
    public enum TimeSpeed { Paused, Normal, Fast }

    public readonly struct TimeSpeedChanged
    {
        public readonly TimeSpeed Speed;
        public TimeSpeedChanged(TimeSpeed speed) => Speed = speed;
    }

    /// <summary>
    /// Space / gamepad Y: aktif duraklatma, 1: normal hız, 2: hızlı akış, gamepad X: normal ↔ hızlı.
    /// Oyun mantığı Time.deltaTime kullanır; kamera ve arayüz unscaledDeltaTime kullanır.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class TimeManager : MonoBehaviour
    {
        [SerializeField] float fastMultiplier = 2f;

        TimeSpeed resumeSpeed = TimeSpeed.Normal;
        float hitStopUntil;

        static TimeManager instance;

        public TimeSpeed Speed { get; private set; } = TimeSpeed.Normal;
        public bool IsPaused => Speed == TimeSpeed.Paused;

        void Awake() => instance = this;

        void Start() => Apply(TimeSpeed.Normal);

        /// <summary>
        /// Vuruş hissi için oyunu çok kısa süre neredeyse dondurur (gerçek zamanla ölçülür). Duraklatılmışsa etkisiz;
        /// süre bitince mevcut hızın zaman ölçeğine döner.
        /// </summary>
        public static void HitStop(float seconds)
        {
            if (instance == null || !instance.enabled || instance.IsPaused) return;
            instance.hitStopUntil = Mathf.Max(instance.hitStopUntil, Time.unscaledTime + seconds);
            Time.timeScale = 0.05f;
        }

        void Update()
        {
            if (hitStopUntil > 0f && Time.unscaledTime >= hitStopUntil)
            {
                hitStopUntil = 0f;
                if (!IsPaused) Time.timeScale = ScaleFor(Speed);
            }

            if (GameInput.PausePressed) TogglePause();
            else if (GameInput.NormalSpeedPressed) SetSpeed(TimeSpeed.Normal);
            else if (GameInput.FastSpeedPressed) SetSpeed(TimeSpeed.Fast);
            else if (GameInput.SpeedTogglePressed) SetSpeed(Speed == TimeSpeed.Fast ? TimeSpeed.Normal : TimeSpeed.Fast);
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            Time.timeScale = 1f;
        }

        float ScaleFor(TimeSpeed speed) => speed switch
        {
            TimeSpeed.Paused => 0f,
            TimeSpeed.Fast => fastMultiplier,
            _ => 1f,
        };

        public void TogglePause() => SetSpeed(IsPaused ? resumeSpeed : TimeSpeed.Paused);

        public void SetSpeed(TimeSpeed speed)
        {
            if (speed == Speed) return;
            if (speed != TimeSpeed.Paused) resumeSpeed = speed;
            Apply(speed);
        }

        void Apply(TimeSpeed speed)
        {
            Speed = speed;
            hitStopUntil = 0f;
            Time.timeScale = ScaleFor(speed);
            EventBus.Publish(new TimeSpeedChanged(speed));
        }
    }
}
