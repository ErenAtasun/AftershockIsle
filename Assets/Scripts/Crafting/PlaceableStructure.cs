using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using AftershockIsle.Economy;
using AftershockIsle.Interaction;
using UnityEngine;

namespace AftershockIsle.Crafting
{
    public readonly struct StructurePlaced
    {
        public readonly PlaceableStructure Structure;
        public StructurePlaced(PlaceableStructure structure) => Structure = structure;
    }

    /// <summary>
    /// Oyuncunun Çalışma Tezgahında üretip sığınak ızgarası (ShelterGrid) üzerine yerleştirdiği yapı/eşya.
    /// Hem 3B çok parçalı görselini üretir hem de aktif/pasif bina içi etkilerini yönetir.
    /// </summary>
    public class PlaceableStructure : MonoBehaviour
    {
        static readonly List<PlaceableStructure> all = new List<PlaceableStructure>();

        public static IReadOnlyList<PlaceableStructure> All => all;

        public RecipeId Recipe { get; private set; }
        public int GridFloor { get; private set; }
        public int CellX { get; private set; }
        public int CellZ { get; private set; }
        public int RotationSteps { get; private set; }

        public RecipeInfo Info => RecipeCatalog.Get(Recipe);
        public int EffectiveWidth => (RotationSteps % 2 == 0) ? Info.GridWidth : Info.GridDepth;
        public int EffectiveDepth => (RotationSteps % 2 == 0) ? Info.GridDepth : Info.GridWidth;

        int daysActive;
        WaterSource attachedWaterSource;

        void OnEnable()
        {
            all.Add(this);
            EventBus.Subscribe<DayStarted>(OnDayStarted);
        }

        void OnDisable()
        {
            all.Remove(this);
            EventBus.Unsubscribe<DayStarted>(OnDayStarted);
            FloorObstacles.Invalidate();
        }

        public static bool HasAny(RecipeId id)
        {
            foreach (var s in all)
                if (s != null && s.isActiveAndEnabled && s.Recipe == id) return true;
            return false;
        }

        public static bool HasOnFloor(RecipeId id, int floor)
        {
            foreach (var s in all)
                if (s != null && s.isActiveAndEnabled && s.Recipe == id && s.GridFloor == floor) return true;
            return false;
        }

        public static int CountOnFloor(RecipeId id, int floor)
        {
            int count = 0;
            foreach (var s in all)
                if (s != null && s.isActiveAndEnabled && s.Recipe == id && s.GridFloor == floor) count++;
            return count;
        }

        public void Initialize(RecipeId recipe, int floor, int cellX, int cellZ, int rotationSteps)
        {
            Recipe = recipe;
            SetGridCoordinates(floor, cellX, cellZ, rotationSteps);
            BuildVisualsAndComponents();
            FloorObstacles.Invalidate();
        }

        public void SetGridCoordinates(int floor, int cellX, int cellZ, int rotationSteps)
        {
            GridFloor = floor;
            CellX = cellX;
            CellZ = cellZ;
            RotationSteps = ((rotationSteps % 4) + 4) % 4;
            transform.rotation = Quaternion.Euler(0f, RotationSteps * 90f, 0f);

            var interactable = GetComponent<Interactable>();
            if (interactable != null) interactable.RefreshFloorFromPosition();

            FloorObstacles.Invalidate();
        }

        void OnDayStarted(DayStarted evt)
        {
            if (evt.Day <= 1) return;

            if (Recipe == RecipeId.WaterPurifier)
            {
                if (attachedWaterSource != null)
                    attachedWaterSource.RestoreState(4);

                daysActive++;
                if (daysActive % 2 == 0 && ResourceStorage.Instance != null)
                {
                    ResourceStorage.Instance.Add(new ResourceAmount(ResourceType.Supplies, 1));
                }
            }
        }

        void BuildVisualsAndComponents()
        {
            var info = Info;
            gameObject.name = $"{info.Name}_{GridFloor}_{CellX}_{CellZ}";

            float widthM = info.GridWidth * 0.92f;
            float depthM = info.GridDepth * 0.92f;
            float heightM = GetColliderHeight(Recipe);

            var box = gameObject.GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, heightM * 0.5f, 0f);
            box.size = new Vector3(widthM, heightM, depthM);

            var visualRoot = new GameObject("Visuals").transform;
            visualRoot.SetParent(transform, false);
            BuildProceduralMesh(visualRoot, Recipe, false);

