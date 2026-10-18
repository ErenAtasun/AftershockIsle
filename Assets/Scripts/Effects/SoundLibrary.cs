using System;
using UnityEngine;

namespace AftershockIsle.Effects
{
    /// <summary>
    /// Oyundaki bütün seslerin atandığı yer (Assets/Resources/SoundLibrary.asset). Her olayın (SoundId) satırına bir ya da
    /// birkaç ses sürüklenir; birden fazlaysa her seferinde rastgele biri çalar. Boş satır kodla üretilen sesi kullanır.
    /// Uzun kayıtlar (kazma, yürüme) "Döngü" işaretliyse iş/yürüyüş sürdükçe döngüde çalar, bitince susar.
    /// </summary>
    [CreateAssetMenu(menuName = "Aftershock/Ses Kütüphanesi", fileName = "SoundLibrary")]
    public class SoundLibrary : ScriptableObject
    {
        public const string ResourcePath = "SoundLibrary";

        [Serializable]
        public class Slot
        {
            // Inspector'da satırın başlığı olur.
            [HideInInspector] public string name;
            public SoundId id;
            [Tooltip("Birden fazla ses atarsan her seferinde rastgele biri çalar. Boşsa kodla üretilen ses çalar.")]
            public AudioClip[] clips = Array.Empty<AudioClip>();
            [Range(0f, 2f)] public float volume = 1f;
            [Tooltip("İş (kazı, tamir) ve yürüyüş sesleri: sürdükçe döngüde çalar, bitince susar. Kapalıysa her darbede baştan çalar.")]
            public bool loop;
            [Tooltip("Uzun kayıtlar için: bu kadar saniye sonra yavaşça kısılıp durur (0 = sonuna kadar çalar).")]
            [Min(0f)] public float maxDuration;

            public bool HasClip
            {
                get
                {
                    foreach (var clip in clips) if (clip != null) return true;
                    return false;
                }
            }

            public AudioClip Pick()
            {
                int count = 0;
                foreach (var clip in clips) if (clip != null) count++;
                if (count == 0) return null;
                int pick = UnityEngine.Random.Range(0, count);
                foreach (var clip in clips)
                {
                    if (clip == null) continue;
                    if (pick-- == 0) return clip;
                }
                return null;
            }
        }

        [Header("Müzik ve Ortam (döngüde çalar)")]
        [Tooltip("Sığınakta (evde) çalan fon müziği")]
        public AudioClip houseMusic;
        [Tooltip("Sefer sahasında (dış dünya) çalan müzik / ortam")]
        public AudioClip fieldMusic;
        [Range(0f, 1f)] public float musicVolume = 0.3f;
        [Tooltip("Sürekli arkada çalan rüzgar/ortam sesi (boşsa kodla üretilen rüzgar)")]
        public AudioClip ambientLoop;
        [Range(0f, 1f)] public float ambientVolume = 0.11f;

        [Header("Efektler (her olay bir satır)")]
        public Slot[] sounds = Array.Empty<Slot>();

        public Slot Get(SoundId id)
        {
            foreach (var slot in sounds)
                if (slot != null && slot.id == id) return slot;
            return null;
        }

        static SoundLibrary cached;

        public static SoundLibrary Load()
        {
            if (cached == null) cached = Resources.Load<SoundLibrary>(ResourcePath);
            return cached;
        }

        // Her SoundId'nin satırı olsun (yeni ses türü eklenince kendiliğinden görünür), başlıklar güncel kalsın.
        void OnValidate() => EnsureSlots();

        public bool EnsureSlots()
        {
            bool changed = false;
            var ids = (SoundId[])Enum.GetValues(typeof(SoundId));
            if (sounds == null) sounds = Array.Empty<Slot>();
            foreach (var id in ids)
            {
                if (Get(id) != null) continue;
                Array.Resize(ref sounds, sounds.Length + 1);
                sounds[sounds.Length - 1] = new Slot { id = id };
                changed = true;
            }
            foreach (var slot in sounds)
            {
                if (slot == null) continue;
                string label = slot.HasClip ? slot.id.ToString() : slot.id + "  (boş: kod sesi)";
                if (slot.name != label)
                {
                    slot.name = label;
                    changed = true;
                }
            }
            return changed;
        }
    }
}
