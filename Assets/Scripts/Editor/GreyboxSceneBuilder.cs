using System.Collections.Generic;
using System.Linq;
using AftershockIsle.Building;
using AftershockIsle.CameraControl;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Crisis;
using AftershockIsle.Endgame;
using AftershockIsle.DebugTools;
using AftershockIsle.Effects;
using AftershockIsle.Economy;
using AftershockIsle.Expedition;
using AftershockIsle.Interaction;
using AftershockIsle.Player;
using AftershockIsle.Save;
using AftershockIsle.Skills;
using AftershockIsle.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AftershockIsle.EditorTools
{
    /// <summary>
    /// GDD'deki 4 katlı kesit binayı gri kutularla kurar ve Main.unity olarak kaydeder.
    /// Ölçüler BuildingLayout'tan okunur; ölçü değişirse menüden tekrar çalıştırmak yeterli.
    /// </summary>
    public static class GreyboxSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
        const string MaterialFolder = "Assets/Art/Materials/Greybox";
        const string DataFolder = "Assets/Data";
        const string PrefabFolder = "Assets/Prefabs";

        const float SlabThickness = 0.3f;
        const float LadderOpening = 1.4f;
        const float ParapetHeight = 1f;
        const float DoorHeight = 2.4f;
        // Merdiven boşluğunun derinliği: döşeme sadece merdivenin çevresinde açıktır.
        const float LadderHoleDepth = 1.8f;
        // Sandalye boyu (m): karakter 1,75 m olduğu için eski 0,95 küçük kalıyordu.
        const float ChairHeight = 1.25f;

        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
                !EditorUtility.DisplayDialog("Greybox Sahnesi",
                    $"{ScenePath} zaten var. Üzerine yazılsın mı?", "Üzerine yaz", "İptal"))
                return;

            EnsureFolder(MaterialFolder);
            EnsureFolder(DataFolder);
            EnsureFolder(PrefabFolder);

            // Elle yerleştirilen eşya prefabları da güncel boyutlarla yenilensin (eski boyutlu prefab sahneye sürüklenmesin).
            BuildPropPrefabs();

            // Ana menü yoksa oyun sahnesinden önce onu kur (build sırası için).
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScenePath) == null) CreateMainMenuScene();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var palette = new Palette();

            var sun = CreateLighting();
            var building = CreateBuilding(palette);
            var cam = CreateCamera(building);

            var systems = new GameObject("[Systems]");
            var timeManager = systems.AddComponent<TimeManager>();
            var dayCycle = systems.AddComponent<DayCycle>();
            SetRef(dayCycle, "config", LoadOrCreateAsset<DayCycleConfig>($"{DataFolder}/DayCycleConfig.asset"));

            systems.AddComponent<StructuralIntegrity>();
            systems.AddComponent<ResourceStorage>();
            var session = systems.AddComponent<GameSession>();
            SetRef(session, "timeManager", timeManager);

            var commands = systems.AddComponent<CommandController>();
            SetRef(commands, "cam", cam);
            SetRef(commands, "building", building);

            // Oyuncu göstergesi (GameHud) ve dünya üstü göstergeler çalışma anında kendini kurar; burada yalnızca test paneli.
            systems.AddComponent<DebugHUD>();

            var aftershocks = systems.AddComponent<AftershockSystem>();
            SetRef(aftershocks, "debrisPrefab", CreateDebrisPrefab(palette));
            SetRef(aftershocks, "debrisParent", building.transform.Find("Props"));
            SetRef(aftershocks, "shelterMaterial", palette.Shelter);

            var power = systems.AddComponent<PowerGrid>();
            var lights = building.transform.Find("Lights").GetComponentsInChildren<Light>();
            var lightsProp = new SerializedObject(power);
            var array = lightsProp.FindProperty("interiorLights");
            array.arraySize = lights.Length;
            for (int i = 0; i < lights.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = lights[i];
            lightsProp.ApplyModifiedPropertiesWithoutUndo();

            var flood = systems.AddComponent<FloodSystem>();
            SetRef(flood, "pipe", building.GetComponentInChildren<BurstPipe>());
            SetRef(flood, "waterVisual", building.transform.Find("Props/Bodrum Suyu"));

            var director = systems.AddComponent<DisasterDirector>();
            SetRef(director, "schedule", LoadDefaultSchedule());
            SetRef(director, "dayCycle", dayCycle);

            systems.AddComponent<SkillTree>();
            systems.AddComponent<UpkeepSystem>();
            systems.AddComponent<SkillTreeWindow>();

            var expeditions = systems.AddComponent<ExpeditionSystem>();
            SetRef(expeditions, "timeManager", timeManager);
            SetRef(expeditions, "dayCycle", dayCycle);
            var gameAssets = GameAssetsSync.Sync();
            SetRef(expeditions, "fieldArt", gameAssets.fieldArt);
            SetRef(expeditions, "characterLibrary", gameAssets.characters);
            systems.AddComponent<ExpeditionWindow>();
            systems.AddComponent<WorkbenchWindow>();
            systems.AddComponent<TutorialHints>();

            var pauseMenu = systems.AddComponent<PauseMenu>();
            SetRef(pauseMenu, "timeManager", timeManager);
            SetRef(pauseMenu, "session", session);

            SetRef(systems.AddComponent<EffectsPlayer>(), "particleMaterial", palette.Particle);
            systems.AddComponent<ScreenFeedback>();
            SetRef(systems.AddComponent<LightingController>(), "sun", sun);

            var evacuation = systems.AddComponent<EvacuationSystem>();
            var (helicopter, rotor) = CreateHelicopter(palette);
            var landingPoint = building.transform.Find("Props/İniş Noktası");
            // Tahliyeye kadar görünmez; editörde de dünya merkezinde (bodrumda) durmasın diye çatının üstünde bekler.
            if (landingPoint != null) helicopter.position = landingPoint.position + new Vector3(-12f, 14f, 0f);
            helicopter.gameObject.SetActive(false);
            SetRef(evacuation, "session", session);
            SetRef(evacuation, "helicopter", helicopter);
            SetRef(evacuation, "rotor", rotor);
            SetRef(evacuation, "landingPoint", landingPoint);
            systems.AddComponent<DebugCheats>();
            systems.AddComponent<SaveManager>();
            systems.AddComponent<ShelterGrid>();
            systems.AddComponent<PlacementController>();
            systems.AddComponent<WeatherSystem>();
            systems.AddComponent<NarrativeEventSystem>();

            SetupModelManager(systems, building);

            var survivorPrefab = CreateSurvivorPrefab(palette);
            SetRef(systems.AddComponent<SurvivorRoster>(), "survivorPrefab", survivorPrefab.GetComponent<Survivor>());

            var hero = (GameObject)PrefabUtility.InstantiatePrefab(survivorPrefab);
            hero.name = "Ana Karakter";
            hero.transform.SetPositionAndRotation(building.Point(2, 0f), Quaternion.Euler(0f, 180f, 0f));
            SetRef(hero.GetComponent<CharacterMover>(), "building", building);
            Configure(hero.GetComponent<Survivor>(), ("displayName", "Ana Karakter"), ("branch", (int)SkillBranch.Construction));

            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterScenes();
            Selection.activeGameObject = systems;

            Debug.Log($"Greybox sahnesi kuruldu: {ScenePath}. Play'e basıp deneyebilirsin.");
        }

        /// <summary>
        /// Greybox aşamasında takvimin doğru kaynağı koddaki varsayılanlardır; yeni afet türleri
        /// eklendikçe asset de güncel kalsın diye her kurulumda sıfırlanır.
        /// </summary>
        static DisasterSchedule LoadDefaultSchedule()
        {
            var schedule = LoadOrCreateAsset<DisasterSchedule>($"{DataFolder}/DisasterSchedule.asset");
            var defaults = ScriptableObject.CreateInstance<DisasterSchedule>();
            schedule.entries = defaults.entries;
            Object.DestroyImmediate(defaults);
            EditorUtility.SetDirty(schedule);
            AssetDatabase.SaveAssets();
            Debug.Log("DisasterSchedule varsayılan takvime sıfırlandı.");
            return schedule;
        }

        // ---------------------------------------------------------------- Sahne parçaları

        static Light CreateLighting()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(35f, 20f, 0f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.35f, 0.36f, 0.4f);
            return sun;
        }

        static Camera CreateCamera(BuildingLayout building)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
            go.AddComponent<AudioListener>();

            go.transform.position = new Vector3(0f, building.FloorY(2) + 1.2f, building.WalkPlaneZ - 24f);
            SetRef(go.AddComponent<SideViewCamera>(), "building", building);
            return cam;
        }

        static BuildingLayout CreateBuilding(Palette p)
        {
            var root = new GameObject("Building");
            var layout = root.AddComponent<BuildingLayout>();
            var geo = new GameObject("Geometry").transform;
            geo.SetParent(root.transform, false);

            float h = layout.FloorHeight;
            float w = layout.Width;
            float d = layout.Depth;
            float t = layout.WallThickness;
            int floors = layout.FloorCount;
            float outerWidth = w + 2f * t;

            // Döşemeler: her katın zemini, üstteki merdiven boşlukları kesilmiş olarak.
            for (int floor = 0; floor < floors; floor++)
            {
                var openings = layout.Ladders.Where(l => l.lowerFloor == floor - 1).Select(l => l.x).OrderBy(x => x);
                float start = -outerWidth * 0.5f;
                int segment = 0;

                foreach (float x in openings.Append(outerWidth * 0.5f + LadderOpening * 0.5f))
                {
                    float end = x - LadderOpening * 0.5f;
                    if (end - start > 0.01f)
                    {
                        Box($"Slab_{floor}_{segment++}", geo,
                            new Vector3((start + end) * 0.5f, floor * h - SlabThickness * 0.5f, 0f),
                            new Vector3(end - start, SlabThickness, d), p.Concrete);
                    }
                    start = x + LadderOpening * 0.5f;
                }

                // Boşluğun önü ve arkası kapalı: derin odada döşeme boydan boya yarılmasın.
                float holeMin = layout.LadderZOffset - LadderHoleDepth * 0.5f;
                float holeMax = layout.LadderZOffset + LadderHoleDepth * 0.5f;
                foreach (float x in openings)
                {
                    float y = floor * h - SlabThickness * 0.5f;
                    Box($"Slab_{floor}_HoleFront_{x:0}", geo, new Vector3(x, y, (-d * 0.5f + holeMin) * 0.5f),
                        new Vector3(LadderOpening, SlabThickness, holeMin + d * 0.5f), p.Concrete);
                    Box($"Slab_{floor}_HoleBack_{x:0}", geo, new Vector3(x, y, (holeMax + d * 0.5f) * 0.5f),
                        new Vector3(LadderOpening, SlabThickness, d * 0.5f - holeMax), p.Concrete);
                }
            }

            // Arka duvarlar: katları ayırt etmek için dönüşümlü ton. Çatının arka duvarı yok.
            for (int floor = 0; floor < floors - 1; floor++)
            {
                Box($"BackWall_{floor}", geo,
                    new Vector3(0f, floor * h + h * 0.5f, d * 0.5f + t * 0.5f),
                    new Vector3(outerWidth, h, t), floor % 2 == 0 ? p.WallA : p.WallB);
            }

            // Yan duvarlar çatıda korkuluk olarak devam eder. Sağ duvarda giriş katı kapısı.
            float wallTop = (floors - 1) * h + ParapetHeight;
            float wallX = w * 0.5f + t * 0.5f;
            WallSegment("Wall_L", geo, -wallX, -SlabThickness, wallTop, t, d, p.Concrete);

            float doorBottom = 1 * h;
            float doorTop = doorBottom + DoorHeight;
            WallSegment("Wall_R_Lower", geo, wallX, -SlabThickness, doorBottom, t, d, p.Concrete);
            WallSegment("Wall_R_Upper", geo, wallX, doorTop, wallTop, t, d, p.Concrete);
            var door = Box("Dış Çıkış Kapısı", geo,
                new Vector3(wallX, (doorBottom + doorTop) * 0.5f, 0f),
                new Vector3(t * 0.6f, DoorHeight, 1.4f), p.Exit);
            Configure(door.AddComponent<ExitDoor>(), ("displayName", "Dış Çıkış Kapısı"), ("workDuration", 1f));

            // Zemin: bodrum yer altında, giriş katı sokak seviyesinde.
            float groundY = 1 * h;
            const float earthDepthBelow = 3f;
            float earthBack = d + 2f * t;
            float earthHeight = groundY + earthDepthBelow;
            float earthCenterY = groundY - earthHeight * 0.5f;
            const float earthCenterZ = 0f;
            const float sideEarthWidth = 40f;

            Box("Earth_L", geo, new Vector3(-outerWidth * 0.5f - sideEarthWidth * 0.5f, earthCenterY, earthCenterZ),
                new Vector3(sideEarthWidth, earthHeight, earthBack), p.Earth);
            Box("Earth_R", geo, new Vector3(outerWidth * 0.5f + sideEarthWidth * 0.5f, earthCenterY, earthCenterZ),
                new Vector3(sideEarthWidth, earthHeight, earthBack), p.Earth);
            Box("Earth_Below", geo,
                new Vector3(0f, -SlabThickness - earthDepthBelow * 0.5f, earthCenterZ),
                new Vector3(outerWidth, earthDepthBelow, earthBack), p.Earth);

            foreach (var ladder in layout.Ladders)
            {
                Box($"Ladder_{ladder.lowerFloor}_{ladder.lowerFloor + 1}", geo,
                    new Vector3(ladder.x, ladder.lowerFloor * h + h * 0.5f, layout.LadderZOffset),
                    new Vector3(0.8f, h, 0.08f), p.Ladder);
            }

            // İç lambalar (çatı hariç): şalter inince söner.
            var lights = new GameObject("Lights").transform;
            lights.SetParent(root.transform, false);
            for (int floor = 0; floor < floors - 1; floor++)
            {
                var lamp = new GameObject($"Lamba_{floor}").AddComponent<Light>();
                lamp.transform.SetParent(lights, false);
                lamp.transform.localPosition = new Vector3(0f, floor * h + h - 0.6f, 0.2f);
                lamp.type = LightType.Point;
                lamp.color = new Color(1f, 0.85f, 0.6f);
                lamp.range = 12f;
                lamp.intensity = 4f;
            }

            CreateProps(root.transform, layout, p);
            return layout;
        }

        // Merdivenler x = -6 (0↔1), +6 (1↔2), -6 (2↔3). Objeler merdiven önlerini kapatmayacak şekilde dizildi.
        static void CreateProps(Transform root, BuildingLayout layout, Palette p)
        {
            var props = new GameObject("Props").transform;
            props.SetParent(root, false);
            float h = layout.FloorHeight;
            float backZ = layout.Depth * 0.5f;

            // Taşıyıcı kolonlar Blender binasında konduğu yerlerde ve seviyede durur (Building.fbx'teki "_Kolon_" objeleri).
            // Props altında değil: ModelManager eski greybox eşyalarını gizlerken kolon modellerini gizlemesin.
            var columns = new GameObject("Columns").transform;
            columns.SetParent(root, false);
            if (!CreateColumnsFromBuildingModel(columns, layout, p))
            {
                // Bina modeli yoksa eski düzen: her katta ikişer kolon, ikisi kırık başlar.
                var broken = new[] { (1, -3f), (2, 3f) };
                for (int floor = 0; floor < layout.FloorCount - 1; floor++)
                {
                    foreach (float x in new[] { -3f, 3f })
                    {
                        float height = h - SlabThickness;
                        var column = Box($"Kolon_{floor}_{(x < 0 ? "Sol" : "Sağ")}", columns,
                            new Vector3(x, floor * h + height * 0.5f, backZ - 0.45f), new Vector3(0.5f, height, 0.5f), p.Column);
                        AddSupportColumn(column, System.Array.IndexOf(broken, (floor, x)) >= 0 ? ColumnState.Broken : ColumnState.Cracked);
                    }
                }
            }

            // Kat 0: Bodrum
            var pipe = Box("Şebeke Borusu", props, new Vector3(0f, 0 * h + 2.7f, backZ - 0.3f), new Vector3(2.4f, 0.25f, 0.25f), p.Water);
            var spray = Box("Su Fışkırması", props, new Vector3(0f, 0 * h + 1.3f, backZ - 0.3f), new Vector3(0.25f, 2.6f, 0.25f), p.WaterSurface);
            Object.DestroyImmediate(spray.GetComponent<Collider>());
            var pipeComponent = pipe.AddComponent<BurstPipe>();
            Configure(pipeComponent, ("displayName", "Şebeke Borusu"), ("workDuration", 6f));
            SetRef(pipeComponent, "sprayVisual", spray);
            ModelSkins.Skin(pipe, ModelKey.Pipe, SkinFit.Width, 2.4f, centered: true);

            var pump = Box("Su Tulumbası", props, new Vector3(5.5f, 0 * h + 0.6f, backZ - 0.5f), new Vector3(1f, 1.2f, 0.8f), p.Water);
            Configure(pump.AddComponent<ManualPump>(), ("displayName", "Manuel Su Tulumbası"), ("workDuration", 1f));

            // Su, karakterleri ve objeleri kapatmasın diye yarı saydam; tıklamaları engellemesin diye collider'sız.
            var water = Box("Bodrum Suyu", props, new Vector3(0f, 0 * h + 0.01f, 0f),
                new Vector3(layout.Width, 0.02f, layout.Depth), p.WaterSurface);
            Object.DestroyImmediate(water.GetComponent<Collider>());
            var cabinet = FurnitureProp("Dolap", props, new Vector3(1.5f, 0 * h + 0.8f, backZ - 0.45f), new Vector3(1.2f, 1.6f, 0.7f), p.Furniture);
            Configure(cabinet, ("hasSearchLoot", true), ("searchLoot.type", (int)ResourceType.Supplies), ("searchLoot.amount", 2));
            ModelSkins.Skin(cabinet.gameObject, ModelKey.Dresser, SkinFit.Width, 1.2f, ModelSkins.FrontToCamera);
            // Odanın ön yarısı boş kalmasın: bazı eşyalar ortada / önde, yürüyen karakter etrafından dolaşır.
            Rubble("Moloz", props, new Vector3(-4.2f, 0 * h, -1.5f), new Vector3(1.1f, 0.6f, 1f), p.Rubble, 3, false);

            // Kat 1: Giriş katı. Dış kapının önü yıkıntıyla kapalı.
            var breaker = Box("Ana Şalter Panosu", props, new Vector3(-4.5f, 1 * h + 1.4f, backZ - 0.2f), new Vector3(0.8f, 1.2f, 0.3f), p.Electric);
            Configure(breaker.AddComponent<BreakerPanel>(), ("displayName", "Ana Şalter Panosu"), ("workDuration", 1.5f));

            var radiator = Box("Eski Radyatör", props, new Vector3(1.8f, 1 * h + 0.45f, backZ - 0.3f), new Vector3(0.9f, 0.9f, 0.3f), p.Metal);
            Configure(radiator.AddComponent<Furniture>(), ("displayName", "Eski Radyatör"), ("workDuration", 6f),
                ("providesShelter", false), ("salvage.type", (int)ResourceType.Metal), ("salvage.amount", 2));

            var firstAid = Box("İlk Yardım Dolabı", props, new Vector3(4.5f, 1 * h + 1.5f, backZ - 0.15f), new Vector3(0.7f, 0.6f, 0.25f), p.Supplies);
            Configure(firstAid.AddComponent<Furniture>(), ("displayName", "İlk Yardım Dolabı"), ("workDuration", 3f),
                ("providesShelter", false), ("salvage.amount", 1),
                ("hasSearchLoot", true), ("searchLoot.type", (int)ResourceType.Supplies), ("searchLoot.amount", 1));

            var table = FurnitureProp("Masa", props, new Vector3(0f, 1 * h + 0.525f, -0.3f), new Vector3(1.6f, 1.05f, 0.9f), p.Furniture);
            ModelSkins.Skin(table.gameObject, ModelKey.Table, SkinFit.Height, 1.05f);

            // Süs: masanın yanında sandalye ve yangın odağının yanında yangın tüpü (etkileşimsiz).
            ModelSkins.Decorate(props, "Sandalye", ModelKey.Chair, new Vector3(0.95f, 1 * h, -0.3f), ChairHeight, 90f);
            ModelSkins.Decorate(props, "Yangın Tüpü", ModelKey.Extinguisher, new Vector3(-3.5f, 1 * h, backZ - 0.5f), 0.9f);
            Rubble("Yıkıntı (Kapı Önü)", props, new Vector3(7.7f, 1 * h, 0f), new Vector3(1.4f, 1.1f, 1.8f), p.Rubble, 5, true);

            // Kat 2: Yaşam / Tezgah
            var bench = Box("Çalışma Tezgahı", props, new Vector3(-1f, 2 * h + 0.5f, backZ - 0.6f), new Vector3(2.2f, 1f, 1f), p.Craft);
            Configure(bench.AddComponent<Workbench>(), ("displayName", "Çalışma Tezgahı"), ("workDuration", 5f));
            ModelSkins.Skin(bench, ModelKey.Workbench, SkinFit.Width, 2.0f, ModelSkins.FrontToCamera);
            var chest = Box("Ortak Sandık", props, new Vector3(1.45f, 2 * h + 0.4f, backZ - 0.5f), new Vector3(1.2f, 0.8f, 0.8f), p.Storage);
            Configure(chest.AddComponent<StorageChest>(), ("displayName", "Ortak Sandık"), ("workDuration", 0.5f));
            ModelSkins.Skin(chest, ModelKey.Crate, SkinFit.Width, 1.2f);
            ModelSkins.Decorate(props, "Sandalye (Tezgah)", ModelKey.Chair, new Vector3(0.4f, 2 * h, backZ - 0.6f), ChairHeight, -90f);
            ModelSkins.Decorate(props, "Yangın Tüpü", ModelKey.Extinguisher, new Vector3(3.2f, 2 * h, backZ - 0.5f), 0.9f);
            ModelSkins.Decorate(props, "Kalas Yığını", ModelKey.Planks, new Vector3(-2.6f, 0 * h + 0.02f, -0.8f), 0.12f, 180f);
            // İkinci Elektronik kaynağı: sefere çıkmadan da Kademe 3 yolu açık kalsın.
            var computer = Box("Eski Bilgisayar", props, new Vector3(-3.3f, 2 * h + 0.45f, backZ - 0.5f), new Vector3(0.8f, 0.9f, 0.6f), p.Electric);
            Configure(computer.AddComponent<Furniture>(), ("displayName", "Eski Bilgisayar"), ("workDuration", 5f),
                ("providesShelter", false), ("salvage.type", (int)ResourceType.Electronics), ("salvage.amount", 1));

            var beds = FurnitureProp("Yataklar", props, new Vector3(4.9f, 2 * h + 0.25f, backZ - 0.6f), new Vector3(2.4f, 0.5f, 1.2f), p.Furniture);
            ModelSkins.Skin(beds.gameObject, ModelKey.Bed, SkinFit.Width, 2.4f, 90f);
            Rubble("Moloz", props, new Vector3(-4.2f, 2 * h, -1.5f), new Vector3(1f, 0.5f, 1f), p.Rubble, 2, false);

            var bucketSpot = Box("Kova (Bodrum Suyu)", props, new Vector3(4.3f, 0 * h + 0.25f, backZ - 0.9f), new Vector3(0.45f, 0.5f, 0.45f), p.Metal);
            Configure(bucketSpot.AddComponent<WaterSource>(), ("displayName", "Kova (Bodrum Suyu)"), ("workDuration", 2f), ("usesFloodWater", true));
            ModelSkins.Skin(bucketSpot, ModelKey.Bucket, SkinFit.Height, 0.65f);

            var tank = Box("Su Bidonu", props, new Vector3(2.3f, 2 * h + 0.45f, backZ - 0.6f), new Vector3(0.5f, 0.9f, 0.5f), p.Water);
            Configure(tank.AddComponent<WaterSource>(), ("displayName", "Su Bidonu"), ("workDuration", 2f),
                ("usesFloodWater", false), ("charges", 3));

            // Yangın odakları ve dış pencereler: giriş ve yaşam katları.
            var collapseRubble = CreateCollapseRubblePrefab(p);
            foreach (var (floor, fireX) in new[] { (1, -2f), (2, 4.6f) })
            {
                CreateFireSpot(props, layout, floor, fireX, collapseRubble, p);
                CreateWindow(props, layout, floor, p);
            }
            CreateGasValve(props, layout, 1, -1.5f, p);

            // Çatı (son kat). Tahliye alanı yıkıntıyla kapalı; zafer için temizlenmesi gerekecek.
            int roof = layout.RoofFloor;
            var radio = Box("Telsiz İstasyonu", props, new Vector3(-3.5f, roof * h + 0.7f, backZ - 0.6f), new Vector3(1.2f, 1.4f, 1f), p.Electric);
            var radioLight = Box("Telsiz Işığı", props, new Vector3(-3.5f, roof * h + 1.55f, backZ - 0.6f), new Vector3(0.25f, 0.25f, 0.25f), p.Supplies);
            Object.DestroyImmediate(radioLight.GetComponent<Collider>());
            var radioComponent = radio.AddComponent<RadioStation>();
            Configure(radioComponent, ("displayName", "Telsiz İstasyonu"));
            SetRef(radioComponent, "statusLight", radioLight.GetComponent<Renderer>());

            Prop("Helikopter Tahliye Alanı", props, new Vector3(4.5f, roof * h + 0.03f, 0f), new Vector3(5f, 0.05f, 3f), p.Helipad);
            var landing = new GameObject("İniş Noktası").transform;
            landing.SetParent(props, false);
            landing.localPosition = new Vector3(4.5f, roof * h + 0.75f, 0f);

            // Üretilene kadar gizli kalan kurulumlar.
            var electricPump = Box("Elektrikli Pompa", props, new Vector3(-1.6f, 0 * h + 0.5f, backZ - 0.5f), new Vector3(0.9f, 1f, 0.8f), p.Metal);
            Configure(electricPump.AddComponent<InstallableVisual>(), ("recipe", (int)RecipeId.ElectricPump));
            Configure(electricPump.AddComponent<ElectricPump>(), ("displayName", "Elektrikli Pompa"), ("workDuration", 5f));

            var valve = Box("Bypass Vanası", props, new Vector3(1.4f, 0 * h + 2.7f, backZ - 0.3f), new Vector3(0.35f, 0.45f, 0.45f), p.Exit);
            Object.DestroyImmediate(valve.GetComponent<Collider>());
            Configure(valve.AddComponent<InstallableVisual>(), ("recipe", (int)RecipeId.BypassValve));

            var rod = Box("Paratoner", props, new Vector3(-7.5f, roof * h + 1.5f, backZ - 0.3f), new Vector3(0.1f, 3f, 0.1f), p.Metal);
            Object.DestroyImmediate(rod.GetComponent<Collider>());
            Configure(rod.AddComponent<InstallableVisual>(), ("recipe", (int)RecipeId.LightningRod));
            Rubble("Çatı Yıkıntısı", props, new Vector3(1.2f, roof * h, 0f), new Vector3(1.4f, 1f, 1.8f), p.Rubble, 4, true);
            Rubble("Moloz", props, new Vector3(5f, roof * h, 0.6f), new Vector3(1.2f, 0.6f, 1.2f), p.Rubble, 3, false);
        }

        const string BuildingModelPath = "Assets/Models/Buildings/Building.fbx";
        // ModelManager binayı Y ekseninde bu kadar döndürüp yerleştirir (ön kesit kameraya baksın diye).
        const float BuildingModelYaw = 180f;
        // Blender'daki kolon ölçüleri (m): genişlik, yükseklik (kat - döşeme), derinlik.
        static readonly Vector3 ColumnSize = new Vector3(0.35f, 3.2f, 0.4f);

        /// <summary>
        /// Building.fbx'teki "F1_Kolon_X+2_00_Lv1" gibi objelerin yerine, aynı konum, yön ve seviyede SupportColumn kurar.
        /// İsimdeki _Lv1/_Lv2/_Lv3 başlangıç seviyesidir. Model yoksa ya da içinde kolon yoksa false.
        /// </summary>
        static bool CreateColumnsFromBuildingModel(Transform parent, BuildingLayout layout, Palette p)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingModelPath);
            if (model == null) return false;

            var placement = Quaternion.Euler(0f, BuildingModelYaw, 0f);
            int count = 0;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.Contains("_Kolon_")) continue;

                // Kolonun pivotu Blender'da taban ortasında.
                var bottom = placement * model.transform.InverseTransformPoint(t.position);
                // Blender'ın X ekseni FBX'te objenin -X'ine düşer; yerleşimden sonraki yönü kolonun dönüşünü verir.
                var right = placement * (model.transform.InverseTransformDirection(t.rotation * Vector3.left));
                float yaw = Mathf.Atan2(-right.z, right.x) * Mathf.Rad2Deg;

                string name = System.Text.RegularExpressions.Regex.Replace(t.name, @"_Lv\d+$", "");
                var column = Box(name, parent, bottom + Vector3.up * (ColumnSize.y * 0.5f), ColumnSize, p.Column);
                column.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                AddSupportColumn(column, LevelFromName(t.name));
                count++;
            }

            if (count > 0) Debug.Log($"{count} kolon Blender konumlarına kuruldu ({BuildingModelPath}).");
            return count > 0;
        }

        static ColumnState LevelFromName(string name)
        {
            var match = System.Text.RegularExpressions.Regex.Match(name, @"_Lv(\d+)$");
            int level = match.Success ? int.Parse(match.Groups[1].Value) : 2;
            return level >= 3 ? ColumnState.Reinforced : level == 2 ? ColumnState.Cracked : ColumnState.Broken;
        }

        static void AddSupportColumn(GameObject column, ColumnState state)
        {
            var component = column.AddComponent<SupportColumn>();
            Configure(component, ("displayName", "Taşıyıcı Kolon"), ("workDuration", 4f), ("state", (int)state));
        }

        static void CreateFireSpot(Transform parent, BuildingLayout layout, int floor, float x, RubblePile collapseRubble, Palette p)
        {
            float floorY = floor * layout.FloorHeight;
            var root = new GameObject($"Yangın Odağı_{floor}");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(x, floorY + 0.05f, 0.3f);

            var flame = Box("Alev", root.transform, new Vector3(0f, 0.8f, 0f), new Vector3(1.6f, 1.6f, 1.2f), p.Fire);
            var flameLight = new GameObject("Alev Işığı").AddComponent<Light>();
            flameLight.transform.SetParent(root.transform, false);
            flameLight.transform.localPosition = new Vector3(0f, 1.2f, -0.8f);
            flameLight.type = LightType.Point;
            flameLight.color = new Color(1f, 0.5f, 0.15f);
            flameLight.range = 7f;

            // Duman tüm katı kaplar; tıklamayı engellememesi için collider'sız.
            float smokeHeight = layout.FloorHeight - SlabThickness;
            var smoke = Box($"Duman_{floor}", parent, new Vector3(0f, floorY + smokeHeight * 0.5f, 0f),
                new Vector3(layout.Width, smokeHeight, layout.Depth), p.Smoke);
            Object.DestroyImmediate(smoke.GetComponent<Collider>());

            var fire = root.AddComponent<FireSpot>();
            Configure(fire, ("displayName", $"Yangın ({layout.GetFloorName(floor)})"), ("workDuration", 1.5f));
            SetRef(fire, "flame", flame);
            SetRef(fire, "flameLight", flameLight);
            SetRef(fire, "smokeVisual", smoke.GetComponent<Renderer>());
            SetRef(fire, "collapseRubblePrefab", collapseRubble);
        }

        /// <summary>Doğalgaz vanası (arka duvarda, borusuyla) ve katı dolduran saydam gaz bulutu.</summary>
        static void CreateGasValve(Transform parent, BuildingLayout layout, int floor, float x, Palette p)
        {
            float floorY = floor * layout.FloorHeight;
            float backZ = layout.Depth * 0.5f;

            var pipe = Box("Gaz Borusu", parent, new Vector3(x, floorY + (layout.FloorHeight - SlabThickness) * 0.5f, backZ - 0.1f),
                new Vector3(0.12f, layout.FloorHeight - SlabThickness, 0.12f), p.Electric);
            Object.DestroyImmediate(pipe.GetComponent<Collider>());

            var valve = Box("Gaz Vanası", parent, new Vector3(x, floorY + 1.1f, backZ - 0.2f), new Vector3(0.45f, 0.35f, 0.25f), p.Exit);

            float gasHeight = layout.FloorHeight - SlabThickness;
            var gas = Box($"Gaz_{floor}", parent, new Vector3(0f, floorY + gasHeight * 0.5f, 0f),
                new Vector3(layout.Width, gasHeight, layout.Depth), p.Gas);
            Object.DestroyImmediate(gas.GetComponent<Collider>());
            gas.GetComponent<Renderer>().enabled = false;

            var leak = valve.AddComponent<GasLeak>();
            Configure(leak, ("displayName", "Gaz Vanası"), ("workDuration", 3f));
            SetRef(leak, "gasVisual", gas.GetComponent<Renderer>());
        }

        // Kesit kamerası yan duvarları kenarından gördüğü için pencere arka dış duvarda.
        static void CreateWindow(Transform parent, BuildingLayout layout, int floor, Palette p)
        {
            const float x = -7.5f;
            float backZ = layout.Depth * 0.5f;
            float y = floor * layout.FloorHeight + 1.7f;
            var frame = Box($"Dış Pencere_{floor}", parent, new Vector3(x, y, backZ - 0.03f), new Vector3(1.4f, 1.2f, 0.06f), p.WindowFrame);
            var glass = Box($"Cam_{floor}", parent, new Vector3(x, y, backZ - 0.08f), new Vector3(1.2f, 1f, 0.04f), p.Glass);
            Object.DestroyImmediate(glass.GetComponent<Collider>());

            var window = frame.AddComponent<SmokeWindow>();
            Configure(window, ("displayName", "Dış Pencere"), ("workDuration", 3f));
            SetRef(window, "glass", glass);
        }

        /// <summary>Yangının çökerttiği tavanın bıraktığı, yolu kapatan büyük yıkıntı.</summary>
        static RubblePile CreateCollapseRubblePrefab(Palette p)
        {
            var go = Box("Çökmüş Tavan", null, Vector3.zero, new Vector3(2.4f, 1.6f, 2f), p.Rubble);
            Configure(go.AddComponent<RubblePile>(),
                ("displayName", "Çökmüş Tavan"), ("workDuration", 5f), ("digCount", 8), ("blocksPath", true));
            ModelSkins.Skin(go, ModelKey.Rubble, SkinFit.Stretch);

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabFolder}/CollapsedCeiling.prefab");
            Object.DestroyImmediate(go);
            return prefab.GetComponent<RubblePile>();
        }

        static Furniture FurnitureProp(string name, Transform parent, Vector3 center, Vector3 size, Material material)
        {
            var go = Box(name, parent, center, size, material);
            var furniture = go.AddComponent<Furniture>();
            Configure(furniture, ("displayName", name), ("workDuration", 6f));
            return furniture;
        }

        /// <summary>floorBase: yığının oturduğu zemin noktası.</summary>
        static void Rubble(string name, Transform parent, Vector3 floorBase, Vector3 size, Material material, int digs, bool blocksPath)
        {
            var go = Box(name, parent, floorBase + Vector3.up * (size.y * 0.5f), size, material);
            Configure(go.AddComponent<RubblePile>(),
                ("displayName", name), ("workDuration", 5f), ("digCount", digs), ("blocksPath", blocksPath));
            ModelSkins.Skin(go, ModelKey.Rubble, SkinFit.Stretch);
        }

        static (Transform helicopter, Transform rotor) CreateHelicopter(Palette p)
        {
            var root = new GameObject("Tahliye Helikopteri").transform;
            var body = Box("Gövde", root, Vector3.zero, new Vector3(3.6f, 1.3f, 1.8f), p.Helicopter);
            var tail = Box("Kuyruk", root, new Vector3(-2.8f, 0.3f, 0f), new Vector3(2.4f, 0.35f, 0.35f), p.Helicopter);
            var rotor = Box("Rotor", root, new Vector3(0f, 0.85f, 0f), new Vector3(6.5f, 0.06f, 0.35f), p.Visor);
            foreach (var part in new[] { body, tail, rotor }) Object.DestroyImmediate(part.GetComponent<Collider>());
            return (root, rotor.transform);
        }

        /// <summary>Kırılan kolonun önüne düşen enkaz. Kolon yeniden dikilmeden önce kazılmalı.</summary>
        static RubblePile CreateDebrisPrefab(Palette p)
        {
            // GDD 8.2: kırılan kolonun düşürdüğü tavan enkazı odayı tıkar; kazılana kadar kat içinde geçilemez.
            var go = Box("Enkaz", null, Vector3.zero, new Vector3(1.4f, 0.9f, 1.6f), p.Rubble);
            Configure(go.AddComponent<RubblePile>(),
                ("displayName", "Tavan Enkazı"), ("workDuration", 4f), ("digCount", 3), ("blocksPath", true));
            ModelSkins.Skin(go, ModelKey.Rubble, SkinFit.Stretch);

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabFolder}/Debris.prefab");
            Object.DestroyImmediate(go);
            return prefab.GetComponent<RubblePile>();
        }

        static GameObject CreateSurvivorPrefab(Palette p)
        {
            var root = new GameObject("Survivor");
            root.AddComponent<CharacterMover>();
            var survivor = root.AddComponent<Survivor>();
            root.AddComponent<TaskRunner>();
            root.AddComponent<SurvivorCondition>();

            var carried = Box("CarriedItem", root.transform, new Vector3(0f, 1.1f, 0.42f), new Vector3(0.5f, 0.35f, 0.35f), p.Carry);
            Object.DestroyImmediate(carried.GetComponent<Collider>());
            SetRef(survivor, "carriedVisual", carried.GetComponent<Renderer>());

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            body.GetComponent<Renderer>().sharedMaterial = p.Survivor;
            SetRef(survivor, "body", body.GetComponent<Renderer>());

            // Yüz yönünü göstermek için vizör.
            var visor = Box("Visor", root.transform, new Vector3(0f, 1.55f, 0.25f), new Vector3(0.35f, 0.12f, 0.12f), p.Visor);
            Object.DestroyImmediate(visor.GetComponent<Collider>());

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "SelectionRing";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            ring.transform.localScale = new Vector3(1.1f, 0.01f, 1.1f);
            ring.GetComponent<Renderer>().sharedMaterial = p.Selection;
            Object.DestroyImmediate(ring.GetComponent<Collider>());

            SetRef(survivor, "selectionIndicator", ring);
            SetRef(survivor, "characters", AssetDatabase.LoadAssetAtPath<CharacterLibrary>("Assets/Art/CharacterLibrary.asset"));

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/Survivor.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ---------------------------------------------------------------- Yardımcılar

        static GameObject Box(string name, Transform parent, Vector3 localCenter, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        static void Prop(string name, Transform parent, Vector3 localCenter, Vector3 size, Material material) =>
            Box(name, parent, localCenter, size, material);

        static void WallSegment(string name, Transform parent, float x, float bottom, float top, float thickness, float depth, Material material) =>
            Box(name, parent, new Vector3(x, (bottom + top) * 0.5f, 0f), new Vector3(thickness, top - bottom, depth), material);

        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"{target.GetType().Name} üzerinde '{field}' alanı bulunamadı.", target);
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Private [SerializeField] alanlarına değer yazar (string, int/enum, float, bool).</summary>
        static void Configure(Object target, params (string field, object value)[] values)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in values)
            {
                var prop = so.FindProperty(field);
                if (prop == null)
                {
                    Debug.LogError($"{target.GetType().Name} üzerinde '{field}' alanı bulunamadı.", target);
                    continue;
                }

                switch (value)
                {
                    case string s: prop.stringValue = s; break;
                    case bool b: prop.boolValue = b; break;
                    case float f: prop.floatValue = f; break;
                    // Enum'a sıra numarası değil değerin kendisi yazılır (ColumnState gibi 1, 3, 4 değerli enum'lar için).
                    case int i when prop.propertyType == SerializedPropertyType.Enum: prop.intValue = i; break;
                    case int i: prop.intValue = i; break;
                    default: Debug.LogError($"'{field}' için desteklenmeyen tür: {value?.GetType().Name}"); break;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static T LoadOrCreateAsset<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        /// <summary>Ana menü her zaman 0. sırada, oyun sahnesi 1. sırada; diğer sahneler arkada kalır.</summary>
        static void RegisterScenes()
        {
            var ordered = new List<EditorBuildSettingsScene>();
            foreach (string path in new[] { MenuScenePath, ScenePath })
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
                    ordered.Add(new EditorBuildSettingsScene(path, true));

            ordered.AddRange(EditorBuildSettings.scenes.Where(s => s.path != MenuScenePath && s.path != ScenePath));
            EditorBuildSettings.scenes = ordered.ToArray();
        }

        /// <summary>Boş bir sahneye sadece kamera ve ana menü bileşenini koyar.</summary>
        static void CreateMainMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.09f);
            cam.gameObject.AddComponent<AudioListener>();

            new GameObject("[Menu]").AddComponent<MainMenu>();
            EditorSceneManager.SaveScene(scene, MenuScenePath);
        }

        public static void BuildMainMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            CreateMainMenuScene();
            RegisterScenes();
            Debug.Log($"Ana menü sahnesi kuruldu: {MenuScenePath}.");
        }

        public static void InstallAndRefreshModelManager()
        {
            var building = Object.FindFirstObjectByType<BuildingLayout>();
            if (building == null)
            {
                Debug.LogWarning("Sahnede BuildingLayout bulunamadı. Önce Main sahnesini açın.");
                return;
            }

            var systems = GameObject.Find("[Systems]") ?? building.gameObject;
            var manager = SetupModelManager(systems, building);
            Selection.activeGameObject = systems;
            EditorSceneManager.MarkSceneDirty(systems.scene);
            Debug.Log("ModelManager kuruldu ve modeller sahneye uygulandı. Inspector'dan prefab slotlarını değiştirebilirsiniz.", manager);
        }

        static ModelManager SetupModelManager(GameObject systems, BuildingLayout building)
        {
            var manager = systems.GetComponent<ModelManager>();
            if (manager == null) manager = systems.AddComponent<ModelManager>();

            SetRef(manager, "building", building);
            Configure(manager,
                ("useCustomBuilding", true),
                ("hideGreyboxWalls", true),
                ("hideGreyboxProps", true),
                ("addWarmWindowBacking", true));

            var buildingFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Buildings/Building.fbx");
            if (buildingFbx != null) SetRef(manager, "buildingPrefab", buildingFbx);

            var colLv1 = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Columns/Kolon_Lv1_Kirik.fbx");
            if (colLv1 != null) SetRef(manager, "columnLv1Prefab", colLv1);

            var colLv2 = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Columns/Kolon_Lv2_Catlak.fbx");
            if (colLv2 != null) SetRef(manager, "columnLv2Prefab", colLv2);

            var colLv3 = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Columns/Kolon_Lv3_Guclendirilmis.fbx");
            if (colLv3 != null) SetRef(manager, "columnLv3Prefab", colLv3);

            var benchFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/workbench/bencht.fbx");
            if (benchFbx != null) SetRef(manager, "workbenchPrefab", benchFbx);

            manager.RefreshAll();
            return manager;
        }

        public static void BuildPropPrefabs()
        {
            const string PropsPrefabFolder = "Assets/Prefabs/Props";
            EnsureFolder(PropsPrefabFolder);
            var p = new Palette();

            // 1. Çalışma Tezgahı (bencht.fbx + Workbench)
            SaveInteractivePrefab($"{PropsPrefabFolder}/Calisma_Tezgahi.prefab", () =>
            {
                var go = Box("Çalışma Tezgahı", null, Vector3.zero, new Vector3(2.2f, 1f, 1f), p.Craft);
                Configure(go.AddComponent<Workbench>(), ("displayName", "Çalışma Tezgahı"), ("workDuration", 5f));
                ModelSkins.Skin(go, ModelKey.Workbench, SkinFit.Width, 2.0f, ModelSkins.FrontToCamera);
                return go;
            });

            // 2. Kalas / Tahtalar (tahtalar.fbx + Furniture -> Kalas verir)
            SaveInteractivePrefab($"{PropsPrefabFolder}/Kalas_Tahtalar.prefab", () =>
            {
                var go = Box("Kalas Yığını", null, Vector3.zero, new Vector3(1.6f, 0.25f, 0.5f), p.Furniture);
                Configure(go.AddComponent<Furniture>(), ("displayName", "Kalas Yığını"), ("workDuration", 3f),
                    ("providesShelter", false), ("salvage.type", (int)ResourceType.Scrap), ("salvage.amount", 2));
                ModelSkins.Skin(go, ModelKey.Planks, SkinFit.Width, 1.8f, ModelSkins.FrontToCamera);
                return go;
            });

            // 3. Ortak Sandık (kutu.fbx + StorageChest)
            SaveInteractivePrefab($"{PropsPrefabFolder}/Ortak_Sandik.prefab", () =>
            {
                var go = Box("Ortak Sandık", null, Vector3.zero, new Vector3(1.2f, 0.8f, 0.8f), p.Storage);
                Configure(go.AddComponent<StorageChest>(), ("displayName", "Ortak Sandık"), ("workDuration", 0.5f));
                ModelSkins.Skin(go, ModelKey.Crate, SkinFit.Width, 1.2f);
                return go;
            });

            // 4. Dolap / Komodin (komodnfbx.fbx + Furniture Aranabilir + Yaşam Üçgeni)
            SaveInteractivePrefab($"{PropsPrefabFolder}/Dolap_Komodin.prefab", () =>
            {
                var go = Box("Dolap", null, Vector3.zero, new Vector3(1.2f, 1.6f, 0.7f), p.Furniture);
                var f = go.AddComponent<Furniture>();
                Configure(f, ("displayName", "Dolap"), ("workDuration", 6f),
                    ("hasSearchLoot", true), ("searchLoot.type", (int)ResourceType.Supplies), ("searchLoot.amount", 2));
                ModelSkins.Skin(go, ModelKey.Dresser, SkinFit.Width, 1.2f, ModelSkins.FrontToCamera);
                return go;
            });

            // 5. Masa (masafbx.fbx + Furniture Yaşam Üçgeni)
            SaveInteractivePrefab($"{PropsPrefabFolder}/Masa.prefab", () =>
            {
                var go = Box("Masa", null, Vector3.zero, new Vector3(1.6f, 1.05f, 0.9f), p.Furniture);
                Configure(go.AddComponent<Furniture>(), ("displayName", "Masa"), ("workDuration", 6f));
                ModelSkins.Skin(go, ModelKey.Table, SkinFit.Height, 1.05f);
                return go;
            });

            // 6. Yatak (yatakfbx.fbx + Furniture Yaşam Üçgeni)
            SaveInteractivePrefab($"{PropsPrefabFolder}/Yatak.prefab", () =>
            {
                var go = Box("Yatak", null, Vector3.zero, new Vector3(2.4f, 0.5f, 1.2f), p.Furniture);
                Configure(go.AddComponent<Furniture>(), ("displayName", "Yatak"), ("workDuration", 6f));
                ModelSkins.Skin(go, ModelKey.Bed, SkinFit.Width, 2.4f, 90f);
                return go;
            });

            // 7. Sandalye (afet.fbx + Furniture)
            SaveInteractivePrefab($"{PropsPrefabFolder}/Sandalye.prefab", () =>
            {
                var go = Box("Sandalye", null, Vector3.zero, new Vector3(0.8f, ChairHeight, 0.8f), p.Furniture);
                Configure(go.AddComponent<Furniture>(), ("displayName", "Sandalye"), ("workDuration", 3f),
                    ("providesShelter", false), ("salvage.type", (int)ResourceType.Scrap), ("salvage.amount", 1));
                ModelSkins.Skin(go, ModelKey.Chair, SkinFit.Height, ChairHeight, 90f);
                return go;
            });

            // 8. Moloz Yığını (molozfbx.fbx + RubblePile)
            SaveInteractivePrefab($"{PropsPrefabFolder}/Moloz_Yigini.prefab", () =>
            {
                var go = Box("Moloz", null, Vector3.zero, new Vector3(1.1f, 0.6f, 1f), p.Rubble);
                Configure(go.AddComponent<RubblePile>(), ("displayName", "Moloz"), ("workDuration", 5f), ("digCount", 3), ("blocksPath", false));
                ModelSkins.Skin(go, ModelKey.Rubble, SkinFit.Stretch);
                return go;
            });

            // 9. Su Kovası (cop/masafbx.fbx + WaterSource)
            SaveInteractivePrefab($"{PropsPrefabFolder}/Su_Kovasi.prefab", () =>
            {
                var go = Box("Su Kovası", null, Vector3.zero, new Vector3(0.45f, 0.5f, 0.45f), p.Metal);
                Configure(go.AddComponent<WaterSource>(), ("displayName", "Su Kovası"), ("workDuration", 2f), ("usesFloodWater", true));
                ModelSkins.Skin(go, ModelKey.Bucket, SkinFit.Height, 0.65f);
                return go;
            });

            // 10. Şebeke Borusu (boru.fbx + BurstPipe)
            SaveInteractivePrefab($"{PropsPrefabFolder}/Sebeke_Borusu.prefab", () =>
            {
                var go = Box("Şebeke Borusu", null, Vector3.zero, new Vector3(2.4f, 0.25f, 0.25f), p.Water);
                var spray = Box("Su Fışkırması", go.transform, new Vector3(0f, -1.4f, 0f), new Vector3(0.1f, 10f, 1f), p.WaterSurface);
                Object.DestroyImmediate(spray.GetComponent<Collider>());
                var pipeComp = go.AddComponent<BurstPipe>();
                Configure(pipeComp, ("displayName", "Şebeke Borusu"), ("workDuration", 6f));
                SetRef(pipeComp, "sprayVisual", spray);
                ModelSkins.Skin(go, ModelKey.Pipe, SkinFit.Width, 2.4f, centered: true);
                return go;
            });

            // 11. Yangın Tüpü (yangıntup.fbx)
            SaveInteractivePrefab($"{PropsPrefabFolder}/Yangin_Tupu.prefab", () =>
            {
                var go = Box("Yangın Tüpü", null, Vector3.zero, new Vector3(0.35f, 0.7f, 0.35f), p.Fire);
                ModelSkins.Skin(go, ModelKey.Extinguisher, SkinFit.Height, 0.9f);
                return go;
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var folderObj = AssetDatabase.LoadAssetAtPath<Object>(PropsPrefabFolder);
            if (folderObj != null) EditorGUIUtility.PingObject(folderObj);
            Debug.Log($"11 adet etkileşimli eşya prefabı '{PropsPrefabFolder}' klasörüne oluşturuldu! İstediğin odaya sürükleyip dizebilirsin.");
        }

        static void SaveInteractivePrefab(string path, System.Func<GameObject> builder)
        {
            var temp = builder();
            PrefabUtility.SaveAsPrefabAsset(temp, path);
            Object.DestroyImmediate(temp);
        }

        /// <summary>Greybox malzemeleri. Varsa mevcut asset'i kullanır ve rengini günceller.</summary>
        class Palette
        {
            public readonly Material Concrete = Get("Concrete", new Color(0.55f, 0.55f, 0.55f));
            public readonly Material WallA = Get("WallA", new Color(0.42f, 0.40f, 0.38f));
            public readonly Material WallB = Get("WallB", new Color(0.36f, 0.35f, 0.34f));
            public readonly Material Earth = Get("Earth", new Color(0.27f, 0.21f, 0.16f));
            public readonly Material Ladder = Get("Ladder", new Color(0.55f, 0.38f, 0.2f));
            public readonly Material Exit = Get("Exit", new Color(0.75f, 0.2f, 0.18f));
            public readonly Material Water = Get("Water", new Color(0.2f, 0.45f, 0.8f));
            public readonly Material Electric = Get("Electric", new Color(0.9f, 0.75f, 0.15f));
            public readonly Material Craft = Get("Craft", new Color(0.3f, 0.65f, 0.35f));
            public readonly Material Storage = Get("Storage", new Color(0.6f, 0.45f, 0.25f));
            public readonly Material Furniture = Get("Furniture", new Color(0.5f, 0.35f, 0.3f));
            public readonly Material Helipad = Get("Helipad", new Color(0.85f, 0.85f, 0.8f));
            public readonly Material Survivor = Get("Survivor", new Color(0.95f, 0.55f, 0.2f));
            public readonly Material Visor = Get("Visor", new Color(0.15f, 0.15f, 0.18f));
            public readonly Material Selection = Get("Selection", new Color(0.3f, 1f, 0.4f));
            public readonly Material Column = Get("Column", new Color(0.62f, 0.62f, 0.6f));
            public readonly Material Rubble = Get("Rubble", new Color(0.45f, 0.42f, 0.38f));
            public readonly Material Carry = Get("Carry", Color.white);
            public readonly Material Shelter = Get("Shelter", new Color(0.2f, 1f, 0.35f));
            public readonly Material Metal = Get("Metal", new Color(0.55f, 0.6f, 0.68f));
            public readonly Material WaterSurface = MakeTransparent(Get("WaterSurface", new Color(0.2f, 0.45f, 0.85f, 0.55f)));
            public readonly Material Fire = Get("Fire", new Color(1f, 0.45f, 0.1f));
            public readonly Material Smoke = MakeTransparent(Get("Smoke", new Color(0.12f, 0.12f, 0.12f, 0.6f)));
            public readonly Material Gas = MakeTransparent(Get("Gas", new Color(0.55f, 0.75f, 0.35f, 0.5f)));
            public readonly Material Glass = MakeTransparent(Get("Glass", new Color(0.7f, 0.85f, 0.95f, 0.45f)));
            public readonly Material WindowFrame = Get("WindowFrame", new Color(0.2f, 0.2f, 0.22f));
            public readonly Material Supplies = Get("Supplies", new Color(0.9f, 0.9f, 0.88f));
            public readonly Material Helicopter = Get("Helicopter", new Color(0.25f, 0.35f, 0.22f));
            public readonly Material Particle = MakeTransparent(Get("Particle", Color.white, "Universal Render Pipeline/Particles/Unlit"));

            // URP Lit'i saydam yüzeye çevirir (Inspector'daki Surface Type: Transparent ile aynı).
            static Material MakeTransparent(Material mat)
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)RenderQueue.Transparent;
                EditorUtility.SetDirty(mat);
                return mat;
            }

            static Material Get(string name, Color color, string shaderName = null)
            {
                string path = $"{MaterialFolder}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    var shader = shaderName != null ? Shader.Find(shaderName) : null;
                    shader ??= GraphicsSettings.currentRenderPipeline != null
                        ? GraphicsSettings.currentRenderPipeline.defaultShader
                        : Shader.Find("Standard");
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }

                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                else mat.color = color;
                EditorUtility.SetDirty(mat);
                return mat;
            }
        public static void AddCandleLightToSelected()
        {
            var go = UnityEditor.Selection.activeGameObject;
            if (go == null)
            {
                Debug.LogWarning("Lütfen önce Hierarchy veya Scene ekranından mum (veya ışık) objesini seçin.");
                return;
            }

            var manager = Object.FindFirstObjectByType<ModelManager>();
            if (manager != null)
            {
                manager.ConfigureCandleLightOnObject(go);
                manager.RefreshAll();
            }
            else
            {
                var light = go.GetComponentInChildren<Light>(true) ?? new GameObject("Mum_Isigi").AddComponent<Light>();
                if (light.transform.parent != go.transform)
                {
                    light.transform.SetParent(go.transform, false);
                    light.transform.localPosition = new Vector3(0f, 0.25f, -0.08f);
                }
                light.type = LightType.Point;
                light.color = new Color(1.0f, 0.58f, 0.22f);
                light.intensity = 0.85f;
                light.range = 3.2f;
                light.shadows = LightShadows.Soft;
            }

            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log($"'{go.name}' objesine karanlık tema uyumlu sıcak mum ışığı eklendi / güncellendi.", go);
        }
        }
    }

    [CustomEditor(typeof(ModelManager))]
    public class ModelManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();

            GUILayout.Space(8);
            if (changed || GUILayout.Button("Modelleri Sahneye Uygula / Güncelle", GUILayout.Height(28)))
            {
                var manager = (ModelManager)target;
                manager.RefreshAll();
                if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            }
        }
    }
}
