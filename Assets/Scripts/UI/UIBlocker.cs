using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AftershockIsle.UI
{
    /// <summary>
    /// IMGUI panellerinin ekran alanları ve uGUI göstergesi. Dünya tıklamaları (seçim/emir) bunların üstündeyse yok sayılır.
    /// IMGUI paneller her OnGUI'de alanlarını yeniden bildirir; uGUI için EventSystem sorulur.
    /// </summary>
    public static class UIBlocker
    {
        static readonly List<Rect> rects = new List<Rect>();

        public static void Clear() => rects.Clear();

        /// <summary>IMGUI koordinatında (sol üst köşe orijin) bir alan ekler.</summary>
        public static void Add(Rect guiRect) => rects.Add(guiRect);

        /// <summary>Input System ekran koordinatı (sol alt köşe orijin) bir panelin üstünde mi.</summary>
        public static bool IsPointerOver(Vector2 screenPosition)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return true;

            var guiPoint = UiTheme.ToGui(screenPosition);
            foreach (var rect in rects)
                if (rect.Contains(guiPoint)) return true;
            return false;
        }
    }
}
