using System;
using System.Collections.Generic;
using AftershockIsle.Core;
using UnityEngine;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Açık bir modal pencere (tezgah, harita, yetenek ağacı, duraklatma) var mı. Varken dünya girdileri
    /// (karakter yürütme, etkileşim) yok sayılır; böylece A tuşu hem menüde hem dünyada çalışmaz.
    /// </summary>
    public static class ModalMenu
    {
        static int lastOpenFrame = -10;

        public static void MarkOpen() => lastOpenFrame = Time.frameCount;

        // Pencerenin kapandığı karenin bir sonrası da sayılır: kapatan tuş dünyaya sızmasın.
        public static bool IsOpen => Time.frameCount - lastOpenFrame <= 1;
    }

    /// <summary>
    /// IMGUI pencereleri için gamepad/klavye gezinmesi. Pencere her Repaint'te butonlarını sırayla kaydeder;
    /// Update'te Tick() odak değiştirir ve onayda odaktaki butonun eylemini çalıştırır. Fare tıklaması aynen çalışır.
    /// </summary>
    public class MenuNav
    {
        // Pencere açılır açılmaz basılı kalan onay tuşu yanlışlıkla bir şey seçmesin.
        const float ConfirmGrace = 0.25f;

        static readonly Color FocusColor = new Color(1f, 0.85f, 0.3f);

        readonly List<Action> pending = new List<Action>();
        readonly List<Action> actions = new List<Action>();
        int drawIndex;
        float openedAt;
        int scrolledFor = -1;

        public int Focus { get; private set; }

        /// <summary>Odaktaki butonun son Repaint'teki konumu (kaydırma alanında görünür tutmak için).</summary>
        public Rect FocusRect { get; private set; }

        /// <summary>Pencere açıldığında çağrılır.</summary>
        public void Reset(int focus = 0)
        {
            Focus = focus;
            scrolledFor = -1;
            openedAt = Time.unscaledTime;
            actions.Clear();
        }

        public void SetFocus(int index) => Focus = Mathf.Max(0, index);

        /// <summary>Pencere açıkken her Update'te çağrılır.</summary>
        public void Tick()
        {
            ModalMenu.MarkOpen();
            if (actions.Count == 0) return;

            int step = GameInput.MenuStep;
            if (step != 0)
            {
                Focus = (Focus + step + actions.Count) % actions.Count;
            }
            Focus = Mathf.Clamp(Focus, 0, actions.Count - 1);

            if (GameInput.MenuConfirmPressed && Time.unscaledTime - openedAt > ConfirmGrace)
                actions[Focus]?.Invoke();
        }

        /// <summary>Pencere içeriği çizilmeden önce (her IMGUI olayında) çağrılır.</summary>
        public void BeginDraw()
        {
            drawIndex = 0;
            if (Event.current.type == EventType.Repaint) pending.Clear();
        }

        /// <summary>Pencere içeriği çizildikten sonra çağrılır.</summary>
        public void EndDraw()
        {
            if (Event.current.type != EventType.Repaint) return;
            actions.Clear();
            actions.AddRange(pending);
        }

        /// <summary>GUILayout butonu. Tıklanırsa ya da odaktayken onaylanırsa onActivate çalışır.</summary>
        public bool Button(string text, Action onActivate, bool enabled = true, GUIStyle style = null, params GUILayoutOption[] options)
        {
            int index = Register(enabled ? onActivate : null);
            bool focused = IsFocused(index);

            var previousColor = GUI.backgroundColor;
            if (focused) GUI.backgroundColor = FocusColor;
            GUI.enabled = enabled;
            bool clicked = GUILayout.Button(focused ? $"> {text}" : text, style ?? GUI.skin.button, options) && Event.current.button == 0;
            GUI.enabled = true;
            GUI.backgroundColor = previousColor;

            if (Event.current.type == EventType.Repaint && index == Focus) FocusRect = GUILayoutUtility.GetLastRect();
            return Activate(clicked, index, onActivate);
        }

        /// <summary>Sabit konumlu buton (ör. harita düğümleri).</summary>
        public bool Button(Rect rect, GUIContent content, Action onActivate, bool enabled = true, GUIStyle style = null)
        {
            int index = Register(enabled ? onActivate : null);
            bool focused = IsFocused(index);

            var previousColor = GUI.backgroundColor;
            if (focused) GUI.backgroundColor = FocusColor;
            GUI.enabled = enabled;
            var shown = focused ? new GUIContent($"> {content.text}") : content;
            bool clicked = GUI.Button(rect, shown, style ?? GUI.skin.button) && Event.current.button == 0;
            GUI.enabled = true;
            GUI.backgroundColor = previousColor;

            if (Event.current.type == EventType.Repaint && index == Focus) FocusRect = rect;
            return Activate(clicked, index, onActivate);
        }

        int Register(Action action)
        {
            int index = drawIndex++;
            if (Event.current.type == EventType.Repaint) pending.Add(action);
            return index;
        }

        // Vurgu sadece gamepad/klavye ile gezinirken; fare kullanırken butonlar normal görünür.
        bool IsFocused(int index) => index == Focus && GameInput.UsingGamepad;

        bool Activate(bool clicked, int index, Action onActivate)
        {
            if (!clicked || Time.unscaledTime - openedAt <= ConfirmGrace) return false;
            Focus = index;
            onActivate?.Invoke();
            return true;
        }

        /// <summary>
        /// Kaydırma alanında odaktaki butonu görünür tutar. Repaint'te, kaydırma alanı çizildikten sonra çağrılır
        /// (FocusRect o zaman alanın içerik koordinatındadır).
        /// </summary>
        public void KeepFocusVisible(ref Vector2 scroll, float viewHeight)
        {
            if (Event.current.type != EventType.Repaint || scrolledFor == Focus || !GameInput.UsingGamepad) return;
            scrolledFor = Focus;
            if (FocusRect.yMax > scroll.y + viewHeight) scroll.y = FocusRect.yMax - viewHeight + 8f;
            else if (FocusRect.y < scroll.y) scroll.y = Mathf.Max(0f, FocusRect.y - 8f);
        }
    }
}
