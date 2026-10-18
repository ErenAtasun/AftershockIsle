using AftershockIsle.Crisis;
using UnityEngine;

namespace AftershockIsle.Building
{
    /// <summary>
    /// Sığınak arka planındaki sis perdesini havaya göre ayarlar: yoğun siste perde kalınlaşıp yükselir, şehir silüeti
    /// sis rengine solar; fırtına, asit yağmuru ve soğukta daha hafif, kendi renginde. Geçişler yumuşaktır.
    /// Bina ve karakterler etkilenmez (gerçek sis yalnızca sefer sahasında açılır, bkz. WeatherSystem).
    /// </summary>
    public class BackdropAtmosphere : MonoBehaviour
    {
        const float MaxHazeAlpha = 0.9f;
        const float MaxPanelTint = 0.65f;
        const float MaxHeightBoost = 2.4f;
        const float BlendSpeed = 0.6f;

        SpriteRenderer haze;
        SpriteRenderer[] panels;
        Color clearColor;
        float clearAlpha;
        float clearTint;
        float baseHeight;

        float amount;
        Color color;

        public void Init(SpriteRenderer hazeRenderer, SpriteRenderer[] renderers, Color hazeColor, float hazeAlpha, float panelTint)
        {
            haze = hazeRenderer;
            clearColor = hazeColor;
            clearAlpha = hazeAlpha;
            clearTint = panelTint;
            baseHeight = haze != null ? haze.transform.localScale.y : 1f;

            var list = new System.Collections.Generic.List<SpriteRenderer>();
            foreach (var r in renderers)
                if (r != null && r != haze) list.Add(r);
            panels = list.ToArray();

            amount = WeatherSystem.BackdropFogAmount;
            color = TargetColor;
            Apply();
        }

        Color TargetColor => WeatherSystem.Current == WeatherType.Clear ? clearColor : WeatherSystem.FogColor;

        void Update()
        {
            float step = BlendSpeed * Time.unscaledDeltaTime;
            float target = WeatherSystem.BackdropFogAmount;
            amount = Mathf.MoveTowards(amount, target, step);
            color = Color.Lerp(color, TargetColor, step * 2f);
            if (Mathf.Abs(color.r - TargetColor.r) + Mathf.Abs(color.g - TargetColor.g) + Mathf.Abs(color.b - TargetColor.b) < 0.003f) color = TargetColor;
            Apply();
        }

        void Apply()
        {
            // Fırtınada şimşek çakınca gökyüzü (sis perdesi) bir an aydınlanır.
            float flash = AftershockIsle.Effects.WeatherVisuals.Flash01;
            if (haze != null)
            {
                var lit = Color.Lerp(color, new Color(0.92f, 0.94f, 1f), flash * 0.7f);
                haze.color = new Color(lit.r, lit.g, lit.b, Mathf.Lerp(clearAlpha, MaxHazeAlpha, amount));
                var scale = haze.transform.localScale;
                haze.transform.localScale = new Vector3(scale.x, baseHeight * Mathf.Lerp(1f, MaxHeightBoost, amount), scale.z);
            }

            float tint = Mathf.Lerp(clearTint, MaxPanelTint, amount);
            var panelColor = Color.Lerp(Color.Lerp(Color.white, color, tint), Color.white, flash);
            foreach (var panel in panels)
                if (panel != null) panel.color = panelColor;
        }
    }
}
