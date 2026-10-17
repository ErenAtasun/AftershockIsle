using UnityEngine;

namespace AftershockIsle.Core
{
    /// <summary>Bir ayar değişti; dinleyen sistemler (kamera, ses) yeni değeri GameSettings'ten okur.</summary>
    public readonly struct SettingsChanged { }

    /// <summary>
    /// Oyuncu ayarları (ses, kamera açısı, tam ekran). PlayerPrefs'te saklanır, oyun açılırken uygulanır;
    /// her değişiklik EventBus üzerinden SettingsChanged olarak duyurulur.
    /// </summary>
    public static class GameSettings
    {
        const string VolumeKey = "settings.volume";
        const string PitchKey = "settings.cameraPitch";
        const string FullscreenKey = "settings.fullscreen";
        const string UiScaleKey = "settings.uiScale";

        public const float MinPitch = 10f;
        public const float MaxPitch = 35f;
        public const float MinUiScale = 0.7f;
        public const float MaxUiScale = 1.4f;

        static bool loaded;
        static float volume = 0.8f;
        static float cameraPitch = 20f;
        static bool fullscreen = true;
        static float uiScale = 1f;

        /// <summary>Ana ses seviyesi (0–1). Ses sistemi gelince AudioListener üzerinden her şeyi etkiler.</summary>
        public static float Volume
        {
            get { EnsureLoaded(); return volume; }
            set { EnsureLoaded(); volume = Mathf.Clamp01(value); Save(); }
        }

        /// <summary>Kameranın aşağı bakış açısı (derece); sığınak ve sahada aynı.</summary>
        public static float CameraPitch
        {
            get { EnsureLoaded(); return cameraPitch; }
            set { EnsureLoaded(); cameraPitch = Mathf.Clamp(value, MinPitch, MaxPitch); Save(); }
        }

        /// <summary>Arayüz boyutu çarpanı (1 = varsayılan). Bütün arayüz (göstergeler, menüler, pencereler, eski paneller) birlikte ölçeklenir.</summary>
        public static float UiScale
        {
            get { EnsureLoaded(); return uiScale; }
            set { EnsureLoaded(); uiScale = Mathf.Clamp(Mathf.Round(value * 20f) / 20f, MinUiScale, MaxUiScale); Save(); }
        }

        public static bool Fullscreen
        {
            get { EnsureLoaded(); return fullscreen; }
            set { EnsureLoaded(); fullscreen = value; Save(); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplyOnStartup()
        {
            loaded = false;
            EnsureLoaded();
            Apply();
        }

        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            volume = PlayerPrefs.GetFloat(VolumeKey, 0.8f);
            cameraPitch = PlayerPrefs.GetFloat(PitchKey, 20f);
            fullscreen = PlayerPrefs.GetInt(FullscreenKey, 1) == 1;
            uiScale = Mathf.Clamp(PlayerPrefs.GetFloat(UiScaleKey, 1f), MinUiScale, MaxUiScale);
        }

        static void Save()
        {
            PlayerPrefs.SetFloat(VolumeKey, volume);
            PlayerPrefs.SetFloat(PitchKey, cameraPitch);
            PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0);
            PlayerPrefs.SetFloat(UiScaleKey, uiScale);
            PlayerPrefs.Save();
            Apply();
            EventBus.Publish(new SettingsChanged());
        }

        static void Apply()
        {
            AudioListener.volume = volume;
#if !UNITY_EDITOR
            if (Screen.fullScreen != fullscreen) Screen.fullScreen = fullscreen;
#endif
        }
    }
}
