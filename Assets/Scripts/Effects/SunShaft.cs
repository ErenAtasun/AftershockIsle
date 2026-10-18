using UnityEngine;

namespace AftershockIsle.Effects
{
    /// <summary>
    /// Pencereden içeri giren güneş: zemine/duvara ışık lekesi düşüren spot ışığı ve pencereden zemine uzanan yarı saydam
    /// ışık sütunu. Gücü ve rengi LightingController'ın gün ışığını izler (öğle güçlü, sabah sıcak, gece soluk ay ışığı).
    /// </summary>
    public class SunShaft : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // Sahneyle kaydedilir: ModelManager editörde bir kez kurar, oyun bu değerlerle çalışır.
        [SerializeField] Light shaftLight;
        [SerializeField] Renderer beam;
        [SerializeField] float baseIntensity;
        [SerializeField] Color baseColor = Color.white;
        [SerializeField] Color beamColor;
        MaterialPropertyBlock block;

        public void Setup(Light light, Renderer beamRenderer, Color beamTint)
        {
            shaftLight = light;
            beam = beamRenderer;
            baseIntensity = light != null ? light.intensity : 0f;
            baseColor = light != null ? light.color : Color.white;
            beamColor = beamTint;
            block = new MaterialPropertyBlock();
            Apply();
        }

        void Update() => Apply();

        void Apply()
        {
            float day = LightingController.Daylight;
            var tint = LightingController.SunTint;

            if (shaftLight != null)
            {
                shaftLight.intensity = baseIntensity * day;
                shaftLight.color = baseColor * tint;
                shaftLight.enabled = day > 0.02f;
            }

            if (beam != null)
            {
                var c = beamColor * tint;
                c.a = beamColor.a * Mathf.Clamp01(day);
                block ??= new MaterialPropertyBlock();
                beam.GetPropertyBlock(block);
                block.SetColor(BaseColorId, c);
                beam.SetPropertyBlock(block);
                beam.enabled = c.a > 0.005f;
            }
        }
    }
}
