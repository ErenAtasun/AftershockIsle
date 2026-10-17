using System.Collections.Generic;
using System.IO;
using AftershockIsle.Characters;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AftershockIsle.EditorTools
{
    /// <summary>
    /// Assets/Models altındaki "Char" klasöründeki karakter FBX'inden CharacterLibrary asset'ini kurar.
    /// Klasör kuralları: ana model (karakter.fbx) iskelet ve hareket klibini taşır. Birden fazla karakter varsa
    /// yolu "berkecan" geçen klasördeki kullanılır (yeni karakter eskisini geçer). Yanına konan dosya adında
    /// "idle" geçen FBX bekleme, "walk" ya da "run" geçen FBX hareket klibi olarak onun yerine kullanılır
    /// (Mixamo'dan "Without Skin" indirilebilir). Dokular (Ch42_1001_Diffuse.png ...) aynı klasörde bulunursa
    /// malzemeye atanır; yoksa model düz renkle boyanır.
    /// </summary>
    public static class CharacterLibraryBuilder
    {
        const string ModelRoot = "Assets/Models";
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
            library.idleClip = null;

            // Yanındaki ek animasyon dosyaları.
            string folder = Path.GetDirectoryName(mainPath).Replace('\\', '/');
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (path == mainPath) continue;

                string lower = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                var clip = FirstClip(path);
                if (clip == null) continue;

                if (lower.Contains("idle")) library.idleClip = clip;
                else if (lower.Contains("walk") || lower.Contains("run"))
                {
                    library.moveClip = clip;
                    library.moveSpeedReference = lower.Contains("walk") ? 1.5f : 4.1f;
                }
            }

            var bodyTexture = FindTexture(folder, "1001_Diffuse");
            var hairTexture = FindTexture(folder, "1002_Diffuse");
            library.bodyMaterial = CreateOrUpdateMaterial("CharacterBody", bodyTexture);
            library.hairMaterial = CreateOrUpdateMaterial("CharacterHair", hairTexture);
            library.hasTextures = bodyTexture != null;

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log($"Karakter kütüphanesi hazır: {mainPath} (hareket: {(library.moveClip != null ? library.moveClip.name : "yok")}, " +
                      $"bekleme: {(library.idleClip != null ? library.idleClip.name : "hareket klibinin sabit karesi")}, doku: {(library.hasTextures ? "var" : "yok, düz renk")}).");
            return library;
        }

        static string FindMainModel()
        {
            string fallback = null;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ModelRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (!path.Contains("/Char/") || !Path.GetFileNameWithoutExtension(path).ToLowerInvariant().Contains("karakter")) continue;

                if (path.ToLowerInvariant().Contains("/berkecan/")) return path;
                fallback ??= path;
            }
            return fallback;
        }

        // FBX'in içindeki gerçek klip ("__preview__" önizlemeleri hariç).
        static AnimationClip FirstClip(string path)
        {
            var clips = new List<AnimationClip>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) clips.Add(clip);
            return clips.Count > 0 ? clips[0] : null;
        }

        static Texture2D FindTexture(string folder, string namePart)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{namePart} t:Texture2D", new[] { folder }))
                return AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
            return null;
        }

        static Material CreateOrUpdateMaterial(string materialName, Texture2D texture)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Art/Materials")) AssetDatabase.CreateFolder("Assets/Art", "Materials");
                AssetDatabase.CreateFolder("Assets/Art/Materials", "Models");
            }

            string path = $"{MaterialFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = GraphicsSettings.currentRenderPipeline != null
                    ? GraphicsSettings.currentRenderPipeline.defaultShader
                    : Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
