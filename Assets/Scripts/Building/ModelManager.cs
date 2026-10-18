using System.Collections.Generic;
using AftershockIsle.Characters;
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
        [Tooltip("Sahnedeki Su_Deposu bir kez su bidonunun katına taşındı mı. Taşındıktan sonra depo istenen yere sürüklenebilir.")]
        [SerializeField] bool waterTankRelocated;
        [Tooltip("Bodrumdaki Bypass Vanası'na giydirilen vana modeli (Assets/Models/vana.fbx)")]
        [SerializeField] GameObject bypassValvePrefab;

        [Header("4. Kolon Seviye Modelleri (Assets/Models/Columns)")]
        [Tooltip("Lv1: Kolon_Lv1_Kirik.fbx")]
        [SerializeField] GameObject columnLv1Prefab;
        [Tooltip("Lv2: Kolon_Lv2_Catlak.fbx")]
        [SerializeField] GameObject columnLv2Prefab;
        [Tooltip("Lv3: Kolon_Lv3_Guclendirilmis.fbx")]
        [SerializeField] GameObject columnLv3Prefab;
        [Tooltip("Kolon modelinin Y eksenindeki dönüşü (hasarlı yüz kameraya baksın diye)")]
        [SerializeField] float columnYaw = 180f;

        [Header("6. Arka Plan, Sis ve Toprak")]
        [Tooltip("Arka plan görselinin binanın arka duvarından kaç metre geride durduğu")]
        [SerializeField, Range(0f, 60f)] float backdropDistance = 18f;
        [Tooltip("Arka plan görselinin sis rengine doğru ne kadar soldurulacağı (0 = olduğu gibi)")]
        [SerializeField, Range(0f, 1f)] float backdropTint = 0.08f;
        [Tooltip("Ufuk sisinin yoğunluğu (0 = sis perdesi yok)")]
        [SerializeField, Range(0f, 1f)] float hazeDensity = 0.3f;
        [SerializeField] Color hazeColor = new Color(0.62f, 0.68f, 0.72f, 1f);
        [Tooltip("Giriş katı seviyesindeki toprak şeridinin rengi")]
        [SerializeField] Color earthColor = new Color(0.34f, 0.27f, 0.2f, 1f);

        [Header("5. Karanlık Atmosfer (This War of Mine tarzı)")]
        [Tooltip("İşaretliyken ortam karanlık olur; ışığı pencerelerden giren gün ışığı ve odalardaki ampuller verir")]
        [SerializeField] bool enableDarkTheme = true;
        [Tooltip("Binanın arkasından pencerelerden giren soğuk gün ışığının şiddeti")]
        [SerializeField, Range(0f, 2f)] float darkSunIntensity = 0.7f;
        [SerializeField] Color sunColor = new Color(0.72f, 0.78f, 0.88f);
        [Tooltip("Hiç ışık almayan köşelerin seviyesi (düşük = zifiri karanlık)")]
        [SerializeField, Range(0f, 0.6f)] float darkAmbientLevel = 0.06f;
        [Tooltip("Abajurlu tavan lambası ışığı (soğuk, hafif mavimsi beyaz)")]
        [SerializeField] Color pendantLampColor = new Color(0.82f, 0.9f, 1f);
        [SerializeField, Range(0f, 10f)] float pendantLampIntensity = 2.2f;
        [SerializeField, Range(1f, 12f)] float pendantLampRange = 5.5f;
        [Tooltip("Ekran kenarlarını karartan siyah vinyetin gücü (0 = yok)")]
        [SerializeField, Range(0f, 1f)] float screenVignette = 0.42f;
        [SerializeField, Range(0.01f, 1f)] float screenVignetteSoftness = 0.45f;
        [Tooltip("Abajurun altından çıkan ışık konisinin açısı")]
        [SerializeField, Range(60f, 170f)] float pendantLampAngle = 115f;
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
            AssignDefaultModelsInEditor();
            ApplyBuilding();
            ApplyWalkBlockers();
            ApplyWorldBackdrop();
            // Ek ampuller bina modelinin altına asılır; model kurulduktan sonra.
            ApplyLightingTheme();
            // Önce eski greybox eşyaları elenir: tezgah/tulumba giydirmesi sahneye konan asıl eşyayı bulsun.
            ApplyGreyboxPropsVisibility();
            ApplyFloorPlanks();
            ApplyWorkbench();
            ApplyWaterTank();
            ApplyPropModels();
            ApplySketchPropMaterials();
            ApplyColumns();
#if UNITY_EDITOR
            // Bina malzeme dosyalarını yenilemede bir kez güncelle; oyun sırasında asset yazma.
            if (!Application.isPlaying) UpgradeExtractedBuildingMaterialsInEditor();
