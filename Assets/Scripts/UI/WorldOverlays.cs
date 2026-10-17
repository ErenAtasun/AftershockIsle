using AftershockIsle.CameraControl;
using AftershockIsle.Characters;
using AftershockIsle.Crisis;
using AftershockIsle.Player;
using UnityEngine;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Dünyaya bağlı küçük göstergeler (IMGUI): kazazedelerin başının üstünde can, stres ve iş ilerlemesi, durum yazısı.
    /// Ekran sade kalsın diye çubuklar yalnızca anlamlıyken çizilir (hasar almış ya da seçili, stresli, çalışıyor);
    /// fare bir nesnenin üstündeyken adı, eylemi ve durumu. Ayrıca her kare IMGUI tıklama alanlarını (UIBlocker) sıfırlar.
    /// GameHud ile birlikte kendiliğinden kurulur.
    /// </summary>
    public class WorldOverlays : MonoBehaviour
    {
        CommandController commands;
        Camera cam;
        GUIStyle statusStyle;
        GUIStyle bubbleStyle;

        void Awake()
        {
            commands = FindFirstObjectByType<CommandController>();
            cam = Camera.main;
        }

        // Paneller alanlarını OnGUI'de yeniden bildirir; LateUpdate OnGUI'den önce çalışır.
        void LateUpdate() => UIBlocker.Clear();

        void OnGUI()
        {
            if (cam == null || SideViewCamera.ViewingField || ModalMenu.IsOpen || PauseMenu.IsOpen) return;
            UiTheme.Apply();
            statusStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

            DrawSurvivors();
            DrawHover();
        }

        void DrawSurvivors()
        {
            var selected = commands != null ? commands.Selected : null;
            foreach (var survivor in Survivor.All)
            {
                if (survivor.IsDead) continue;

                Vector3 screen = cam.WorldToScreenPoint(survivor.transform.position + Vector3.up * 2.2f);
                if (screen.z < 0f) continue;

                var at = UiTheme.ToGui(screen);
                float x = at.x - 24f;
                float y = at.y;

                float health = survivor.Health / survivor.MaxHealth;
                if (health < 0.99f || survivor == selected)
                {
                    DrawBar(new Rect(x, y, 48f, 5f), health, Color.Lerp(Color.red, Color.green, health));
                    y += 6f;
                }

                var condition = survivor.Condition;
                float stress = condition.Stress / condition.MaxStress;
                if (stress >= 0.35f)
                {
                    DrawBar(new Rect(x, y, 48f, 4f), stress, new Color(0.75f, 0.35f, 0.9f));
                    y += 5f;
                }

                float progress = survivor.Tasks.Progress01;
                if (progress > 0f) DrawBar(new Rect(x, y, 48f, 5f), progress, Color.white);

                string status = survivor.IsTrapped ? $"ENKAZ ALTINDA {Mathf.CeilToInt(survivor.TrappedRemaining)} sn"
                    : condition.IsPanicking ? $"PANİK {Mathf.CeilToInt(condition.PanicRemaining)} sn"
                    : condition.IsExhausted ? "Tükenmiş"
                    : null;

                float topY = at.y - 22f;
                if (status != null)
                {
                    GUI.Label(new Rect(at.x - 70f, topY, 140f, 20f), status, statusStyle);
                    topY -= 18f;
                }

                bool isGuard = NightDefenseSystem.Guard == survivor;
                if (isGuard)
                {
                    DrawGuardBadge(at.x, topY);
                    topY -= 20f;
                }

                var chatter = survivor.GetComponent<SurvivorChatter>();
                if (chatter != null && chatter.HasActiveBubble)
                {
                    DrawSpeechBubble(at.x, topY, chatter);
                }
            }
        }

        void DrawGuardBadge(float centerX, float y)
        {
            float w = 92f;
            float h = 18f;
            var rect = new Rect(centerX - w * 0.5f, y, w, h);
            var prev = GUI.color;
            GUI.color = new Color(0.10f, 0.12f, 0.18f, 0.94f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(0.95f, 0.75f, 0.25f, 0.95f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1.5f), Texture2D.whiteTexture);
            GUI.Label(rect, "🛡️ NÖBETTE", statusStyle);
            GUI.color = prev;
        }

        void DrawSpeechBubble(float centerX, float y, SurvivorChatter chatter)
        {
            bubbleStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Normal,
                wordWrap = false
            };

            string text = chatter.ActiveText;
            if (string.IsNullOrEmpty(text)) return;

            // Fade in and out
            float alpha = Mathf.Clamp01(chatter.RemainingTime / 0.5f);
            if (chatter.TotalDuration > 0.6f)
            {
                alpha = Mathf.Min(alpha, Mathf.Clamp01((chatter.TotalDuration - chatter.RemainingTime) / 0.3f));
            }

            var content = new GUIContent(text);
            var size = bubbleStyle.CalcSize(content);
            float padX = 14f;
            float padY = 6f;
            float w = Mathf.Max(60f, size.x + padX);
            float h = size.y + padY;

            var bubbleRect = new Rect(centerX - w * 0.5f, y - h, w, h);

            var prevColor = GUI.color;
            // Dark bubble background
            GUI.color = new Color(0.08f, 0.10f, 0.14f, 0.93f * alpha);
            GUI.DrawTexture(bubbleRect, Texture2D.whiteTexture);

            // Accent border line at bottom
            var tint = chatter.BubbleColor;
            GUI.color = new Color(tint.r, tint.g, tint.b, 0.85f * alpha);
            GUI.DrawTexture(new Rect(bubbleRect.x, bubbleRect.yMax - 1.5f, bubbleRect.width, 1.5f), Texture2D.whiteTexture);

            // Pointer notch
            GUI.DrawTexture(new Rect(centerX - 3f, bubbleRect.yMax, 6f, 3f), Texture2D.whiteTexture);

            // Text
            GUI.color = new Color(tint.r, tint.g, tint.b, alpha);
            GUI.Label(bubbleRect, text, bubbleStyle);

            GUI.color = prevColor;
        }

        void DrawHover()
        {
            var hovered = commands != null ? commands.Hovered : null;
            if (hovered == null) return;

            Vector2 mouse = UiTheme.ToGui(Core.GameInput.PointerPosition);
            string text = $"{hovered.DisplayName} — Sağ tık: {hovered.ActionLabel}";
            if (!string.IsNullOrEmpty(hovered.StatusText)) text += $"\n{hovered.StatusText}";

            var size = GUI.skin.box.CalcSize(new GUIContent(text));
            GUI.Box(new Rect(mouse.x + 16f, mouse.y + 8f, size.x + 8f, size.y + 4f), text);
        }

        static void DrawBar(Rect rect, float fill, Color color)
        {
            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x + 1f, rect.y + 1f, (rect.width - 2f) * Mathf.Clamp01(fill), rect.height - 2f), Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
