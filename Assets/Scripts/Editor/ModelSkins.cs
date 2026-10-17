using System;
using System.Collections.Generic;
using AftershockIsle.Expedition;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AftershockIsle.EditorTools
{
    public enum ModelKey { Chair, Pipe, Bucket, Dresser, Crate, Table, Rubble, Bed, Extinguisher }

    public enum SkinFit
    {
        /// <summary>Modeli orantılı büyütür; yüksekliği verilen değere eşitler.</summary>
        Height,
        /// <summary>Modeli orantılı büyütür; genişliği (X) verilen değere eşitler.</summary>
        Width,
        /// <summary>Modeli kutunun üç boyutuna da oturacak şekilde gerer (moloz yığınları için).</summary>
        Stretch,
    }

    /// <summary>
    /// Greybox kutuların üstüne gerçek FBX modelleri giydirir. Oyun mantığı kutuda kalır (collider, Interactable);
    /// kutunun görseli kapatılır, model ona göre ölçeklenip yerleştirilir. Moloz kazılınca kutu küçüldüğü için
    /// model de onunla birlikte alçalır.
    /// </summary>
    public static class ModelSkins
    {
        const string ModelRoot = "Assets/Models";
        const string MaterialFolder = "Assets/Art/Materials/Models";

        /// <summary>
        /// Modellerin ön yüzü dosyada +Z'ye bakıyor; kamera -Z'den baktığı için ön yüzü olan
        /// modellere bu kadar dönüş verilir. Yön yanlış görünürse sadece buradaki sayı değişir.
        /// </summary>
        public const float FrontToCamera = 180f;

        static readonly Dictionary<ModelKey, Material> materials = new Dictionary<ModelKey, Material>();

        // ---------------------------------------------------------------- Giydirme

        /// <summary>
        /// Var olan bir kutuya model giydirir. size: Height/Width modunda hedef ölçü (metre).
        /// centered: duvara monte nesneler (boru) kutu merkezine, diğerleri kutunun tabanına hizalanır.
        /// </summary>
        public static GameObject Skin(GameObject box, ModelKey key, SkinFit fit, float size = 1f, float yaw = 0f, bool centered = false)
        {
            var prefab = LoadModel(key);
            if (prefab == null)
            {
                Debug.LogWarning($"Model bulunamadı: {key}. Kutu olarak bırakıldı.");
                return null;
            }

            var boxScale = box.transform.lossyScale;

            // Pivot, kutunun ölçeğini iptal eder; böylece model çarpık (shear) olmadan döndürülüp ölçeklenebilir.
            // Kutunun Y'si sonradan küçülürse (moloz kazılırken) pivot ve model de aynı oranda alçalır.
            var pivot = new GameObject("Model").transform;
            pivot.SetParent(box.transform, false);
            pivot.localScale = new Vector3(1f / boxScale.x, 1f / boxScale.y, 1f / boxScale.z);

            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, pivot);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            model.transform.localScale = Vector3.one;
            ApplyMaterial(model, key);

            var natural = WorldBounds(model).size;
            switch (fit)
            {
                case SkinFit.Height: model.transform.localScale = Vector3.one * (size / natural.y); break;
                case SkinFit.Width: model.transform.localScale = Vector3.one * (size / natural.x); break;
                default:
                    model.transform.localScale = new Vector3(boxScale.x / natural.x, boxScale.y / natural.y, boxScale.z / natural.z);
                    break;
            }

            var bounds = WorldBounds(model);
            var targetPoint = centered ? box.transform.position : box.transform.position + Vector3.down * (boxScale.y * 0.5f);
            var modelPoint = centered ? bounds.center : new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            model.transform.position += targetPoint - modelPoint;

            // Tıklama alanı modele uysun: kutunun collider'ı kapanır, modelin sınırları kadar yenisi pivotta açılır.
            foreach (var collider in box.GetComponents<Collider>()) collider.enabled = false;
            var box2 = pivot.gameObject.AddComponent<BoxCollider>();
            bounds = WorldBounds(model);
            box2.center = pivot.InverseTransformPoint(bounds.center);
            box2.size = bounds.size;

            var boxRenderer = box.GetComponent<MeshRenderer>();
            if (boxRenderer != null) boxRenderer.enabled = false;
            return model;
        }

        /// <summary>Etkileşimsiz süs modeli. Verilen noktaya (taban merkezi) yüksekliğe göre yerleştirilir.</summary>
        public static GameObject Decorate(Transform parent, string name, ModelKey key, Vector3 bottomCenter, float height, float yaw = 0f)
        {
            var prefab = LoadModel(key);
            if (prefab == null) return null;

            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.position = bottomCenter;

            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            ApplyMaterial(model, key);

            float natural = WorldBounds(model).size.y;
            model.transform.localScale = Vector3.one * (height / natural);

            var bounds = WorldBounds(model);
            model.transform.position += bottomCenter - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            return root.gameObject;
        }

        // ---------------------------------------------------------------- Dış saha kütüphanesi

        const string FieldArtPath = "Assets/Art/FieldArtLibrary.asset";

        /// <summary>
        /// Dış sahanın çalışma anında kullandığı model kütüphanesini oluşturur ya da günceller
        /// (saha sefer başında kurulduğu için modelleri prefab referansıyla bilmesi gerekir).
        /// </summary>
        public static FieldArtLibrary BuildFieldArtLibrary()
        {
            EnsureMaterialFolder();
            var library = AssetDatabase.LoadAssetAtPath<FieldArtLibrary>(FieldArtPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<FieldArtLibrary>();
                AssetDatabase.CreateAsset(library, FieldArtPath);
            }

            var entries = new List<FieldArtLibrary.Entry>();
            foreach (ArtKey art in Enum.GetValues(typeof(ArtKey)))
            {
                var key = ToModelKey(art);
                var prefab = LoadModel(key);
                if (prefab == null)
                {
                    Debug.LogWarning($"Saha modeli bulunamadı: {art}. Sahada kutu olarak kalacak.");
                    continue;
                }
                entries.Add(new FieldArtLibrary.Entry { key = art, prefab = prefab, material = MaterialFor(key) });
            }

            library.entries = entries.ToArray();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        static ModelKey ToModelKey(ArtKey art) => art switch
        {
            ArtKey.Crate => ModelKey.Crate,
            ArtKey.Dresser => ModelKey.Dresser,
            ArtKey.Table => ModelKey.Table,
            ArtKey.Bed => ModelKey.Bed,
            ArtKey.Chair => ModelKey.Chair,
            ArtKey.Rubble => ModelKey.Rubble,
            ArtKey.Bucket => ModelKey.Bucket,
            _ => ModelKey.Extinguisher,
        };

        // ---------------------------------------------------------------- Bulma ve malzeme

        static GameObject LoadModel(ModelKey key)
        {
            foreach (string path in AssetPaths("t:Model"))
                if (MatchesModel(key, path)) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return null;
        }

        static bool MatchesModel(ModelKey key, string path) => key switch
        {
            ModelKey.Chair => path.EndsWith("/modeller/sandalye/afet.fbx"),
            ModelKey.Pipe => path.Contains("/borular/"),
            ModelKey.Bucket => path.Contains("/cop/"),
            ModelKey.Dresser => path.Contains("/komodin/"),
            ModelKey.Crate => path.Contains("/kutu/"),
            ModelKey.Table => path.Contains("/masa/"),
            ModelKey.Rubble => path.Contains("/moloz/"),
            ModelKey.Bed => path.Contains("/yatak/"),
            _ => path.EndsWith("ntup.fbx"), // yangın tüpü: klasör adındaki Türkçe karakter bozuk görünebiliyor
        };

        /// <summary>
        /// FBX'lerin içindeki doku yolları eski bilgisayardan kalma ve yanlış (ör. yangın tüpü), bu yüzden
        /// dokuyu klasörden bulup malzemeyi kendimiz atıyoruz. Aynı atlası paylaşan modeller aynı malzemeyi kullanır.
        /// </summary>
        static Material MaterialFor(ModelKey key)
        {
            if (materials.TryGetValue(key, out var cached) && cached != null) return cached;

            string materialName;
            string texturePath;
            switch (key)
            {
                case ModelKey.Dresser: case ModelKey.Crate: case ModelKey.Bed:
                    materialName = "ModelAtlasB"; texturePath = FindTexture("/yatak/Yatak_Doku.png"); break;
                case ModelKey.Pipe:
                    materialName = "ModelRust"; texturePath = FindTexture("tex_rust.png"); break;
                case ModelKey.Extinguisher:
                    materialName = "ModelExtinguisher"; texturePath = FindTexture("ntup_Doku.png"); break;
                default:
                    materialName = "ModelAtlasA"; texturePath = FindTexture("/modeller/sandalye/sandalye_Doku.png"); break;
            }

            EnsureMaterialFolder();
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

            var texture = texturePath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath) : null;
            if (texture == null) Debug.LogWarning($"{materialName}: doku bulunamadı ({texturePath}).");

            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(material);

            materials[key] = material;
            return material;
        }

        static void ApplyMaterial(GameObject model, ModelKey key)
        {
            var material = MaterialFor(key);
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                var slots = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < slots.Length; i++) slots[i] = material;
                renderer.sharedMaterials = slots;
            }
        }

        static string FindTexture(string pathEnd)
        {
            foreach (string path in AssetPaths("t:Texture2D"))
                if (path.EndsWith(pathEnd)) return path;
            return null;
        }

        static IEnumerable<string> AssetPaths(string filter)
        {
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { ModelRoot }))
                yield return AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
        }

        static void EnsureMaterialFolder()
        {
            if (AssetDatabase.IsValidFolder(MaterialFolder)) return;
            if (!AssetDatabase.IsValidFolder("Assets/Art")) AssetDatabase.CreateFolder("Assets", "Art");
            if (!AssetDatabase.IsValidFolder("Assets/Art/Materials")) AssetDatabase.CreateFolder("Assets/Art", "Materials");
            AssetDatabase.CreateFolder("Assets/Art/Materials", "Models");
        }

        static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
