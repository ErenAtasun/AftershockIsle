using System.Collections.Generic;
using UnityEngine;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Arayüz görselleri (Assets/Models/1x). Editördeki varlık senkronu (GameAssetsSync) dosyalardan doldurur;
    /// eksik görsel olursa arayüz kod içinde üretilen düz plakalara düşer.
    /// </summary>
    [CreateAssetMenu(menuName = "Aftershock/UI Skin", fileName = "UiSkin")]
    public class UiSkin : ScriptableObject
    {
        [Header("Paneller ve yazılar")]
        public Sprite mainMenuBackground;
        public Sprite settingsPanel;
        public Sprite pausedTitle;

        [Header("Butonlar")]
        public Sprite startButton;
        public Sprite howToButton;
        public Sprite playButton;
        public Sprite settingsButton;
        public Sprite closeButton;

        [Header("Kaynak rozetleri")]
        public Sprite scrapBadge;
        public Sprite metalBadge;
        public Sprite electricBadge;

        [Header("Göstergeler (dolu → boş, 4 kare)")]
        public Sprite[] hungerFrames = new Sprite[0];
        public Sprite[] stressFrames = new Sprite[0];

        /// <summary>0: dolu/iyi ... 3: boş/kötü. Kare yoksa null.</summary>
        public static Sprite Frame(IReadOnlyList<Sprite> frames, int index)
        {
            if (frames == null || frames.Count == 0) return null;
            return frames[Mathf.Clamp(index, 0, frames.Count - 1)];
        }
    }
}
