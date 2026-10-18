using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AftershockIsle.Characters;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AftershockIsle.EditorTools
{
    /// <summary>
    /// Assets/Adventure_Character altındaki ana karakter prefabından (Man_01.prefab),
    /// Assets/RSG_DogsPack altındaki köpek modelinden (P_GermanShepherd / SK_GermanShepherd_01),
    /// Assets/Models altındaki animasyonlardan ve Assets/Low Poly Guns altındaki silahlardan
    /// CharacterLibrary asset'ini kurar. URP uyumluluğu için malzemeleri otomatik günceller.
    /// </summary>
    public static class CharacterLibraryBuilder
    {
        const string ModelRoot = "Assets/Models";
        const string GunsRoot = "Assets/Low Poly Guns/Models/Guns";
        const string LibraryPath = "Assets/Art/CharacterLibrary.asset";
        const string MaterialFolder = "Assets/Art/Materials/Models";

        public static CharacterLibrary Build()
        {
            UpgradeMaterialsToUrp();

            string mainPath = FindMainModel();
            if (mainPath == null)
            {
                Debug.LogWarning("Karakter modeli bulunamadı (Assets/Adventure_Character veya Assets/Models/**/Char/*.fbx).");
                return null;
            }

            var library = AssetDatabase.LoadAssetAtPath<CharacterLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<CharacterLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            library.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(mainPath);
            library.moveClip = null;
            library.walkClip = null;
            library.idleClip = null;
            library.walkBackClip = null;
            library.runBackClip = null;
            library.strafeLeftClip = null;
            library.strafeRightClip = null;
            library.fireClip = null;
            library.reloadClip = null;
            library.hitClip = null;
            library.jumpClip = null;
            library.tossClip = null;
            library.turnLeftClip = null;
            library.turnRightClip = null;

            // Animasyon kliplerini ara: Basic Shooter Pack veya Models klasörleri
            var animFolders = new List<string>();
            string mainFolder = Path.GetDirectoryName(mainPath).Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(mainFolder)) animFolders.Add(mainFolder);
            if (AssetDatabase.IsValidFolder("Assets/Models/Char/Basic Shooter Pack")) animFolders.Add("Assets/Models/Char/Basic Shooter Pack");
            if (AssetDatabase.IsValidFolder(ModelRoot) && !animFolders.Contains(ModelRoot)) animFolders.Add(ModelRoot);

            foreach (string guid in AssetDatabase.FindAssets("t:Model", animFolders.ToArray()))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (path == mainPath) continue;

                string lower = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                var clip = FirstClip(path);
                if (clip == null) continue;

                if (lower.Contains("idle"))
                {
                    library.idleClip ??= clip;
                }
                else if (lower.Contains("walk") && lower.Contains("back"))
                {
                    library.walkBackClip ??= clip;
                }
                else if (lower.Contains("walk"))
                {
                    library.walkClip ??= clip;
                    library.walkSpeedReference = 1.7f;
                }
                else if (lower.Contains("run") && lower.Contains("back"))
                {
                    library.runBackClip ??= clip;
                }
                else if (lower.Contains("run"))
                {
                    library.moveClip ??= clip;
                    library.moveSpeedReference = 4.1f;
                }
                else if (lower.Contains("strafe") && lower.Contains("left"))
                {
                    library.strafeLeftClip ??= clip;
                }
                else if (lower.Contains("strafe") && lower.Contains("right"))
                {
                    library.strafeRightClip ??= clip;
                }
                else if (lower.Contains("strafe"))
                {
                    if (library.strafeLeftClip == null) library.strafeLeftClip = clip;
                    else if (library.strafeRightClip == null) library.strafeRightClip = clip;
                }
                else if (lower.Contains("firing") || lower.Contains("fire") || lower.Contains("shoot"))
                {
                    library.fireClip ??= clip;
                }
                else if (lower.Contains("reload"))
                {
                    library.reloadClip ??= clip;
                }
                else if (lower.Contains("hit"))
                {
                    library.hitClip ??= clip;
                }
                else if (lower.Contains("jump"))
                {
                    library.jumpClip ??= clip;
                }
                else if (lower.Contains("toss") || lower.Contains("grenade"))
                {
                    library.tossClip ??= clip;
                }
                else if (lower.Contains("turn") && lower.Contains("left"))
                {
                    library.turnLeftClip ??= clip;
                }
                else if (lower.Contains("turn") && lower.Contains("right"))
                {
                    library.turnRightClip ??= clip;
                }
            }

            if (library.moveClip == null && library.walkClip != null)
            {
                library.moveClip = library.walkClip;
                library.moveSpeedReference = library.walkSpeedReference;
            }

            // Adventure_Character veya eski model için dokular
            var bodyTexture = FindTexture("Assets/Adventure_Character/Textures", mainPath, "Man_Body_Albedo")
                              ?? FindTexture("Assets/Adventure_Character/Textures", mainPath, "Albedo")
                              ?? FindTexture(mainFolder, mainPath, "1001_Diffuse");
            var bodyNormal = FindTexture("Assets/Adventure_Character/Textures", mainPath, "Man_Body_Normals")
                             ?? FindTexture(mainFolder, mainPath, "1001_Normal");
            var hairTexture = FindTexture("Assets/Adventure_Character/Textures", mainPath, "Man_Cloth_Up_Albedo_01")
                              ?? FindTexture(mainFolder, mainPath, "1002_Diffuse");
            var hairNormal = FindTexture("Assets/Adventure_Character/Textures", mainPath, "Man_Cloth_Up_Normals")
                             ?? FindTexture(mainFolder, mainPath, "1002_Normal");

            library.bodyMaterial = CreateOrUpdateMaterial("CharacterBody", bodyTexture, bodyNormal);
            library.hairMaterial = CreateOrUpdateMaterial("CharacterHair", hairTexture, hairNormal);
            library.hasTextures = bodyTexture != null;
            library.hasSecondaryTexture = hairTexture != null;

            // Silahlar
            library.guns = BuildGunEntries();

            // Köpek varlıkları (RSG_DogsPack)
            var (dogPrefab, dogIdle, dogWalk, dogRun) = FindDogAssets();
            library.dogPrefab = dogPrefab;
            library.dogIdleClip = dogIdle;
            library.dogWalkClip = dogWalk;
            library.dogRunClip = dogRun;
            library.dogYawOffset = 0f;

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Karakter Kütüphanesi] Hazır: {mainPath} (Köpek: {(library.dogPrefab != null ? library.dogPrefab.name : "yok")}, " +
                      $"koşu: {(library.moveClip != null ? library.moveClip.name : "yok")}, " +
                      $"yürüyüş: {(library.walkClip != null ? library.walkClip.name : "yok")}, " +
                      $"bekleme: {(library.idleClip != null ? library.idleClip.name : "sabit kare")}, " +
                      $"silah sayısı: {library.guns.Length}).");
            return library;
        }

        static string FindMainModel()
        {
            // 1. Öncelikli: Adventure_Character paketi (Man_03 eksiksiz gövde, pantolon ve ayakkabı içerir)
            if (AssetDatabase.IsValidFolder("Assets/Adventure_Character"))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Adventure_Character/Prefabs", "Assets/Adventure_Character" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                    if (path.EndsWith("Man_03.prefab", StringComparison.OrdinalIgnoreCase)) return path;
                }
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Adventure_Character/Prefabs", "Assets/Adventure_Character" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                    if (path.EndsWith("Man_04.prefab", StringComparison.OrdinalIgnoreCase)) return path;
                }
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Adventure_Character/Prefabs", "Assets/Adventure_Character" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                    if (path.Contains("Man_")) return path;
                }
            }

            // 2. Yedek: Basic Shooter Pack / Berkecan / Diğerleri
            string shooterPack = null;
            string berkecan = null;
            string fallback = null;

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ModelRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (!path.Contains("/Char/")) continue;

                bool hasMesh = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().Any();
                if (!hasMesh) continue;

                string lowerPath = path.ToLowerInvariant();
                string lowerName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (lowerPath.Contains("basic shooter pack") || lowerName.Contains("ch15"))
                {
                    shooterPack = path;
                    break;
                }
                if (lowerPath.Contains("/berkecan/")) berkecan ??= path;
                if (lowerName.Contains("karakter") || fallback == null) fallback ??= path;
            }
            return shooterPack ?? berkecan ?? fallback;
        }

        static (GameObject prefab, AnimationClip idle, AnimationClip walk, AnimationClip run) FindDogAssets()
        {
            GameObject dogPrefab = null;
            AnimationClip idle = null;
            AnimationClip walk = null;
            AnimationClip run = null;

            if (AssetDatabase.IsValidFolder("Assets/RSG_DogsPack"))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/RSG_DogsPack" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                    if (path.Contains("GermanShepherd"))
                    {
                        dogPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (dogPrefab != null) break;
                    }
                }

                if (dogPrefab == null)
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/RSG_DogsPack" }))
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                        if (path.Contains("GermanShepherd") && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                        {
                            dogPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                            if (dogPrefab != null) break;
                        }
                    }
                }

                foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/RSG_DogsPack" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                    string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                    var clip = FirstClip(path);
                    if (clip == null) continue;

                    if (name.Contains("idle") && name.Contains("breath")) idle = clip;
                    else if (name.Contains("idle") && idle == null) idle = clip;
                    else if (name.Contains("walk") && !name.Contains("turn")) walk = clip;
                    else if (name.Contains("run") && name.Contains("loop")) run = clip;
                    else if (name.Contains("run") && run == null) run = clip;
                }
            }

            return (dogPrefab, idle, walk, run);
        }

        static void UpgradeMaterialsToUrp()
        {
            var urpShader = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultShader
                : Shader.Find("Universal Render Pipeline/Lit");

            if (urpShader == null) urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null) return;

            // 1. Adventure_Character malzemeleri
            if (AssetDatabase.IsValidFolder("Assets/Adventure_Character/Materials"))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Adventure_Character/Materials" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null) continue;

                    Texture mainTex = null;
                    if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null) mainTex = mat.GetTexture("_BaseMap");
                    else if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null) mainTex = mat.GetTexture("_MainTex");

                    Texture bumpTex = null;
                    if (mat.HasProperty("_BumpMap") && mat.GetTexture("_BumpMap") != null) bumpTex = mat.GetTexture("_BumpMap");

                    Texture metallicTex = null;
                    if (mat.HasProperty("_MetallicGlossMap") && mat.GetTexture("_MetallicGlossMap") != null) metallicTex = mat.GetTexture("_MetallicGlossMap");
                    else if (mat.HasProperty("_MetallicSpecGlossMap") && mat.GetTexture("_MetallicSpecGlossMap") != null) metallicTex = mat.GetTexture("_MetallicSpecGlossMap");

                    Texture occTex = null;
                    if (mat.HasProperty("_OcclusionMap") && mat.GetTexture("_OcclusionMap") != null) occTex = mat.GetTexture("_OcclusionMap");

                    if (mat.shader != urpShader) mat.shader = urpShader;
                    if (mainTex != null) mat.SetTexture("_BaseMap", mainTex);
                    if (bumpTex != null)
                    {
                        mat.SetTexture("_BumpMap", bumpTex);
                        mat.EnableKeyword("_NORMALMAP");
                    }
                    if (metallicTex != null)
                    {
                        mat.SetTexture("_MetallicSpecGlossMap", metallicTex);
                        mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                    }
                    if (occTex != null) mat.SetTexture("_OcclusionMap", occTex);
                    mat.SetColor("_BaseColor", Color.white);
                    mat.SetFloat("_Smoothness", 0.35f);
                    EditorUtility.SetDirty(mat);
                }
            }

            // 2. RSG_DogsPack malzemeleri
            if (AssetDatabase.IsValidFolder("Assets/RSG_DogsPack/HDRP/Materials"))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/RSG_DogsPack/HDRP/Materials" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null) continue;

                    Texture mainTex = null;
                    if (mat.HasProperty("_BaseColorMap") && mat.GetTexture("_BaseColorMap") != null) mainTex = mat.GetTexture("_BaseColorMap");
                    else if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null) mainTex = mat.GetTexture("_BaseMap");
                    else if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null) mainTex = mat.GetTexture("_MainTex");

                    Texture bumpTex = null;
                    if (mat.HasProperty("_NormalMap") && mat.GetTexture("_NormalMap") != null) bumpTex = mat.GetTexture("_NormalMap");
                    else if (mat.HasProperty("_BumpMap") && mat.GetTexture("_BumpMap") != null) bumpTex = mat.GetTexture("_BumpMap");

                    Texture maskTex = null;
                    if (mat.HasProperty("_MaskMap") && mat.GetTexture("_MaskMap") != null) maskTex = mat.GetTexture("_MaskMap");

                    if (mat.shader != urpShader) mat.shader = urpShader;
                    if (mainTex != null) mat.SetTexture("_BaseMap", mainTex);
                    if (bumpTex != null)
                    {
                        mat.SetTexture("_BumpMap", bumpTex);
                        mat.EnableKeyword("_NORMALMAP");
                    }
                    if (maskTex != null)
                    {
                        mat.SetTexture("_MetallicSpecGlossMap", maskTex);
                        mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                    }
                    mat.SetColor("_BaseColor", Color.white);
                    mat.SetFloat("_Smoothness", 0.25f);

                    if (mat.name.Contains("Transparent"))
                    {
                        mat.SetFloat("_Surface", 0f);
                        mat.SetFloat("_AlphaClip", 1f);
                        mat.SetFloat("_Cutoff", 0.5f);
                        mat.EnableKeyword("_ALPHATEST_ON");
                        mat.renderQueue = 2450;
                    }

                    EditorUtility.SetDirty(mat);
                }
            }
        }

        static CharacterLibrary.GunVisualEntry[] BuildGunEntries()
        {
            var entries = new List<CharacterLibrary.GunVisualEntry>();
            if (!AssetDatabase.IsValidFolder(GunsRoot)) return entries.ToArray();

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { GunsRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                string id = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                var (kind, worldLength) = ClassifyGun(id);
                if (kind == GunModelKind.None) continue;

                string gunFolder = Path.GetDirectoryName(path).Replace('\\', '/');
                var mat = EnsureGunMaterial(gunFolder, id);
                entries.Add(new CharacterLibrary.GunVisualEntry
                {
                    id = id,
                    kind = kind,
                    prefab = prefab,
                    material = mat,
                    worldLength = worldLength
                });
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            return entries.ToArray();
        }

        static (GunModelKind kind, float length) ClassifyGun(string id)
        {
            if (id.StartsWith("pistol")) return (GunModelKind.Pistol, 0.30f);
            if (id.StartsWith("smg")) return (GunModelKind.Smg, 0.52f);
            if (id.StartsWith("shotgun")) return (GunModelKind.Shotgun, 0.84f);
            if (id.StartsWith("assault")) return (GunModelKind.AssaultRifle, 0.78f);
            if (id.StartsWith("sniper")) return (GunModelKind.SniperRifle, 0.96f);
            return (GunModelKind.None, 0.5f);
        }

        static Material EnsureGunMaterial(string gunFolder, string id)
        {
            Material mat = null;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { gunFolder }))
            {
                mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat != null) break;
            }

            Texture2D diffuse = null;
            Texture2D normal = null;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { gunFolder }))
            {
                string texPath = AssetDatabase.GUIDToAssetPath(guid);
                string lower = Path.GetFileNameWithoutExtension(texPath).ToLowerInvariant();
                if (lower.Contains("diffuse")) diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                else if (lower.Contains("normal")) normal = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            }

            var urpShader = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultShader
                : Shader.Find("Universal Render Pipeline/Lit");

            if (mat == null)
            {
                mat = CreateOrUpdateMaterial($"Gun_{id}", diffuse, normal);
                return mat;
            }

            if (urpShader != null && mat.shader != urpShader) mat.shader = urpShader;
            if (diffuse != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", diffuse);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", diffuse);
            }
            if (normal != null && mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", normal);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.35f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static AnimationClip FirstClip(string path)
        {
            var clips = new List<AnimationClip>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) clips.Add(clip);
            return clips.Count > 0 ? clips[0] : null;
        }

        static Texture2D FindTexture(string folder, string mainPath, string namePart)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                foreach (string guid in AssetDatabase.FindAssets($"{namePart} t:Texture2D", new[] { folder }))
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
            }

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(mainPath))
                if (asset is Texture2D tex && tex.name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                    return tex;

            return null;
        }

        static Material CreateOrUpdateMaterial(string materialName, Texture2D texture, Texture2D normalMap = null)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Art/Materials")) AssetDatabase.CreateFolder("Assets/Art", "Materials");
                AssetDatabase.CreateFolder("Assets/Art/Materials", "Models");
            }

            string path = $"{MaterialFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultShader
                : Shader.Find("Universal Render Pipeline/Lit");

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (shader != null && material.shader != shader)
            {
                material.shader = shader;
            }

            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (normalMap != null && material.HasProperty("_BumpMap")) material.SetTexture("_BumpMap", normalMap);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
