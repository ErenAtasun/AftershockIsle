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
        [SerializeField] bool addWarmWindowBacking;

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
            ApplyWorldBackdrop();
            ApplyFloorPlanks();
            ApplyWorkbench();
            ApplyWaterTank();
            ApplyColumns();
            ApplyGreyboxPropsVisibility();
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
                float earthDepth = building.Depth + 2f * building.WallThickness;
                foreach (var renderer in geo.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null) continue;
                    renderer.enabled = showGreybox;
                    if (renderer.name.StartsWith("Earth_"))
                    {
                        var lp = renderer.transform.localPosition;
                        var ls = renderer.transform.localScale;
                        renderer.transform.localPosition = new Vector3(lp.x, lp.y, 0f);
                        renderer.transform.localScale = new Vector3(ls.x, ls.y, earthDepth);
                    }
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

            bool hasWorldBg = AftershockIsle.Core.GameAssets.Ui != null &&
                              (AftershockIsle.Core.GameAssets.Ui.worldBackground != null || AftershockIsle.Core.GameAssets.Ui.mainMenuBackground != null);
            CleanBuildingInstance(instance, addWarmWindowBacking && !hasWorldBg);
            FixBuildingMaterials(instance);
        }

        /// <summary>
        /// Binanın hemen arkasına, binayla birebir aynı zemin/kat seviyesinde sabit duran şehir silüetini yerleştirir.
        /// Kamera hareket ettiğinde arka plan hareket etmez; arkada gerçek binalar duruyormuş gibi sabit kalır.
        /// </summary>
        void ApplyWorldBackdrop()
        {
            const string BackdropName = "WorldBackdrop";
            var oldCam = Camera.main != null ? Camera.main.transform.Find("Genel Arka Plan") : null;
            if (oldCam != null) DestroySafe(oldCam.gameObject);

            var existing = building.transform.Find(BackdropName);
            if (existing != null) DestroySafe(existing.gameObject);

            var skin = AftershockIsle.Core.GameAssets.Ui;
            Sprite bg = skin != null ? (skin.worldBackground != null ? skin.worldBackground : skin.mainMenuBackground) : null;
            if (bg == null) return;

            if (Camera.main != null)
                Camera.main.backgroundColor = new Color(0.553f, 0.620f, 0.667f, 1f);

            var root = new GameObject(BackdropName).transform;
            root.SetParent(building.transform, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            float backZ = building.Depth * 0.5f + building.WallThickness + 0.65f;
            const float bottomY = 0f;
            const float worldHeight = 24f;

            Vector2 spriteSize = bg.bounds.size;
            float spriteW = Mathf.Max(0.01f, spriteSize.x);
            float spriteH = Mathf.Max(0.01f, spriteSize.y);
            float scale = worldHeight / spriteH;
            float panelWidth = spriteW * scale;
            float centerY = bottomY + worldHeight * 0.5f;

            // Ortada ana panel, sağ ve solda aynalanmış devam panelleri: kamera nereye kayarsa kaysın kesintisiz şehir silüeti.
            for (int side = -1; side <= 1; side++)
            {
                var panel = new GameObject($"CityPanel_{side}").AddComponent<SpriteRenderer>();
                panel.transform.SetParent(root, false);
                panel.transform.localPosition = new Vector3(side * (panelWidth - 0.05f), centerY, backZ);
                panel.transform.localRotation = Quaternion.identity;
                panel.transform.localScale = new Vector3(scale, scale, 1f);
                panel.sprite = bg;
                panel.flipX = side != 0;
                panel.sortingOrder = -30000;
                panel.shadowCastingMode = ShadowCastingMode.Off;
                panel.receiveShadows = false;
            }

            // Bodrum katının altında kalan zemin kuşağı (arka planın alt koyu silüet rengiyle bütünleşir).
            var skirt = GameObject.CreatePrimitive(PrimitiveType.Quad);
            skirt.name = "UndergroundSkirt";
            skirt.transform.SetParent(root, false);
            const float skirtHeight = 24f;
            skirt.transform.localPosition = new Vector3(0f, bottomY - skirtHeight * 0.5f + 0.1f, backZ + 0.02f);
            skirt.transform.localRotation = Quaternion.identity;
            skirt.transform.localScale = new Vector3(panelWidth * 3f, skirtHeight, 1f);
            var col = skirt.GetComponent<Collider>();
            if (col != null) DestroySafe(col);
            var r = skirt.GetComponent<Renderer>();
            if (r != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                var mat = new Material(shader) { name = "Backdrop_Underground_Unlit" };
                Color darkGround = new Color(0.18f, 0.21f, 0.24f, 1f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", darkGround);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", darkGround);
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
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

            if (n.Contains("bodrum_duvar"))
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

            SkinPropWithCustomPrefab(pump.gameObject, waterTankPrefab, 1.25f, 180f);
        }

        // ---------------------------------------------------------------- 4. Kolonlar

        void ApplyColumns()
        {
            foreach (var column in FindObjectsByType<SupportColumn>(FindObjectsSortMode.None))
                column.ApplyVisual();
        }

        void ApplyGreyboxPropsVisibility()
        {
            bool useCustom = useCustomBuilding && buildingPrefab != null;
            var propsRoot = building.transform.Find("Props");
            if (propsRoot != null)
            {
                bool showOldProps = !useCustom || !hideGreyboxProps;
                foreach (var renderer in propsRoot.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null) continue;
                    renderer.enabled = showOldProps;
                }
            }

            if (useCustom && hideGreyboxProps)
            {
                var planks = building.transform.Find(PlanksHolderName);
                if (planks != null) planks.gameObject.SetActive(false);
            }
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
