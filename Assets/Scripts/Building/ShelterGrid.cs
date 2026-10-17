using System.Collections.Generic;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Crisis;
using AftershockIsle.Interaction;
using UnityEngine;

namespace AftershockIsle.Building
{
    /// <summary>
    /// Sığınak içi 3B yerleştirme ızgarası (Grid Sistemi).
    /// Her katı 16 X sütunu (1.0m) ve 3 Z derinlik şeridine (Ön, Orta, Arka Duvar) böler.
    /// Çakışma, merdiven ağzı, çıkış kapısı ve kat kurallarını denetler.
    /// </summary>
    public class ShelterGrid : MonoBehaviour
    {
        public const int Columns = 16;
        public const int Rows = 3;
        public const float CellSizeX = 1.0f;
        public const float FirstCellX = -7.5f;

        static readonly float[] RowWorldZ = { -1.55f, 0.15f, 1.95f };
        public const float CellSizeZ = 1.35f;

        public static ShelterGrid Instance { get; private set; }

        BuildingLayout building;
        Transform placedRoot;
        readonly Collider[] overlapBuffer = new Collider[32];

        public BuildingLayout Building => building != null ? building : (building = FindFirstObjectByType<BuildingLayout>());

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstance()
        {
            if (Instance == null)
            {
                var existing = FindFirstObjectByType<ShelterGrid>();
                if (existing != null)
                {
                    Instance = existing;
                    return;
                }

                var systems = GameObject.Find("[Systems]");
                if (systems != null)
                {
                    systems.AddComponent<ShelterGrid>();
                }
            }
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            building = FindFirstObjectByType<BuildingLayout>();
            EnsurePlacedRoot();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void EnsurePlacedRoot()
        {
            if (placedRoot != null) return;
            var b = Building;
            var parent = b != null ? b.transform : transform;
            var existing = parent.Find("PlacedStructures");
            if (existing != null)
            {
                placedRoot = existing;
            }
            else
            {
                placedRoot = new GameObject("PlacedStructures").transform;
                placedRoot.SetParent(parent, false);
            }
        }

        public float CellToWorldX(int cellX)
        {
            float baseOffset = Building != null ? Building.transform.position.x : 0f;
            return baseOffset + FirstCellX + cellX * CellSizeX;
        }

        public float CellToWorldZ(int cellZ)
        {
            int clamped = Mathf.Clamp(cellZ, 0, Rows - 1);
            float baseOffset = Building != null ? Building.transform.position.z : 0f;
            return baseOffset + RowWorldZ[clamped];
        }

        public Vector3 GetCellWorldCenter(int floor, int cellX, int cellZ)
        {
            float y = Building != null ? Building.FloorY(floor) : floor * 4.5f;
            return new Vector3(CellToWorldX(cellX), y, CellToWorldZ(cellZ));
        }

        public Vector3 GetFootprintWorldPosition(int floor, int startCellX, int startCellZ, int widthCells, int depthCells)
        {
            float minX = CellToWorldX(startCellX);
            float maxX = CellToWorldX(startCellX + widthCells - 1);
            float minZ = CellToWorldZ(startCellZ);
            float maxZ = CellToWorldZ(startCellZ + depthCells - 1);
            float y = Building != null ? Building.FloorY(floor) : floor * 4.5f;
            return new Vector3((minX + maxX) * 0.5f, y, (minZ + maxZ) * 0.5f);
        }

        public bool WorldToCell(Vector3 worldPos, out int floor, out int cellX, out int cellZ)
        {
            var b = Building;
            floor = b != null ? b.FloorAtHeight(worldPos.y + 0.25f) : Mathf.Clamp(Mathf.FloorToInt((worldPos.y + 0.25f) / 4.5f), 0, 3);

            float localX = worldPos.x - (b != null ? b.transform.position.x : 0f);
            cellX = Mathf.RoundToInt((localX - FirstCellX) / CellSizeX);

            float localZ = worldPos.z - (b != null ? b.transform.position.z : 0f);
            int bestZ = 0;
            float bestDist = float.MaxValue;
            for (int r = 0; r < Rows; r++)
            {
                float d = Mathf.Abs(localZ - RowWorldZ[r]);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestZ = r;
                }
            }
            cellZ = bestZ;

            return cellX >= 0 && cellX < Columns && cellZ >= 0 && cellZ < Rows;
        }

        public static string GetRowName(int cellZ) => cellZ switch
        {
            0 => "Ön Şerit",
            1 => "Orta Koridor",
            _ => "Arka Duvar Önü"
        };

        public bool IsCellReservedByArchitecture(int floor, int cellX, int cellZ, out string reason)
        {
            float wx = CellToWorldX(cellX);
            var b = Building;

            // 1. Merdiven ağızları
            if (b != null && cellZ >= 1)
            {
                foreach (var ladder in b.Ladders)
                {
                    if (ladder.lowerFloor == floor || ladder.lowerFloor + 1 == floor)
                    {
                        float lx = b.transform.position.x + ladder.x;
                        if (Mathf.Abs(wx - lx) < 1.05f)
                        {
                            reason = "Merdiven iniş/çıkış ağzı kapatılamaz.";
                            return true;
                        }
                    }
                }
            }

            // 2. Giriş katı dış kapı önü
            if (floor == 1 && wx > 6.3f)
            {
                reason = "Dış çıkış kapısının önü kapatılamaz.";
                return true;
            }

            // 3. Çatı helikopter pisti
            if (floor == 3 && wx >= 1.8f && wx <= 7.2f)
            {
                reason = "Helikopter tahliye pisti üzerine yapı kurulamaz.";
                return true;
            }

            reason = null;
            return false;
        }

