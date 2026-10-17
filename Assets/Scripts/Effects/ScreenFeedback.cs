using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using UnityEngine;

namespace AftershockIsle.Effects
{
    /// <summary>
    /// Ekran kenarı (vignette) geri bildirimi: ağır bina hasarında kırmızı flaş, sarsıntı uyarısında titreyen kırmızı kenar,
    /// bütünlük düşükken yavaş nabız. Geçici IMGUI arayüzün arkasına çizilir.
    /// </summary>
    public class ScreenFeedback : MonoBehaviour
    {
        [SerializeField] float flashDecayPerSecond = 1.6f;
        [Tooltip("Bu kadar büyük tek seferlik hasar flaş tetikler (saniyelik aşınma tetiklemez)")]
        [SerializeField] float flashThreshold = 4f;
        [SerializeField, Range(0f, 1f)] float lowIntegrity = 0.25f;

        Texture2D vignette;
        float flash;

        void Awake() => vignette = CreateVignette(128);

        void OnEnable()
        {
            EventBus.Subscribe<IntegrityChanged>(OnIntegrityChanged);
            EventBus.Subscribe<SurvivorDied>(OnSurvivorDied);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<IntegrityChanged>(OnIntegrityChanged);
            EventBus.Unsubscribe<SurvivorDied>(OnSurvivorDied);
        }

        void OnIntegrityChanged(IntegrityChanged evt)
        {
            if (evt.Delta <= -flashThreshold) flash = Mathf.Max(flash, Mathf.Clamp01(-evt.Delta / 12f));
        }

        void OnSurvivorDied(SurvivorDied evt) => flash = 1f;

        void Update() => flash = Mathf.Max(0f, flash - flashDecayPerSecond * Time.unscaledDeltaTime);

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;

            float alpha = flash * 0.85f;

            var quake = AftershockSystem.Instance;
            if (quake != null && quake.Phase == QuakePhase.Warning)
                alpha = Mathf.Max(alpha, 0.3f + 0.2f * Mathf.Sin(Time.unscaledTime * 9f));

            var integrity = StructuralIntegrity.Instance;
            if (integrity != null && !integrity.IsCollapsed && integrity.Normalized < lowIntegrity)
                alpha = Mathf.Max(alpha, 0.18f + 0.12f * Mathf.Sin(Time.unscaledTime * 3f));

            if (alpha <= 0.01f) return;

            var previous = GUI.color;
            GUI.depth = 5; // Büyük depth arkada çizilir: HUD'un üstüne binmez.
            GUI.color = new Color(0.85f, 0.05f, 0.05f, alpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), vignette, ScaleMode.StretchToFill);
            GUI.color = previous;
        }

        // Kenarlara doğru opaklaşan, ortası şeffaf radyal doku.
        static Texture2D CreateVignette(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x / (size - 1f)) * 2f - 1f;
                    float dy = (y / (size - 1f)) * 2f - 1f;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.SmoothStep(0.35f, 0.95f, radius)));
                }
            }
            texture.Apply();
            return texture;
        }
    }
}
