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
    /// Assets/Models altındaki "Char" klasöründeki karakter FBX'inden (Basic Shooter Pack / Ch15_nonPBR.fbx vb.)
    /// ve Assets/Low Poly Guns altındaki silah modellerinden CharacterLibrary asset'ini kurar.
    /// </summary>
    public static class CharacterLibraryBuilder
    {
        const string ModelRoot = "Assets/Models";
        const string GunsRoot = "Assets/Low Poly Guns/Models/Guns";
        const string LibraryPath = "Assets/Art/CharacterLibrary.asset";
        const string MaterialFolder = "Assets/Art/Materials/Models";

        public static CharacterLibrary Build()
        {
            string mainPath = FindMainModel();
            if (mainPath == null)
            {
                Debug.LogWarning("Karakter modeli bulunamadı (Assets/Models/**/Char/*.fbx). Karakterler kapsül olarak kalacak.");
                return null;
            }

            var library = AssetDatabase.LoadAssetAtPath<CharacterLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<CharacterLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            library.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(mainPath);
            library.moveClip = FirstClip(mainPath);
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

            // Yanındaki ek animasyon dosyaları (Basic Shooter Pack vb.).
            string folder = Path.GetDirectoryName(mainPath).Replace('\\', '/');
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (path == mainPath) continue;

                string lower = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                var clip = FirstClip(path);
                if (clip == null) continue;

                if (lower.Contains("idle"))
                {
                    library.idleClip = clip;
                }
                else if (lower.Contains("walk") && lower.Contains("back"))
                {
                    library.walkBackClip = clip;
                }
                else if (lower.Contains("walk"))
                {
                    library.walkClip = clip;
                    library.walkSpeedReference = 1.7f;
                }
                else if (lower.Contains("run") && lower.Contains("back"))
                {
                    library.runBackClip = clip;
                }
                else if (lower.Contains("run"))
                {
                    library.moveClip = clip;
                    library.moveSpeedReference = 4.1f;
                }
                else if (lower.Contains("strafe") && lower.Contains("left"))
                {
                    library.strafeLeftClip = clip;
                }
                else if (lower.Contains("strafe") && lower.Contains("right"))
                {
                    library.strafeRightClip = clip;
                }
                else if (lower.Contains("strafe"))
                {
                    if (library.strafeLeftClip == null) library.strafeLeftClip = clip;
                    else if (library.strafeRightClip == null) library.strafeRightClip = clip;
                }
                else if (lower.Contains("firing") || lower.Contains("fire") || lower.Contains("shoot"))
                {
                    library.fireClip = clip;
                }
                else if (lower.Contains("reload"))
                {
                    library.reloadClip = clip;
                }
                else if (lower.Contains("hit"))
                {
                    library.hitClip = clip;
                }
                else if (lower.Contains("jump"))
                {
                    library.jumpClip = clip;
                }
                else if (lower.Contains("toss") || lower.Contains("grenade"))
                {
                    library.tossClip = clip;
                }
                else if (lower.Contains("turn") && lower.Contains("left"))
                {
                    library.turnLeftClip = clip;
                }
                else if (lower.Contains("turn") && lower.Contains("right"))
                {
                    library.turnRightClip = clip;
                }
            }

            if (library.moveClip == null && library.walkClip != null)
            {
                library.moveClip = library.walkClip;
                library.moveSpeedReference = library.walkSpeedReference;
            }

            var bodyTexture = FindTexture(folder, mainPath, "1001_Diffuse");
            var bodyNormal = FindTexture(folder, mainPath, "1001_Normal");
            var hairTexture = FindTexture(folder, mainPath, "1002_Diffuse");
            var hairNormal = FindTexture(folder, mainPath, "1002_Normal");
            library.bodyMaterial = CreateOrUpdateMaterial("CharacterBody", bodyTexture, bodyNormal);
            library.hairMaterial = CreateOrUpdateMaterial("CharacterHair", hairTexture, hairNormal);
            library.hasTextures = bodyTexture != null;
            library.hasSecondaryTexture = hairTexture != null;

            library.guns = BuildGunEntries();

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log($"Karakter kütüphanesi hazır: {mainPath} (koşu: {(library.moveClip != null ? library.moveClip.name : "yok")}, " +
                      $"yürüyüş: {(library.walkClip != null ? library.walkClip.name : "yok")}, " +
                      $"bekleme: {(library.idleClip != null ? library.idleClip.name : "sabit kare")}, " +
                      $"silah sayısı: {library.guns.Length}).");
            return library;
        }

        static string FindMainModel()
        {
            string shooterPack = null;
            string berkecan = null;
            string fallback = null;

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ModelRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (!path.Contains("/Char/")) continue;

                // Sadece içinde SkinnedMeshRenderer / Mesh bulunan ana karakter FBX'lerini seç (Without Skin animasyonlarını atla).
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
                : Shader.Find("Standard");

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

        // FBX'in içindeki gerçek klip ("__preview__" önizlemeleri hariç).
        static AnimationClip FirstClip(string path)
        {
            var clips = new List<AnimationClip>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) clips.Add(clip);
            return clips.Count > 0 ? clips[0] : null;
        }

        static Texture2D FindTexture(string folder, string mainPath, string namePart)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{namePart} t:Texture2D", new[] { folder }))
                return AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));

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
                : Shader.Find("Standard");

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
