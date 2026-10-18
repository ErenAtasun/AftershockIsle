using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AftershockIsle.Expedition;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AftershockIsle.EditorTools
{
    /// <summary>
    /// Blender'da tasarlanan dış mekan parçalarını (Assets/Models/Outdoor/*.fbx) oyuna bağlar: dokuları ve malzemeleri kurar,
    /// FBX içe aktarma ayarlarını yapar (malzeme eşlemesi, ölçek kontrolü), ön yüzü kameraya (-Z) bakacak şekilde prefab'lar
    /// üretir ve Resources/OutdoorKit.asset'i doldurur. Proje açılınca kit eksik ya da eskiyse kendiliğinden çalışır;
    /// elle: Aftershock → Dış Mekan Kitini Kur.
    /// </summary>
    public static class OutdoorKitBuilder
    {
        const string ModelRoot = "Assets/Models/Outdoor";
        const string TextureRoot = ModelRoot + "/Textures";
        const string ManifestPath = ModelRoot + "/outdoor_manifest.json";
        const string MaterialFolder = "Assets/Art/Materials/Outdoor";
        const string PrefabFolder = "Assets/Prefabs/Outdoor";
        const string KitPath = "Assets/Resources/OutdoorKit.asset";

        // Blender'daki malzeme adları (kit_core.MATS) ve kullandıkları doku.
        static readonly (string slot, string texture)[] Slots =
        {
            ("pal", "outdoor_palette.png"),
            ("asphalt", "outdoor_asphalt.png"),
            ("sidewalk", "outdoor_sidewalk.png"),
            ("concrete", "outdoor_concrete.png"),
            ("brick", "outdoor_brick.png"),
            ("plaster_beige", "outdoor_plaster_beige.png"),
            ("plaster_grey", "outdoor_plaster_grey.png"),
        };

        [Serializable]
        class Manifest { public List<Item> items = new List<Item>(); }

        [Serializable]
        class Item
        {
            public string name;
            public string category;
            public int zbias;
            public List<float> size = new List<float>();
            public float facade;
        }

        [MenuItem("Aftershock/Dış Mekan Kitini Kur")]
        public static void Menu() => BuildKit(true);

        static double waitStarted;

        [InitializeOnLoadMethod]
        static void EnsureOnLoad()
        {
            waitStarted = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        // Derleme / içe aktarma bitince bir kez çalışır (en çok 10 dakika bekler).
        static void Poll()
        {
            if (Application.isPlaying || EditorApplication.timeSinceStartup - waitStarted > 600.0)
            {
                EditorApplication.update -= Poll;
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= Poll;

            if (!File.Exists(ManifestPath)) return;

            var kit = AssetDatabase.LoadAssetAtPath<OutdoorKit>(KitPath);
            if (kit != null && kit.HasContent && kit.sourceHash == SourceHash()) return;
            try { BuildKit(false); }
            catch (Exception exception) { Debug.LogWarning($"Dış mekan kiti kurulamadı: {exception}"); }
        }

        static string SourceHash()
        {
            var builder = new StringBuilder();
            if (File.Exists(ManifestPath)) builder.Append(File.GetLastWriteTimeUtc(ManifestPath).Ticks);
            string root = Path.Combine(Directory.GetCurrentDirectory(), ModelRoot);
            if (Directory.Exists(root))
            {
                long newest = 0;
                int count = 0;
                foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    newest = Math.Max(newest, File.GetLastWriteTimeUtc(file).Ticks);
                    count++;
                }
                builder.Append('|').Append(count).Append('|').Append(newest);
            }
            builder.Append("|v2");   // kurucu ayarları değişince artır: kit yeniden kurulur
            return builder.ToString();
        }

        public static void BuildKit(bool verbose)
        {
            if (!File.Exists(ManifestPath))
            {
                if (verbose) Debug.LogWarning($"Manifest yok: {ManifestPath}. Önce Blender'dan dışa aktar.");
                return;
            }

            AssetDatabase.Refresh();
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.items.Count == 0)
            {
                Debug.LogWarning("Dış mekan manifesti boş.");
                return;
            }

            EnsureFolder("Assets/Art/Materials", "Outdoor");
            EnsureFolder("Assets", "Prefabs");
            EnsureFolder("Assets/Prefabs", "Outdoor");
            EnsureFolder("Assets", "Resources");

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var slot in Slots) ConfigureTexture($"{TextureRoot}/{slot.texture}", slot.slot == "pal");
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.Refresh();

            var materials = new Dictionary<string, Material>();
            foreach (var slot in Slots) materials[slot.slot] = CreateMaterial(slot.slot, $"{TextureRoot}/{slot.texture}");

            // FBX içe aktarma: malzemeleri kendi malzemelerimizle eşle.
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var item in manifest.items) ConfigureModel($"{ModelRoot}/{item.name}.fbx", materials, 1f);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.Refresh();

            // Ölçek kontrolü: manifestteki boyutla içe aktarılan boyut arasında 100 kat fark varsa ayarla.
            int rescaled = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var item in manifest.items)
                {
                    string path = $"{ModelRoot}/{item.name}.fbx";
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (model == null) continue;

                    float expected = new Vector3(item.size[0], item.size[2], item.size[1]).magnitude;
                    float actual = MeasureBounds(model).size.magnitude;
                    if (expected <= 0.001f || actual <= 0.0001f) continue;

                    float ratio = actual / expected;
                    if (ratio > 0.5f && ratio < 2f) continue;

                    // En yakın 10'un kuvvetiyle düzelt (cm/m karışıklığı).
                    float correction = Mathf.Pow(10f, Mathf.Round(-Mathf.Log10(ratio)));
                    ConfigureModel(path, materials, correction);
                    rescaled++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            if (rescaled > 0) AssetDatabase.Refresh();

            // Yön: tüm parçalar aynı kuralla dışa aktarıldığı için cephesi/kütlesi "ön"de olan parçalardan yön okunur.
            float flipScore = 0f;
            foreach (var item in manifest.items)
            {
                if (item.zbias == 0) continue;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelRoot}/{item.name}.fbx");
                if (model == null) continue;
                float z = MeasureBounds(model).center.z;
                if (Mathf.Abs(z) > 0.05f) flipScore += item.zbias * z;
            }
            bool flip = flipScore < 0f;

            var entries = new List<OutdoorKit.Entry>();
            foreach (var item in manifest.items)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelRoot}/{item.name}.fbx");
                if (model == null)
                {
                    Debug.LogWarning($"Model bulunamadı: {item.name}");
                    continue;
                }

                var prefab = SavePrefab(item, model, flip && item.zbias != 0);
                var size = MeasureBounds(prefab).size;
                entries.Add(new OutdoorKit.Entry
                {
                    name = item.name,
                    category = ParseCategory(item.category),
                    prefab = prefab,
                    size = size,
                    facade = item.facade,
                });
            }

            var kit = AssetDatabase.LoadAssetAtPath<OutdoorKit>(KitPath);
            if (kit == null)
            {
                kit = ScriptableObject.CreateInstance<OutdoorKit>();
                AssetDatabase.CreateAsset(kit, KitPath);
            }

            kit.entries = entries.ToArray();
            var materialEntries = new List<OutdoorKit.MaterialEntry>();
            foreach (var pair in materials) materialEntries.Add(new OutdoorKit.MaterialEntry { name = pair.Key, material = pair.Value });
            kit.materials = materialEntries.ToArray();
            kit.sourceHash = SourceHash();
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();

            Debug.Log($"Dış mekan kiti hazır: {entries.Count} parça, {materials.Count} malzeme" +
                      (rescaled > 0 ? $", {rescaled} modelin ölçeği düzeltildi" : "") + (flip ? ", yön çevrildi." : "."));
        }

        // ---------------------------------------------------------------- Doku ve malzeme

        static void ConfigureTexture(string path, bool palette)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = palette ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.filterMode = palette ? FilterMode.Bilinear : FilterMode.Trilinear;
            importer.mipmapEnabled = !palette;   // palet: komşu renkler mip'te karışmasın
            importer.anisoLevel = palette ? 0 : 8;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }

        static Material CreateMaterial(string slot, string texturePath)
        {
            string path = $"{MaterialFolder}/M_Outdoor_{slot}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = GraphicsSettings.currentRenderPipeline != null
                    ? GraphicsSettings.currentRenderPipeline.defaultShader
                    : Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null) Debug.LogWarning($"Doku bulunamadı: {texturePath}");
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.08f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---------------------------------------------------------------- Model içe aktarma

        static void ConfigureModel(string path, Dictionary<string, Material> materials, float scaleCorrection)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return;

            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.isReadable = false;
            importer.addCollider = false;
            importer.generateSecondaryUV = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
            importer.materialSearch = ModelImporterMaterialSearch.Local;
            if (Mathf.Abs(scaleCorrection - 1f) > 0.001f) importer.globalScale *= scaleCorrection;

            foreach (var pair in materials)
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key);
                importer.RemoveRemap(id);
                importer.AddRemap(id, pair.Value);
            }
            importer.SaveAndReimport();
        }

        // ---------------------------------------------------------------- Prefab

        static GameObject SavePrefab(Item item, GameObject model, bool flip)
        {
            string path = $"{PrefabFolder}/{item.name}.prefab";
            var root = new GameObject(item.name);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = flip ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            // Düz zemin ve uzak parçalar gölge düşürmez: geniş düz yüzeylerde gölge titremesini (acne) önler.
            var category = ParseCategory(item.category);
            bool noShadow = category == OutdoorCategory.Ground || category == OutdoorCategory.Curb ||
                            category == OutdoorCategory.GroundBack || category == OutdoorCategory.Skyline;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                renderer.shadowCastingMode = noShadow ? ShadowCastingMode.Off : ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        static Bounds MeasureBounds(GameObject asset)
        {
            var instance = UnityEngine.Object.Instantiate(asset);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;
            var renderers = instance.GetComponentsInChildren<Renderer>();
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (i == 0) bounds = renderers[i].bounds;
                else bounds.Encapsulate(renderers[i].bounds);
            }
            UnityEngine.Object.DestroyImmediate(instance);
            return bounds;
        }

        static OutdoorCategory ParseCategory(string value) => (value ?? "").ToLowerInvariant() switch
        {
            "ground" => OutdoorCategory.Ground,
            "curb" => OutdoorCategory.Curb,
            "groundback" => OutdoorCategory.GroundBack,
            "car" => OutdoorCategory.Car,
            "van" => OutdoorCategory.Van,
            "pickup" => OutdoorCategory.Pickup,
            "barrier" => OutdoorCategory.Barrier,
            "sandbags" => OutdoorCategory.Sandbags,
            "dumpster" => OutdoorCategory.Dumpster,
            "building" => OutdoorCategory.Building,
            "skyline" => OutdoorCategory.Skyline,
            "debris" => OutdoorCategory.Debris,
            _ => OutdoorCategory.Prop,
        };

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
