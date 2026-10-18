using AftershockIsle.Building;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using AftershockIsle.Save;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Gün geçişi: gecenin son saniyesinde ekran kararır, yeni gün başlayınca karanlıkta "GÜN N" ve hava yazısı belirir,
    /// sonra sabah açılır. Kararmış ekran sırasında zaman durur; gün kartı geçiş bitince açılır.
    /// </summary>
    public class DayTransition : MonoBehaviour
    {
        const float DuskSeconds = 1.2f;   // Oyun zamanı: gece bitmeden kararmaya başlar.
        const float HoldSeconds = 1.1f;   // Gerçek zaman.
        const float FadeInSeconds = 0.9f;

        static DayTransition instance;

        Image curtain;
        Text title;
        Text subtitle;
        DayCycle dayCycle;
        float holdUntil = -1f;
        float fadeStart = -1f;
        bool showing;

        /// <summary>Geçiş sürüyor (gün kartı beklesin).</summary>
        public static bool Busy => instance != null && instance.showing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindFirstObjectByType<BuildingLayout>() == null || FindFirstObjectByType<DayTransition>() != null) return;
            new GameObject("[Gün Geçişi]").AddComponent<DayTransition>();
        }

        void Awake()
        {
            instance = this;
            var canvas = UiFactory.Canvas("[Gün Geçişi Perdesi]", 80, transform);
            curtain = UiFactory.Image(canvas.transform, "Perde", null, new Color(0f, 0f, 0f, 0f));
            curtain.raycastTarget = false;
            UiFactory.Stretch(curtain.rectTransform);

            title = UiFactory.Label(curtain.transform, "", 72, UiFactory.Accent);
            UiFactory.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1200f, 100f));
            subtitle = UiFactory.Label(curtain.transform, "", 26, UiFactory.Text, TextAnchor.MiddleCenter, FontStyle.Normal);
            UiFactory.Place(subtitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(1200f, 50f));
            SetAlpha(0f);
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void OnEnable() => EventBus.Subscribe<DayStarted>(OnDayStarted);

        void OnDisable()
        {
            EventBus.Unsubscribe<DayStarted>(OnDayStarted);
            TimeManager.Release(this);
        }

        void OnDayStarted(DayStarted evt)
        {
            if (evt.Day < 1 || SaveManager.IsApplying || SaveManager.PendingLoad) return;

            title.text = $"GÜN {evt.Day}";
            subtitle.text = evt.Day <= 1 ? "Depremden sonraki ilk sabah" : "Sabah oldu";
            showing = true;
            holdUntil = Time.unscaledTime + (evt.Day <= 1 ? 0.6f : HoldSeconds);
            fadeStart = -1f;
            SetAlpha(1f);
            TimeManager.Hold(this);
        }

        void Update()
        {
            if (showing)
            {
                // Hava yeni günle aynı olayda değişir; bir kare sonra doğru yazılır.
                if (subtitle.text == "Sabah oldu" && WeatherSystem.Instance != null)
                    subtitle.text = $"Sabah oldu  ·  {WeatherSystem.ToDisplayName(WeatherSystem.Current)}";

                if (fadeStart < 0f)
                {
                    if (Time.unscaledTime < holdUntil) return;
                    fadeStart = Time.unscaledTime;
                    TimeManager.Release(this);
                }

                float t = (Time.unscaledTime - fadeStart) / FadeInSeconds;
                SetAlpha(1f - Mathf.SmoothStep(0f, 1f, t));
                if (t >= 1f)
                {
                    showing = false;
                    SetAlpha(0f);
                }
                return;
            }

            // Gecenin son anları: ekran yavaşça kararır (yazı yok).
            if (dayCycle == null) dayCycle = FindFirstObjectByType<DayCycle>();
            if (dayCycle == null || dayCycle.Phase != DayPhase.Night || TimeManager.IsHeld)
            {
                if (curtain.color.a > 0f) SetAlpha(0f);
                return;
            }

            float remaining = dayCycle.PhaseRemaining;
            float alpha = remaining < DuskSeconds ? 1f - remaining / DuskSeconds : 0f;
            title.text = "";
            subtitle.text = "";
            SetAlpha(alpha);
        }

        void SetAlpha(float alpha)
        {
            curtain.color = new Color(0f, 0f, 0f, alpha);
            title.color = new Color(title.color.r, title.color.g, title.color.b, alpha);
            subtitle.color = new Color(subtitle.color.r, subtitle.color.g, subtitle.color.b, alpha);
            // Görünmezken çizilmesin.
            curtain.enabled = alpha > 0.001f;
            title.enabled = curtain.enabled;
            subtitle.enabled = curtain.enabled;
        }
    }
}