            AttachFunctionalComponent();
        }

        void AttachFunctionalComponent()
        {
            switch (Recipe)
            {
                case RecipeId.ElectricPump:
                    var pump = gameObject.AddComponent<ElectricPump>();
                    pump.ConfigureRuntime("Elektrikli Pompa", 5f);
                    break;

                case RecipeId.StorageRack:
                    var chest = gameObject.AddComponent<StorageChest>();
                    chest.ConfigureRuntime("Ek Depo Rafı", 0.5f);
                    break;

                case RecipeId.WaterPurifier:
                    attachedWaterSource = gameObject.AddComponent<WaterSource>();
                    attachedWaterSource.ConfigureRuntime("Su Arıtma İstasyonu", 1.5f);
                    attachedWaterSource.RestoreState(4);
                    break;

                case RecipeId.ComfortCorner:
                case RecipeId.ShelterHeater:
                case RecipeId.MedStation:
                case RecipeId.FireSuppressor:
                case RecipeId.BackupGenerator:
                case RecipeId.LightningRod:
                    var station = gameObject.AddComponent<ShelterStation>();
                    station.Setup(this);
                    break;
            }
        }

        public static float GetColliderHeight(RecipeId id) => id switch
        {
            RecipeId.ComfortCorner => 0.75f,
            RecipeId.ShelterHeater => 1.3f,
            RecipeId.StorageRack => 1.6f,
            RecipeId.WaterPurifier => 1.25f,
            RecipeId.MedStation => 1.0f,
            RecipeId.FireSuppressor => 1.15f,
            RecipeId.BackupGenerator => 1.1f,
            RecipeId.ElectricPump => 1.0f,
            RecipeId.LightningRod => 2.8f,
            _ => 1.0f
        };

