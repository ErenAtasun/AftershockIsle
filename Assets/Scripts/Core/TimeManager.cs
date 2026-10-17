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

        public TimeSpeed Speed { get; private set; } = TimeSpeed.Normal;
        public bool IsPaused => Speed == TimeSpeed.Paused;

        void Start() => Apply(TimeSpeed.Normal);

        void Update()
        {
            if (GameInput.PausePressed) TogglePause();
            else if (GameInput.NormalSpeedPressed) SetSpeed(TimeSpeed.Normal);
            else if (GameInput.FastSpeedPressed) SetSpeed(TimeSpeed.Fast);
            else if (GameInput.SpeedTogglePressed) SetSpeed(Speed == TimeSpeed.Fast ? TimeSpeed.Normal : TimeSpeed.Fast);
        }

        void OnDestroy() => Time.timeScale = 1f;

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
            Time.timeScale = speed switch
            {
                TimeSpeed.Paused => 0f,
                TimeSpeed.Fast => fastMultiplier,
                _ => 1f,
            };
            EventBus.Publish(new TimeSpeedChanged(speed));
        }
    }
}
