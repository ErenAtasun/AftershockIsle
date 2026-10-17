using AftershockIsle.Building;
using UnityEngine;
using UnityEngine.Rendering;

namespace AftershockIsle.Effects
{
    /// <summary>Şalter inince bina kararır: genel ışık ve ortam ışığı yumuşakça kısılır, şalter kalkınca geri gelir.</summary>
    public class LightingController : MonoBehaviour
    {
        [SerializeField] Light sun;
        [SerializeField, Range(0.05f, 1f)] float blackoutSun = 0.4f;
        [SerializeField, Range(0.05f, 1f)] float blackoutAmbient = 0.35f;
        [SerializeField] float fadeSpeed = 2f;

        float baseSun;
        Color baseAmbient;
        float factor = 1f;

        void Awake() => Rebase();

        /// <summary>Şalter açıkken hedeflenen güneş ve ortam ışığını şimdiki değerlerden yeniden okur (ışık teması değişince).</summary>
        public void Rebase()
        {
            if (sun != null) baseSun = sun.intensity;
            baseAmbient = RenderSettings.ambientLight;
        }

        void OnDestroy()
        {
            // Sahne yeniden yüklenirken ortam ışığı eski haline dönsün.
            RenderSettings.ambientLight = baseAmbient;
        }

        void Update()
        {
            float target = PowerGrid.IsOn ? 1f : 0f;
            factor = Mathf.MoveTowards(factor, target, fadeSpeed * Time.unscaledDeltaTime);
            if (Mathf.Approximately(factor, target) && target >= 1f && sun != null && Mathf.Approximately(sun.intensity, baseSun)) return;

            if (sun != null) sun.intensity = baseSun * Mathf.Lerp(blackoutSun, 1f, factor);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = baseAmbient * Mathf.Lerp(blackoutAmbient, 1f, factor);
        }
    }
}
