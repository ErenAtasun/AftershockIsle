using System;
using System.Collections.Generic;
using UnityEngine;

namespace AftershockIsle.Save
{
    [Serializable]
    public class GameSaveData
    {
        public string saveDate;
        public int saveVersion = 1;

        public DayCycleSaveData dayCycle;
        public StructuralIntegritySaveData integrity;
        public PowerGridSaveData powerGrid;
        public ResourceStorageSaveData resources;
        public WorkbenchSaveData workbench;
        public SkillTreeSaveData skills;
        public DisasterDirectorSaveData disasters;
        public FloodSaveData flood;
        public RadioStationSaveData radio;
        public ElectricPumpSaveData pump;
        public BurstPipeSaveData pipe;

        public List<ColumnSaveData> columns = new List<ColumnSaveData>();
        public List<WindowSaveData> windows = new List<WindowSaveData>();
        public List<WaterSourceSaveData> waterSources = new List<WaterSourceSaveData>();
        public List<FireSpotSaveData> fireSpots = new List<FireSpotSaveData>();
        public List<GasLeakSaveData> gasLeaks = new List<GasLeakSaveData>();
        public List<FurnitureSaveData> furnitures = new List<FurnitureSaveData>();
        public List<RubbleSaveData> rubbles = new List<RubbleSaveData>();
        public List<PlankPatchSaveData> plankPatches = new List<PlankPatchSaveData>();
        public List<SurvivorSaveData> survivors = new List<SurvivorSaveData>();
        public List<PlacedStructureSaveData> placedStructures = new List<PlacedStructureSaveData>();

        public int currentWeather;
        public List<int> triggeredNarrativeDays = new List<int>();
        public int motherGiftDueDay = -1;
        public int raiderAttention;

        // Kapı karşılaşması destesi (aynı olay hepsi görülmeden tekrar etmez) ve gece baskını durumu.
        public List<int> encounterDeck = new List<int>();
        public int lastEncounter = -1;
        public bool nightRaidPending;
        public float nightRaidAt;
        public string nightGuard;

        // Akşam planı: bu gecenin planı yapıldı mı (yapılmadıysa yüklenince yeniden açılır) ve gece keşfindekiler.
        public bool eveningPlanDone = true;

        // Oyun modu (0 kampanya, 1 sonsuz) ve sonsuz modda telsizin yeniden yardım çağırabileceği gün.
        public int gameMode;
        public int supplyDropDay;
        public List<string> nightScouts = new List<string>();
        public List<int> nightScoutSites = new List<int>();
    }

    [Serializable]
    public struct PlacedStructureSaveData
    {
        public int recipeId;
        public int floor;
        public int cellX;
        public int cellZ;
        public int rotationSteps;
    }

    [Serializable]
    public struct DayCycleSaveData
    {
        public int day;
        public int phase;
        public float phaseElapsed;
    }

    [Serializable]
    public struct StructuralIntegritySaveData
    {
        public float current;
        public bool isCollapsed;
    }

    [Serializable]
    public struct PowerGridSaveData
    {
        public bool isOn;
    }

    [Serializable]
    public struct ResourceStorageSaveData
    {
        public int scrap;
        public int metal;
        public int electronics;
        public int supplies;
    }

    [Serializable]
    public struct WorkbenchSaveData
    {
        public int tier;
        public int steelBeams;
        public int ammo;
        public List<int> craftedRecipeIds;
        public List<int> unplacedRecipeIds;
        public List<int> unplacedRecipeCounts;
    }

    [Serializable]
    public struct SkillTreeSaveData
    {
        public int points;
        public List<int> unlockedSkillIds;
    }

    [Serializable]
    public struct DisasterDirectorSaveData
    {
        public List<int> firedScheduleIndices;
        public int endlessDay;
        public List<int> firedEndlessIndices;
    }

    [Serializable]
    public struct FloodSaveData
    {
        public float level;
        public float fullTimer;
    }

    [Serializable]
    public struct RadioStationSaveData
    {
        public bool isRepaired;
        public float signal01;
        public bool signalComplete;
    }

    [Serializable]
    public struct ElectricPumpSaveData
    {
        public bool isBroken;
        public float wear;
    }

    [Serializable]
    public struct BurstPipeSaveData
    {
        public bool isBurst;
    }

    [Serializable]
    public struct ColumnSaveData
    {
        public string name;
        public int floor;
        public float x;
        public int state;
        public int supportHitsRemaining;
    }

    [Serializable]
    public struct WindowSaveData
    {
        public int floor;
        public bool isBroken;
    }

    [Serializable]
    public struct PlankPatchSaveData
    {
        public string name;
        public bool patched;
    }

    [Serializable]
    public struct WaterSourceSaveData
    {
        public string name;
        public int charges;
    }

    [Serializable]
    public struct FireSpotSaveData
    {
        public int floor;
        public float intensity;
        public float smoke;
        public float fullBurnTimer;
        public bool hasCollapsed;
    }

    [Serializable]
    public struct GasLeakSaveData
    {
        public int floor;
        public bool isLeaking;
        public float concentration;
    }

    [Serializable]
    public struct FurnitureSaveData
    {
        public string name;
        public bool isDismantled;
        public bool isSearched;
    }

    [Serializable]
    public struct RubbleSaveData
    {
        public string name;
        public int floor;
        public Vector3 position;
        public Vector3 scale;
        public int remaining;
        public int digCount;
        public bool blocksPath;
        public bool isCleared;
    }

    [Serializable]
    public struct ResourceItemSaveData
    {
        public int type;
        public int amount;
    }

    [Serializable]
    public struct SurvivorSaveData
    {
        public string displayName;
        public int branch;
        public float health;
        public bool isDead;
        public Vector3 position;
        public bool isCarrying;
        public ResourceItemSaveData carried;
        public List<ResourceItemSaveData> bag;
        public float trappedRemaining;

        public float stress;
        public int hunger;
        public float panicRemaining;
        public List<int> traitIds;
    }
}
