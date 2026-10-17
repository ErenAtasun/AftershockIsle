using System.Collections.Generic;
using System.Text;
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
    ///
    /// Zamanı oyuncu dışında durdurmak isteyen her sistem (olay penceresi, duraklatma menüsü, sefer haritası, oyun sonu)
    /// Hold(this) / Release(this) kullanır. Tutan biri varken zaman durur ve hız tuşları çalışmaz; hepsi bırakınca
    /// oyuncunun seçtiği hıza dönülür. Oyuncunun hızı (Speed) tutmalardan etkilenmez, böylece sistemler birbirinin
    /// "önceki hızını" bozamaz. Tutan obje yok edilirse tutması kendiliğinden düşer.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class TimeManager : MonoBehaviour
    {
        [SerializeField] float fastMultiplier = 2f;

        TimeSpeed resumeSpeed = TimeSpeed.Normal;
        float hitStopUntil;
        readonly HashSet<object> holds = new HashSet<object>();
        readonly List<object> staleHolds = new List<object>();

        static TimeManager instance;

        public TimeSpeed Speed { get; private set; } = TimeSpeed.Normal;
        public bool IsPaused => Speed == TimeSpeed.Paused;

        /// <summary>Bir sistem zamanı tutuyor mu (olay penceresi, menü, harita, oyun sonu).</summary>
        public static bool IsHeld => instance != null && instance.holds.Count > 0;

        void Awake() => instance = this;

        void Start() => Apply(TimeSpeed.Normal);

        /// <summary>owner bırakana kadar zamanı durdurur. Aynı owner birden çok kez tutsa da tek sayılır.</summary>
        public static void Hold(object owner)
        {
            if (instance == null || owner == null) return;
            if (instance.holds.Add(owner)) instance.ApplyScale();
        }

        /// <summary>owner'ın tutmasını bırakır; başka tutan yoksa oyuncunun hızına dönülür.</summary>
        public static void Release(object owner)
        {
            if (instance == null || owner == null) return;
            if (instance.holds.Remove(owner)) instance.ApplyScale();
        }

        /// <summary>
        /// Vuruş hissi için oyunu çok kısa süre neredeyse dondurur (gerçek zamanla ölçülür). Duraklatılmış ya da
        /// tutulmuşsa etkisiz; süre bitince mevcut hızın zaman ölçeğine döner.
        /// </summary>
        public static void HitStop(float seconds)
        {
            if (instance == null || !instance.enabled || instance.IsPaused || IsHeld) return;
            instance.hitStopUntil = Mathf.Max(instance.hitStopUntil, Time.unscaledTime + seconds);
            Time.timeScale = 0.05f;
        }

        void Update()
        {
            DropDestroyedHolders();

            if (hitStopUntil > 0f && Time.unscaledTime >= hitStopUntil)
            {
                hitStopUntil = 0f;
                ApplyScale();
            }

            GuardTimeScale();

            if (holds.Count > 0) return;

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

        float ExpectedScale => holds.Count > 0 ? 0f : ScaleFor(Speed);

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
            ApplyScale();
            EventBus.Publish(new TimeSpeedChanged(speed));
        }

        void ApplyScale()
        {
            hitStopUntil = 0f;
            Time.timeScale = ExpectedScale;
        }

        // Tutan sistem bırakmadan yok olduysa (sahne değişimi, hata) zaman sonsuza dek durmasın.
        void DropDestroyedHolders()
        {
            if (holds.Count == 0) return;
            staleHolds.Clear();
            foreach (var owner in holds)
                if (owner is Object unityObject && unityObject == null) staleHolds.Add(owner);
            if (staleHolds.Count == 0) return;

            foreach (var owner in staleHolds) holds.Remove(owner);
            Debug.LogWarning($"[Zaman] Yok edilmiş {staleHolds.Count} tutucu bırakıldı.");
            ApplyScale();
        }

        // Başka bir kod Time.timeScale'i doğrudan değiştirdiyse düzelt: zaman yalnızca buradan yönetilir.
        void GuardTimeScale()
        {
            if (hitStopUntil > 0f) return;
            float expected = ExpectedScale;
            if (Mathf.Approximately(Time.timeScale, expected)) return;

            Debug.LogWarning($"[Zaman] Beklenmeyen zaman ölçeği {Time.timeScale:0.##} → {expected:0.##} düzeltildi. {Describe()}");
            Time.timeScale = expected;
        }

        /// <summary>Test için: kimin zamanı tuttuğu.</summary>
        public static string Describe()
        {
            if (instance == null) return "TimeManager yok.";
            var text = new StringBuilder($"Hız: {instance.Speed}");
            if (instance.holds.Count == 0) return text.ToString();
            text.Append(", tutanlar: ");
            foreach (var owner in instance.holds) text.Append(owner is Object o ? o.name + $" ({o.GetType().Name})" : owner.GetType().Name).Append("; ");
            return text.ToString();
        }
    }
}
