using System.Collections.Generic;
using AftershockIsle.Crafting;
using AftershockIsle.Interaction;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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

        [Header("1. Bina Modeli")]
        [Tooltip("İşaretliyken eski gri kutu binayı gizler ve yeni bina modelini gösterir")]
        [SerializeField] bool useCustomBuilding = true;
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
        [Tooltip("İşaretliyken sahnedeki eski kutu eşyaları ve mavi bodrum suyu küpünü gizler (2. resimdeki temiz bina görünümü için)")]
        [SerializeField] bool hideGreyboxProps = true;
        [Tooltip("Pencerelerin arkasına 2. resimdeki gibi kirli sarı/kehribar buğulu cam arka planı ekler")]
        [SerializeField] bool addWarmWindowBacking = true;

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

        [Header("4. Kolon Seviye Modelleri (Assets/Models/Columns)")]
        [Tooltip("Lv1: Kolon_Lv1_Kirik.fbx")]
        [SerializeField] GameObject columnLv1Prefab;
        [Tooltip("Lv2: Kolon_Lv2_Catlak.fbx")]
        [SerializeField] GameObject columnLv2Prefab;
        [Tooltip("Lv3: Kolon_Lv3_Guclendirilmis.fbx")]
        [SerializeField] GameObject columnLv3Prefab;
        [Tooltip("Kolon modelinin Y eksenindeki dönüşü (hasarlı yüz kameraya baksın diye)")]
        [SerializeField] float columnYaw = 180f;

        [Header("5. Karanlık Atmosfer (This War of Mine tarzı)")]
        [Tooltip("İşaretliyken ortam karanlık olur; ışığı pencerelerden giren gün ışığı ve odalardaki ampuller verir")]
        [SerializeField] bool enableDarkTheme = true;
        [Tooltip("Binanın arkasından pencerelerden giren soğuk gün ışığının şiddeti")]
        [SerializeField, Range(0f, 2f)] float darkSunIntensity = 0.7f;
        [SerializeField] Color sunColor = new Color(0.72f, 0.78f, 0.88f);
        [Tooltip("Hiç ışık almayan köşelerin seviyesi (düşük = zifiri karanlık)")]
        [SerializeField, Range(0f, 0.6f)] float darkAmbientLevel = 0.06f;
        [Tooltip("Sarkan ampul ışığı")]
        [SerializeField] Color lampColor = new Color(1f, 0.74f, 0.46f);
        [SerializeField, Range(0f, 10f)] float lampIntensity = 3.2f;
        [SerializeField, Range(1f, 12f)] float lampRange = 6f;
        [Tooltip("Katların ortasındaki lambalara ek olarak ampul asılan noktalar (bina yerel X, Y, Z). " +
                 "Ampulsüz odalar bilerek karanlık kalır.")]
        [SerializeField] Vector3[] extraLampPositions =
        {
            new Vector3(-5.5f, 2.75f, 0.3f),   // bodrum, merdiven tarafı
            new Vector3(-6.6f, 6.25f, 0.3f),   // giriş katı sol oda
            new Vector3(6.8f, 9.75f, 0.3f),    // yaşam katı sağ oda
            new Vector3(-6.2f, 13.25f, 0.3f),  // 3. kat sol oda
        };
        [Tooltip("Mum / fener objelerine eklenen ışığın şiddeti ve çapı (ConfigureCandleLightOnObject)")]
        [SerializeField, Range(0.2f, 3f)] float maxCandleIntensity = 1.4f;
        [SerializeField, Range(1f, 8f)] float candleRange = 4f;

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

            runtimeMaterials.Clear();
            ApplyBuilding();
            // Ek ampuller bina modelinin altına asılır; model kurulduktan sonra.
            ApplyLightingTheme();
            // Önce eski greybox eşyaları elenir: tezgah/tulumba giydirmesi sahneye konan asıl eşyayı bulsun.
            ApplyGreyboxPropsVisibility();
            ApplyFloorPlanks();
            ApplyWorkbench();
            ApplyWaterTank();
            ApplySketchPropMaterials();
            ApplyColumns();
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

            CleanBuildingInstance(instance, addWarmWindowBacking);
            FixBuildingMaterials(instance);
        }

        /// <summary>
        /// Blender sahnesinden yanlışlıkla gelen Kamera/Işık objelerini kapatır,
        /// Blender'daki açık kapı açılarını (kapi_acik_aci) uygular,
        /// arka duvarla çakışan tuğla/harç/çatlak detaylarını 2 cm öne çeker ve
        /// pencerelerin arkasına 2. resimdeki sarı buğulu cam panelini yerleştirir.
        /// </summary>
        static void CleanBuildingInstance(GameObject instance, bool createWindowBacking)
        {
            foreach (var cam in instance.GetComponentsInChildren<Camera>(true))
                cam.gameObject.SetActive(false);

            foreach (var light in instance.GetComponentsInChildren<Light>(true))
                light.gameObject.SetActive(false);

            foreach (var child in instance.GetComponentsInChildren<Transform>(true))
            {
                if (child == null || child == instance.transform) continue;
                string n = child.name;

                // Blender'daki "_Kolon_" objeleri sahnedeki SupportColumn'larla aynı yerde durur; görüntüyü onların
                // seviye modeli verir, binadaki sabit kopya gizlenir.
                if (n.StartsWith("Bakir_Mavi") || n.StartsWith("Kivilcim") || n == "Camera" || n == "Light" || n.Contains("_Kolon_"))
                {
                    child.gameObject.SetActive(false);
                    continue;
                }

                // Arka duvarın (Y=2.0, Solidify=1.985) içinde kalıp gizlenen tuğla, harç ve çatlakları kameraya (+Z) yaklaştır
                if (n.Contains("Brick_Mortar"))
                {
                    var p = child.localPosition;
                    child.localPosition = new Vector3(p.x, p.y, 0.018f);
                }
                else if (n.Contains("BackWall_Crack"))
                {
                    var p = child.localPosition;
                    child.localPosition = new Vector3(p.x, p.y, 0.020f);
                }
                else if (n.Contains("Brick_Details"))
                {
                    var p = child.localPosition;
                    child.localPosition = new Vector3(p.x, p.y, 0.025f);
                }
            }

            if (createWindowBacking)
                CreateWarmWindowBackings(instance.transform);

            // Bina mesh'leri tıklamaları engellemesin; yalnızca kapıların (F ile açılır) tıklama alanı açık kalır.
            foreach (var col in instance.GetComponentsInChildren<Collider>(true))
                col.enabled = col.GetComponent<BuildingDoor>() != null;
        }

        static void CreateWarmWindowBackings(Transform buildingRoot)
        {
            var shader = Shader.Find("Aftershock/CelBuilding")
                         ?? (GraphicsSettings.currentRenderPipeline != null
                             ? GraphicsSettings.currentRenderPipeline.defaultShader
                             : Shader.Find("Standard"));
            var glassMat = new Material(shader) { name = "Window_Amber_Backing_Cel" };
            // 2. resimdeki kirli sarı / kehribar pencere içi rengi
            Color amber = new Color(0.56f, 0.45f, 0.21f, 1f);
            Color amberPatch = new Color(0.46f, 0.36f, 0.16f, 1f);
            if (glassMat.HasProperty("_BaseColor")) glassMat.SetColor("_BaseColor", amber);
            if (glassMat.HasProperty("_Color")) glassMat.SetColor("_Color", amber);
            if (glassMat.HasProperty("_PatchColor")) glassMat.SetColor("_PatchColor", amberPatch);
            if (glassMat.HasProperty("_NoiseScale")) glassMat.SetFloat("_NoiseScale", 3.0f);
            if (glassMat.HasProperty("_NoiseThreshold")) glassMat.SetFloat("_NoiseThreshold", 0.65f);
            if (glassMat.HasProperty("_NoiseStrength")) glassMat.SetFloat("_NoiseStrength", 0.6f);
            if (glassMat.HasProperty("_CornerAO")) glassMat.SetFloat("_CornerAO", 0.15f);

            float[] floorWindowCenterY = { 5.08f, 8.58f, 12.08f };
            for (int i = 0; i < floorWindowCenterY.Length; i++)
            {
                var backing = GameObject.CreatePrimitive(PrimitiveType.Cube);
                backing.name = $"Window_Amber_Backing_F{i + 1}";
                backing.transform.SetParent(buildingRoot, false);
                // Demir parmaklıkların (-2.04..-2.12) hemen arkasında, dış duvar kalınlığının (-2.00..-2.35) içinde
                backing.transform.localPosition = new Vector3(0f, floorWindowCenterY[i], -2.18f);
                backing.transform.localScale = new Vector3(14.5f, 1.42f, 0.02f);
                var col = backing.GetComponent<Collider>();
                if (col != null) DestroySafe(col);
                var r = backing.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = glassMat;
            }
        }

        void FixBuildingMaterials(GameObject instance)
        {
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.gameObject.name.StartsWith("Window_Amber_Backing")) continue;

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