        /// <summary>Hem gerçek yapı hem de hayalet önizleme (Ghost Preview) için çok parçalı 3B model kurar.</summary>
        public static void BuildProceduralMesh(Transform parent, RecipeId recipe, bool isGhost)
        {
            Color wood = new Color(0.52f, 0.35f, 0.2f);
            Color darkMetal = new Color(0.28f, 0.31f, 0.35f);
            Color steel = new Color(0.6f, 0.65f, 0.72f);
            Color fabricBlue = new Color(0.25f, 0.45f, 0.68f);
            Color white = new Color(0.9f, 0.9f, 0.92f);
            Color medicalRed = new Color(0.85f, 0.18f, 0.18f);
            Color hazardYellow = new Color(0.92f, 0.75f, 0.15f);
            Color emberOrange = new Color(1f, 0.45f, 0.1f);
            Color waterCyan = new Color(0.2f, 0.62f, 0.92f);
            Color copper = new Color(0.82f, 0.48f, 0.25f);

            switch (recipe)
            {
                case RecipeId.ComfortCorner:
                    // Ahşap yatak kasası + mavi şilte + yastık + başucu lambası
                    Part(parent, PrimitiveType.Cube, new Vector3(0f, 0.18f, 0f), new Vector3(1.75f, 0.28f, 0.85f), wood);
                    Part(parent, PrimitiveType.Cube, new Vector3(-0.82f, 0.45f, 0f), new Vector3(0.1f, 0.65f, 0.85f), wood);
                    Part(parent, PrimitiveType.Cube, new Vector3(0.04f, 0.36f, 0f), new Vector3(1.6f, 0.16f, 0.78f), fabricBlue);
                    Part(parent, PrimitiveType.Cube, new Vector3(-0.55f, 0.48f, 0f), new Vector3(0.32f, 0.1f, 0.55f), white);
                    break;

                case RecipeId.ShelterHeater:
                    // Döküm soba gövdesi + baca borusu + ateş çekirdeği + sıcak ışık
                    Part(parent, PrimitiveType.Cylinder, new Vector3(0f, 0.45f, 0f), new Vector3(0.62f, 0.45f, 0.62f), darkMetal);
                    Part(parent, PrimitiveType.Cylinder, new Vector3(0f, 1.15f, -0.1f), new Vector3(0.16f, 0.45f, 0.16f), steel);
                    Part(parent, PrimitiveType.Cube, new Vector3(0f, 0.42f, -0.28f), new Vector3(0.28f, 0.22f, 0.08f), emberOrange);
                    if (!isGhost)
                    {
                        var lightGo = new GameObject("HeaterLight");
                        lightGo.transform.SetParent(parent, false);
                        lightGo.transform.localPosition = new Vector3(0f, 0.9f, -0.4f);
                        var light = lightGo.AddComponent<Light>();
                        light.type = LightType.Point;
                        light.color = new Color(1f, 0.65f, 0.3f);
                        light.range = 9.5f;
                        light.intensity = 3.2f;
                    }
                    break;

                case RecipeId.StorageRack:
                    // İki katlı endüstriyel raf + sandık kutuları
                    Part(parent, PrimitiveType.Cube, new Vector3(0f, 0.08f, 0f), new Vector3(1.7f, 0.08f, 0.8f), darkMetal);
                    Part(parent, PrimitiveType.Cube, new Vector3(0f, 0.75f, 0f), new Vector3(1.7f, 0.08f, 0.8f), darkMetal);
                    Part(parent, PrimitiveType.Cube, new Vector3(0f, 1.45f, 0f), new Vector3(1.7f, 0.08f, 0.8f), darkMetal);
                    Part(parent, PrimitiveType.Cube, new Vector3(-0.78f, 0.78f, 0f), new Vector3(0.08f, 1.5f, 0.78f), steel);
                    Part(parent, PrimitiveType.Cube, new Vector3(0.78f, 0.78f, 0f), new Vector3(0.08f, 1.5f, 0.78f), steel);
                    Part(parent, PrimitiveType.Cube, new Vector3(-0.38f, 0.36f, 0f), new Vector3(0.55f, 0.45f, 0.6f), wood);
                    Part(parent, PrimitiveType.Cube, new Vector3(0.35f, 0.36f, 0f), new Vector3(0.55f, 0.45f, 0.6f), hazardYellow);
                    Part(parent, PrimitiveType.Cube, new Vector3(-0.2f, 1.05f, 0f), new Vector3(0.65f, 0.45f, 0.6f), wood);
                    break;

                case RecipeId.WaterPurifier:
                    // Metal ayak + mavi arıtma tankı + üst filtre hunisi
                    Part(parent, PrimitiveType.Cube, new Vector3(0f, 0.2f, 0f), new Vector3(0.7f, 0.4f, 0.7f), darkMetal);
                    Part(parent, PrimitiveType.Cylinder, new Vector3(0f, 0.75f, 0f), new Vector3(0.6f, 0.42f, 0.6f), waterCyan);
                    Part(parent, PrimitiveType.Cylinder, new Vector3(0f, 1.22f, 0f), new Vector3(0.38f, 0.12f, 0.38f), steel);
                    Part(parent, PrimitiveType.Cube, new Vector3(0f, 0.55f, -0.34f), new Vector3(0.12f, 0.12f, 0.18f), copper);
                    break;

                case RecipeId.MedStation:
                    // Revir sedyesi/masası + kırmızı haç + tıbbi dolap
                    Part(parent, PrimitiveType.Cube, new Vector3(-0.2f, 0.42f, 0f), new Vector3(1.25f, 0.78f, 0.8f), white);
                    Part(parent, PrimitiveType.Cube, new Vector3(-0.2f, 0.45f, -0.41f), new Vector3(0.42f, 0.14f, 0.04f), medicalRed);
                    Part(parent, PrimitiveType.Cube, new Vector3(-0.2f, 0.45f, -0.41f), new Vector3(0.14f, 0.42f, 0.04f), medicalRed);
                    Part(parent, PrimitiveType.Cube, new Vector3(0.62f, 0.58f, 0f), new Vector3(0.38f, 1.1f, 0.65f), steel);
                    Part(parent, PrimitiveType.Cube, new Vector3(0.62f, 0.85f, -0.34f), new Vector3(0.24f, 0.24f, 0.05f), medicalRed);
                    break;

                case RecipeId.FireSuppressor:
                    // İkiz kırmızı basınç tüpü + taban + siyah lans
                    Part(parent, PrimitiveType.Cube, new Vector3(0f, 0.08f, 0f), new Vector3(0.68f, 0.14f, 0.55f), darkMetal);
                    Part(parent, PrimitiveType.Cylinder, new Vector3(-0.16f, 0.58f, 0f), new Vector3(0.26f, 0.48f, 0.26f), medicalRed);
                    Part(parent, PrimitiveType.Cylinder, new Vector3(0.16f, 0.58f, 0f), new Vector3(0.26f, 0.48f, 0.26f), medicalRed);
                    Part(parent, PrimitiveType.Cube, new Vector3(0f, 1.08f, 0f), new Vector3(0.45f, 0.1f, 0.16f), steel);
                    break;

                case RecipeId.BackupGenerator:
                    // Sarı-siyah jeneratör gövdesi + bakır bobin + egzoz
                    Part(parent, PrimitiveType.Cube, new Vector3(0f, 0.45f, 0f), new Vector3(1.55f, 0.82f, 0.8f), hazardYellow);
                    Part(parent, PrimitiveType.Cube, new Vector3(-0.35f, 0.48f, -0.42f), new Vector3(0.65f, 0.55f, 0.05f), darkMetal);
                    Part(parent, PrimitiveType.Cylinder, new Vector3(0.42f, 0.5f, 0f), new Vector3(0.48f, 0.35f, 0.48f), copper);
                    Part(parent, PrimitiveType.Cylinder, new Vector3(-0.55f, 1.02f, 0.2f), new Vector3(0.14f, 0.25f, 0.14f), darkMetal);
                    break;

                case RecipeId.ElectricPump:
                    // Motor gövdesi + emiş/tahliye borusu
                    Part(parent, PrimitiveType.Cube, new Vector3(0f, 0.42f, 0f), new Vector3(0.78f, 0.78f, 0.72f), steel);
                    Part(parent, PrimitiveType.Cylinder, new Vector3(0f, 0.55f, -0.25f), new Vector3(0.42f, 0.22f, 0.42f), waterCyan);
                    Part(parent, PrimitiveType.Cube, new Vector3(0f, 0.88f, 0f), new Vector3(0.35f, 0.18f, 0.35f), hazardYellow);
                    break;

                case RecipeId.LightningRod:
                    // Çatı üçayak kaidesi + yüksek paratoner direği + bakır halkalar
                    Part(parent, PrimitiveType.Cube, new Vector3(0f, 0.15f, 0f), new Vector3(0.75f, 0.28f, 0.75f), darkMetal);
                    Part(parent, PrimitiveType.Cylinder, new Vector3(0f, 1.45f, 0f), new Vector3(0.12f, 1.35f, 0.12f), steel);
                    Part(parent, PrimitiveType.Cylinder, new Vector3(0f, 2.35f, 0f), new Vector3(0.32f, 0.05f, 0.32f), copper);
                    Part(parent, PrimitiveType.Sphere, new Vector3(0f, 2.75f, 0f), new Vector3(0.22f, 0.22f, 0.22f), copper);
                    break;

                default:
                    Part(parent, PrimitiveType.Cube, new Vector3(0f, 0.5f, 0f), new Vector3(0.8f, 0.9f, 0.8f), steel);
                    break;
            }
        }

