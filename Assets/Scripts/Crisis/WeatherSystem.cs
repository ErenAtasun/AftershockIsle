using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Interaction;
using UnityEngine;

namespace AftershockIsle.Crisis
{
    public enum WeatherType
    {
        Clear = 0,
        DenseFog = 1,
        AcidRain = 2,
        Storm = 3,
        ColdWave = 4
    }

    public readonly struct WeatherChanged
    {
        public readonly WeatherType Weather;
        public WeatherChanged(WeatherType weather) => Weather = weather;
    }

    /// <summary>
    /// Dinamik Hava Durumu ve Çevre Değişkenleri Sistemi.
    /// Her yeni gün başında hava durumunu belirler; görsel sis/atmosferi,
    /// dış saha gizliliğini, duman tahliyesini ve çatı/soğuk etkilerini yönetir.
    /// </summary>
    public class WeatherSystem : MonoBehaviour
    {
        public static WeatherSystem Instance { get; private set; }
        public static WeatherType Current => Instance != null ? Instance.CurrentWeather : WeatherType.Clear;

        public WeatherType CurrentWeather { get; private set; } = WeatherType.Clear;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstance()
        {
            if (Instance == null)
            {
                var existing = FindFirstObjectByType<WeatherSystem>();
                if (existing != null)
                {
                    Instance = existing;
                    return;
                }

                var systems = GameObject.Find("[Systems]");
                if (systems != null)
                {
                    systems.AddComponent<WeatherSystem>();
                }
            }
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            ApplyAtmosphereImmediate();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable() => EventBus.Subscribe<DayStarted>(OnDayStarted);
        void OnDisable() => EventBus.Unsubscribe<DayStarted>(OnDayStarted);

        void OnDayStarted(DayStarted evt)
        {
            if (evt.Day <= 1)
            {
                SetWeather(WeatherType.Clear);
                return;
            }

            var next = PickWeather(evt.Day, CurrentWeather);
            SetWeather(next);

            // Asit/Kül yağmuru yağdığında su bidonlarını +1 kova doldurur
            if (next == WeatherType.AcidRain)
            {
                foreach (var ws in FindObjectsByType<WaterSource>(FindObjectsSortMode.None))
                {
                    if (ws != null && !ws.UsesFloodWater)
                        ws.RestoreState(Mathf.Min(5, ws.Charges + 1));
                }
            }
        }

        /// <summary>
        /// Ağırlıklı hava seçimi: açık hava en sık; sis 2. günden, sert havalar (asit, fırtına, soğuk) 3. günden itibaren gelir.
        /// Aynı sert hava iki gün üst üste gelmez; 6. günden sonra açık hava seyrekleşir (oyun zorlaşır).
        /// </summary>
        static WeatherType PickWeather(int day, WeatherType previous)
        {
            float clear = day >= 6 ? 28f : 40f;
            float harsh = day >= 3 ? 1f : 0f;
            float[] weights =
            {
                clear,           // Clear
                18f,             // DenseFog
                15f * harsh,     // AcidRain
                15f * harsh,     // Storm
                12f * harsh,     // ColdWave
            };
            if (previous != WeatherType.Clear) weights[(int)previous] = 0f;

            float total = 0f;
            foreach (float weight in weights) total += weight;

            float roll = Random.value * total;
            for (int i = 0; i < weights.Length; i++)
            {
                roll -= weights[i];
                if (roll <= 0f && weights[i] > 0f) return (WeatherType)i;
            }
            return WeatherType.Clear;
        }

        public void SetWeather(WeatherType weather)
        {
            CurrentWeather = weather;
            ApplyAtmosphereImmediate();
            EventBus.Publish(new WeatherChanged(weather));
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Asit yağmurunda çatıda (Kat 3) maskesiz çalışan kazazedeler hafif stres alır
            if (CurrentWeather == WeatherType.AcidRain && !Workbench.Has(RecipeId.GasMask))
            {
                foreach (var survivor in Survivor.All)
                {
                    if (survivor == null || survivor.IsDead) continue;
                    if (survivor.Mover.CurrentFloor == 3 && survivor.Tasks.State == TaskState.Working)
                    {
                        survivor.Condition.AddStress(0.65f * dt);
                    }
                }
            }
        }

        void ApplyAtmosphereImmediate()
        {
            switch (CurrentWeather)
            {
                case WeatherType.Clear:
                    RenderSettings.fog = false;
                    break;

                case WeatherType.DenseFog:
                    RenderSettings.fog = true;
                    RenderSettings.fogMode = FogMode.ExponentialSquared;
                    RenderSettings.fogColor = new Color(0.58f, 0.65f, 0.72f);
                    RenderSettings.fogDensity = 0.022f;
                    break;

                case WeatherType.AcidRain:
                    RenderSettings.fog = true;
                    RenderSettings.fogMode = FogMode.ExponentialSquared;
                    RenderSettings.fogColor = new Color(0.55f, 0.58f, 0.36f);
                    RenderSettings.fogDensity = 0.014f;
                    break;

                case WeatherType.Storm:
                    RenderSettings.fog = true;
                    RenderSettings.fogMode = FogMode.ExponentialSquared;
                    RenderSettings.fogColor = new Color(0.32f, 0.36f, 0.44f);
                    RenderSettings.fogDensity = 0.015f;
                    break;

                case WeatherType.ColdWave:
                    RenderSettings.fog = true;
                    RenderSettings.fogMode = FogMode.ExponentialSquared;
                    RenderSettings.fogColor = new Color(0.62f, 0.75f, 0.88f);
                    RenderSettings.fogDensity = 0.012f;
                    break;
            }
        }

        public static float SmokeVentMultiplier => Current == WeatherType.Storm ? 2.0f : 1.0f;
        public static float EnemyVisionMultiplier => Current == WeatherType.DenseFog ? 0.65f : 1.0f;
        public static float ColdDarkStressMultiplier => Current == WeatherType.ColdWave ? 1.5f : 1.0f;

        public static string ToDisplayName(WeatherType type) => type switch
        {
            WeatherType.Clear => "Açık Hava",
            WeatherType.DenseFog => "Yoğun Sis",
            WeatherType.AcidRain => "Kül & Asit Yağmuru",
            WeatherType.Storm => "Şiddetli Fırtına",
            WeatherType.ColdWave => "Dondurucu Ayaz",
            _ => "Açık Hava"
        };

        public static string ToEffectSummary(WeatherType type) => type switch
        {
            WeatherType.Clear => "Normal görüş ve sıcaklık",
            WeatherType.DenseFog => "Sahada düşman görüşü -%35 (Gizlilik avantajı)",
            WeatherType.AcidRain => "Su kaynakları +1 dolar; çatıda maskesiz çalışmak stres verir",
            WeatherType.Storm => "Kırık pencerelerden duman ve gaz 2x hızlı tahliye olur",
            WeatherType.ColdWave => "Elektrik/Soba olmayan katlarda üşüme stresi +%50 artar",
            _ => ""
        };
    }
}
