using UnityEngine;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Henüz uGUI'ye taşınmamış IMGUI pencereleri (tezgah, harita, yetenek ağacı, olaylar, saha göstergesi) için ortak görünüm:
    /// koyu taş plakalar, siyah kenar, sarı başlık. Her OnGUI'nin başında Apply() çağrılır (Unity skin'i her çağrıda sıfırlar).
    /// </summary>
    public static class UiTheme
    {
        static GUISkin skin;

        /// <summary>IMGUI birimi başına ekran pikseli: uGUI göstergesiyle aynı (UiFactory.CanvasScale).</summary>
        public static float Scale => Mathf.Max(0.1f, UiFactory.CanvasScale);

        /// <summary>IMGUI koordinat uzayının genişliği ve yüksekliği (ölçeklenmiş). Screen.width/height yerine bunlar kullanılır.</summary>
        public static float Width => Screen.width / Scale;
        public static float Height => Screen.height / Scale;

        /// <summary>Ekran noktası (sol alt orijin, piksel) → IMGUI noktası (sol üst orijin, ölçeklenmiş).</summary>
        public static Vector2 ToGui(Vector2 screenPoint) => new Vector2(screenPoint.x / Scale, (Screen.height - screenPoint.y) / Scale);

        /// <summary>Her OnGUI'nin başında çağrılır: temayı uygular ve çizimi arayüz ölçeğine getirir.</summary>
        public static void Apply()
        {
            if (skin == null) Build();
            GUI.skin = skin;
            float scale = Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        }

        static void Build()
        {
            skin = Object.Instantiate(GUI.skin);
            skin.name = "Aftershock";
            skin.hideFlags = HideFlags.DontSave;

            var slate = Texture(UiFactory.Slate);
            var window = Texture(new Color(0.13f, 0.14f, 0.17f, 0.97f));
            var button = Texture(UiFactory.SlateLight);
            var hover = Texture(new Color(0.36f, 0.38f, 0.43f, 1f));
            var active = Texture(new Color(0.2f, 0.21f, 0.24f, 1f));
            var border = new RectOffset(12, 12, 12, 12);

            skin.box.normal.background = slate;
            skin.box.border = border;
            skin.box.padding = new RectOffset(10, 10, 8, 8);
            skin.box.normal.textColor = UiFactory.Text;
            skin.box.fontSize = 14;

            skin.window.normal.background = window;
            skin.window.onNormal.background = window;
            skin.window.border = new RectOffset(14, 14, 30, 14);
            skin.window.padding = new RectOffset(14, 14, 34, 12);
            skin.window.normal.textColor = UiFactory.Accent;
            skin.window.onNormal.textColor = UiFactory.Accent;
            skin.window.fontSize = 16;
            skin.window.fontStyle = FontStyle.Bold;

            skin.button.normal.background = button;
            skin.button.hover.background = hover;
            skin.button.active.background = active;
            skin.button.focused.background = hover;
            skin.button.onNormal.background = hover;
            skin.button.border = border;
            skin.button.normal.textColor = UiFactory.Text;
            skin.button.hover.textColor = UiFactory.Accent;
            skin.button.active.textColor = UiFactory.Text;
            skin.button.fontSize = 14;
            skin.button.fontStyle = FontStyle.Bold;

            skin.label.normal.textColor = UiFactory.Text;
            skin.label.fontSize = 14;
        }

        static Texture2D Texture(Color fill) => UiFactory.RoundedTexture(40, 10, 3, fill, UiFactory.Ink);
    }
}
