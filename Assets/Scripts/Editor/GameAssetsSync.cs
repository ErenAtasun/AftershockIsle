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
        const string WatchedGunsRoot = "Assets/Low Poly Guns/";
        const string ResourcesFolder = "Assets/Resources";
        const string GameAssetsPath = ResourcesFolder + "/GameAssets.asset";
        const string UiSkinPath = "Assets/Art/UiSkin.asset";
        const string UiFolderSuffix = "/1x/";

        static bool scheduled;

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(path =>
                {
                    string p = path.Replace('\\', '/');
                    return (p.StartsWith(WatchedRoot) || p.StartsWith(WatchedGunsRoot)) && IsLibrarySource(p);
                }))
            {
                Schedule();
            }
        }

        // Proje açılınca ya da yeni karakter/silah/arayüz paketleri henüz işlenmemişse bir kez kur.
        [InitializeOnLoadMethod]
        static void EnsureOnLoad()
        {
            EditorApplication.delayCall += () =>
            {
                    var assets = AssetDatabase.LoadAssetAtPath<GameAssets>(GameAssetsPath);
                    if (assets == null || assets.characters == null || assets.characters.prefab == null ||
                        assets.characters.guns == null || assets.characters.guns.Length == 0 ||
                        !AssetDatabase.GetAssetPath(assets.characters.prefab).Replace('\\', '/').Contains("Basic Shooter Pack") ||
                        assets.ui == null || assets.ui.mainMenuBackground == null || assets.ui.worldBackground == null ||
                        assets.ui.skillTreeBackground == null || assets.ui.cityMap == null || assets.ui.inventorySheet == null ||
                        assets.ui.pbhLogo == null)
                    {
                        Schedule();
                    }
            };
        }

        // Yalnızca kütüphaneyi gerçekten değiştiren dosyalar (model, görsel, prefab) yeniden kurmayı tetikler.
        // Malzeme (.mat) ve diğer asset yazımları sayılmaz: Sync, ModelManager'ı yeniler, o da bina malzemelerini
        // Assets/Models altında günceller; bunlar tetikleseydi import sonsuz döngüye girerdi.
        static readonly string[] LibraryExtensions = { ".fbx", ".obj", ".blend", ".prefab", ".png", ".jpg", ".jpeg", ".tga", ".psd" };

        static bool IsLibrarySource(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return LibraryExtensions.Contains(ext);
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
            if (AftershockIsle.Building.ModelManager.Instance != null)
                AftershockIsle.Building.ModelManager.Instance.RefreshAll();
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
                string file = Path.GetFileNameWithoutExtension(path);
                string lowerFile = file.ToLowerInvariant();

                // Pencere görselleri ve stüdyo logosu (dosya adıyla eşleşir).
                Sprite named = null;
                switch (lowerFile)
                {
                    case "city_map": named = LoadUiSprite(path); if (named != null) skin.cityMap = named; break;
                    case "inventory_sheet": named = LoadUiSprite(path); if (named != null) skin.inventorySheet = named; break;
                    case "pbh_logo": named = LoadUiSprite(path); if (named != null) skin.pbhLogo = named; break;
                    case "pbh_logo_transparent": named = LoadUiSprite(path); if (named != null) skin.pbhLogoTransparent = named; break;
                }
                if (lowerFile == "city_map" || lowerFile == "inventory_sheet" || lowerFile == "pbh_logo" || lowerFile == "pbh_logo_transparent") continue;

                if (lowerFile.Contains("anabanner") || lowerFile.Contains("anasayfa") || lowerFile.Contains("mainmenu"))
                {
                    EnsureSpriteImport(path, false);
                    var bannerSprite = AssetDatabase.LoadAssetAtPath<Sprite>(path)
                                       ?? AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
                    if (bannerSprite != null) skin.mainMenuBackground = bannerSprite;
                    continue;
                }

                if (lowerFile.Contains("arkaplan") || lowerFile.Contains("background") || lowerFile.Contains("backdrop"))
                {
                    EnsureSpriteImport(path, false);
                    var bannerSprite = AssetDatabase.LoadAssetAtPath<Sprite>(path)
                                       ?? AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
                    if (bannerSprite != null) skin.worldBackground = bannerSprite;
                    continue;
                }

                if (lowerFile.Contains("skill") || lowerFile.Contains("yetenek"))
                {
                    EnsureSpriteImport(path, true);
                    var bannerSprite = AssetDatabase.LoadAssetAtPath<Sprite>(path)
                                       ?? AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
                    if (bannerSprite != null) skin.skillTreeBackground = bannerSprite;
                    continue;
                }

                if (!path.Contains(UiFolderSuffix)) continue;

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

            if (skin.worldBackground == null && skin.mainMenuBackground != null)
                skin.worldBackground = skin.mainMenuBackground;
            if (skin.mainMenuBackground == null && skin.worldBackground != null)
                skin.mainMenuBackground = skin.worldBackground;

            EditorUtility.SetDirty(skin);
            return skin;
        }

        static Sprite LoadUiSprite(string path)
        {
            EnsureSpriteImport(path, false);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path)
                   ?? AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
        }

        // Arayüz görselleri sprite olmalı ve mipmap'siz olmalı (bulanıklaşmasın). Dilimlenmiş (çoklu) sprite'lara dokunulmaz.
        static void EnsureSpriteImport(string path, bool requireReadable = false)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
            if (importer.textureType == TextureImporterType.Sprite && !importer.mipmapEnabled &&
                (!requireReadable || importer.isReadable)) return;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
            }
            if (requireReadable) importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
    }
}
