using System.Collections.Generic;
using AftershockIsle.Crafting;
using AftershockIsle.Interaction;
using UnityEngine;
using UnityEngine.Rendering;

namespace AftershockIsle.Building
{
    /// <summary>
    /// Eski Greybox binayı ve objeleri bozmadan, Inspector'a bırakılan 3B bina ve eşya prefablarını
    /// otomatik algılayıp sahneye giydiren merkezi görsel yöneticisi.
    /// Slot boşaltılırsa eski Greybox görseli otomatik geri açılır.
    /// </summary>
    [ExecuteAlways]
    public class ModelManager : MonoBehaviour
    {
        const string BuildingHolderName = "CustomBuildingModel";
        const string PlanksHolderName = "CustomFloorPlanks";
        const string CustomSkinHolderName = "CustomModelSkin";

        [Header("Hedef Bina Sistemi")]
        [SerializeField] BuildingLayout building;

        [Header("1. Bina Modeli (İsteğe Bağlı)")]
        [Tooltip("İşaretlenirse ve aşağıya bina prefabı bırakılırsa eski gri duvarları gizleyip yeni binayı gösterir")]
        [SerializeField] bool useCustomBuilding;
        [Tooltip("Buraya Building.fbx veya bina prefabını sürükle")]
        [SerializeField] GameObject buildingPrefab;
        [Tooltip("Blender'daki tek katın iç ölçüsü (Genişlik X, Kat Yüksekliği Y, Derinlik Z)")]
        [SerializeField] Vector3 buildingBaseUnit = new Vector3(18f, 3.5f, 4f);
        [Tooltip("Bina modelinin Y eksenindeki dönüşü (ön kesit kameraya baksın diye 180)")]
        [SerializeField] float buildingYaw = 180f;
        [Tooltip("Modeli BuildingLayout içindeki kat yüksekliği ve derinliğe otomatik oturtur")]
        [SerializeField] bool fitBuildingToLayout = true;
        [Tooltip("Bina modeli atandığında eski gri duvar ve döşeme kutularını gizler (kapı ve mantık çalışmaya devam eder)")]
        [SerializeField] bool hideGreyboxWalls = true;

        [Header("2. Çukur Kalasları (Tahtalar)")]
        [Tooltip("Buraya tahtalar.fbx modelini bırakabilirsin")]
        [SerializeField] GameObject floorPlanksPrefab;
        [SerializeField] Texture2D floorPlanksTexture;
        [SerializeField] Vector3 floorPlanksPosition = new Vector3(2.2f, 0.04f, -0.8f);
        [SerializeField] Vector3 floorPlanksRotation = new Vector3(90f, 0f, 0f);
        [SerializeField] float floorPlanksScale = 1f;

        [Header("3. Eşya Prefab Slotları (Otomatik Giydirir)")]
        [Tooltip("Buraya bencht.fbx (Çalışma Tezgahı) modelini bırakabilirsin")]
        [SerializeField] GameObject workbenchPrefab;
        [Tooltip("Buraya bodrumdaki su deposu / varil modelini bırakabilirsin (Su Tulumbasına giydirir)")]
        [SerializeField] GameObject waterTankPrefab;

        static Material outlineMaterial;
        readonly Dictionary<Material, Material> runtimeMaterials = new Dictionary<Material, Material>();

        public static ModelManager Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            if (building == null) building = FindFirstObjectByType<BuildingLayout>();
            if (Application.isPlaying) RefreshAll();
        }

        void OnEnable()
        {
            Instance = this;
            if (building == null) building = FindFirstObjectByType<BuildingLayout>();
        }

        [ContextMenu("Modelleri Şimdi Güncelle")]
        public void RefreshAll()
        {
            if (building == null) building = FindFirstObjectByType<BuildingLayout>();
            if (building == null) return;

            ApplyBuilding();
            ApplyFloorPlanks();
            ApplyWorkbench();
            ApplyWaterTank();
        }

        // ---------------------------------------------------------------- 1. Bina

        void ApplyBuilding()
        {
            var geo = building.transform.Find("Geometry");
            bool useCustom = useCustomBuilding && buildingPrefab != null;

            // Eski bina dursun: sadece duvar/döşeme kutularının MeshRenderer'ını aç/kapat (ExitDoor collider'ı ve mantık korunur).
            if (geo != null)
            {
                bool showGreybox = !useCustom || !hideGreyboxWalls;
                foreach (var renderer in geo.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null) continue;
                    if (renderer.gameObject.name.StartsWith("Earth_"))
                    {
                        renderer.enabled = true;
                        continue;
                    }
                    renderer.enabled = showGreybox;
                }
            }