#if UNITY_EDITOR
            UpgradeExtractedBuildingMaterialsInEditor();
#endif
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

            var celShader = Shader.Find("Aftershock/CelBuilding");
            var shader = celShader != null
                ? celShader
                : (GraphicsSettings.currentRenderPipeline != null
                    ? GraphicsSettings.currentRenderPipeline.defaultShader
                    : Shader.Find("Standard"));

            var mat = new Material(shader) { name = source.name + "_Cel" };
            ConfigureCelBuildingMaterial(mat, source);
            runtimeMaterials[source] = mat;
            return mat;
        }

        static void ConfigureCelBuildingMaterial(Material target, Material source)
        {
            string n = (source != null ? source.name : target.name).ToLowerInvariant();

            Color baseColor = new Color(0.56f, 0.56f, 0.54f, 1f);
            if (source != null)
            {
                if (source.HasProperty("_BaseColor")) baseColor = source.GetColor("_BaseColor");
                else if (source.HasProperty("_Color")) baseColor = source.GetColor("_Color");
            }

            Color patchColor = new Color(baseColor.r * 0.84f, baseColor.g * 0.84f, baseColor.b * 0.83f, 1f);
            float noiseScale = 1.75f;
            float noiseThreshold = 0.74f;
            float noiseStrength = 0.85f;
            float cornerAO = 0.32f;
            float useBasementDamp = 0f;

            if (n.StartsWith("cizim_boya"))
            {
                // Su deposu varili (Blender çizim stili): mavi-gri boya üzerinde keskin kenarlı turuncu pas lekeleri.
                baseColor = new Color(0.25f, 0.42f, 0.52f, 1f);
                patchColor = new Color(0.62f, 0.3f, 0.12f, 1f);
                noiseScale = 4.5f;
                noiseThreshold = 0.6f;
                noiseStrength = 1f;
                cornerAO = 0f;
            }
            else if (n.StartsWith("cizim_"))
            {
                // Varilin metal, pirinç, hortum parçaları: düz renk, binaya göre hesaplanan köşe gölgesi yok.
                noiseStrength = 0.15f;
                cornerAO = 0f;
            }
            else if (n.Contains("bodrum_duvar"))
            {
                // 2. resimdeki bodrum sıvası + alt yosun yeşili rutubet şeridi + oda köşe gölgesi
                baseColor = new Color(0.54f, 0.55f, 0.54f, 1f);
                patchColor = new Color(0.43f, 0.44f, 0.43f, 1f);
                noiseScale = 1.7f;
                noiseThreshold = 0.73f;
                noiseStrength = 0.90f;
                cornerAO = 0.38f;
                useBasementDamp = 1f;
            }
            else if (n.Contains("kat_duvar") || n.Contains("bolme_duvar"))
            {
                // 2. resimdeki sıcak gri sıva + seyrek hafif lekeler + yumuşak köşe kararması
                baseColor = new Color(0.58f, 0.57f, 0.55f, 1f);
                patchColor = new Color(0.47f, 0.46f, 0.44f, 1f);
                noiseScale = 1.8f;
                noiseThreshold = 0.75f;
                noiseStrength = 0.85f;
                cornerAO = 0.36f;
            }
            else if (n.Contains("tavan") || n.Contains("cati"))
            {
                // 2. resimde tavanlar pürüzsüz düz gri, sadece kenarları/köşeleri hafif koyu
                baseColor = new Color(0.46f, 0.47f, 0.48f, 1f);
                patchColor = new Color(0.41f, 0.42f, 0.43f, 1f);
                noiseStrength = 0.08f;
                cornerAO = 0.34f;
            }
            else if (n.Contains("bodrum_zemin"))
            {
                // 2. resimdeki bodrum zemini: koyu gri beton + geniş ıslak koyu lekeler
                baseColor = new Color(0.40f, 0.41f, 0.42f, 1f);
                patchColor = new Color(0.28f, 0.29f, 0.30f, 1f);
                noiseScale = 1.25f;
                noiseThreshold = 0.64f;
                noiseStrength = 0.95f;
                cornerAO = 0.38f;
            }
            else if (n.Contains("zemin"))
            {
                // Üst kat zeminleri: temiz mat şap + çok seyrek ufak izler + köşe gölgesi
                baseColor = new Color(0.46f, 0.45f, 0.43f, 1f);
                patchColor = new Color(0.38f, 0.37f, 0.35f, 1f);
                noiseScale = 2.0f;
                noiseThreshold = 0.80f;
                noiseStrength = 0.60f;
                cornerAO = 0.34f;
            }
            else if (n.Contains("kolon"))
            {
                baseColor = new Color(0.52f, 0.53f, 0.54f, 1f);
                patchColor = new Color(0.43f, 0.44f, 0.45f, 1f);
                noiseScale = 2.0f;
                noiseThreshold = 0.74f;
                noiseStrength = 0.55f;
                cornerAO = 0.30f;
            }
            else if (n.Contains("tugla_harc"))
            {
                // Tuğla oyuğu arka harç boşluğu
                baseColor = new Color(0.24f, 0.21f, 0.19f, 1f);
                patchColor = new Color(0.18f, 0.16f, 0.14f, 1f);
                noiseStrength = 0.20f;
                cornerAO = 0.15f;
            }
            else if (n.Contains("tugla"))
            {
                // 2. resimdeki soluk kahve-kiremit tuğlalar (cırtlak kırmızı değil)
                baseColor = new Color(0.46f, 0.29f, 0.21f, 1f);
                patchColor = new Color(0.35f, 0.21f, 0.15f, 1f);
                noiseScale = 4.2f;
                noiseThreshold = 0.60f;
                noiseStrength = 0.85f;
                cornerAO = 0.15f;
            }
            else if (n.Contains("kapi_boya"))
            {
                // 2. resimdeki küf yeşili/turkuaz kapılar + turuncu/kahve pas lekeleri
                baseColor = new Color(0.36f, 0.48f, 0.45f, 1f);
                patchColor = new Color(0.58f, 0.34f, 0.16f, 1f);
                noiseScale = 3.2f;
                noiseThreshold = 0.67f;
                noiseStrength = 0.95f;
                cornerAO = 0.20f;
            }
            else if (n.Contains("ahsap"))
            {
                // Merdivenler ve kapı kasaları: eskitilmiş ahşap + pas/aşınma tonu
                baseColor = new Color(0.46f, 0.39f, 0.31f, 1f);
                patchColor = new Color(0.60f, 0.37f, 0.17f, 1f);
                noiseScale = 3.4f;
                noiseThreshold = 0.68f;
                noiseStrength = 0.85f;
                cornerAO = 0.18f;
            }
            else if (n.Contains("pasli"))
            {
                // Pencere korkulukları, merdiven korkulukları ve borular
                baseColor = new Color(0.34f, 0.32f, 0.30f, 1f);
                patchColor = new Color(0.60f, 0.35f, 0.16f, 1f);
                noiseScale = 3.8f;
                noiseThreshold = 0.66f;
                noiseStrength = 0.90f;
                cornerAO = 0.15f;
            }
            else if (n.Contains("kesit_cerceve"))
            {
                // 2. resimdeki düz mat koyu antrasit kesit çerçevesi (lekesiz)
                baseColor = new Color(0.25f, 0.26f, 0.28f, 1f);
                patchColor = new Color(0.25f, 0.26f, 0.28f, 1f);
                noiseStrength = 0f;
                cornerAO = 0.12f;
            }
            else if (n.Contains("catlak"))
            {
                baseColor = new Color(0.08f, 0.08f, 0.08f, 1f);
                patchColor = new Color(0.08f, 0.08f, 0.08f, 1f);
                noiseStrength = 0f;
                cornerAO = 0f;
            }

            if (target.HasProperty("_BaseColor")) target.SetColor("_BaseColor", baseColor);
            if (target.HasProperty("_Color")) target.SetColor("_Color", baseColor);
            if (target.HasProperty("_PatchColor")) target.SetColor("_PatchColor", patchColor);
            if (target.HasProperty("_NoiseScale")) target.SetFloat("_NoiseScale", noiseScale);
            if (target.HasProperty("_NoiseThreshold")) target.SetFloat("_NoiseThreshold", noiseThreshold);
            if (target.HasProperty("_NoiseStrength")) target.SetFloat("_NoiseStrength", noiseStrength);
            if (target.HasProperty("_CornerAO")) target.SetFloat("_CornerAO", cornerAO);
            if (target.HasProperty("_UseBasementDamp")) target.SetFloat("_UseBasementDamp", useBasementDamp);
            if (target.HasProperty("_Smoothness")) target.SetFloat("_Smoothness", 0.08f);
            if (target.HasProperty("_MinAmbient"))
            {
                float minAmb = Instance != null && Instance.enableDarkTheme ? Instance.darkAmbientLevel : 0.36f;
                target.SetFloat("_MinAmbient", minAmb);
            }
        }

        // ---------------------------------------------------------------- 5. Karanlık Tema & Mum Işığı

        const string LampPrefix = "Ampul_";
        static Material bulbMaterial;
        static Material cordMaterial;

        /// <summary>
        /// This War of Mine tarzı ışık: ortam neredeyse karanlık; binanın arkasından pencerelerden soğuk gün ışığı
        /// girer, katlardaki ve bazı odalardaki sarkan ampuller sıcak ışık havuzu yapar, ampulsüz odalar karanlık kalır.
        /// Ampuller şaltere bağlıdır. Renk solukluğu/vinyet/gren için "Aftershock/Karanlık Atmosferi Kur" Volume'u kurar.
        /// </summary>
        void ApplyLightingTheme()
        {
            if (!enableDarkTheme) return;

            var holder = building.transform.Find(BuildingHolderName);
            foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (light == null) continue;

                if (light.type == LightType.Directional)
                {
                    // Arkadan (+Z) kameraya doğru, pencerelerden içeri eğik düşer.
                    light.transform.rotation = Quaternion.Euler(32f, 195f, 0f);
                    light.intensity = darkSunIntensity;
                    light.color = sunColor;
                    light.shadows = LightShadows.Soft;
                    light.shadowStrength = 0.95f;
                    continue;
                }

                // Katların ortasındaki lambalar (sahne kurucunun Lamba_0..4) sarkan ampul olur.
                if (light.transform.IsChildOf(building.transform) && light.name.StartsWith("Lamba_"))
                {
                    light.gameObject.SetActive(true);
                    ConfigureLamp(light);
                    if (holder != null) CreateBulbVisual(holder, holder.InverseTransformPoint(light.transform.position));
                }
            }

            // Bazı odalara ek ampul; bina modeliyle birlikte yerleşir ve şaltere bağlanır.
            if (holder != null)
            {
                for (int i = 0; i < extraLampPositions.Length; i++)
                {
                    var lampGo = new GameObject($"{LampPrefix}Isik_{i}");
                    lampGo.transform.SetParent(holder, false);
                    lampGo.transform.localPosition = ToHolderLocal(extraLampPositions[i]);
                    var light = lampGo.AddComponent<Light>();
                    ConfigureLamp(light);
                    if (Application.isPlaying) PowerGrid.RegisterLight(light);
                    CreateBulbVisual(holder, lampGo.transform.localPosition);
                }
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(darkAmbientLevel, darkAmbientLevel * 1.02f, darkAmbientLevel * 1.12f);

            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.02f, 0.02f, 0.025f);
            }

            // Şalter kesintisi bu karanlık değerleri temel alsın.
            var lighting = FindFirstObjectByType<AftershockIsle.Effects.LightingController>();
            if (lighting != null) lighting.Rebase();

            ApplyPostProcessing(cam);
        }

        const string AtmosphereVolumeName = "Karanlık Atmosfer (Volume)";

        /// <summary>
        /// Neredeyse renksiz, kontrastlı, kenarları kararan, grenli görüntü. Sahnede bu isimde elle ayarlanmış bir
        /// Volume varsa ona dokunulmaz; yoksa kaydedilmeyen geçici bir Volume her açılışta kodla kurulur.
        /// </summary>
        static void ApplyPostProcessing(Camera cam)
        {
            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null) data.renderPostProcessing = true;
            }

            var existing = GameObject.Find(AtmosphereVolumeName);
            if (existing != null)
            {
                var userVolume = existing.GetComponent<Volume>();
                if (userVolume != null && userVolume.sharedProfile != null && (existing.hideFlags & HideFlags.DontSave) == 0) return;
                if (userVolume != null && userVolume.sharedProfile != null) DestroySafe(userVolume.sharedProfile);
                DestroySafe(existing);
            }

            var go = new GameObject(AtmosphereVolumeName) { hideFlags = HideFlags.DontSave };
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "AftershockDark_Runtime";
            profile.hideFlags = HideFlags.DontSave;

            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(-0.35f);
            color.contrast.Override(30f);
            color.saturation.Override(-78f);
            color.colorFilter.Override(new Color(0.93f, 0.95f, 1f));

            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.92f, 0.96f, 1.05f, -0.08f));
            smh.highlights.Override(new Vector4(1.04f, 1f, 0.94f, 0f));

            var vignette = profile.Add<Vignette>(true);
            vignette.color.Override(Color.black);
            vignette.intensity.Override(0.45f);
            vignette.smoothness.Override(0.5f);

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.4f);
            grain.response.Override(0.75f);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.7f);

            volume.sharedProfile = profile;
        }

        void ConfigureLamp(Light light)
        {
            light.type = LightType.Point;
            light.color = lampColor;
            light.intensity = lampIntensity;
            light.range = lampRange;
            light.shadows = LightShadows.Soft;
        }

        /// <summary>Bina yerel konumunu (Blender ölçüsü) model tutucunun yerel uzayına çevirir: tutucu 180° döndürülmüş.</summary>
        static Vector3 ToHolderLocal(Vector3 buildingLocal) => Quaternion.Euler(0f, 180f, 0f) * buildingLocal;

        /// <summary>Tavandan kabloyla sarkan, kendinden ışıklı küçük ampul (bloom ile parlar).</summary>
        static void CreateBulbVisual(Transform holder, Vector3 localPosition)
        {
            bulbMaterial ??= CreateUnlit("Ampul_Isik", new Color(1f, 0.85f, 0.6f) * 2.2f);
            cordMaterial ??= CreateUnlit("Ampul_Kablo", new Color(0.03f, 0.03f, 0.03f));

            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = LampPrefix + "Gorsel";
            bulb.transform.SetParent(holder, false);
            bulb.transform.localPosition = localPosition;
            bulb.transform.localScale = Vector3.one * 0.13f;
            bulb.GetComponent<Renderer>().sharedMaterial = bulbMaterial;
            bulb.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            DestroySafe(bulb.GetComponent<Collider>());

            var cord = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cord.name = LampPrefix + "Kablo";
            cord.transform.SetParent(holder, false);
            cord.transform.localPosition = localPosition + Vector3.up * 0.25f;
            cord.transform.localScale = new Vector3(0.012f, 0.45f, 0.012f);
            cord.GetComponent<Renderer>().sharedMaterial = cordMaterial;
            cord.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            DestroySafe(cord.GetComponent<Collider>());
        }

        static Material CreateUnlit(string name, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }

        /// <summary>
        /// Seçili bir mum / fener objesine (veya mevcut Point Light'a) patlama yapmayan ideal karanlık tema mum ışığı ekler.
        /// </summary>
        public void ConfigureCandleLightOnObject(GameObject target)
        {
            if (target == null) return;

            var light = target.GetComponentInChildren<Light>(true);
            if (light == null)
            {
                var lightGo = new GameObject("Mum_Isigi");
                lightGo.transform.SetParent(target.transform, false);
                // Işık mumun tam içinde (0,0,0) olursa 1/d^2 yüzünden mumu bembeyaz patlatır; fitilin biraz üstüne ve önüne koy
                var b = CalculateBounds(target);
                float topOffset = b.size.y > 0.01f ? (b.max.y - target.transform.position.y) + 0.12f : 0.25f;
                lightGo.transform.localPosition = new Vector3(0f, topOffset, -0.08f);
                light = lightGo.AddComponent<Light>();
            }

            light.type = LightType.Point;
            light.color = new Color(1.0f, 0.58f, 0.22f); // Sıcak kehribar mum alevi
            light.intensity = maxCandleIntensity;
            light.range = candleRange;
            light.shadows = LightShadows.Soft;
            light.gameObject.SetActive(true);
        }