        static GameObject Part(Transform parent, PrimitiveType type, Vector3 localPos, Vector3 localScale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var mat = new Material(shader) { color = color };
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                renderer.sharedMaterial = mat;
            }
            return go;
        }
    }

    /// <summary>
    /// Grid ile yerleştirilen yapıların sağ tık (Interactable) etkileşimlerini yürütür
    /// (Dinlenme Köşesi, Sığınak Sobası, Tıbbi Revir, Yangın Tüpü, Yedek Jeneratör, Paratoner).
    /// </summary>
    public class ShelterStation : Interactable
    {
        PlaceableStructure structure;

        public void Setup(PlaceableStructure owner)
        {
            structure = owner;
            ConfigureRuntime(owner.Info.Name, GetDuration(owner.Recipe));
        }

        static float GetDuration(RecipeId id) => id switch
        {
            RecipeId.ComfortCorner => 4f,
            RecipeId.ShelterHeater => 3f,
            RecipeId.MedStation => 4f,
            RecipeId.FireSuppressor => 2f,
            RecipeId.BackupGenerator => 1.5f,
            _ => 2f
        };

        public override bool AllowedInSmoke => structure != null && structure.Recipe == RecipeId.FireSuppressor;

        public override WorkCategory Category => structure != null && structure.Recipe == RecipeId.FireSuppressor
            ? WorkCategory.Firefight
            : WorkCategory.Operate;

        public override string ActionLabel
        {
            get
            {
                if (structure == null) return "Kullan";
                return structure.Recipe switch
                {
                    RecipeId.ComfortCorner => "Dinlen (-25 Stres)",
                    RecipeId.ShelterHeater => "Isın (-15 Stres)",
                    RecipeId.MedStation => "Tedavi Ol (1 Yaşam Kiti)",
                    RecipeId.FireSuppressor => "Tüple Söndür",
                    RecipeId.BackupGenerator => "Elektriği Aç",
                    RecipeId.LightningRod => "Paratoner (Aktif)",
                    _ => "Kullan"
                };
            }
        }

        public override string StatusText
        {
            get
            {
                if (structure == null) return null;
                return structure.Recipe switch
                {
                    RecipeId.ComfortCorner => "Kattaki boşta kazazedelerin stresini 2.5x hızlı düşürür. Sağ tıkla dinlen (-25 stres, +10 can).",
                    RecipeId.ShelterHeater => "Bu katta karanlıkta çalışma stresini sıfırlar. Sağ tıkla ısın (-15 stres).",
                    RecipeId.MedStation => "1 Yaşam Kiti ile +55 can iyileştirir ve -25 stres düşürür.",
                    RecipeId.FireSuppressor => "Bu kattaki yangın yayılımını %50 yavaşlatır. Sağ tıkla kovasız yangın söndür.",
                    RecipeId.BackupGenerator => PowerGrid.IsOn
                        ? "Aktif — Depremde şalter atma riskini %65 düşürür."
                        : "Şalter atmış! Sağ tıklayarak elektriği buradan geri aç.",
                    RecipeId.LightningRod => "Aktif — Deprem kıvılcımlarının yangın çıkarmasını engeller.",
                    _ => structure.Info.Description
                };
            }
        }

        public override bool CanStart(Survivor worker, out string reason)
        {
            if (structure == null)
            {
                reason = "Yapı hazır değil.";
                return false;
            }

            switch (structure.Recipe)
            {
                case RecipeId.ComfortCorner:
                    if (worker.Condition.Stress <= 1f && worker.Health >= worker.MaxHealth - 1f)
                    {
                        reason = $"{worker.DisplayName} zaten dinç ve sakin.";
                        return false;
                    }
                    reason = null;
                    return true;

                case RecipeId.ShelterHeater:
                    if (worker.Condition.Stress <= 1f)
                    {
                        reason = $"{worker.DisplayName} zaten sakin.";
                        return false;
                    }
                    reason = null;
                    return true;

                case RecipeId.MedStation:
                    if (worker.Health >= worker.MaxHealth - 1f && worker.Condition.Stress < 15f)
                    {
                        reason = $"{worker.DisplayName} tedaviye ihtiyaç duymuyor.";
                        return false;
                    }
                    var cost = new ResourceAmount(ResourceType.Supplies, 1);
                    if (!ResourceStorage.Instance.Has(cost))
                    {
                        reason = "Tedavi için depoda 1 Yaşam Kiti gerekli.";
                        return false;
                    }
                    reason = null;
                    return true;

                case RecipeId.FireSuppressor:
                    var fire = FireSpot.ForFloor(Floor);
                    if (fire == null || (!fire.IsBurning && fire.Smoke < 5f))
                    {
                        reason = "Bu katta söndürülecek yangın ya da duman yok.";
                        return false;
                    }
                    reason = null;
                    return true;

                case RecipeId.BackupGenerator:
                    if (PowerGrid.IsOn)
                    {
                        reason = "Elektrik zaten açık.";
                        return false;
                    }
                    reason = null;
                    return true;

                case RecipeId.LightningRod:
                    reason = "Paratoner pasif olarak çatıyı koruyor.";
                    return false;

                default:
                    reason = null;
                    return true;
            }
        }

        public override void Complete(Survivor worker)
        {
            if (structure == null) return;

            switch (structure.Recipe)
            {
                case RecipeId.ComfortCorner:
                    worker.Condition.RelieveStress(25f);
                    worker.Heal(10f);
                    break;

                case RecipeId.ShelterHeater:
                    worker.Condition.RelieveStress(15f);
                    break;

                case RecipeId.MedStation:
                    if (ResourceStorage.Instance.TrySpend(new ResourceAmount(ResourceType.Supplies, 1)))
                    {
                        worker.Heal(55f);
                        worker.Condition.RelieveStress(25f);
                    }
                    break;

                case RecipeId.FireSuppressor:
                    var fire = FireSpot.ForFloor(Floor);
                    if (fire != null) fire.ExtinguishDirect(45f, 45f);
                    break;

                case RecipeId.BackupGenerator:
                    if (PowerGrid.Instance != null) PowerGrid.Instance.SetPower(true);
                    break;
            }
        }
    }
}
