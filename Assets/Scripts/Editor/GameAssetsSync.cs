using System.Collections.Generic;
using System.IO;
using System.Linq;
using AftershockIsle.Core;
using AftershockIsle.UI;
using UnityEditor;
using UnityEngine;

namespace AftershockIsle.EditorTools
{
    /// <summary>
    /// Assets/Models altında bir dosya eklenince, silinince ya da taşınınca karakter, saha ve arayüz kütüphanelerini yeniden kurar
    /// ve Assets/Resources/GameAssets.asset'e yazar. Oyun bu kaydı çalışma anında okuduğu için yeni bir model ya da arayüz görseli
    /// eklemek sahneyi yeniden kurmayı gerektirmez; sahne kurucu yalnızca bina düzeni değişince çalıştırılır.
    /// El ile tetiklemek için: Aftershock → Varlıkları Yenile.
    /// </summary>
    public class GameAssetsSync : AssetPostprocessor
    {
        const string WatchedRoot = "Assets/Models/";
        const string ResourcesFolder = "Assets/Resources";
        const string GameAssetsPath = ResourcesFolder + "/GameAssets.asset";
        const string UiSkinPath = "Assets/Art/UiSkin.asset";
        const string UiFolderSuffix = "/1x/";

        static bool scheduled;

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(path => path.Replace('\\', '/').StartsWith(WatchedRoot)))
                Schedule();
        }

        // Proje açılınca kayıt yoksa bir kez kur.
        [InitializeOnLoadMethod]
        static void EnsureOnLoad()
        {
            EditorApplication.delayCall += () =>
            {
                if (AssetDatabase.LoadAssetAtPath<GameAssets>(GameAssetsPath) == null) Schedule();
            };
        }

        static void Schedule()
        {
            if (scheduled) return;
            scheduled = true;
            EditorApplication.delayCall += RunWhenIdle;
        }

        // İçe aktarma ya da oyun sırasında asset yazmak güvenli değil; boş bir ana kadar bekle.
        static void RunWhenIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunWhenIdle;
                return;
            }
            scheduled = false;
            Sync();
        }

        [MenuItem("Aftershock/Varlıkları Yenile")]
        public static GameAssets Sync()
        {
            if (!AssetDatabase.IsValidFolder(ResourcesFolder)) AssetDatabase.CreateFolder("Assets", "Resources");

            var assets = AssetDatabase.LoadAssetAtPath<GameAssets>(GameAssetsPath);
            if (assets == null)
            {
                assets = ScriptableObject.CreateInstance<GameAssets>();
                AssetDatabase.CreateAsset(assets, GameAssetsPath);
            }

            assets.characters = CharacterLibraryBuilder.Build();
            assets.fieldArt = ModelSkins.BuildFieldArtLibrary();
            assets.ui = BuildUiSkin();

            EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssets();
            Debug.Log("[Varlıklar] GameAssets güncellendi: karakter, saha modelleri ve arayüz görselleri. Sahneyi yeniden kurmak gerekmez.");
            return assets;
        }

        // ---------------------------------------------------------------- Arayüz görselleri

        /// <summary>Asset N.png numaralarına göre görevler (tasarım dosyasından dışa aktarılan sıra).</summary>
        static readonly Dictionary<int, string> UiSlots = new Dictionary<int, string>
        {
            { 2, nameof(UiSkin.metalBadge) },
            { 3, nameof(UiSkin.settingsPanel) },
            { 4, nameof(UiSkin.scrapBadge) },
            { 5, nameof(UiSkin.electricBadge) },
            { 6, nameof(UiSkin.howToButton) },
            { 7, nameof(UiSkin.playButton) },
            { 8, nameof(UiSkin.startButton) },
            { 9, nameof(UiSkin.settingsButton) },
            { 10, nameof(UiSkin.closeButton) },
            { 11, nameof(UiSkin.hungerFrames) },
            { 12, nameof(UiSkin.stressFrames) },
            { 13, nameof(UiSkin.pausedTitle) },
        };

        public static UiSkin BuildUiSkin()
        {
            var skin = AssetDatabase.LoadAssetAtPath<UiSkin>(UiSkinPath);
            if (skin == null)
            {
                skin = ScriptableObject.CreateInstance<UiSkin>();
                AssetDatabase.CreateAsset(skin, UiSkinPath);
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Models" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (!path.Contains(UiFolderSuffix)) continue;

                string file = Path.GetFileNameWithoutExtension(path);
                if (!file.StartsWith("Asset ") || !int.TryParse(file.Substring(6), out int number)) continue;
                if (!UiSlots.TryGetValue(number, out string slot)) continue;

                EnsureSpriteImport(path);
                var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s => s.rect.x).ToArray();
                if (sprites.Length == 0) continue;

                switch (slot)
                {
                    case nameof(UiSkin.hungerFrames): skin.hungerFrames = sprites; break;
                    case nameof(UiSkin.stressFrames): skin.stressFrames = sprites; break;
                    default: typeof(UiSkin).GetField(slot).SetValue(skin, sprites[0]); break;
                }
            }

            EditorUtility.SetDirty(skin);
            return skin;
        }

        // Arayüz görselleri sprite olmalı ve mipmap'siz olmalı (bulanıklaşmasın). Dilimlenmiş (çoklu) sprite'lara dokunulmaz.
        static void EnsureSpriteImport(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
            if (importer.textureType == TextureImporterType.Sprite && !importer.mipmapEnabled) return;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
            }
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
    }
}
