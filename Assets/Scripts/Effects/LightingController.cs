using AftershockIsle.Building;
using AftershockIsle.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AftershockIsle.Effects
{
    /// <summary>
    /// Sahnenin genel ışığı: gün fazına göre (sabah / öğle / gece) pencerelerden giren güneş, ortam ışığı ve pozlama değişir;
    /// gece güneş zayıf, soğuk mavi ay ışığına döner ve sahne kararır, ampuller öne çıkar. Şalter inince de bina ayrıca
    /// kararır. Tüm geçişler yumuşaktır.
    /// </summary>
    public class LightingController : MonoBehaviour
    {
        [SerializeField] Light sun;

        [Header("Şalter kesintisi")]
        [SerializeField, Range(0.05f, 1f)] float blackoutSun = 0.4f;
        [SerializeField, Range(0.05f, 1f)] float blackoutAmbient = 0.35f;
        [SerializeField] float fadeSpeed = 2f;

        // Çarpanlar ModelManager'ın karanlık temasının (This War of Mine) güneş ve ortam ışığına göredir: 1 = temanın kendisi.
        // Odaların içini asıl ortam ışığı aydınlatır (güneş yalnızca arka pencerelerden girer), gündüzü o taşır.
        [Header("Öğle")]
        [SerializeField, Range(0f, 4f)] float noonSunScale = 1.9f;
        [SerializeField, Range(0f, 8f)] float noonAmbientScale = 4f;
        [Tooltip("Genel pozlama (artı = daha aydınlık)")]
        [SerializeField, Range(-3f, 2f)] float noonExposureValue = 0.4f;

        [Header("Sabah")]
        [SerializeField, Range(0f, 4f)] float morningSunScale = 1.4f;
        [SerializeField, Range(0f, 8f)] float morningAmbientScale = 3f;
        [SerializeField, Range(-3f, 2f)] float morningExposureValue = 0.2f;
        [SerializeField] Color morningTint = new Color(1f, 0.86f, 0.7f);

        [Header("Gece")]
        [SerializeField, Range(0f, 2f)] float nightSunScale = 0.16f;
        [SerializeField, Range(0f, 4f)] float nightAmbientScale = 0.45f;
        [Tooltip("Gece genel pozlaması (eksi = daha karanlık; lambalar da biraz kısılır)")]
        [SerializeField, Range(-3f, 2f)] float nightExposureValue = -0.75f;
        [SerializeField] Color moonTint = new Color(0.55f, 0.66f, 1f);
        [Tooltip("Fazlar arası geçiş hızı (saniyede)")]
        [SerializeField] float phaseFadeSpeed = 0.35f;

        float baseSun;
        Color baseSunColor = Color.white;
        Color baseAmbient;
        float powerFactor = 1f;

        float sunScale = 1f;
        float ambientScale = 1f;
        float exposure;
        Color tint = Color.white;
        bool phaseInitialized;

        DayCycle dayCycle;
        ColorAdjustments exposureOverride;

        /// <summary>Pencereden giren güneşin öğleye oranı (öğle 1, sabah ~0.7, gece ~0.1); güneş hüzmeleri bunu izler.</summary>
        public static float Daylight { get; private set; } = 1f;
        /// <summary>Güneşin o anki tonu (sabah sıcak, gece ay mavisi).</summary>
        public static Color SunTint { get; private set; } = Color.white;

        void Awake()
        {
            dayCycle = FindFirstObjectByType<DayCycle>();
            Rebase();
        }

        /// <summary>Öğle ve şalter açıkken hedeflenen güneş ve ortam ışığını şimdiki değerlerden yeniden okur (ışık teması değişince).</summary>
        public void Rebase()
        {
            if (sun != null)
            {
                baseSun = sun.intensity;
                baseSunColor = sun.color;
            }
            baseAmbient = RenderSettings.ambientLight;
            exposureOverride = null;
            phaseInitialized = false;
        }

        void OnDestroy()
        {
            // Sahne yeniden yüklenirken ışık eski haline dönsün.
            RenderSettings.ambientLight = baseAmbient;
            if (sun != null)
            {
                sun.intensity = baseSun;
                sun.color = baseSunColor;
            }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            powerFactor = Mathf.MoveTowards(powerFactor, PowerGrid.IsOn ? 1f : 0f, fadeSpeed * dt);

            if (dayCycle == null) dayCycle = FindFirstObjectByType<DayCycle>();
            var phase = dayCycle != null ? dayCycle.Phase : DayPhase.Noon;
            GetPhaseTargets(phase, out float sunTarget, out float ambientTarget, out float exposureTarget, out Color tintTarget);

            if (!phaseInitialized)
            {
                // Oyun (ya da kayıt) gece açılırsa geçiş beklenmeden doğrudan gece başlasın.
                sunScale = sunTarget;
                ambientScale = ambientTarget;
                exposure = exposureTarget;
                tint = tintTarget;
                phaseInitialized = true;
            }
            else
            {
                // Üstel yaklaşma: aralık büyük de olsa geçiş birkaç saniyede biter.
                float k = 1f - Mathf.Exp(-phaseFadeSpeed * 3f * dt);
                sunScale = Mathf.Lerp(sunScale, sunTarget, k);
                ambientScale = Mathf.Lerp(ambientScale, ambientTarget, k);
                exposure = Mathf.Lerp(exposure, exposureTarget, k);
                tint = Color.Lerp(tint, tintTarget, k);
            }

            Daylight = noonSunScale > 0.001f ? sunScale / noonSunScale : 0f;
            SunTint = tint;

            if (sun != null)
            {
                sun.intensity = baseSun * sunScale * Mathf.Lerp(blackoutSun, 1f, powerFactor);
                sun.color = baseSunColor * tint;
            }

            // Gece ortam ışığı da aya doğru hafif maviye kayar.
            var ambientTint = Color.Lerp(Color.white, tint, 0.5f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = baseAmbient * ambientTint * (ambientScale * Mathf.Lerp(blackoutAmbient, 1f, powerFactor));

            ApplyExposure(exposure);
        }

        void GetPhaseTargets(DayPhase phase, out float sunTarget, out float ambientTarget, out float exposureTarget, out Color tintTarget)
        {
            switch (phase)
            {
                case DayPhase.Morning:
                    sunTarget = morningSunScale; ambientTarget = morningAmbientScale; exposureTarget = morningExposureValue; tintTarget = morningTint;
                    break;
                case DayPhase.Night:
                    sunTarget = nightSunScale; ambientTarget = nightAmbientScale; exposureTarget = nightExposureValue; tintTarget = moonTint;
                    break;
                default:
                    sunTarget = noonSunScale; ambientTarget = noonAmbientScale; exposureTarget = noonExposureValue; tintTarget = Color.white;
                    break;
            }
        }

        /// <summary>ModelManager'ın kurduğu siyah vinyet Volume'undaki pozlama ayarını sürer.</summary>
        void ApplyExposure(float value)
        {
            if (exposureOverride == null)
            {
                var go = GameObject.Find(ModelManager.VignetteVolumeName);
                if (go == null || !go.TryGetComponent<Volume>(out var volume) || volume.sharedProfile == null) return;
                if (!volume.sharedProfile.TryGet(out exposureOverride)) return;
            }
            exposureOverride.postExposure.Override(value);
        }
    }
}
