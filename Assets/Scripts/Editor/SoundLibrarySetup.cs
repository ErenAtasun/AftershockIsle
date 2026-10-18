using System.Collections.Generic;
using System.IO;
using AftershockIsle.Effects;
using UnityEditor;
using UnityEngine;

namespace AftershockIsle.EditorTools
{
    /// <summary>
    /// Ses Kütüphanesi (Assets/Resources/SoundLibrary.asset) yoksa kurar ve Assets/Sound'daki dosyaları adlarına göre
    /// bir kez uygun satırlara yerleştirir. Sonrası senin: satırlara ses sürükle, sesleri değiştir.
    /// Menü: Aftershock → Ses Kütüphanesini Aç.
    /// </summary>
    [InitializeOnLoad]
    static class SoundLibrarySetup
    {
        const string AssetPath = "Assets/Resources/" + SoundLibrary.ResourcePath + ".asset";
        const string SoundFolder = "Assets/Sound";

        // Dosya adı (uzantısız) → olay. Döngü: iş/yürüyüş sürdükçe çalar. Süre: uzun kayıt bu kadar saniyede kısılır.
        static readonly (string file, SoundId id, bool loop, float maxDuration)[] Defaults =
        {
            ("deprem", SoundId.AftershockImpact, false, 0f),
            ("emergency", SoundId.AftershockWarning, false, 8f),
            ("envanteracma", SoundId.InventoryOpen, false, 0f),
            ("evdencikgir", SoundId.DoorTransition, false, 4f),
            ("fail", SoundId.Fail, false, 0f),
            ("haritaacma", SoundId.MapOpen, false, 0f),
            ("harita2", SoundId.MapOpen, false, 0f),
            ("kazma_demir", SoundId.DigMetal, true, 0f),
            ("kazma_moloz", SoundId.Dig, true, 0f),
            ("kürek", SoundId.Dig, true, 0f),
            ("materyaltoplama", SoundId.Pickup, false, 0f),
            ("medkit", SoundId.Medkit, false, 3f),
            ("yurume", SoundId.Footstep, true, 0f),
        };

        static SoundLibrarySetup()
        {
            EditorApplication.delayCall += () => EnsureLibrary();
        }

        [MenuItem("Aftershock/Ses Kütüphanesini Aç")]
        static void Open()
        {
            var library = EnsureLibrary();
            if (library == null) return;
            Selection.activeObject = library;
            EditorGUIUtility.PingObject(library);
        }

        static SoundLibrary EnsureLibrary()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return null;

            var library = AssetDatabase.LoadAssetAtPath<SoundLibrary>(AssetPath);
            if (library != null)
            {
                // Yeni ses türleri eklenince satırları görünsün.
                if (library.EnsureSlots())
                {
                    EditorUtility.SetDirty(library);
                    AssetDatabase.SaveAssets();
                }
                return library;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            library = ScriptableObject.CreateInstance<SoundLibrary>();
            library.EnsureSlots();
            AssignDefaults(library);
            library.EnsureSlots();
            AssetDatabase.CreateAsset(library, AssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Ses] Ses Kütüphanesi kuruldu: {AssetPath}. Assets/Sound dosyaları satırlarına yerleştirildi; " +
                      "değiştirmek için Aftershock → Ses Kütüphanesini Aç.", library);
            return library;
        }

        static void AssignDefaults(SoundLibrary library)
        {
            var byName = new Dictionary<string, AudioClip>();
            if (AssetDatabase.IsValidFolder(SoundFolder))
            {
                foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { SoundFolder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (clip != null) byName[Path.GetFileNameWithoutExtension(path).ToLowerInvariant()] = clip;
                }
            }

            foreach (var (file, id, loop, maxDuration) in Defaults)
            {
                if (!byName.TryGetValue(file.ToLowerInvariant(), out var clip)) continue;
                var slot = library.Get(id);
                if (slot == null) continue;
                var list = new List<AudioClip>(slot.clips) { clip };
                slot.clips = list.ToArray();
                slot.loop = loop;
                if (maxDuration > 0f) slot.maxDuration = maxDuration;
            }

            if (byName.TryGetValue("fonmuzik", out var house)) library.houseMusic = house;
            if (byName.TryGetValue("disdunya", out var field)) library.fieldMusic = field;
        }
    }
}
