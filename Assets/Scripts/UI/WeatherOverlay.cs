using AftershockIsle.CameraControl;
using AftershockIsle.Crisis;
using UnityEngine;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Hava durumunu ekrana giydirilen yarı saydam bir perdeyle gösterir (yoğun sis, asit yağmuru, fırtına, soğuk).
    /// Gerçek sis (RenderSettings.fog) kullanılmaz: karanlık sahnede her malzemeyi sis rengine boyayıp renkleri siliyordu.
    /// Perde hafif tutulur, HUD'un arkasında kalır, tıklamaları engellemez. Sığınakta hafif, açık havada (sefer) biraz daha belirgindir.
    /// </summary>
    public class WeatherOverlay : MonoBehaviour
    {
        const float BlendSpeed = 0.5f;

        /// <summary>Filtre gibi göründüğü için şimdilik kapalı; hava, arka plandaki sis perdesiyle (BackdropAtmosphere) gösteriliyor.</summary>
        public const bool Enabled = false;

        Image veil;
        Color color;
        float alpha;

        public static WeatherOverlay Create(Transform parent)
        {
            var go = new GameObject("[Hava Perdesi]");
            go.transform.SetParent(parent, false);
            return go.AddComponent<WeatherOverlay>();
        }

        void Awake()
        {
            var canvas = UiFactory.Canvas("Hava Perdesi", 5, transform);
            veil = UiFactory.Image(canvas.transform, "Perde", null, Color.clear);
            veil.sprite = null;
            veil.type = Image.Type.Simple;
            veil.raycastTarget = false;
            UiFactory.Stretch(veil.rectTransform);
            color = TargetColor;
            alpha = TargetAlpha;
            Apply();
        }

        Color TargetColor => WeatherSystem.Current switch
        {
            WeatherType.DenseFog => new Color(0.78f, 0.82f, 0.84f),
            WeatherType.AcidRain => new Color(0.52f, 0.58f, 0.30f),
            WeatherType.Storm => new Color(0.16f, 0.19f, 0.26f),
            WeatherType.ColdWave => new Color(0.62f, 0.76f, 0.92f),
            _ => Color.white,
        };

        float TargetAlpha
        {
            get
            {
                float strength = WeatherSystem.Current switch
                {
                    WeatherType.DenseFog => 0.22f,
                    WeatherType.AcidRain => 0.14f,
                    WeatherType.Storm => 0.20f,
                    WeatherType.ColdWave => 0.12f,
                    _ => 0f,
                };
                return SideViewCamera.ViewingField ? strength : strength * 0.4f;
            }
        }

        void Update()
        {
            float step = BlendSpeed * Time.unscaledDeltaTime;
            alpha = Mathf.MoveTowards(alpha, TargetAlpha, step);
            color = Color.Lerp(color, TargetColor, step * 3f);
            Apply();
        }

        void Apply()
        {
            if (veil == null) return;
            veil.color = new Color(color.r, color.g, color.b, alpha);
            veil.enabled = alpha > 0.002f;
        }
    }
}