        public bool CanPlace(RecipeId recipe, int floor, int startCellX, int startCellZ, int rotationSteps,
                             out string reason, PlaceableStructure ignoreSelf = null)
        {
            var info = RecipeCatalog.Get(recipe);
            if (!info.IsPlaceable)
            {
                reason = "Bu tarif ızgaraya yerleştirilen bir yapı değil.";
                return false;
            }

            if (!info.AllowsFloor(floor))
            {
                string floorName = Building != null ? Building.GetFloorName(floor) : $"Kat {floor}";
                reason = $"{info.Name} {floorName} katına kurulamaz ({info.AllowedFloorsText()}).";
                return false;
            }

            int rot = ((rotationSteps % 4) + 4) % 4;
            int w = (rot % 2 == 0) ? info.GridWidth : info.GridDepth;
            int d = (rot % 2 == 0) ? info.GridDepth : info.GridWidth;

            if (startCellX < 0 || startCellX + w > Columns || startCellZ < 0 || startCellZ + d > Rows)
            {
                reason = "Kat sınırlarının dışında.";
                return false;
            }

            // Hücre bazlı mimari ve diğer PlaceableStructure kontrolü
            for (int cx = startCellX; cx < startCellX + w; cx++)
            {
                for (int cz = startCellZ; cz < startCellZ + d; cz++)
                {
                    if (IsCellReservedByArchitecture(floor, cx, cz, out reason))
                        return false;

                    foreach (var placed in PlaceableStructure.All)
                    {
                        if (placed == null || placed == ignoreSelf || placed.GridFloor != floor) continue;
                        if (cx >= placed.CellX && cx < placed.CellX + placed.EffectiveWidth &&
                            cz >= placed.CellZ && cz < placed.CellZ + placed.EffectiveDepth)
                        {
                            reason = $"{placed.Info.Name} ile çakışıyor.";
                            return false;
                        }
                    }
                }
            }

            // Sahnedeki sabit eşyalarla (Tezgah, Sandık, Kolon, Moloz, Mobilya vb.) fiziksel çakışma kontrolü
            Vector3 center = GetFootprintWorldPosition(floor, startCellX, startCellZ, w, d);
            Vector3 halfExtents = new Vector3(w * CellSizeX * 0.42f, 0.42f, d * CellSizeZ * 0.38f);
            Vector3 boxCenter = center + Vector3.up * 0.55f;

            int hitCount = Physics.OverlapBoxNonAlloc(boxCenter, halfExtents, overlapBuffer, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                var col = overlapBuffer[i];
                if (col == null) continue;
                if (ignoreSelf != null && col.transform.IsChildOf(ignoreSelf.transform)) continue;

                var existingPlaced = col.GetComponentInParent<PlaceableStructure>();
                if (existingPlaced != null && existingPlaced != ignoreSelf)
                {
                    reason = $"{existingPlaced.Info.Name} ile çakışıyor.";
                    return false;
                }

                var interactable = col.GetComponentInParent<Interactable>();
                if (interactable != null)
                {
                    if (interactable is FireSpot fire && !fire.IsBurning) continue;
                    if (interactable is SmokeWindow || interactable is GasLeak || interactable is BurstPipe) continue;
                    reason = $"{interactable.DisplayName} ile çakışıyor.";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        public PlaceableStructure PlaceStructure(RecipeId recipe, int floor, int cellX, int cellZ, int rotationSteps)
        {
            EnsurePlacedRoot();
            var info = RecipeCatalog.Get(recipe);
            int rot = ((rotationSteps % 4) + 4) % 4;
            int w = (rot % 2 == 0) ? info.GridWidth : info.GridDepth;
            int d = (rot % 2 == 0) ? info.GridDepth : info.GridWidth;

            Vector3 worldPos = GetFootprintWorldPosition(floor, cellX, cellZ, w, d);
            var go = new GameObject(info.Name);
            go.transform.SetParent(placedRoot, false);
            go.transform.position = worldPos;

            var structure = go.AddComponent<PlaceableStructure>();
            structure.Initialize(recipe, floor, cellX, cellZ, rot);

            EventBus.Publish(new StructurePlaced(structure));
            return structure;
        }

        public bool MoveStructure(PlaceableStructure structure, int newFloor, int newCellX, int newCellZ, int newRotationSteps, out string reason)
        {
            if (structure == null)
            {
                reason = "Taşınacak yapı seçilmedi.";
                return false;
            }

            if (!CanPlace(structure.Recipe, newFloor, newCellX, newCellZ, newRotationSteps, out reason, structure))
                return false;

            int rot = ((newRotationSteps % 4) + 4) % 4;
            int w = (rot % 2 == 0) ? structure.Info.GridWidth : structure.Info.GridDepth;
            int d = (rot % 2 == 0) ? structure.Info.GridDepth : structure.Info.GridWidth;

            structure.transform.position = GetFootprintWorldPosition(newFloor, newCellX, newCellZ, w, d);
            structure.SetGridCoordinates(newFloor, newCellX, newCellZ, rot);
            return true;
        }

        public void ClearAllPlaced()
        {
            var copy = new List<PlaceableStructure>(PlaceableStructure.All);
            foreach (var s in copy)
            {
                if (s != null) Destroy(s.gameObject);
            }
            FloorObstacles.Invalidate();
        }
    }
}