#endif
        }

        /// <summary>Boş bırakılmış model alanlarına projedeki bilinen modeli atar (sahne kaydedilince kalıcı olur).</summary>
        void AssignDefaultModelsInEditor()
        {
#if UNITY_EDITOR
            if (waterTankPrefab == null)
            {
                waterTankPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/WaterTank/Su_Deposu.fbx");
                if (waterTankPrefab != null && !Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
            }
            if (bypassValvePrefab == null)
            {
                bypassValvePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/vana.fbx");
                if (bypassValvePrefab != null && !Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
            }
            if (handPumpPrefab == null)
            {
                handPumpPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PropModelFolder + "El_Tulumbasi.fbx");
                if (handPumpPrefab != null && !Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
            }
            if (propModels == null || propModels.Length == 0)
            {
                var list = new List<PropModelSlot>();
                foreach (var (prop, file, height) in DefaultPropModels)
                {
                    list.Add(new PropModelSlot
                    {
                        propName = prop,
                        model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PropModelFolder + file + ".fbx"),
                        height = height,
                        yaw = 180f,
                    });
                }
                propModels = list.ToArray();
                if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
            }
            if (structureModels == null || structureModels.Length == 0)
            {
                var list = new List<StructureModelSlot>();
                foreach (var recipe in DefaultStructureModels)
                {
                    list.Add(new StructureModelSlot
                    {
                        recipe = recipe,
                        model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(StructureModelFolder + recipe + ".fbx"),
                    });
                }
                structureModels = list.ToArray();
                if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
            }
#endif
        }

#if UNITY_EDITOR
        bool editorRefreshQueued;

        // Inspector'da bir model değişince ya da kod derlenince eşya modelleri editörde de sahneye yerleşir (Play gerekmez).
        void OnValidate()
        {
            if (Application.isPlaying || editorRefreshQueued) return;
            editorRefreshQueued = true;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                editorRefreshQueued = false;
                if (this == null || Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;
                RefreshPropModelsInEditor();
            };
        }

        /// <summary>
        /// Editörde yalnızca eşya giydirmeleri yenilenir (bina modeli değil): atanan prefab'lar gri kutuların yerine sahneye
        /// konur ve sahneyle kaydedilir. Bina dahil her şey için sağ tık → "Modelleri Şimdi Güncelle".
        /// </summary>
        [ContextMenu("Eşya Modellerini Sahneye Yerleştir")]
        public void RefreshPropModelsInEditor()
        {
            if (building == null) building = FindFirstObjectByType<BuildingLayout>();
            if (building == null) return;
            AssignDefaultModelsInEditor();
            ApplyGreyboxPropsVisibility();
            ApplyWorkbench();
            ApplyWaterTank();
            ApplyPropModels();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif

        // ---------------------------------------------------------------- 1. Bina

        const string WalkBlockersName = "Yürüme Engelleri (Model)";

        // Blender binasının bölme duvarları (bina yerel X; kalınlık 0.2 m). Kapı aralığı Z -0.6..0.6, duvar Z -2..2.
        static readonly (int floor, float x)[] ModelPartitions =
        {
            (1, -4.5f), (1, 4f),
            (2, -1f), (2, 4.5f),
            (3, -3f), (3, 3f),
        };

        // Döşemedeki kırık delikler (kat, X aralığı, Z aralığı). Merdiven boşlukları tırmanma yeri olduğu için yok.
        static readonly (int floor, float minX, float maxX, float minZ, float maxZ)[] ModelFloorHoles =
        {
            (1, -1.6f, -0.2f, -1.8f, -0.2f),
            (3, 0.2f, 1.6f, -1.8f, -0.2f),
        };

        /// <summary>
        /// Blender binasında içinden geçilmemesi gereken yerler: bölme duvarları (kapı aralığı hariç; kapalı kapıyı
        /// BuildingDoor kapatır) ve döşeme delikleri. Fizik collider'ı değil, FloorObstacles'ın okuduğu kutulardır.
        /// </summary>
        void ApplyWalkBlockers()
        {
            var old = building.transform.Find(WalkBlockersName);
            if (old != null) DestroySafe(old.gameObject);
            if (!useCustomBuilding || buildingPrefab == null) return;

            var root = new GameObject(WalkBlockersName) { hideFlags = HideFlags.DontSave }.transform;
            root.SetParent(building.transform, false);

            const float doorHalf = 0.6f;
            float halfDepth = building.Depth * 0.5f;
            float h = building.FloorHeight;
            foreach (var (floor, x) in ModelPartitions)
            {
                float y = floor * h;
                float segment = halfDepth - doorHalf;
                AddWalkBlocker(root, $"Bölme_{floor}_{x:0.#}_Ön", new Vector3(x, y + h * 0.5f, -doorHalf - segment * 0.5f), new Vector3(0.2f, h, segment));
                AddWalkBlocker(root, $"Bölme_{floor}_{x:0.#}_Arka", new Vector3(x, y + h * 0.5f, doorHalf + segment * 0.5f), new Vector3(0.2f, h, segment));
            }

            foreach (var (floor, minX, maxX, minZ, maxZ) in ModelFloorHoles)
            {
                AddWalkBlocker(root, $"Delik_{floor}",
                    new Vector3((minX + maxX) * 0.5f, floor * h + 0.5f, (minZ + maxZ) * 0.5f),
                    new Vector3(maxX - minX, 1f, maxZ - minZ));
            }
        }

        static void AddWalkBlocker(Transform root, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(root, false);
            go.AddComponent<WalkBlocker>().Configure(center, size, false);
        }

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
                    bool isEarth = renderer.name.StartsWith("Earth_");
                    renderer.enabled = showGreybox || isEarth;
                    if (!isEarth) continue;

                    // Toprak giriş katı seviyesinde ufka kadar uzar; ön yüzü (kesit) yerinde kalır, arkası arka plana değer.
                    float frontZ = -earthDepth * 0.5f;
                    float backZ = BackdropZ() - 0.3f;
                    var lp = renderer.transform.localPosition;
                    var ls = renderer.transform.localScale;
                    float x = lp.x;
                    float width = ls.x;
                    if (renderer.name == "Earth_L" || renderer.name == "Earth_R")
                    {
                        // Yan toprak binaya yakın kenarını korur, dışa doğru arka planın genişliğine kadar uzar.
                        float dir = renderer.name == "Earth_L" ? -1f : 1f;
                        float inner = lp.x - dir * ls.x * 0.5f;
                        width = 160f;
                        x = inner + dir * width * 0.5f;
                    }
                    renderer.transform.localPosition = new Vector3(x, lp.y, (frontZ + backZ) * 0.5f);
                    renderer.transform.localScale = new Vector3(width, ls.y, backZ - frontZ);

                    var block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block);
                    block.SetColor("_BaseColor", earthColor);
                    block.SetColor("_Color", earthColor);
                    renderer.SetPropertyBlock(block);
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
        /// Arka plan görselinin dünyada durduğu derinlik (bina yerel Z). Binanın arka duvarından backdropDistance kadar geride.
        /// Toprak şeridi de buraya kadar uzar, böylece sokak ufka doğru devam eder.
        /// </summary>
        float BackdropZ() => building.Depth * 0.5f + building.WallThickness + 0.65f + backdropDistance;

        /// <summary>
        /// Arka plan görselini binanın çok gerisine, dünyaya sabit olarak yerleştirir (kamera kaysa da yerinde durur, hafif paralaks verir).
        /// Uzaklaştıkça küçülmesin diye boyutu mesafeyle orantılı büyür; altı zemin seviyesindedir (giriş katı),
        /// önünde ufka doğru yoğunlaşan bir sis perdesi durur.
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

            var root = new GameObject(BackdropName).transform;
            root.SetParent(building.transform, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            // Eski (yakın) yerleşimle aynı görünür boyut: kameradan uzaklığa oranla büyüt.
            const float nominalCameraZ = -22.5f;
            float nearZ = building.Depth * 0.5f + building.WallThickness + 0.65f;
            float backZ = BackdropZ();
            float distanceScale = (backZ - nominalCameraZ) / (nearZ - nominalCameraZ);

            float groundY = building.FloorHeight; // giriş katı = sokak seviyesi
            float worldHeight = 24f * distanceScale;

            Vector2 spriteSize = bg.bounds.size;
            float scale = worldHeight / Mathf.Max(0.01f, spriteSize.y);
            float panelWidth = Mathf.Max(0.01f, spriteSize.x) * scale;
            float centerY = groundY + worldHeight * 0.45f;

            // Ortada ana panel, sağ ve solda aynalanmış devam panelleri: kamera nereye kayarsa kaysın kesintisiz silüet.
            for (int side = -1; side <= 1; side++)
            {
                var panel = new GameObject($"CityPanel_{side}").AddComponent<SpriteRenderer>();
                panel.transform.SetParent(root, false);
                panel.transform.localPosition = new Vector3(side * (panelWidth - 0.05f), centerY, backZ);
                panel.transform.localRotation = Quaternion.identity;
                panel.transform.localScale = new Vector3(scale, scale, 1f);
                panel.sprite = bg;
                panel.flipX = side != 0;
                panel.color = Color.Lerp(Color.white, hazeColor, backdropTint);
                panel.sortingOrder = -30000;
                panel.shadowCastingMode = ShadowCastingMode.Off;
                panel.receiveShadows = false;
            }

            // Zemin seviyesinin altı: görselin altından aşağı doğru dolgu (toprak/sis rengi).
            var skirt = GameObject.CreatePrimitive(PrimitiveType.Quad);
            skirt.name = "UndergroundSkirt";
            skirt.transform.SetParent(root, false);
            const float skirtHeight = 60f;
            skirt.transform.localPosition = new Vector3(0f, groundY - skirtHeight * 0.5f + 0.5f, backZ + 0.02f);
            skirt.transform.localRotation = Quaternion.identity;
            skirt.transform.localScale = new Vector3(panelWidth * 3f, skirtHeight, 1f);
            var col = skirt.GetComponent<Collider>();
            if (col != null) DestroySafe(col);
            var r = skirt.GetComponent<Renderer>();
            if (r != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                var mat = new Material(shader) { name = "Backdrop_Underground_Unlit" };
                Color ground = new Color(0.17f, 0.14f, 0.11f, 1f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", ground);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", ground);
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            var haze = CreateHaze(root, panelWidth * 3f, worldHeight, groundY, backZ - 1f);
            if (Application.isPlaying)
                root.gameObject.AddComponent<BackdropAtmosphere>().Init(haze, root.GetComponentsInChildren<SpriteRenderer>(),
                    hazeColor, hazeDensity, backdropTint);

            if (Camera.main != null)
                Camera.main.backgroundColor = Color.Lerp(new Color(0.553f, 0.620f, 0.667f, 1f), hazeColor, 0.5f);
        }

        /// <summary>
        /// Arka planın önünde, ufka doğru (alta) yoğunlaşan yarı saydam sis perdesi. Doku beyazdır; rengi ve yoğunluğu
        /// SpriteRenderer.color ile verilir (havaya göre BackdropAtmosphere değiştirir).
        /// </summary>
        SpriteRenderer CreateHaze(Transform root, float width, float height, float groundY, float z)
        {
            const int steps = 64;
            var tex = new Texture2D(1, steps, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "BackdropHaze",
            };
            for (int y = 0; y < steps; y++)
            {
                float t = y / (steps - 1f); // 0 = zemin, 1 = tepe
                float alpha = Mathf.Pow(1f - Mathf.Clamp01(t / 0.4f), 1.6f);
                tex.SetPixel(0, y, new Color(1f, 1f, 1f, alpha));
            }
            tex.Apply(false, true);

            // 1 piksel = 1 dünya birimi; yatayda genişliğe, dikeyde yüksekliğe gerilir.
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, steps), new Vector2(0.5f, 0f), 1f);
            var haze = new GameObject("Sis").AddComponent<SpriteRenderer>();
            haze.transform.SetParent(root, false);
            haze.transform.localPosition = new Vector3(0f, groundY - 0.1f, z);
            haze.transform.localScale = new Vector3(width, height / steps, 1f);
            haze.sprite = sprite;
            haze.color = new Color(hazeColor.r, hazeColor.g, hazeColor.b, hazeDensity);
            haze.sortingOrder = -29990;
            haze.shadowCastingMode = ShadowCastingMode.Off;
            haze.receiveShadows = false;
            return haze;
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
                // Etkileşim konturu kendi malzemesini kullanır.
                if (renderer.gameObject.name == AftershockIsle.UI.InteractableHighlighter.ShellName) continue;

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
                    else if (mat.name.StartsWith(CrackMaterialPrefix))
                    {
                        shared[i] = GetCrackMaterial(mat);
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
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

        const string CrackMaterialPrefix = "Catlak_Doku_";

        /// <summary>
        /// Blender'daki çatlak çıkartmaları: dokusu Resources/Catlaklar'dan okunur, duvarın üstüne yarı saydam oturur
        /// ve ortam ışığıyla birlikte kararır (karanlık odada parlamasın diye Lit).
        /// </summary>
        Material GetCrackMaterial(Material source)
        {
            if (runtimeMaterials.TryGetValue(source, out var cached) && cached != null) return cached;

            string textureName = source.name.Substring(CrackMaterialPrefix.Length).Replace(" (Instance)", "");
            var texture = Resources.Load<Texture2D>("Catlaklar/" + textureName);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            var mat = new Material(shader) { name = source.name + "_Decal" };
            if (texture == null) Debug.LogWarning($"Çatlak dokusu bulunamadı: Resources/Catlaklar/{textureName}");
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
            if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 0f);
            if (mat.HasProperty("_EnvironmentReflections")) mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");

            // Saydam (alfa karışımlı), iki yüzlü, derinlik yazmaz.
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_Cull", (float)CullMode.Off);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;

            runtimeMaterials[source] = mat;
            return mat;
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
            else if (n.StartsWith("esya_"))
            {
                // Blender'daki eşya seti (mutfak, pano, pompa, kitaplık...): kendi rengi, üstünde hafif kir lekesi.
                patchColor = new Color(baseColor.r * 0.72f, baseColor.g * 0.7f, baseColor.b * 0.66f, 1f);
                noiseScale = 3.2f;
                noiseThreshold = 0.66f;
                noiseStrength = 0.55f;
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
        static Material shadeMaterial;
        static Mesh shadeMesh;

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
            ApplyScreenVignette();
            ApplySunShafts();
        }

        const string SunShaftsName = "Gün Işığı Hüzmeleri";
        static readonly Quaternion SunRotation = Quaternion.Euler(32f, 195f, 0f);
        static Material beamMaterial;

        [Header("6b. Pencereden Giren Güneş")]
        [Tooltip("Pencereden zemine vuran güneş lekesinin gücü (öğle). Gece LightingController bunu ay ışığına kısar.")]
        [SerializeField, Range(0f, 20f)] float sunPatchIntensity = 7f;
        [SerializeField] Color sunPatchColor = new Color(1f, 0.9f, 0.72f);
        [Tooltip("Pencereden zemine uzanan ışık sütununun görünürlüğü (0 = yok)")]
        [SerializeField, Range(0f, 1f)] float sunBeamOpacity = 0.22f;

        /// <summary>
        /// Her arka duvar penceresine: dışarıdan güneş yönünde içeri vuran spot ışığı (zemine ışık lekesi) ve pencereden
        /// zemine uzanan yarı saydam ışık sütunu. Gücü gün fazına göre SunShaft ile değişir.
        /// </summary>
        void ApplySunShafts()
        {
            var old = building.transform.Find(SunShaftsName);
            if (old != null) DestroySafe(old.gameObject);
            if (!useCustomBuilding || buildingPrefab == null) return;

            var root = new GameObject(SunShaftsName) { hideFlags = HideFlags.DontSave }.transform;
            root.SetParent(building.transform, false);

            var dir = SunRotation * Vector3.forward;              // bina yereli: arkadan öne ve aşağı
            float backZ = building.Depth * 0.5f;
            const float halfW = 0.6f, sill = 0.95f, top = 2.25f;

            foreach (var pair in ModelWindowX)
            {
                float floorY = pair.Key * building.FloorHeight;
                foreach (float x in pair.Value)
                {
                    var go = new GameObject($"Güneş_{pair.Key}_{x:0.#}");
                    go.transform.SetParent(root, false);

                    var center = new Vector3(x, floorY + (sill + top) * 0.5f, backZ);
                    go.transform.localPosition = center - dir * 1.6f;
                    go.transform.localRotation = Quaternion.LookRotation(dir);
                    var light = go.AddComponent<Light>();
                    light.type = LightType.Spot;
                    light.spotAngle = 48f;
                    light.innerSpotAngle = 20f;
                    light.range = 7f;
                    light.intensity = sunPatchIntensity;
                    light.color = sunPatchColor;
                    light.shadows = LightShadows.Soft;

                    Renderer beam = null;
                    if (sunBeamOpacity > 0.001f)
                    {
                        var beamGo = new GameObject("Hüzme");
                        beamGo.transform.SetParent(root, false);
                        beamGo.AddComponent<MeshFilter>().sharedMesh = BuildBeamMesh(x, floorY, backZ, halfW, sill, top, dir, -building.Depth * 0.5f);
                        beam = beamGo.AddComponent<MeshRenderer>();
                        beam.sharedMaterial = GetBeamMaterial();
                        beam.shadowCastingMode = ShadowCastingMode.Off;
                        beam.receiveShadows = false;
                    }

                    go.AddComponent<AftershockIsle.Effects.SunShaft>().Setup(light, beam,
                        new Color(sunPatchColor.r, sunPatchColor.g, sunPatchColor.b, sunBeamOpacity));
                }
            }
        }

        /// <summary>Pencere dikdörtgeninin köşeleri güneş yönünde zemine izdüşürülür; aradaki prizmanın dört yüzü ışık sütunudur.</summary>
        static Mesh BuildBeamMesh(float x, float floorY, float backZ, float halfW, float sill, float top, Vector3 dir, float frontZ)
        {
            Vector3 Project(Vector3 p)
            {
                float t = (p.y - floorY) / Mathf.Max(0.05f, -dir.y);
                var q = p + dir * t;
                if (q.z < frontZ)       // ön kenara ulaşırsa orada kes
                {
                    t = (p.z - frontZ) / Mathf.Max(0.05f, -dir.z);
                    q = p + dir * t;
                }
                return q;
            }

            var w = new[]
            {
                new Vector3(x - halfW, floorY + sill, backZ), new Vector3(x + halfW, floorY + sill, backZ),
                new Vector3(x + halfW, floorY + top, backZ), new Vector3(x - halfW, floorY + top, backZ),
            };
            var f = new Vector3[4];
            for (int i = 0; i < 4; i++) f[i] = Project(w[i]);

            var verts = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var tris = new System.Collections.Generic.List<int>();
            void Side(int a, int b)
            {
                int s = verts.Count;
                verts.AddRange(new[] { w[a], w[b], f[b], f[a] });
                uvs.AddRange(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
                tris.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
            Side(0, 1); Side(3, 2); Side(0, 3); Side(1, 2);

            var mesh = new Mesh { name = "Gunes_Huzmesi" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Toplamalı (additive), iki yüzlü, derinlik yazmayan ışık sütunu malzemesi; pencereden zemine doğru söner.</summary>
        static Material GetBeamMaterial()
        {
            if (beamMaterial != null) return beamMaterial;

            var tex = new Texture2D(32, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Huzme_Gradyan" };
            for (int y = 0; y < 64; y++)
            {
                float v = y / 63f;                                   // 0 = pencere, 1 = zemin
                float fade = Mathf.Pow(1f - v, 1.4f) * Mathf.SmoothStep(0f, 1f, v * 8f);
                for (int x = 0; x < 32; x++)
                {
                    float u = x / 31f;
                    float edge = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 0.7f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, fade * edge));
                }
            }
            tex.Apply();

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
            beamMaterial = new Material(shader) { name = "Gunes_Huzmesi" };
            if (beamMaterial.HasProperty("_BaseMap")) beamMaterial.SetTexture("_BaseMap", tex);
            beamMaterial.mainTexture = tex;
            beamMaterial.SetFloat("_Surface", 1f);
            beamMaterial.SetFloat("_Blend", 2f);
            beamMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            beamMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
            beamMaterial.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            beamMaterial.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            beamMaterial.SetFloat("_ZWrite", 0f);
            beamMaterial.SetFloat("_Cull", (float)CullMode.Off);
            beamMaterial.SetOverrideTag("RenderType", "Transparent");
            beamMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            beamMaterial.renderQueue = (int)RenderQueue.Transparent + 10;
            return beamMaterial;
        }

        public const string VignetteVolumeName = "Siyah Vinyet (Volume)";

        /// <summary>
        /// Ekran kenarlarına siyah vinyet. Sahnedeki atmosfer Volume'unun profiline dokunmadan, ondan yüksek öncelikli
        /// ayrı (kaydedilmeyen) bir global Volume ile eklenir; yalnızca vinyeti belirler.
        /// </summary>
        void ApplyScreenVignette()
        {
            var existing = GameObject.Find(VignetteVolumeName);
            if (existing != null)
            {
                var oldVolume = existing.GetComponent<Volume>();
                if (oldVolume != null && oldVolume.sharedProfile != null) DestroySafe(oldVolume.sharedProfile);
                DestroySafe(existing);
            }
            var go = new GameObject(VignetteVolumeName) { hideFlags = HideFlags.DontSave };
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 50f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "SiyahVinyet_Runtime";
            profile.hideFlags = HideFlags.DontSave;
            if (screenVignette > 0.001f)
            {
                var vignette = profile.Add<Vignette>(true);
                vignette.color.Override(Color.black);
                vignette.center.Override(new Vector2(0.5f, 0.5f));
                vignette.intensity.Override(screenVignette);
                vignette.smoothness.Override(screenVignetteSoftness);
                vignette.rounded.Override(false);
            }
            // Gece/gündüz pozlaması: LightingController gün fazına göre sürer (yalnızca pozlama değeri ezilir).
            profile.Add<ColorAdjustments>(true).postExposure.Override(0f);
            volume.sharedProfile = profile;
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

        /// <summary>Abajur ışığı yalnızca aşağı ve yanlara döker; tavan ve duvar üstleri karanlıkta kalır.</summary>
        void ConfigureLamp(Light light)
        {
            light.type = LightType.Spot;
            light.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            light.spotAngle = pendantLampAngle;
            // Geniş yumuşak kenar: ışık havuzu ortada toplanır, kenara doğru karanlığa erir.
            light.innerSpotAngle = pendantLampAngle * 0.25f;
            light.color = pendantLampColor;
            light.intensity = pendantLampIntensity;
            light.range = pendantLampRange;
            light.shadows = LightShadows.Soft;
        }

        /// <summary>Bina yerel konumunu (Blender ölçüsü) model tutucunun yerel uzayına çevirir: tutucu 180° döndürülmüş.</summary>
        static Vector3 ToHolderLocal(Vector3 buildingLocal) => Quaternion.Euler(0f, 180f, 0f) * buildingLocal;

        /// <summary>
        /// Tavandan kabloyla sarkan siyah kubbe abajur ve altında parlayan çıplak ampul (bloom ile parlar).
        /// Abajur gölge düşürmez; ışığın aşağı dökülmesini spot açısı sağlar.
        /// </summary>
        static void CreateBulbVisual(Transform holder, Vector3 localPosition)
        {
            bulbMaterial ??= CreateUnlit("Ampul_Isik", new Color(0.9f, 0.95f, 1f) * 3f);
            cordMaterial ??= CreateUnlit("Ampul_Kablo", new Color(0.03f, 0.03f, 0.03f));
            shadeMaterial ??= CreateUnlit("Ampul_Abajur", new Color(0.035f, 0.037f, 0.04f));
            if (shadeMaterial.HasProperty("_Cull")) shadeMaterial.SetFloat("_Cull", (float)CullMode.Off);
            shadeMesh ??= CreateShadeMesh();

            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = LampPrefix + "Gorsel";
            bulb.transform.SetParent(holder, false);
            bulb.transform.localPosition = localPosition;
            bulb.transform.localScale = Vector3.one * 0.1f;
            bulb.GetComponent<Renderer>().sharedMaterial = bulbMaterial;
            bulb.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            DestroySafe(bulb.GetComponent<Collider>());

            var shade = new GameObject(LampPrefix + "Abajur");
            shade.transform.SetParent(holder, false);
            shade.transform.localPosition = localPosition + Vector3.up * 0.01f;
            shade.AddComponent<MeshFilter>().sharedMesh = shadeMesh;
            var shadeRenderer = shade.AddComponent<MeshRenderer>();
            shadeRenderer.sharedMaterial = shadeMaterial;
            shadeRenderer.shadowCastingMode = ShadowCastingMode.Off;

            var cord = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cord.name = LampPrefix + "Kablo";
            cord.transform.SetParent(holder, false);
            cord.transform.localPosition = localPosition + Vector3.up * 0.42f;
            cord.transform.localScale = new Vector3(0.012f, 0.5f, 0.012f);
            cord.GetComponent<Renderer>().sharedMaterial = cordMaterial;
            cord.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            DestroySafe(cord.GetComponent<Collider>());
        }

        /// <summary>Altı açık, yayvan kubbe abajur: profil Y ekseni etrafında döndürülür (merkez ampul hizası).</summary>
        static Mesh CreateShadeMesh()
        {
            Vector2[] profile =
            {
                new Vector2(0.025f, 0.19f), new Vector2(0.07f, 0.18f), new Vector2(0.15f, 0.14f),
                new Vector2(0.22f, 0.08f), new Vector2(0.27f, 0.02f), new Vector2(0.285f, -0.01f),
            };
            const int segments = 24;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int s = 0; s <= segments; s++)
            {
                float a = s * Mathf.PI * 2f / segments;
                float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                foreach (var p in profile) vertices.Add(new Vector3(p.x * c, p.y, p.x * sn));
            }
            int ring = profile.Length;
            for (int s = 0; s < segments; s++)
            {
                for (int k = 0; k < ring - 1; k++)
                {
                    int a0 = s * ring + k, a1 = a0 + 1, b0 = a0 + ring, b1 = b0 + 1;
                    triangles.AddRange(new[] { a0, b0, a1, a1, b0, b1 });
                }
            }
            var mesh = new Mesh { name = "Ampul_Abajur" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
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
                if (mat == null || mat.name.StartsWith(CrackMaterialPrefix)) continue;

                // Yalnızca gerçekten değişen malzeme kirli işaretlenir: aksi halde her yenilemede .mat dosyaları yeniden
                // yazılır, Assets/Models altındaki bu yazım GameAssetsSync'i tetikler ve import döngüsü oluşur.
                string before = UnityEditor.EditorJsonUtility.ToJson(mat);

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
                if (UnityEditor.EditorJsonUtility.ToJson(mat) != before) UnityEditor.EditorUtility.SetDirty(mat);
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

        /// <summary>
        /// Su bidonu (bodrum suyu kullanmayan su kaynağı) sahneye elle konan su deposudur: depo başka kattaysa bidonun
        /// yerine taşınır, bidonun etkileşimi deponun üstüne oturur. Depo yoksa bidona su deposu modeli giydirilir.
        /// Bodrumdaki manuel tulumbaya el tulumbası modeli giydirilir.
        /// </summary>
        void ApplyWaterTank()
        {
            var placedTank = FindPlacedWaterTank();
            foreach (var source in FindObjectsByType<WaterSource>(FindObjectsSortMode.None))
            {
                if (source.UsesFloodWater || !source.transform.IsChildOf(building.transform)) continue;
                if (placedTank != null)
                {
                    // Depo yalnızca ilk seferde bidonun katına taşınır; sonra nereye çekilirse orada kalır, su alma da onu izler.
                    if (!waterTankRelocated)
                    {
                        MoveTankToSource(placedTank, source);
                        waterTankRelocated = true;
#if UNITY_EDITOR
                        if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
                    }
                    MoveInteractableOntoModel(source, placedTank);
                }
                else
                {
                    ApplyLookInPlay(SkinPropWithCustomPrefab(source.gameObject, waterTankPrefab, 0.95f, 180f));
                }
                break;
            }

            var pump = FindFirstObjectByType<ManualPump>();
            if (pump == null) return;

            // Eski sürüm tulumbayı depo merkezine taşımıştı: kutu tabanı yeniden zemine otursun.
            var t = pump.transform;
            float floorY = building.FloorY(building.FloorAtHeight(t.position.y));
            float bottom = t.position.y - t.lossyScale.y * 0.5f;
            if (Mathf.Abs(bottom - floorY) > 0.02f) t.position += Vector3.up * (floorY - bottom);
            pump.RefreshFloorFromPosition();

            ApplyLookInPlay(SkinPropWithCustomPrefab(pump.gameObject, handPumpPrefab, 1.27f, 180f));
        }

        /// <summary>Depo bidonun katında değilse (örn. bodruma konmuşsa) tabanı bidon kutusunun tabanına gelecek şekilde taşınır.</summary>
        void MoveTankToSource(GameObject tank, WaterSource source)
        {
            var bounds = CalculateBounds(tank);
            int tankFloor = building.FloorAtHeight(bounds.min.y + 0.05f);
            var st = source.transform;
            int sourceFloor = building.FloorAtHeight(st.position.y);
            if (tankFloor == sourceFloor) return;

            var target = st.position + Vector3.down * (st.lossyScale.y * 0.5f);
            var current = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            tank.transform.position += target - current;
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(tank.transform);
#endif
        }

        const string PlacedWaterTankName = "Su_Deposu";

        /// <summary>Bina dışında (sahnede) duran, adı Su_Deposu ile başlayan obje: elle yerleştirilmiş depo.</summary>
        GameObject FindPlacedWaterTank()
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!t.name.StartsWith(PlacedWaterTankName) || t.IsChildOf(building.transform)) continue;
                if (t.parent != null && t.parent.name.StartsWith(PlacedWaterTankName)) continue;
                if (t.GetComponentInChildren<Renderer>() == null) continue;
                return t.gameObject;
            }
            return null;
        }

        /// <summary>
        /// Eşyanın gri kutusunu ve önceki giydirmesini gizler, etkileşimi elle konan modelin üstüne oturtur:
        /// tıklama alanı ve yürüme engeli modelin sınırı olur.
        /// </summary>
        void MoveInteractableOntoModel(Interactable interactable, GameObject tank)
        {
            var go = interactable.gameObject;
            var oldSkin = go.transform.Find(CustomSkinHolderName);
            if (oldSkin != null) DestroySafe(oldSkin.gameObject);
            var defaultModel = go.transform.Find("Model");
            if (defaultModel != null) defaultModel.gameObject.SetActive(false);
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;

            var bounds = CalculateBounds(tank);
            go.transform.position = bounds.center;
            FitBoxColliderToBounds(go, bounds);
            interactable.RefreshFloorFromPosition();
        }

        // ---------------------------------------------------------------- 3b. Gri kutu eşyalara model

        const string PropModelFolder = "Assets/Resources/Esyalar/";
        const string StructureModelFolder = "Assets/Resources/Yapilar/";

        // Inspector'daki listeler boşsa editörün dolduracağı varsayılanlar: sahne kurucunun kutusu → model dosyası, yükseklik.
        static readonly (string prop, string model, float height)[] DefaultPropModels =
        {
            ("Ana Şalter Panosu", "Ana_Salter_Panosu", 1.61f),
            ("Eski Radyatör", "Eski_Radyator", 0.99f),
            ("İlk Yardım Dolabı", "Ilk_Yardim_Dolabi", 0.61f),
            ("Eski Bilgisayar", "Eski_Bilgisayar", 0.92f),
            ("Telsiz İstasyonu", "Telsiz_Istasyonu", 1.71f),
            ("Gaz Vanası", "Gaz_Vanasi", 0.46f),
        };

        static readonly RecipeId[] DefaultStructureModels =
        {
            RecipeId.ElectricPump, RecipeId.LightningRod, RecipeId.ComfortCorner, RecipeId.ShelterHeater, RecipeId.StorageRack,
            RecipeId.WaterPurifier, RecipeId.MedStation, RecipeId.FireSuppressor, RecipeId.BackupGenerator,
        };

        [System.Serializable]
        public struct PropModelSlot
        {
            [Tooltip("Props altındaki gri kutunun adı (örn. Eski Radyatör)")]
            public string propName;
            public GameObject model;
            [Tooltip("Modelin ölçekleneceği yükseklik (m)")]
            public float height;
            [Tooltip("Y dönüşü; ön yüz kameraya bakmıyorsa 0 / 180 dene")]
            public float yaw;
        }

        [System.Serializable]
        public struct StructureModelSlot
        {
            public RecipeId recipe;
            public GameObject model;
        }

        [Header("7. Eşya Modelleri (prefab ata, sahnede hemen görünür)")]
        [Tooltip("Gri kutu eşyalara giydirilen modeller. Boşsa editör Resources/Esyalar'dakilerle doldurur.")]
        [SerializeField] PropModelSlot[] propModels;
        [Tooltip("Bodrumdaki manuel tulumbaya giydirilen model")]
        [SerializeField] GameObject handPumpPrefab;
        [Tooltip("Izgarayla yerleştirilen yapıların modelleri (soba, raf, jeneratör...). Boşsa Resources/Yapilar kullanılır.")]
        [SerializeField] StructureModelSlot[] structureModels;

        /// <summary>Yerleştirilen yapının Inspector'da atanmış modeli (yoksa null).</summary>
        public GameObject GetStructureModel(RecipeId recipe)
        {
            if (structureModels == null) return null;
            foreach (var slot in structureModels)
                if (slot.recipe == recipe && slot.model != null) return slot.model;
            return null;
        }

        /// <summary>Cel görünümü yalnızca oyunda verilir: editörde çalışma anı malzemeleri sahneye kaydedilemez.</summary>
        void ApplyLookInPlay(GameObject model)
        {
            if (model != null && Application.isPlaying) FixBuildingMaterials(model);
        }

        /// <summary>
        /// Modeli olmayan gri kutu eşyalara Inspector'daki modelleri giydirir; Bypass Vanası'na vana.fbx giyer.
        /// Oyun mantığı (collider, Interactable) kutuda kalır. Editörde de çalışır, model sahneye kaydedilir.
        /// </summary>
        void ApplyPropModels()
        {
            var propsRoot = building.transform.Find("Props");
            if (propsRoot == null || !useCustomBuilding || buildingPrefab == null) return;

            if (propModels != null)
            {
                foreach (var slot in propModels)
                {
                    if (string.IsNullOrEmpty(slot.propName)) continue;
                    var prop = propsRoot.Find(slot.propName);
                    if (prop == null || slot.model == null || !prop.gameObject.activeSelf) continue;
                    ApplyLookInPlay(SkinPropWithCustomPrefab(prop.gameObject, slot.model, slot.height > 0f ? slot.height : 1f, slot.yaw));
                }
            }

            // Telsizin durum ışığı kutu yerine telsizin üstünde küçük bir lamba olsun.
            var radioLight = propsRoot.Find("Telsiz Işığı");
            if (radioLight != null && radioLight.TryGetComponent<MeshFilter>(out var lightFilter))
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lightFilter.sharedMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
                DestroySafe(sphere);
                radioLight.localScale = Vector3.one * 0.12f;
            }

            var bypass = propsRoot.Find("Bypass Vanası");
            if (bypass != null && bypassValvePrefab != null)
            {
                ApplyLookInPlay(SkinPropWithCustomPrefab(bypass.gameObject, bypassValvePrefab, 0.45f, 0f));
                // Vana tarifi üretilene kadar gizli kalır: yeni modelin görünürlüğü de buna uysun.
                if (Application.isPlaying && bypass.TryGetComponent<InstallableVisual>(out var installable)) installable.Refresh();
            }
        }

        /// <summary>Çalışma anında kurulan bir modele (yerleştirilen yapılar) binanın Cel görünümünü ve dış hattını verir.</summary>
        public static void ApplyBuildingLook(GameObject model)
        {
            if (Instance != null && model != null) Instance.FixBuildingMaterials(model);
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
                    if (mat == null || !(mat.name.StartsWith("Cizim_") || mat.name.StartsWith("Esya_"))) continue;
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
                    if (interactable is SmokeWindow smokeWindow)
                    {
                        AlignSmokeWindowToModel(smokeWindow, propsRoot);
                        continue;
                    }

                    if (interactable != null)
                    {
                        bool replaced = IsReplacedByPlacedItem(interactable, placed);
                        prop.gameObject.SetActive(!replaced);
                        if (!replaced)
                        {
                            RestoreRenderers(prop);
                            FitColliderToDefaultModel(interactable);
                        }
                        continue;
                    }

                    // Dış pencerenin camı: pencere artık Blender modelinde.
                    if (prop.name.StartsWith("Cam_"))
                    {
                        foreach (var renderer in prop.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
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

        // Blender binasındaki arka duvar pencerelerinin merkez X'leri (kat → pencereler). Pencere 1.2 x 1.3 m, denizlik 0.95 m.
        static readonly Dictionary<int, float[]> ModelWindowX = new Dictionary<int, float[]>
        {
            { 1, new[] { -6.2f, 0f, 5.7f } },
            { 2, new[] { -4.75f, 0.3f, 3.2f, 6.2f } },
            { 3, new[] { -5f, 0f } },
        };

        /// <summary>
        /// Gri kutu "Dış Pencere" görünmez olur ve o kattaki en yakın gerçek pencerenin üstüne taşınır;
        /// pencereyi kırma etkileşimi böylece modeldeki pencereden yapılır.
        /// </summary>
        void AlignSmokeWindowToModel(SmokeWindow window, Transform propsRoot)
        {
            foreach (var renderer in window.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;

            var t = window.transform;
            int floor = Mathf.RoundToInt((building.transform.InverseTransformPoint(t.position).y - 1.6f) / building.FloorHeight);
            if (!ModelWindowX.TryGetValue(floor, out var xs)) return;

            Vector3 local = building.transform.InverseTransformPoint(t.position);
            float bestX = xs[0];
            foreach (float x in xs)
                if (Mathf.Abs(x - local.x) < Mathf.Abs(bestX - local.x)) bestX = x;

            float backZ = building.Depth * 0.5f;
            t.position = building.transform.TransformPoint(new Vector3(bestX, floor * building.FloorHeight + 1.6f, backZ - 0.03f));

            if (window.TryGetComponent<BoxCollider>(out var box))
            {
                Vector3 s = t.lossyScale;
                box.center = Vector3.zero;
                box.size = new Vector3(1.2f / Mathf.Max(0.001f, s.x), 1.3f / Mathf.Max(0.001f, s.y), 0.2f / Mathf.Max(0.001f, s.z));
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

                // Tıklama ve yürüme engeli eski kutunun değil, görünen modelin sınırı olsun.
                FitBoxColliderToBounds(targetBox, CalculateBounds(model));
            }

            var boxRenderer = targetBox.GetComponent<MeshRenderer>();
            if (boxRenderer != null) boxRenderer.enabled = false;
            return model;
        }

        /// <summary>Kutuya giydirilmiş varsayılan "Model" çocuğu varsa collider o modelin sınırına oturur.</summary>
        static void FitColliderToDefaultModel(Interactable interactable)
        {
            var model = interactable.transform.Find("Model");
            if (model == null || !model.gameObject.activeInHierarchy) return;
            if (model.GetComponentInChildren<Renderer>() == null) return;
            FitBoxColliderToBounds(interactable.gameObject, CalculateBounds(model.gameObject));
        }

        /// <summary>Nesnenin BoxCollider'ını dünya sınırına oturtur (nesne dönük/ölçekli olabilir).</summary>
        static void FitBoxColliderToBounds(GameObject target, Bounds worldBounds)
        {
            var box = target.GetComponent<BoxCollider>();
            if (box == null || worldBounds.size.sqrMagnitude < 0.0001f) return;

            var t = target.transform;
            var x = t.InverseTransformVector(new Vector3(worldBounds.size.x, 0f, 0f));
            var y = t.InverseTransformVector(new Vector3(0f, worldBounds.size.y, 0f));
            var z = t.InverseTransformVector(new Vector3(0f, 0f, worldBounds.size.z));
            box.center = t.InverseTransformPoint(worldBounds.center);
            box.size = new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            FloorObstacles.Invalidate();
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
