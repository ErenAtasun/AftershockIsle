using System;
using System.Collections.Generic;
using System.IO;
using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Crisis;
using AftershockIsle.Economy;
using AftershockIsle.Endgame;
using AftershockIsle.Expedition;
using AftershockIsle.Interaction;
using AftershockIsle.Skills;
using UnityEngine;

namespace AftershockIsle.Save
{
    public readonly struct SaveCompleted
    {
        public readonly string SlotName;
        public readonly bool Success;
        public SaveCompleted(string slotName, bool success)
        {
            SlotName = slotName;
            Success = success;
        }
    }

    public readonly struct GameLoaded
    {
        public readonly bool Success;
        public GameLoaded(bool success) => Success = success;
    }

    /// <summary>
    /// Oyunun durumunu JSON formatında kaydeder ve yükler.
    /// Her yeni gün başında otomatik kayıt (Auto-save) alır.
    /// </summary>
    public class SaveManager : MonoBehaviour
    {
        public const string DefaultSaveFile = "savegame.json";
        public const string AutoSaveFile = "autosave.json";

        public static SaveManager Instance { get; private set; }
        public static bool PendingLoad { get; set; }
        /// <summary>Kayıt uygulanırken true: bu sırada yayınlanan gün/faz olayları yeni bir gün ya da gece değildir.</summary>
        public static bool IsApplying { get; private set; }

        public static string SaveDirectory => Path.Combine(Application.persistentDataPath, "Saves");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstance()
        {
            if (Instance == null)
            {
                var existing = FindFirstObjectByType<SaveManager>();
                if (existing != null)
                {
                    Instance = existing;
                    return;
                }

                var systems = GameObject.Find("[Systems]");
                if (systems != null)
                {
                    systems.AddComponent<SaveManager>();
                }
            }
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            EnsureDirectory();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            EventBus.Subscribe<DayStarted>(OnDayStarted);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<DayStarted>(OnDayStarted);
        }

        void Start()
        {
            if (PendingLoad && HasSave())
            {
                LoadGame();
                PendingLoad = false;
            }
        }

        // Sefer sırasında kayda girmeyen durum (saha, çanta, süre) olduğu için o sırada gelen otomatik kayıt sefer bitince alınır.
        bool autosavePending;

        void OnDayStarted(DayStarted evt)
        {
            if (evt.Day < 1) return;
            autosavePending = true;
            TryAutosave();
        }

        void Update()
        {
            if (autosavePending) TryAutosave();
        }

        void TryAutosave()
        {
            if (!CanSave(out _)) return;
            autosavePending = false;
            SaveGame(AutoSaveFile);
            SaveGame(DefaultSaveFile);
        }

        /// <summary>Şu an kayıt alınabilir mi. Sefer sürerken (saha durumu kayda girmez) ya da oyun bitmişken alınmaz.</summary>
        public static bool CanSave(out string reason)
        {
            var expeditions = ExpeditionSystem.Instance;
            var session = FindFirstObjectByType<GameSession>();
            if (expeditions != null && expeditions.Stage != ExpeditionStage.None && expeditions.Stage != ExpeditionStage.ChoosingSite)
                reason = "Sefer sürerken kayıt alınamaz. Kâşif dönünce kaydet.";
            else if (session != null && session.Outcome != GameOutcome.None)
                reason = "Oyun bitti; kayıt alınamaz.";
            else
                reason = null;
            return reason == null;
        }

        public static void EnsureDirectory()
        {
            if (!Directory.Exists(SaveDirectory))
                Directory.CreateDirectory(SaveDirectory);
        }

        public static string GetSavePath(string filename = DefaultSaveFile)
        {
            EnsureDirectory();
            return Path.Combine(SaveDirectory, filename);
        }

        public static bool HasSave(string filename = DefaultSaveFile)
        {
            string path = GetSavePath(filename);
            return File.Exists(path) && new FileInfo(path).Length > 10;
        }

        public static void DeleteSave(string filename = DefaultSaveFile)
        {
            string path = GetSavePath(filename);
            if (File.Exists(path)) File.Delete(path);
        }