#if UNITY_EDITOR
        static void UpgradeExtractedBuildingMaterialsInEditor()
        {
            const string matFolder = "Assets/Models/Buildings/Materials";
            if (!UnityEditor.AssetDatabase.IsValidFolder(matFolder)) return;

            var celShader = Shader.Find("Aftershock/CelBuilding");
            var unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (celShader == null) return;

            var guids = UnityEditor.AssetDatabase.FindAssets("t:Material", new[] { matFolder });
            foreach (string g in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                if (mat.name.StartsWith("Cizim_Dis_Hat") || mat.name.Contains("Outline"))
                {
                    if (unlitShader != null && mat.shader != unlitShader) mat.shader = unlitShader;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.black);
                    if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Back);
                }
                else
                {
                    if (mat.shader != celShader) mat.shader = celShader;
                    ConfigureCelBuildingMaterial(mat, mat);
                }
                UnityEditor.EditorUtility.SetDirty(mat);
            }
        }
#endif

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

            var model = SkinPropWithCustomPrefab(pump.gameObject, waterTankPrefab, 1.25f, 180f);
            if (model != null) FixBuildingMaterials(model);
        }

        /// <summary>
        /// Sahneye elle konan Blender çizim stili eşyalara (Su_Deposu.fbx gibi, "Cizim_" malzemeli) binayla aynı Cel
        /// görünümünü verir. Yalnızca oyunda: editörde yapılırsa sahneye kaydedilemeyen geçici malzemeler yazılırdı.
        /// </summary>
        void ApplySketchPropMaterials()
        {
            if (!Application.isPlaying) return;
            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (renderer.transform.IsChildOf(building.transform)) continue;
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null || !mat.name.StartsWith("Cizim_")) continue;
                    FixBuildingMaterials(renderer.gameObject);
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- 4. Kolonlar

        void ApplyColumns()
        {
            SyncColumnsWithBlenderBuilding();
            foreach (var column in FindObjectsByType<SupportColumn>(FindObjectsSortMode.None))
                column.ApplyVisual();
        }

        /// <summary>
        /// Blender binasındaki "_Kolon_" objelerinin (örn. F1_Kolon_X+2_00_Lv1) tam konumlarını okur,
        /// sahnedeki etkileşimli SupportColumn objelerini o noktalara taşır ve başlangıç seviyelerini (Lv1/Lv2/Lv3)
        /// eşleştirir. Böylece kolonlar Blender'da konulan yerlerde durur ve seviyeye göre prefab değiştirir.
        /// </summary>
        void SyncColumnsWithBlenderBuilding()
        {
            var buildingHolder = building != null ? building.transform.Find(BuildingHolderName) : null;
            if (buildingHolder == null) return;

            var blenderColumns = new List<Transform>();
            foreach (var t in buildingHolder.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name.Contains("_Kolon_") && t.GetComponent<MeshFilter>() != null)
                    blenderColumns.Add(t);
            }
            if (blenderColumns.Count == 0) return;

            var sceneColumns = new List<SupportColumn>(
                FindObjectsByType<SupportColumn>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            sceneColumns.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            blenderColumns.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            // Önce isimle eşle ("F1_Kolon_X+2_00" ↔ "F1_Kolon_X+2_00_Lv1"), kalanları sırayla.
            var pairs = new List<(SupportColumn col, Transform bCol)>();
            var freeScene = new List<SupportColumn>(sceneColumns);
            var freeBlender = new List<Transform>();
            foreach (var b in blenderColumns)
            {
                string key = System.Text.RegularExpressions.Regex.Replace(b.name, @"_Lv\d+$", "");
                var match = freeScene.Find(c => c.name == key || c.name == b.name);
                if (match != null) { pairs.Add((match, b)); freeScene.Remove(match); }
                else freeBlender.Add(b);
            }
            for (int i = 0; i < Mathf.Min(freeScene.Count, freeBlender.Count); i++) pairs.Add((freeScene[i], freeBlender[i]));

            foreach (var (col, bCol) in pairs)
            {
                var mf = bCol.GetComponent<MeshFilter>();
                float colHeight = mf != null && mf.sharedMesh != null
                    ? mf.sharedMesh.bounds.size.y * Mathf.Abs(bCol.lossyScale.y)
                    : 3.21f;
                if (colHeight < 0.5f) colHeight = 3.21f;

                // Blender'daki kolonların pivotu taban merkezindedir; SupportColumn kutusunun merkezi ise ortasındadır.
                Vector3 bottomWorld = bCol.position;
                col.gameObject.SetActive(true);
                col.transform.position = bottomWorld + Vector3.up * (colHeight * 0.5f);
                col.transform.localScale = new Vector3(0.35f, colHeight, 0.4f);
                col.RefreshFloorFromPosition();

                // Oyun her zaman Blender'daki başlangıç seviyesiyle açılır (sahnede kayıtlı değer bozuk olsa bile);
                // kayıtlı oyun yüklenirse SaveManager kendi seviyesini sonradan yazar.
                var match = System.Text.RegularExpressions.Regex.Match(bCol.name, @"_Lv(\d+)$");
                int level = match.Success ? int.Parse(match.Groups[1].Value) : 2;
                var targetState = level >= 3 ? ColumnState.Reinforced : level == 2 ? ColumnState.Cracked : ColumnState.Broken;
                col.RestoreState(targetState, 1);
            }
        }

        /// <summary>
        /// Eski greybox eşyaları ya tamamen vardır ya tamamen yoktur; görünmez ama etkileşimli kalıp boşlukta
        /// "[F] ..." yazısı çıkarmaz. Sahneye aynı türde (ve aynı isimde) bir eşya prefabı konduysa greybox kopyası
        /// kapanır, etkileşim yerleştirilen eşyanın üstünde olur. Prefabı konmamış oyun nesneleri (şalter, telsiz,
        /// yangın...) görünür kalır; duman, su gibi efekt kutularına dokunulmaz. Yalnızca süs modelleri gizlenir.
        /// </summary>
        void ApplyGreyboxPropsVisibility()
        {
            bool useCustom = useCustomBuilding && buildingPrefab != null;
            var propsRoot = building.transform.Find("Props");
            if (propsRoot != null && useCustom && hideGreyboxProps)
            {
                var placed = new List<Interactable>();
                foreach (var item in FindObjectsByType<Interactable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (!item.transform.IsChildOf(building.transform)) placed.Add(item);

                foreach (Transform prop in propsRoot)
                {
                    var interactable = prop.GetComponentInChildren<Interactable>(true);
                    if (interactable != null)
                    {
                        bool replaced = IsReplacedByPlacedItem(interactable, placed);
                        prop.gameObject.SetActive(!replaced);
                        if (!replaced) RestoreRenderers(prop);
                        continue;
                    }

                    // Etkileşimsiz: kendi kutusu olmayan süs modeli (sandalye, yangın tüpü...) gizlenir;
                    // duman, su, pist gibi kendi görseli olan efekt kutuları oyun sistemlerine bırakılır.
                    if (prop.GetComponent<Renderer>() == null) prop.gameObject.SetActive(false);
                    else RestoreRenderers(prop);
                }
            }

            if (useCustom && hideGreyboxProps)
            {
                var planks = building.transform.Find(PlanksHolderName);
                if (planks != null) planks.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Eski sürümün kapattığı görselleri geri açar. Model giydirilmiş kutunun kendisi ("Model" ya da özel model
        /// çocuğu olan) ve yangının eski kutu alevi (partikül kullanılıyor) kapalı kalır.
        /// </summary>
        static void RestoreRenderers(Transform prop)
        {
            foreach (var renderer in prop.GetComponentsInChildren<Renderer>(true))
            {
                var t = renderer.transform;
                bool skinnedBox = t.Find("Model") != null || t.Find(CustomSkinHolderName) != null;
                if (skinnedBox || t.name == "Alev") continue;
                renderer.enabled = true;
            }
        }

        /// <summary>Bina dışına (sahneye) aynı türde bir eşya konmuş mu. Tekil sistem nesnelerinde tür yeter, diğerlerinde isim de eşleşmeli.</summary>
        static bool IsReplacedByPlacedItem(Interactable greybox, List<Interactable> placed)
        {
            bool singleton = greybox is Workbench || greybox is StorageChest || greybox is BurstPipe;
            string name = greybox.DisplayName ?? string.Empty;
            foreach (var item in placed)
            {
                if (item.GetType() != greybox.GetType()) continue;
                if (singleton) return true;

                string other = item.DisplayName ?? string.Empty;
                if (other.Length > 0 && name.Length > 0 && (name.StartsWith(other) || other.StartsWith(name))) return true;
            }
            return false;
        }

        GameObject ColumnPrefab(int level) => level switch
        {
            3 => columnLv3Prefab,
            2 => columnLv2Prefab,
            _ => columnLv1Prefab,
        };

        /// <summary>
        /// Kolonun kutusuna mevcut seviyesinin modelini giydirir (eskisini siler). Seviyenin modeli atanmamışsa
        /// false döner; kolon o zaman renkle gösterilir.
        /// </summary>
        public bool ApplyColumnModel(SupportColumn column)
        {
            var prefab = ColumnPrefab(column.Level);
            if (prefab == null) return false;

            var model = SkinPropWithCustomPrefab(column.gameObject, prefab, column.transform.lossyScale.y, columnYaw);
            if (model != null) FixBuildingMaterials(model);
            return model != null;
        }

        static GameObject SkinPropWithCustomPrefab(GameObject targetBox, GameObject prefab, float targetHeight, float yaw)
        {
            var defaultModel = targetBox.transform.Find("Model");
            var prevCustom = targetBox.transform.Find(CustomSkinHolderName);
            if (prevCustom != null)
            {
                // Play modunda Destroy kare sonunda çalışır; aynı karede yenisiyle karışmasın diye adını değiştir.
                prevCustom.name = CustomSkinHolderName + "_Old";
                DestroySafe(prevCustom.gameObject);
            }

            if (prefab == null)
            {
                if (defaultModel != null) defaultModel.gameObject.SetActive(true);
                else
                {
                    var boxR = targetBox.GetComponent<MeshRenderer>();
                    if (boxR != null) boxR.enabled = true;
                }
                return null;
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
            return model;
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

        static void DestroySafe(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
