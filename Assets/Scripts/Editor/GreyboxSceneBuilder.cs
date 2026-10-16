using System.Collections.Generic;
using System.Linq;
using AftershockIsle.Building;
using AftershockIsle.CameraControl;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using AftershockIsle.DebugTools;
using AftershockIsle.Economy;
using AftershockIsle.Interaction;
using AftershockIsle.Player;
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
        const string MaterialFolder = "Assets/Art/Materials/Greybox";
        const string DataFolder = "Assets/Data";
        const string PrefabFolder = "Assets/Prefabs";

        const float SlabThickness = 0.3f;
        const float LadderOpening = 1.4f;
        const float ParapetHeight = 1f;
        const float DoorHeight = 2.4f;
        const float LadderZ = 0.45f;

        [MenuItem("Aftershock/Greybox Sahnesini Oluştur")]
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

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var palette = new Palette();

            CreateLighting();
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

            var hud = systems.AddComponent<DebugHUD>();
            SetRef(hud, "timeManager", timeManager);
            SetRef(hud, "dayCycle", dayCycle);
            SetRef(hud, "commands", commands);
            SetRef(hud, "building", building);
            SetRef(hud, "session", session);

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

            systems.AddComponent<DebugCheats>();

            var survivorPrefab = CreateSurvivorPrefab(palette);
            var hero = (GameObject)PrefabUtility.InstantiatePrefab(survivorPrefab);
            hero.name = "Ana Karakter";
            hero.transform.SetPositionAndRotation(building.Point(2, 0f), Quaternion.Euler(0f, 180f, 0f));
            SetRef(hero.GetComponent<CharacterMover>(), "building", building);
            Configure(hero.GetComponent<Survivor>(), ("displayName", "Ana Karakter"));

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            Selection.activeGameObject = hero;

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

        static void CreateLighting()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(35f, 20f, 0f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.35f, 0.36f, 0.4f);
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
            Box("Dış Çıkış Kapısı", geo,
                new Vector3(wallX, (doorBottom + doorTop) * 0.5f, 0f),
                new Vector3(t * 0.6f, DoorHeight, 1.4f), p.Exit);

            // Zemin: bodrum yer altında, giriş katı sokak seviyesinde.
            float groundY = 1 * h;
            const float earthDepthBelow = 3f;
            const float earthBack = 24f;
            float earthHeight = groundY + earthDepthBelow;
            float earthCenterY = groundY - earthHeight * 0.5f;
            float earthCenterZ = -d * 0.5f + earthBack * 0.5f;
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
                    new Vector3(ladder.x, ladder.lowerFloor * h + h * 0.5f, LadderZ),
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

            // Taşıyıcı kolonlar: bodrum, giriş ve yaşam katlarında ikişer. Yaşam katındakiler çatıyı taşır.
            var cracked = new[] { (1, -3f), (2, 3f) };
            for (int floor = 0; floor < layout.FloorCount - 1; floor++)
            {
                foreach (float x in new[] { -3f, 3f })
                {
                    float height = h - SlabThickness;
                    var column = Box($"Kolon_{floor}_{(x < 0 ? "Sol" : "Sağ")}", props,
                        new Vector3(x, floor * h + height * 0.5f, backZ - 0.45f), new Vector3(0.5f, height, 0.5f), p.Column);
                    var component = column.AddComponent<SupportColumn>();
                    Configure(component, ("displayName", "Taşıyıcı Kolon"), ("workDuration", 4f));
                    if (System.Array.IndexOf(cracked, (floor, x)) >= 0)
                        Configure(component, ("state", (int)ColumnState.Cracked));
                }
            }

            // Kat 0: Bodrum
            var pipe = Box("Şebeke Borusu", props, new Vector3(0f, 0 * h + 2.7f, backZ - 0.3f), new Vector3(2.4f, 0.25f, 0.25f), p.Water);
            var spray = Box("Su Fışkırması", props, new Vector3(0f, 0 * h + 1.3f, backZ - 0.3f), new Vector3(0.25f, 2.6f, 0.25f), p.WaterSurface);
            Object.DestroyImmediate(spray.GetComponent<Collider>());
            var pipeComponent = pipe.AddComponent<BurstPipe>();
            Configure(pipeComponent, ("displayName", "Şebeke Borusu"), ("workDuration", 6f));
            SetRef(pipeComponent, "sprayVisual", spray);

            var pump = Box("Su Tulumbası", props, new Vector3(5.5f, 0 * h + 0.6f, backZ - 0.5f), new Vector3(1f, 1.2f, 0.8f), p.Water);
            Configure(pump.AddComponent<ManualPump>(), ("displayName", "Manuel Su Tulumbası"), ("workDuration", 1f));

            // Su, karakterleri ve objeleri kapatmasın diye yarı saydam; tıklamaları engellemesin diye collider'sız.
            var water = Box("Bodrum Suyu", props, new Vector3(0f, 0 * h + 0.01f, 0f),
                new Vector3(layout.Width, 0.02f, layout.Depth), p.WaterSurface);
            Object.DestroyImmediate(water.GetComponent<Collider>());
            FurnitureProp("Dolap", props, new Vector3(1.5f, 0 * h + 0.8f, backZ - 0.45f), new Vector3(1.2f, 1.6f, 0.7f), p.Furniture);
            Rubble("Moloz", props, new Vector3(-4.5f, 0 * h, backZ - 0.9f), new Vector3(1.1f, 0.6f, 1f), p.Rubble, 3, false);

            // Kat 1: Giriş katı. Dış kapının önü yıkıntıyla kapalı.
            var breaker = Box("Ana Şalter Panosu", props, new Vector3(-4.5f, 1 * h + 1.4f, backZ - 0.2f), new Vector3(0.8f, 1.2f, 0.3f), p.Electric);
            Configure(breaker.AddComponent<BreakerPanel>(), ("displayName", "Ana Şalter Panosu"), ("workDuration", 1.5f));

            var radiator = Box("Eski Radyatör", props, new Vector3(1.8f, 1 * h + 0.45f, backZ - 0.3f), new Vector3(0.9f, 0.9f, 0.3f), p.Metal);
            Configure(radiator.AddComponent<Furniture>(), ("displayName", "Eski Radyatör"), ("workDuration", 6f),
                ("providesShelter", false), ("salvage.type", (int)ResourceType.Metal), ("salvage.amount", 2));
            FurnitureProp("Masa", props, new Vector3(0f, 1 * h + 0.4f, backZ - 0.8f), new Vector3(1.6f, 0.8f, 0.9f), p.Furniture);
            Rubble("Yıkıntı (Kapı Önü)", props, new Vector3(7.7f, 1 * h, 0f), new Vector3(1.4f, 1.1f, 1.8f), p.Rubble, 5, true);

            // Kat 2: Yaşam / Tezgah
            Prop("Çalışma Tezgahı", props, new Vector3(-1f, 2 * h + 0.5f, backZ - 0.6f), new Vector3(2.2f, 1f, 1f), p.Craft);
            var chest = Box("Ortak Sandık", props, new Vector3(1.4f, 2 * h + 0.4f, backZ - 0.5f), new Vector3(1.2f, 0.8f, 0.8f), p.Storage);
            Configure(chest.AddComponent<StorageChest>(), ("displayName", "Ortak Sandık"), ("workDuration", 0.5f));
            FurnitureProp("Yataklar", props, new Vector3(4.6f, 2 * h + 0.25f, backZ - 0.6f), new Vector3(2f, 0.5f, 1f), p.Furniture);
            Rubble("Moloz", props, new Vector3(-4.6f, 2 * h, backZ - 0.9f), new Vector3(1f, 0.5f, 1f), p.Rubble, 2, false);

            var bucketSpot = Box("Kova (Bodrum Suyu)", props, new Vector3(4.3f, 0 * h + 0.25f, backZ - 0.9f), new Vector3(0.45f, 0.5f, 0.45f), p.Metal);
            Configure(bucketSpot.AddComponent<WaterSource>(), ("displayName", "Kova (Bodrum Suyu)"), ("workDuration", 2f), ("usesFloodWater", true));

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

            // Kat 3: Çatı. Tahliye alanı yıkıntıyla kapalı; zafer için temizlenmesi gerekecek.
            Prop("Telsiz İstasyonu", props, new Vector3(-3.5f, 3 * h + 0.7f, backZ - 0.6f), new Vector3(1.2f, 1.4f, 1f), p.Electric);
            Prop("Helikopter Tahliye Alanı", props, new Vector3(4.5f, 3 * h + 0.03f, 0f), new Vector3(5f, 0.05f, 3f), p.Helipad);
            Rubble("Çatı Yıkıntısı", props, new Vector3(1.2f, 3 * h, 0f), new Vector3(1.4f, 1f, 1.8f), p.Rubble, 4, true);
            Rubble("Moloz", props, new Vector3(5f, 3 * h, 0.6f), new Vector3(1.2f, 0.6f, 1.2f), p.Rubble, 3, false);
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

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabFolder}/CollapsedCeiling.prefab");
            Object.DestroyImmediate(go);
            return prefab.GetComponent<RubblePile>();
        }

        static void FurnitureProp(string name, Transform parent, Vector3 center, Vector3 size, Material material)
        {
            var go = Box(name, parent, center, size, material);
            Configure(go.AddComponent<Furniture>(), ("displayName", name), ("workDuration", 6f));
        }

        /// <summary>floorBase: yığının oturduğu zemin noktası.</summary>
        static void Rubble(string name, Transform parent, Vector3 floorBase, Vector3 size, Material material, int digs, bool blocksPath)
        {
            var go = Box(name, parent, floorBase + Vector3.up * (size.y * 0.5f), size, material);
            Configure(go.AddComponent<RubblePile>(),
                ("displayName", name), ("workDuration", 5f), ("digCount", digs), ("blocksPath", blocksPath));
        }

        /// <summary>Kırılan kolonun önüne düşen enkaz. Kolon yeniden dikilmeden önce kazılmalı.</summary>
        static RubblePile CreateDebrisPrefab(Palette p)
        {
            var go = Box("Enkaz", null, Vector3.zero, new Vector3(1.3f, 0.6f, 1.2f), p.Rubble);
            Configure(go.AddComponent<RubblePile>(),
                ("displayName", "Enkaz"), ("workDuration", 4f), ("digCount", 2), ("blocksPath", false));

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

            var carried = Box("CarriedItem", root.transform, new Vector3(0f, 1.1f, 0.42f), new Vector3(0.5f, 0.35f, 0.35f), p.Carry);
            Object.DestroyImmediate(carried.GetComponent<Collider>());
            SetRef(survivor, "carriedVisual", carried.GetComponent<Renderer>());

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            body.GetComponent<Renderer>().sharedMaterial = p.Survivor;

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
                    case int i when prop.propertyType == SerializedPropertyType.Enum: prop.enumValueIndex = i; break;
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

        static void AddSceneToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(path, true) };
            scenes.AddRange(EditorBuildSettings.scenes.Where(s => s.path != path));
            EditorBuildSettings.scenes = scenes.ToArray();
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
            public readonly Material Glass = MakeTransparent(Get("Glass", new Color(0.7f, 0.85f, 0.95f, 0.45f)));
            public readonly Material WindowFrame = Get("WindowFrame", new Color(0.2f, 0.2f, 0.22f));

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

            static Material Get(string name, Color color)
            {
                string path = $"{MaterialFolder}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    var shader = GraphicsSettings.currentRenderPipeline != null
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
        }
    }
}