        public bool SaveGame(string filename = DefaultSaveFile)
        {
            if (!CanSave(out string reason))
            {
                Debug.Log($"[SaveManager] Kayıt alınmadı: {reason}");
                return false;
            }

            try
            {
                var data = CaptureSaveData();
                string json = JsonUtility.ToJson(data, true);
                string path = GetSavePath(filename);
                File.WriteAllText(path, json);
                Debug.Log($"[SaveManager] Oyun başarıyla kaydedildi: {path}");
                EventBus.Publish(new SaveCompleted(filename, true));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] Kaydetme hatası: {ex}");
                EventBus.Publish(new SaveCompleted(filename, false));
                return false;
            }
        }

        public bool LoadGame(string filename = DefaultSaveFile)
        {
            string path = GetSavePath(filename);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[SaveManager] Kayıt dosyası bulunamadı: {path}");
                EventBus.Publish(new GameLoaded(false));
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                var data = JsonUtility.FromJson<GameSaveData>(json);
                if (data == null)
                {
                    Debug.LogError("[SaveManager] Kayıt verisi çözülemedi (null).");
                    EventBus.Publish(new GameLoaded(false));
                    return false;
                }

                IsApplying = true;
                try
                {
                    ApplySaveData(data);
                }
                finally
                {
                    IsApplying = false;
                }
                Debug.Log($"[SaveManager] Oyun başarıyla yüklendi (Gün {data.dayCycle.day}).");
                EventBus.Publish(new GameLoaded(true));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] Yükleme hatası: {ex}");
                EventBus.Publish(new GameLoaded(false));
                return false;
            }
        }

        // ---------------------------------------------------------------- Durum Yakalama
        GameSaveData CaptureSaveData()
        {
            var data = new GameSaveData
            {
                saveDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                saveVersion = 2,
            };

            // 1. DayCycle
            var dayCycle = FindFirstObjectByType<DayCycle>();
            if (dayCycle != null)
            {
                data.dayCycle = new DayCycleSaveData
                {
                    day = dayCycle.Day,
                    phase = (int)dayCycle.Phase,
                    phaseElapsed = dayCycle.PhaseElapsed,
                };
            }

            // 2. StructuralIntegrity
            var integrity = StructuralIntegrity.Instance;
            if (integrity != null)
            {
                data.integrity = new StructuralIntegritySaveData
                {
                    current = integrity.Current,
                    isCollapsed = integrity.IsCollapsed,
                };
            }

            // 3. PowerGrid
            data.powerGrid = new PowerGridSaveData
            {
                isOn = PowerGrid.IsOn,
            };

            // 4. ResourceStorage
            var storage = ResourceStorage.Instance;
            if (storage != null)
            {
                data.resources = new ResourceStorageSaveData
                {
                    scrap = storage.Get(ResourceType.Scrap),
                    metal = storage.Get(ResourceType.Metal),
                    electronics = storage.Get(ResourceType.Electronics),
                    supplies = storage.Get(ResourceType.Supplies),
                };
            }

            // 5. Workbench & Grid Placed Structures
            var bench = Workbench.Instance;
            if (bench != null)
            {
                var craftedList = new List<int>();
                foreach (var id in bench.GetCraftedRecipes()) craftedList.Add((int)id);

                var unplacedIds = new List<int>();
                var unplacedCounts = new List<int>();
                foreach (var kv in bench.UnplacedInventory)
                {
                    if (kv.Value <= 0) continue;
                    unplacedIds.Add((int)kv.Key);
                    unplacedCounts.Add(kv.Value);
                }

                data.workbench = new WorkbenchSaveData
                {
                    tier = bench.Tier,
                    steelBeams = bench.SteelBeams,
                    ammo = bench.Ammo,
                    craftedRecipeIds = craftedList,
                    unplacedRecipeIds = unplacedIds,
                    unplacedRecipeCounts = unplacedCounts,
                };
            }

            foreach (var placed in PlaceableStructure.All)
            {
                if (placed == null) continue;
                data.placedStructures.Add(new PlacedStructureSaveData
                {
                    recipeId = (int)placed.Recipe,
                    floor = placed.GridFloor,
                    cellX = placed.CellX,
                    cellZ = placed.CellZ,
                    rotationSteps = placed.RotationSteps,
                });
            }

            // 6. SkillTree
            var skills = SkillTree.Instance;
            if (skills != null)
            {
                var unlockedList = new List<int>();
                foreach (var id in skills.GetUnlockedSkills()) unlockedList.Add((int)id);

                data.skills = new SkillTreeSaveData
                {
                    points = skills.Points,
                    unlockedSkillIds = unlockedList,
                };
            }

            // 7. DisasterDirector
            var director = FindFirstObjectByType<DisasterDirector>();
            if (director != null)
            {
                var firedSchedule = new List<int>(director.GetFiredScheduleIndices());
                var firedEndless = new List<int>(director.GetFiredEndlessIndices());

                data.disasters = new DisasterDirectorSaveData
                {
                    firedScheduleIndices = firedSchedule,
                    endlessDay = director.EndlessDay,
                    firedEndlessIndices = firedEndless,
                };
            }

            // 8. FloodSystem
            var flood = FloodSystem.Instance;
            if (flood != null)
            {
                data.flood = new FloodSaveData
                {
                    level = flood.Level,
                    fullTimer = flood.FullTimer,
                };
            }

            // 9. RadioStation
            var radio = RadioStation.Instance;
            if (radio != null)
            {
                data.radio = new RadioStationSaveData
                {
                    isRepaired = radio.IsRepaired,
                    signal01 = radio.Signal01,
                    signalComplete = radio.SignalComplete,
                };
            }

            // 10. ElectricPump
            var pump = ElectricPump.FindActive();
            if (pump != null)
            {
                data.pump = new ElectricPumpSaveData
                {
                    isBroken = pump.IsBroken,
                    wear = pump.Wear,
                };
            }

            // 11. BurstPipe
            var pipe = FindFirstObjectByType<BurstPipe>();
            if (pipe != null)
            {
                data.pipe = new BurstPipeSaveData
                {
                    isBurst = pipe.IsBurst,
                };
            }

            // 12. SupportColumns
            foreach (var col in SupportColumn.All)
            {
                if (col == null) continue;
                data.columns.Add(new ColumnSaveData
                {
                    name = col.gameObject.name,
                    floor = col.Floor,
                    x = col.transform.position.x,
                    state = (int)col.State,
                    supportHitsRemaining = col.SupportHitsRemaining,
                });
            }

            // 13. SmokeWindows
            foreach (var win in FindObjectsByType<SmokeWindow>(FindObjectsSortMode.None))
            {
                data.windows.Add(new WindowSaveData
                {
                    floor = win.Floor,
                    isBroken = win.IsBroken,
                });
            }

            // 14. WaterSources
            foreach (var ws in FindObjectsByType<WaterSource>(FindObjectsSortMode.None))
            {
                if (!ws.UsesFloodWater)
                {
                    data.waterSources.Add(new WaterSourceSaveData
                    {
                        name = ws.gameObject.name,
                        charges = ws.Charges,
                    });
                }
            }

            // 15. FireSpots
            foreach (var fire in FireSpot.All)
            {
                if (fire == null) continue;
                data.fireSpots.Add(new FireSpotSaveData
                {
                    floor = fire.Floor,
                    intensity = fire.Intensity,
                    smoke = fire.Smoke,
                    fullBurnTimer = fire.FullBurnTimer,
                    hasCollapsed = fire.HasCollapsed,
                });
            }

            // 16. GasLeaks
            foreach (var gas in GasLeak.All)
            {
                if (gas == null) continue;
                data.gasLeaks.Add(new GasLeakSaveData
                {
                    floor = gas.Floor,
                    isLeaking = gas.IsLeaking,
                    concentration = gas.Concentration,
                });
            }

            // 17. Furniture
            foreach (var f in FindObjectsByType<Furniture>(FindObjectsSortMode.None))
            {
                data.furnitures.Add(new FurnitureSaveData
                {
                    name = f.gameObject.name,
                    isDismantled = false,
                    isSearched = f.IsSearched,
                });
            }

            // 18. RubblePiles
            foreach (var rubble in RubblePile.All)
            {
                if (rubble == null) continue;
                data.rubbles.Add(new RubbleSaveData
                {
                    name = rubble.gameObject.name,
                    floor = rubble.Floor,
                    position = rubble.transform.position,
                    scale = rubble.transform.localScale,
                    remaining = rubble.Remaining,
                    digCount = rubble.DigCount,
                    blocksPath = rubble.BlocksPath,
                    isCleared = rubble.Remaining <= 0,
                });
            }

            // 19. Survivors
            var allSurvivors = new List<Survivor>(Survivor.All);
            var away = ExpeditionSystem.AwayExplorer;
            if (away != null && !allSurvivors.Contains(away)) allSurvivors.Add(away);
            var awayComp = ExpeditionSystem.AwayCompanion;
            if (awayComp != null && !allSurvivors.Contains(awayComp)) allSurvivors.Add(awayComp);
            foreach (var scout in NightScavenge.AwaySurvivors())
                if (!allSurvivors.Contains(scout)) allSurvivors.Add(scout);

            foreach (var s in allSurvivors)
            {
                if (s == null) continue;
                var bagList = new List<ResourceItemSaveData>();
                foreach (var b in s.Bag)
                    bagList.Add(new ResourceItemSaveData { type = (int)b.type, amount = b.amount });

                var traitList = new List<int>();
                foreach (var t in s.Traits) traitList.Add((int)t);

                data.survivors.Add(new SurvivorSaveData
                {
                    displayName = s.DisplayName,
                    branch = (int)s.Branch,
                    health = s.Health,
                    isDead = s.IsDead,
                    position = s.transform.position,
                    isCarrying = s.IsCarrying,
                    carried = new ResourceItemSaveData
                    {
                        type = (int)s.Carried.type,
                        amount = s.Carried.amount,
                    },
                    bag = bagList,
                    trappedRemaining = s.TrappedRemaining,
                    stress = s.Condition != null ? s.Condition.Stress : 0f,
                    hunger = s.Condition != null ? s.Condition.Hunger : 0,
                    panicRemaining = s.Condition != null ? s.Condition.PanicRemaining : 0f,
                    traitIds = traitList,
                });
            }

            // 20. Weather & Narrative
            data.currentWeather = (int)WeatherSystem.Current;
            if (NarrativeEventSystem.Instance != null)
            {
                data.triggeredNarrativeDays = new List<int>(NarrativeEventSystem.Instance.GetTriggeredDays());
                data.motherGiftDueDay = NarrativeEventSystem.Instance.MotherGiftDueDay;
            }
            if (NarrativeEventSystem.Instance != null)
            {
                data.encounterDeck = new List<int>(NarrativeEventSystem.Instance.EncounterDeck);
                data.lastEncounter = NarrativeEventSystem.Instance.LastEncounter;
            }
            if (NightDefenseSystem.Instance != null)
            {
                data.raiderAttention = NightDefenseSystem.Instance.Attention;
                NightDefenseSystem.Instance.CaptureNight(out data.nightRaidPending, out data.nightRaidAt, out data.nightGuard);
            }
            NightScavenge.Instance?.Capture(data.nightScouts, data.nightScoutSites);
            data.eveningPlanDone = AftershockIsle.UI.EveningPlan.PlanDone;
            data.gameMode = (int)GameMode.Current;
            data.supplyDropDay = EvacuationSystem.NextDropDay;

            return data;
        }

        static void SpawnSavedRubble(RubbleSaveData rData)
        {
            if (rData.scale.x <= 0.01f || rData.scale.y <= 0.01f) return;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.SetActive(false);
            go.name = rData.name;
            go.transform.position = rData.position;
            go.transform.localScale = rData.scale;

            var color = new Color(0.45f, 0.42f, 0.38f);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            go.GetComponent<Renderer>().sharedMaterial = material;

            var pile = go.AddComponent<RubblePile>();
            // Kalan kazı tam yığın sayılır (kayıttaki ölçü zaten alçalmış hali).
            pile.SetupRuntime(rData.remaining, rData.blocksPath);
            pile.ConfigureRuntime(rData.blocksPath ? "Tavan Enkazı" : "Moloz", 4f);
            go.SetActive(true);
        }

        // ---------------------------------------------------------------- Durum Uygulama
        void ApplySaveData(GameSaveData data)
        {
            // 0. Oyun modu (diğer sistemler buna göre davranır).
            GameMode.Current = (GameModeType)Mathf.Clamp(data.gameMode, 0, 1);
            EvacuationSystem.NextDropDay = data.supplyDropDay;

            // 1. DayCycle
            var dayCycle = FindFirstObjectByType<DayCycle>();
            if (dayCycle != null && data.dayCycle.day > 0)
            {
                dayCycle.RestoreState(data.dayCycle.day, (DayPhase)data.dayCycle.phase, data.dayCycle.phaseElapsed);
            }

            // 2. StructuralIntegrity
            var integrity = StructuralIntegrity.Instance;
            if (integrity != null)
            {
                integrity.RestoreState(data.integrity.current, data.integrity.isCollapsed);
            }

            // 3. PowerGrid
            var power = PowerGrid.Instance;
            if (power != null)
            {
                power.SetPower(data.powerGrid.isOn);
            }

            // 4. ResourceStorage
            var storage = ResourceStorage.Instance;
            if (storage != null && data.resources.scrap >= 0)
            {
                storage.RestoreState(data.resources.scrap, data.resources.metal, data.resources.electronics, data.resources.supplies);
            }

            // 5. Workbench & Grid Placed Structures
            var bench = Workbench.Instance;
            if (bench != null && data.workbench.tier > 0)
            {
                var crafted = new List<RecipeId>();
                if (data.workbench.craftedRecipeIds != null)
                    foreach (var id in data.workbench.craftedRecipeIds) crafted.Add((RecipeId)id);

                var unplacedDict = new Dictionary<RecipeId, int>();
                if (data.workbench.unplacedRecipeIds != null && data.workbench.unplacedRecipeCounts != null)
                {
                    int n = Mathf.Min(data.workbench.unplacedRecipeIds.Count, data.workbench.unplacedRecipeCounts.Count);
                    for (int i = 0; i < n; i++)
                        unplacedDict[(RecipeId)data.workbench.unplacedRecipeIds[i]] = data.workbench.unplacedRecipeCounts[i];
                }

                bench.RestoreState(data.workbench.tier, data.workbench.steelBeams, data.workbench.ammo, crafted, unplacedDict);
                InstallableVisual.RefreshAll();
            }

            if (ShelterGrid.Instance != null)
            {
                ShelterGrid.Instance.ClearAllPlaced();
                if (data.placedStructures != null)
                {
                    foreach (var pData in data.placedStructures)
                    {
                        ShelterGrid.Instance.PlaceStructure((RecipeId)pData.recipeId, pData.floor, pData.cellX, pData.cellZ, pData.rotationSteps);
                    }
                }
            }

            // 6. SkillTree
            var skills = SkillTree.Instance;
            if (skills != null)
            {
                var unlocked = new List<SkillId>();
                if (data.skills.unlockedSkillIds != null)
                    foreach (var id in data.skills.unlockedSkillIds) unlocked.Add((SkillId)id);

                skills.RestoreState(data.skills.points, unlocked);
            }

            // 7. DisasterDirector
            var director = FindFirstObjectByType<DisasterDirector>();
            if (director != null && data.disasters.firedScheduleIndices != null)
            {
                director.RestoreState(data.disasters.firedScheduleIndices, data.disasters.endlessDay, data.disasters.firedEndlessIndices);
            }

            // 8. FloodSystem
            var flood = FloodSystem.Instance;
            if (flood != null)
            {
                flood.RestoreState(data.flood.level, data.flood.fullTimer);
            }

            // 9. RadioStation
            var radio = RadioStation.Instance;
            if (radio != null)
            {
                radio.RestoreState(data.radio.isRepaired, data.radio.signal01, data.radio.signalComplete);
            }

            // 10. ElectricPump
            var pump = ElectricPump.FindActive();
            if (pump != null)
            {
                pump.RestoreState(data.pump.isBroken, data.pump.wear);
            }

            // 11. BurstPipe
            var pipe = FindFirstObjectByType<BurstPipe>();
            if (pipe != null)
            {
                pipe.RestoreState(data.pipe.isBurst);
            }

            // 12. SupportColumns
            if (data.columns != null && data.columns.Count > 0)
            {
                var colDict = new Dictionary<string, ColumnSaveData>();
                foreach (var c in data.columns) colDict[c.name] = c;

                foreach (var col in SupportColumn.All)
                {
                    if (col == null) continue;
                    if (colDict.TryGetValue(col.gameObject.name, out var cData))
                        col.RestoreState((ColumnState)cData.state, cData.supportHitsRemaining);
                }
            }

            // 13. SmokeWindows
            if (data.windows != null && data.windows.Count > 0)
            {
                var winDict = new Dictionary<int, bool>();
                foreach (var w in data.windows) winDict[w.floor] = w.isBroken;

                foreach (var win in FindObjectsByType<SmokeWindow>(FindObjectsSortMode.None))
                {
                    if (winDict.TryGetValue(win.Floor, out bool isBroken))
                        win.RestoreState(isBroken);
                }
            }

            // 14. WaterSources
            if (data.waterSources != null && data.waterSources.Count > 0)
            {
                var wsDict = new Dictionary<string, int>();
                foreach (var ws in data.waterSources) wsDict[ws.name] = ws.charges;

                foreach (var ws in FindObjectsByType<WaterSource>(FindObjectsSortMode.None))
                {
                    if (!ws.UsesFloodWater && wsDict.TryGetValue(ws.gameObject.name, out int charges))
                        ws.RestoreState(charges);
                }
            }

            // 15. FireSpots
            if (data.fireSpots != null && data.fireSpots.Count > 0)
            {
                var fireDict = new Dictionary<int, FireSpotSaveData>();
                foreach (var f in data.fireSpots) fireDict[f.floor] = f;

                foreach (var fire in FireSpot.All)
                {
                    if (fire == null) continue;
                    if (fireDict.TryGetValue(fire.Floor, out var fData))
                        fire.RestoreState(fData.intensity, fData.smoke, fData.fullBurnTimer, fData.hasCollapsed);
                }
            }

            // 16. GasLeaks
            if (data.gasLeaks != null && data.gasLeaks.Count > 0)
            {
                var gasDict = new Dictionary<int, GasLeakSaveData>();
                foreach (var g in data.gasLeaks) gasDict[g.floor] = g;

                foreach (var gas in GasLeak.All)
                {
                    if (gas == null) continue;
                    if (gasDict.TryGetValue(gas.Floor, out var gData))
                        gas.RestoreState(gData.isLeaking, gData.concentration);
                }
            }

            // 17. Furniture
            if (data.furnitures != null && (data.furnitures.Count > 0 || data.saveVersion >= 2))
            {
                var fDict = new Dictionary<string, FurnitureSaveData>();
                foreach (var f in data.furnitures) fDict[f.name] = f;

                foreach (var furn in FindObjectsByType<Furniture>(FindObjectsSortMode.None))
                {
                    if (furn == null) continue;
                    if (fDict.TryGetValue(furn.gameObject.name, out var fData))
                    {
                        if (fData.isDismantled) Destroy(furn.gameObject);
                        else furn.RestoreState(fData.isSearched);
                    }
                    // Kayıtta yoksa kaydedilmeden önce parçalanmıştı (sürüm 2'den itibaren güvenilir).
                    else if (data.saveVersion >= 2) Destroy(furn.gameObject);
                }
            }

            // 18. RubblePiles
            if (data.rubbles != null && (data.rubbles.Count > 0 || data.saveVersion >= 2))
            {
                var rubbleDict = new Dictionary<string, RubbleSaveData>();
                foreach (var r in data.rubbles) rubbleDict[r.name] = r;

                foreach (var rubble in new List<RubblePile>(RubblePile.All))
                {
                    if (rubble == null) continue;
                    if (rubbleDict.TryGetValue(rubble.gameObject.name, out var rData))
                        rubble.RestoreState(rData.isCleared ? 0 : rData.remaining);
                    // Kayıtta yoksa kaydedilmeden önce temizlenmişti.
                    else if (data.saveVersion >= 2) rubble.RestoreState(0);
                }

                // Oyun sırasında düşen enkaz (kırık kolon, çöken tavan) sahnede yok: kayıttaki yerinde yeniden oluşturulur.
                var sceneNames = new Dictionary<string, int>();
                foreach (var rubble in RubblePile.All)
                {
                    if (rubble == null) continue;
                    sceneNames.TryGetValue(rubble.gameObject.name, out int n);
                    sceneNames[rubble.gameObject.name] = n + 1;
                }
                foreach (var rData in data.rubbles)
                {
                    if (rData.isCleared || rData.remaining <= 0) continue;
                    if (sceneNames.TryGetValue(rData.name, out int n) && n > 0)
                    {
                        sceneNames[rData.name] = n - 1;
                        continue;
                    }
                    SpawnSavedRubble(rData);
                }
            }

            // 19. Survivors
            if (data.survivors != null && data.survivors.Count > 0)
            {
                var sceneSurvivors = new List<Survivor>(Survivor.All);
                var roster = SurvivorRoster.Instance;

                foreach (var sData in data.survivors)
                {
                    var match = sceneSurvivors.Find(s => s.DisplayName == sData.displayName);
                    if (match == null && roster != null && !sData.isDead)
                    {
                        // Sahnede olmayan (kurtarılmış) kazazedeyi oluştur
                        var layout = FindFirstObjectByType<BuildingLayout>();
                        int floor = layout != null ? layout.FloorAtHeight(sData.position.y + 0.25f)
                            : Mathf.Clamp(Mathf.RoundToInt(sData.position.y / 4.5f), 0, 4);
                        match = roster.Spawn(sData.displayName, (SkillBranch)sData.branch, floor, sData.position.x);
                    }

                    if (match != null)
                    {
                        var carried = new ResourceAmount((ResourceType)sData.carried.type, sData.carried.amount);
                        var bag = new List<ResourceAmount>();
                        if (sData.bag != null)
                            foreach (var b in sData.bag) bag.Add(new ResourceAmount((ResourceType)b.type, b.amount));

                        if (sData.traitIds != null && sData.traitIds.Count > 0)
                        {
                            var traits = new List<TraitId>();
                            foreach (var t in sData.traitIds) traits.Add((TraitId)t);
                            match.SetTraits(traits);
                        }

                        match.RestoreState(sData.health, sData.isDead, carried, sData.isCarrying, sData.trappedRemaining, bag, sData.position);
                        if (match.Condition != null)
                            match.Condition.RestoreState(sData.stress, sData.hunger, sData.panicRemaining);
                    }
                }
            }

            // 20. Weather & Narrative
            if (WeatherSystem.Instance != null)
            {
                WeatherSystem.Instance.SetWeather((WeatherType)data.currentWeather);
            }
            if (NarrativeEventSystem.Instance != null)
            {
                NarrativeEventSystem.Instance.RestoreState(data.triggeredNarrativeDays, data.motherGiftDueDay);
                NarrativeEventSystem.Instance.RestoreDeck(data.encounterDeck, data.lastEncounter);
            }
            if (NightDefenseSystem.Instance != null)
            {
                NightDefenseSystem.Instance.RestoreState(data.raiderAttention);
                NightDefenseSystem.Instance.RestoreNight(data.nightRaidPending, data.nightRaidAt, data.nightGuard);
            }

            // 21. Akşam planı ve gece keşfi
            bool night = dayCycle != null && dayCycle.Phase == DayPhase.Night;
            AftershockIsle.UI.EveningPlan.Restore(data.eveningPlanDone, night);
            NightScavenge.Instance?.Restore(data.nightScouts, data.nightScoutSites, night, dayCycle != null ? dayCycle.Day : 1);
        }
    }
}
