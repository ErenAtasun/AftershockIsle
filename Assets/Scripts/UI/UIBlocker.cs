using System.Collections.Generic;
using UnityEngine;

namespace AftershockIsle.UI
{
    /// <summary>
    /// IMGUI panellerinin ekran alanları. Dünya tıklamaları (seçim/emir) bu alanların üstündeyse yok sayılır.
    /// Paneller her OnGUI'de alanlarını yeniden bildirir.
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
            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            foreach (var rect in rects)
                if (rect.Contains(guiPoint)) return true;
            return false;
        }
    }
}