            var existing = building.transform.Find(BuildingHolderName);
            if (!useCustom)
            {
                if (existing != null) DestroySafe(existing.gameObject);
                return;
            }

            if (existing != null) DestroySafe(existing.gameObject);

            var holder = new GameObject(BuildingHolderName).transform;
            holder.SetParent(building.transform, false);
            holder.localPosition = Vector3.zero;
            holder.localRotation = Quaternion.Euler(0f, buildingYaw, 0f);
            holder.localScale = fitBuildingToLayout
                ? new Vector3(
                    building.Width / Mathf.Max(0.1f, buildingBaseUnit.x),
                    building.FloorHeight / Mathf.Max(0.1f, buildingBaseUnit.y),
                    building.Depth / Mathf.Max(0.1f, buildingBaseUnit.z))
                : Vector3.one;

            var instance = InstantiateModel(buildingPrefab, holder);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            CleanBuildingInstance(instance);
            FixBuildingMaterials(instance);
        }

        /// <summary>
        /// Blender sahnesinden yanlışlıkla gelen Kamera, Işık veya bina dışı objeleri kapatır;
        /// bina mesh collider'larının içerideki karakter/eşya tıklamalarını engellemesini önler.
        /// </summary>
        static void CleanBuildingInstance(GameObject instance)
        {
            foreach (var cam in instance.GetComponentsInChildren<Camera>(true))
                cam.gameObject.SetActive(false);

            foreach (var light in instance.GetComponentsInChildren<Light>(true))
                light.gameObject.SetActive(false);

            foreach (var child in instance.GetComponentsInChildren<Transform>(true))
            {
                if (child == null || child == instance.transform) continue;
                string n = child.name;
                if (n.StartsWith("Bakir_Mavi") || n.StartsWith("Kivilcim") || n == "Camera" || n == "Light")
                    child.gameObject.SetActive(false);
            }

            foreach (var col in instance.GetComponentsInChildren<Collider>(true))
                col.enabled = false;
        }

        void FixBuildingMaterials(GameObject instance)
        {
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var shared = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < shared.Length; i++)
                {
                    var mat = shared[i];
                    if (mat == null) continue;

                    if (mat.name.StartsWith("Cizim_Dis_Hat") || mat.name.Contains("Outline"))
                    {
                        shared[i] = GetOutlineMaterial();
                        changed = true;
                    }
                    else
                    {
                        shared[i] = GetMatteMaterial(mat);
                        changed = true;
                    }
                }

                if (changed) renderer.sharedMaterials = shared;
            }
        }

        static Material GetOutlineMaterial()
        {
            if (outlineMaterial != null) return outlineMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            outlineMaterial = new Material(shader) { name = "Building_Outline_Black" };
            if (outlineMaterial.HasProperty("_BaseColor")) outlineMaterial.SetColor("_BaseColor", Color.black);
            if (outlineMaterial.HasProperty("_Color")) outlineMaterial.SetColor("_Color", Color.black);
            // Solidify ters normalli kabuk olduğu için yalnızca arka yüzeyleri (dış konturu) çiz, ön yüzeyi kapatmasın.
            if (outlineMaterial.HasProperty("_Cull")) outlineMaterial.SetFloat("_Cull", (float)CullMode.Back);
            return outlineMaterial;
        }

        Material GetMatteMaterial(Material source)
        {
            if (runtimeMaterials.TryGetValue(source, out var cached) && cached != null) return cached;

            var shader = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultShader
                : Shader.Find("Standard");

            Color color = Color.gray;
            if (source.HasProperty("_BaseColor")) color = source.GetColor("_BaseColor");
            else if (source.HasProperty("_Color")) color = source.GetColor("_Color");

            var mat = new Material(shader) { name = source.name + "_URP" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.12f);
            runtimeMaterials[source] = mat;
            return mat;
        }

        // ---------------------------------------------------------------- 2. Çukur Kalasları

        void ApplyFloorPlanks()
        {
            var existing = building.transform.Find(PlanksHolderName);
            if (existing != null) DestroySafe(existing.gameObject);
            if (floorPlanksPrefab == null) return;

            var holder = new GameObject(PlanksHolderName).transform;
            holder.SetParent(building.transform, false);
            holder.localPosition = floorPlanksPosition;
            holder.localRotation = Quaternion.Euler(floorPlanksRotation);
            holder.localScale = Vector3.one * Mathf.Max(0.01f, floorPlanksScale);

            var instance = InstantiateModel(floorPlanksPrefab, holder);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            if (floorPlanksTexture != null) ApplySingleTexture(instance, floorPlanksTexture);
        }

        // ---------------------------------------------------------------- 3. Özel Eşyalar

        void ApplyWorkbench()
        {
            var bench = FindFirstObjectByType<Workbench>();
            if (bench == null) return;

            SkinPropWithCustomPrefab(bench.gameObject, workbenchPrefab, 2.05f, 180f);
        }

        void ApplyWaterTank()
        {
            var pump = FindFirstObjectByType<ManualPump>();
            if (pump == null) return;

            SkinPropWithCustomPrefab(pump.gameObject, waterTankPrefab, 1.25f, 180f);
        }

        static void SkinPropWithCustomPrefab(GameObject targetBox, GameObject prefab, float targetHeight, float yaw)
        {
            var defaultModel = targetBox.transform.Find("Model");
            var prevCustom = targetBox.transform.Find(CustomSkinHolderName);
            if (prevCustom != null) DestroySafe(prevCustom.gameObject);

            if (prefab == null)
            {
                if (defaultModel != null) defaultModel.gameObject.SetActive(true);
                else
                {
                    var boxR = targetBox.GetComponent<MeshRenderer>();
                    if (boxR != null) boxR.enabled = true;
                }
                return;
            }

            if (defaultModel != null) defaultModel.gameObject.SetActive(false);

            var boxScale = targetBox.transform.lossyScale;
            var pivot = new GameObject(CustomSkinHolderName).transform;
            pivot.SetParent(targetBox.transform, false);
            pivot.localScale = new Vector3(1f / boxScale.x, 1f / boxScale.y, 1f / boxScale.z);

            var model = InstantiateModel(prefab, pivot);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            model.transform.localScale = Vector3.one;

            AutoAssignFolderTextures(prefab, model);

            var bounds = CalculateBounds(model);
            if (bounds.size.y > 0.001f)
            {
                model.transform.localScale = Vector3.one * (targetHeight / bounds.size.y);
                bounds = CalculateBounds(model);
                var bottomTarget = targetBox.transform.position + Vector3.down * (boxScale.y * 0.5f);
                var modelBottom = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                model.transform.position += bottomTarget - modelBottom;
            }

            var boxRenderer = targetBox.GetComponent<MeshRenderer>();
            if (boxRenderer != null) boxRenderer.enabled = false;
        }

        static void AutoAssignFolderTextures(GameObject sourcePrefab, GameObject instance)
        {
#if UNITY_EDITOR
            string assetPath = UnityEditor.AssetDatabase.GetAssetPath(sourcePrefab);
            if (string.IsNullOrEmpty(assetPath)) return;

            int slash = assetPath.LastIndexOf('/');
            if (slash < 0) return;
            string folder = assetPath.Substring(0, slash);

            var guids = UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            if (guids.Length == 0) return;

            var textures = new List<Texture2D>();
            foreach (string g in guids)
            {
                var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(UnityEditor.AssetDatabase.GUIDToAssetPath(g));
                if (tex != null) textures.Add(tex);
            }
            if (textures.Count == 0) return;

            var shader = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultShader
                : Shader.Find("Standard");

            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;

                    // Örn: mat_wood.002 -> tex_wood
                    string key = m.name.ToLowerInvariant().Replace("mat_", "");
                    int dot = key.IndexOf('.');
                    if (dot > 0) key = key.Substring(0, dot);

                    Texture2D matched = textures.Count == 1 ? textures[0] : null;
                    if (matched == null)
                    {
                        foreach (var tex in textures)
                        {
                            if (tex.name.ToLowerInvariant().Contains(key))
                            {
                                matched = tex;
                                break;
                            }
                        }
                    }

                    if (matched == null) continue;

                    var newMat = new Material(shader) { name = m.name + "_Tex" };
                    if (newMat.HasProperty("_BaseMap")) newMat.SetTexture("_BaseMap", matched);
                    if (newMat.HasProperty("_MainTex")) newMat.SetTexture("_MainTex", matched);
                    if (newMat.HasProperty("_BaseColor")) newMat.SetColor("_BaseColor", Color.white);
                    if (newMat.HasProperty("_Smoothness")) newMat.SetFloat("_Smoothness", 0.15f);
                    mats[i] = newMat;
                }
                r.sharedMaterials = mats;
            }
#endif
        }

        static void ApplySingleTexture(GameObject root, Texture2D texture)
        {
            var shader = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultShader
                : Shader.Find("Standard");
            var mat = new Material(shader) { name = texture.name + "_Mat" };
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.15f);

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var arr = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < arr.Length; i++) arr[i] = mat;
                r.sharedMaterials = arr;
            }
        }

        static Bounds CalculateBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        static GameObject InstantiateModel(GameObject prefab, Transform parent)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.PrefabUtility.IsPartOfPrefabAsset(prefab))
                return (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
#endif
            return Instantiate(prefab, parent);
        }

        static void DestroySafe(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }
}
