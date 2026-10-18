using AftershockIsle.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Oyun açılırken stüdyo (PBH Studio) logosu: açık zeminde belirir, kısa süre kalır, solar ve ana menü açılır.
    /// Her uygulama oturumunda bir kez gösterilir; herhangi bir tuş ya da tıkla geçilebilir.
    /// Logo görseli yoksa (UiSkin) hiçbir şey göstermez.
    /// </summary>
    public class SplashScreen : MonoBehaviour
    {
        const float FadeIn = 0.7f;
        const float Hold = 1.6f;
        const float FadeOut = 0.7f;
        const float SkipAfter = 0.5f;

        // Logo görselinin zemin rengi (241, 240, 239).
        static readonly Color Background = new Color(0.945f, 0.941f, 0.937f, 1f);

        static bool shown;

        float startedAt;
        CanvasGroup group;
        System.Action onDone;

        /// <summary>Bu oturumda gösterildiyse ya da logo yoksa false döner (menü doğrudan açılır).</summary>
        public static bool TryPlay(Transform parent, System.Action done)
        {
            var skin = GameAssets.Ui;
            var logo = skin != null ? (skin.pbhLogoTransparent != null ? skin.pbhLogoTransparent : skin.pbhLogo) : null;
            if (shown || logo == null) return false;
            shown = true;

            var go = new GameObject("[Açılış Logosu]");
            go.transform.SetParent(parent, false);
            var splash = go.AddComponent<SplashScreen>();
            splash.Build(logo);
            splash.onDone = done;
            return true;
        }

        void Build(Sprite logo)
        {
            UiFactory.EnsureEventSystem();
            var canvas = UiFactory.Canvas("Açılış", 200, transform);
            group = canvas.gameObject.AddComponent<CanvasGroup>();

            // Zemin tüm ekranı kaplar ve tıklamayı alır (menü düğmeleri logo sürerken basılamasın).
            var back = UiFactory.Panel(canvas.transform, "Zemin", Background);
            UiFactory.Stretch(back.rectTransform);

            var image = UiFactory.Image(canvas.transform, "Logo", logo, Color.white);
            image.preserveAspect = true;
            UiFactory.Place(image.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 820f));

            startedAt = Time.unscaledTime;
            group.alpha = 0f;
        }

        static bool AnyPressed()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            return (keyboard != null && keyboard.anyKey.wasPressedThisFrame) ||
                   (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) ||
                   (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
        }

        void Update()
        {
            float t = Time.unscaledTime - startedAt;
            float total = FadeIn + Hold + FadeOut;

            bool skip = t > SkipAfter && AnyPressed();
            if (t >= total || skip)
            {
                onDone?.Invoke();
                Destroy(gameObject);
                return;
            }

            group.alpha = t < FadeIn ? Mathf.SmoothStep(0f, 1f, t / FadeIn)
                : t < FadeIn + Hold ? 1f
                : 1f - Mathf.SmoothStep(0f, 1f, (t - FadeIn - Hold) / FadeOut);
        }
    }
}
