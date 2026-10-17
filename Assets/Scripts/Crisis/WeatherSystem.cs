using AftershockIsle.CameraControl;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Interaction;
using AftershockIsle.UI;
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
            RenderSettings.fog = false;
            if (WeatherOverlay.Enabled) WeatherOverlay.Create(transform);
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
                    var building = survivor.Mover.Building;
                    if (building != null && survivor.Mover.CurrentFloor == building.RoofFloor && survivor.Tasks.State == TaskState.Working)
                    {
                        survivor.Condition.AddStress(0.65f * dt);
                    }
                }
            }
        }

        // Gerçek sis (RenderSettings.fog) kullanılmaz: karanlık sahnede her malzemeyi sis rengine boyayıp renkleri siliyordu.
        // Hava, WeatherOverlay (ekran perdesi) ve BackdropAtmosphere (arka plan sisi) ile gösterilir.
        static Color FogColorFor(WeatherType weather) => weather switch
        {
            WeatherType.DenseFog => new Color(0.58f, 0.65f, 0.72f),
            WeatherType.AcidRain => new Color(0.55f, 0.58f, 0.36f),
            WeatherType.Storm => new Color(0.32f, 0.36f, 0.44f),
            WeatherType.ColdWave => new Color(0.62f, 0.75f, 0.88f),
            _ => new Color(0.62f, 0.68f, 0.72f),
        };

        /// <summary>Şu anki havanın sis rengi (sığınak arka planındaki sis perdesi de bunu kullanır).</summary>
        public static Color FogColor => FogColorFor(Current);

        /// <summary>Arka plandaki sis perdesinin ek yoğunluğu (0 = açık hava, 1 = en yoğun).</summary>
        public static float BackdropFogAmount => Current switch
        {
            WeatherType.DenseFog => 0.85f,
            WeatherType.Storm => 0.55f,
            WeatherType.AcidRain => 0.5f,
            WeatherType.ColdWave => 0.35f,
            _ => 0f,
        };

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
