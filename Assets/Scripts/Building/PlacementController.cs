using System.Collections.Generic;
using AftershockIsle.CameraControl;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.UI;
using UnityEngine;

namespace AftershockIsle.Building
{
    /// <summary>
    /// Sığınak içi Grid Yerleştirme ve Düzenleme Modu ([B] tuşu veya Tezgah/HUD üzerinden açılır).
    /// Kat zemininde 16x3 ızgara hücrelerini, 3B hayalet önizlemeyi (Ghost Preview),
    /// 90° döndürmeyi ([R]) ve mevcut yapıları taşımayı/depoya kaldırmayı yönetir.
    /// </summary>
    public class PlacementController : MonoBehaviour
    {
        public static PlacementController Instance { get; private set; }
        public static bool IsPlacementActive => Instance != null && Instance.IsActive;

        public bool IsActive { get; private set; }
        public RecipeId? SelectedRecipe { get; private set; }
        public PlaceableStructure MovingStructure { get; private set; }

        int hoverFloor = 2;
        int hoverCellX = 7;
        int hoverCellZ = 2;
        int rotationSteps;
        bool hoverValid;
        string hoverReason;

        Transform gridOverlayRoot;
        Renderer[,] tileRenderers;
        MaterialPropertyBlock propBlock;

        Transform ghostRoot;
        RecipeId? ghostRecipe;
        readonly List<Renderer> ghostRenderers = new List<Renderer>();

        GUIStyle barTitleStyle;
        GUIStyle barSubStyle;
        GUIStyle tooltipStyle;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstance()
        {
            if (Instance == null)
            {
                var existing = FindFirstObjectByType<PlacementController>();
                if (existing != null)
                {
                    Instance = existing;
                    return;
                }

                var systems = GameObject.Find("[Systems]");
                if (systems != null)
                {
                    systems.AddComponent<PlacementController>();
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
            propBlock = new MaterialPropertyBlock();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            EventBus.Subscribe<PlaceableItemReady>(OnPlaceableItemReady);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<PlaceableItemReady>(OnPlaceableItemReady);
        }

        void OnPlaceableItemReady(PlaceableItemReady evt)
        {
            // Yeni bir yerleştirilebilir yapı üretildiğinde doğrudan yerleştirme modunu o eşya seçili olarak aç!
            if (!SideViewCamera.ViewingField)
            {
                EnterPlacementForRecipe(evt.Id);
            }
        }

        public void EnterPlacementForRecipe(RecipeId recipe)
        {
            IsActive = true;
            MovingStructure = null;
            SelectedRecipe = recipe;
            rotationSteps = 0;

            var info = RecipeCatalog.Get(recipe);
            if (!info.AllowsFloor(hoverFloor))
            {
                for (int f = 0; f < 4; f++)
                {
                    if (info.AllowsFloor(f))
                    {
                        hoverFloor = f;
                        break;
                    }
                }
            }

            RebuildGhost(recipe);
            EnsureGridVisuals();
            SetVisualsActive(true);
        }

        public void StartMovingStructure(PlaceableStructure structure)
        {
            if (structure == null) return;
            IsActive = true;
            MovingStructure = structure;
            SelectedRecipe = structure.Recipe;
            hoverFloor = structure.GridFloor;
            hoverCellX = structure.CellX;
            hoverCellZ = structure.CellZ;
            rotationSteps = structure.RotationSteps;

            RebuildGhost(structure.Recipe);
            EnsureGridVisuals();
            SetVisualsActive(true);
        }

        public void ToggleBuildMode()
        {
            if (IsActive)
            {
                ExitPlacementMode();
            }
            else
            {
                IsActive = true;
                MovingStructure = null;
                SelectedRecipe = FirstAvailableUnplacedRecipe();
                rotationSteps = 0;
                RebuildGhost(SelectedRecipe);
                EnsureGridVisuals();
                SetVisualsActive(true);
            }
        }

        public void ExitPlacementMode()
        {
            IsActive = false;
            SelectedRecipe = null;
            MovingStructure = null;
            SetVisualsActive(false);
        }

        RecipeId? FirstAvailableUnplacedRecipe()
        {
            var bench = Workbench.Instance;
            if (bench == null) return null;
            foreach (var kv in bench.UnplacedInventory)
            {
                if (kv.Value > 0) return kv.Key;
            }
            return null;
        }

        void Update()
        {
            if (SideViewCamera.ViewingField)
            {
                if (IsActive) ExitPlacementMode();
                return;
            }

            if (GameInput.BuildModeTogglePressed)
            {
                ToggleBuildMode();
                return;
            }

            if (!IsActive) return;

            if (GameInput.EscapePressed || GameInput.CommandPressed)
            {
                if (MovingStructure != null)
                {
                    MovingStructure = null;
                    SelectedRecipe = FirstAvailableUnplacedRecipe();
                    RebuildGhost(SelectedRecipe);
                    return;
                }
                ExitPlacementMode();
                return;
            }

            if (GameInput.RotatePlacementPressed)
            {
                rotationSteps = (rotationSteps + 1) % 4;
            }

            UpdateHoveredGridCell();
            UpdateGridAndGhostVisuals();

            if (!UIBlocker.IsPointerOver(GameInput.PointerPosition))
            {
                if (GameInput.SelectPressed)
                {
                    HandleLeftClick();
                }
                else if (GameInput.CancelOrderPressed)
                {
                    HandleStoreHoveredStructure();
                }
            }
        }

        void HandleLeftClick()
        {
            var grid = ShelterGrid.Instance;
            if (grid == null) return;

            // 1. Eğer bir yapıyı taşıyorsak (MovingStructure)
            if (MovingStructure != null)
            {
                if (grid.MoveStructure(MovingStructure, hoverFloor, hoverCellX, hoverCellZ, rotationSteps, out _))
                {
                    MovingStructure = null;
                    SelectedRecipe = FirstAvailableUnplacedRecipe();
                    RebuildGhost(SelectedRecipe);
                }
                return;
            }

            // 2. Eğer envanterden yeni bir yapı yerleştiriyorsak
            if (SelectedRecipe.HasValue)
            {
                var recipe = SelectedRecipe.Value;
                var bench = Workbench.Instance;
                if (bench != null && bench.GetUnplacedCount(recipe) > 0 &&
                    grid.CanPlace(recipe, hoverFloor, hoverCellX, hoverCellZ, rotationSteps, out _))
                {
                    if (bench.TryConsumeUnplaced(recipe))
                    {
                        grid.PlaceStructure(recipe, hoverFloor, hoverCellX, hoverCellZ, rotationSteps);
                        if (bench.GetUnplacedCount(recipe) <= 0)
                        {
                            SelectedRecipe = FirstAvailableUnplacedRecipe();
                            RebuildGhost(SelectedRecipe);
                        }
                    }
                    return;
                }
            }

            // 3. Elimizde seçili tarif yoksa veya mevcut hücrede kurulu bir yapıya tıklandıysa onu taşıma moduna al
            var clickedPlaced = FindPlacedAt(hoverFloor, hoverCellX, hoverCellZ);
            if (clickedPlaced != null)
            {
                StartMovingStructure(clickedPlaced);
            }
        }

        void HandleStoreHoveredStructure()
        {
            var target = MovingStructure != null ? MovingStructure : FindPlacedAt(hoverFloor, hoverCellX, hoverCellZ);
            if (target == null || Workbench.Instance == null) return;

            RecipeId id = target.Recipe;
            Workbench.Instance.AddUnplaced(id, 1);
            Destroy(target.gameObject);
            MovingStructure = null;
            SelectedRecipe = id;
            RebuildGhost(SelectedRecipe);
        }

        PlaceableStructure FindPlacedAt(int floor, int cellX, int cellZ)
        {
            foreach (var s in PlaceableStructure.All)
            {
                if (s == null || s.GridFloor != floor) continue;
                if (cellX >= s.CellX && cellX < s.CellX + s.EffectiveWidth &&
                    cellZ >= s.CellZ && cellZ < s.CellZ + s.EffectiveDepth)
                {
                    return s;
                }
            }
            return null;
        }

        void UpdateHoveredGridCell()
        {
            var cam = Camera.main;
            var grid = ShelterGrid.Instance;
            if (cam == null || grid == null || grid.Building == null) return;

            var b = grid.Building;
            Ray ray = cam.ScreenPointToRay(GameInput.PointerPosition);

            // Önce farenin hangi katta olduğunu bulalım (Z = 0 düzlemiyle kesişim)
            var walkPlane = new Plane(Vector3.forward, new Vector3(0f, 0f, b.WalkPlaneZ));
            if (walkPlane.Raycast(ray, out float distWalk))
            {
                Vector3 hitWalk = ray.GetPoint(distWalk);
                int detectedFloor = b.ClampFloor(Mathf.FloorToInt((hitWalk.y - b.transform.position.y) / b.FloorHeight));
                hoverFloor = detectedFloor;

                // O katın taban düzlemiyle kesişimi kontrol et; açı dikse kat içindeki dikey orandan Z şeridini (Ön/Orta/Arka) belirle
                float floorY = b.FloorY(hoverFloor);
                var floorPlane = new Plane(Vector3.up, new Vector3(0f, floorY, 0f));
                float worldX = hitWalk.x;
                int rowZ = hoverCellZ;

                if (floorPlane.Raycast(ray, out float distFloor) && distFloor > 0f)
                {
                    Vector3 hitFloor = ray.GetPoint(distFloor);
                    float localZ = hitFloor.z - b.transform.position.z;
                    if (localZ >= -3.1f && localZ <= 3.1f && Mathf.Abs(hitFloor.x - b.transform.position.x) <= b.Width * 0.52f)
                    {
                        worldX = hitFloor.x;
                        grid.WorldToCell(new Vector3(worldX, floorY + 0.1f, hitFloor.z), out _, out _, out rowZ);
                    }
                    else
                    {
                        float relY = Mathf.Clamp01((hitWalk.y - floorY) / (b.FloorHeight * 0.65f));
                        rowZ = relY < 0.28f ? 0 : relY < 0.58f ? 1 : 2;
                    }
                }

                float localX = worldX - b.transform.position.x;
                int cx = Mathf.RoundToInt((localX - ShelterGrid.FirstCellX) / ShelterGrid.CellSizeX);

                int w = 1;
                int d = 1;
                if (SelectedRecipe.HasValue)
                {
                    var info = RecipeCatalog.Get(SelectedRecipe.Value);
                    int rot = ((rotationSteps % 4) + 4) % 4;
                    w = (rot % 2 == 0) ? info.GridWidth : info.GridDepth;
                    d = (rot % 2 == 0) ? info.GridDepth : info.GridWidth;
                }

                hoverCellX = Mathf.Clamp(cx, 0, ShelterGrid.Columns - w);
                hoverCellZ = Mathf.Clamp(rowZ, 0, ShelterGrid.Rows - d);
            }

            if (SelectedRecipe.HasValue)
            {
                hoverValid = grid.CanPlace(SelectedRecipe.Value, hoverFloor, hoverCellX, hoverCellZ, rotationSteps,
                    out hoverReason, MovingStructure);
            }
            else
            {
                hoverValid = false;
                hoverReason = null;
            }
        }

        void EnsureGridVisuals()
        {
            if (gridOverlayRoot != null) return;

            gridOverlayRoot = new GameObject("[ShelterGridOverlay]").transform;
            gridOverlayRoot.SetParent(transform, false);
            tileRenderers = new Renderer[ShelterGrid.Columns, ShelterGrid.Rows];

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            var baseMat = new Material(shader);

            for (int x = 0; x < ShelterGrid.Columns; x++)
            {
                for (int z = 0; z < ShelterGrid.Rows; z++)
                {
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    quad.name = $"Cell_{x}_{z}";
                    Destroy(quad.GetComponent<Collider>());
                    quad.transform.SetParent(gridOverlayRoot, false);
                    quad.transform.localScale = new Vector3(ShelterGrid.CellSizeX * 0.88f, 0.025f, ShelterGrid.CellSizeZ * 0.78f);

                    var r = quad.GetComponent<Renderer>();
                    r.sharedMaterial = baseMat;
                    tileRenderers[x, z] = r;
                }
            }
        }

        void RebuildGhost(RecipeId? recipe)
        {
            if (ghostRoot != null)
            {
                Destroy(ghostRoot.gameObject);
                ghostRoot = null;
                ghostRenderers.Clear();
            }

            ghostRecipe = recipe;
            if (!recipe.HasValue) return;

            ghostRoot = new GameObject("[PlacementGhost]").transform;
            ghostRoot.SetParent(transform, false);
            PlaceableStructure.BuildProceduralMesh(ghostRoot, recipe.Value, true);
            ghostRenderers.AddRange(ghostRoot.GetComponentsInChildren<Renderer>());
        }

        void SetVisualsActive(bool active)
        {
            if (gridOverlayRoot != null) gridOverlayRoot.gameObject.SetActive(active);
            if (ghostRoot != null) ghostRoot.gameObject.SetActive(active && SelectedRecipe.HasValue);
        }

        void UpdateGridAndGhostVisuals()
        {
            var grid = ShelterGrid.Instance;
            if (grid == null) return;

            EnsureGridVisuals();
            gridOverlayRoot.gameObject.SetActive(true);

            int selW = 0;
            int selD = 0;
            if (SelectedRecipe.HasValue)
            {
                var info = RecipeCatalog.Get(SelectedRecipe.Value);
                int rot = ((rotationSteps % 4) + 4) % 4;
                selW = (rot % 2 == 0) ? info.GridWidth : info.GridDepth;
                selD = (rot % 2 == 0) ? info.GridDepth : info.GridWidth;
            }

            Color freeCellColor = new Color(0.18f, 0.72f, 0.52f, 0.45f);
            Color occupiedCellColor = new Color(0.78f, 0.22f, 0.22f, 0.45f);
            Color validFootprintColor = new Color(0.15f, 0.98f, 0.38f, 0.85f);
            Color invalidFootprintColor = new Color(0.98f, 0.18f, 0.18f, 0.85f);
            Color hoveredExistingColor = new Color(0.95f, 0.78f, 0.18f, 0.85f);

            for (int x = 0; x < ShelterGrid.Columns; x++)
            {
                for (int z = 0; z < ShelterGrid.Rows; z++)
                {
                    var r = tileRenderers[x, z];
                    r.transform.position = grid.GetCellWorldCenter(hoverFloor, x, z) + Vector3.up * 0.03f;

                    bool inFootprint = SelectedRecipe.HasValue &&
                                       x >= hoverCellX && x < hoverCellX + selW &&
                                       z >= hoverCellZ && z < hoverCellZ + selD;

                    Color c;
                    if (inFootprint)
                    {
                        c = hoverValid ? validFootprintColor : invalidFootprintColor;
                    }
                    else if (!SelectedRecipe.HasValue && x == hoverCellX && z == hoverCellZ && FindPlacedAt(hoverFloor, x, z) != null)
                    {
                        c = hoveredExistingColor;
                    }
                    else
                    {
                        bool cellFree = !grid.IsCellReservedByArchitecture(hoverFloor, x, z, out _) &&
                                        FindPlacedAt(hoverFloor, x, z) == null;
                        c = cellFree ? freeCellColor : occupiedCellColor;
                    }

                    ApplyColor(r, c);
                }
            }

            if (SelectedRecipe != ghostRecipe)
                RebuildGhost(SelectedRecipe);

            if (ghostRoot != null && SelectedRecipe.HasValue)
            {
                ghostRoot.gameObject.SetActive(true);
                ghostRoot.position = grid.GetFootprintWorldPosition(hoverFloor, hoverCellX, hoverCellZ, selW, selD);
                ghostRoot.rotation = Quaternion.Euler(0f, rotationSteps * 90f, 0f);

                Color ghostTint = hoverValid ? new Color(0.25f, 0.95f, 0.45f) : new Color(0.95f, 0.25f, 0.25f);
                foreach (var gr in ghostRenderers)
                {
                    if (gr != null) ApplyColor(gr, ghostTint);
                }
            }
            else if (ghostRoot != null)
            {
                ghostRoot.gameObject.SetActive(false);
            }
        }

        void ApplyColor(Renderer r, Color color)
        {
            r.GetPropertyBlock(propBlock);
            propBlock.SetColor(BaseColorId, color);
            propBlock.SetColor(ColorId, color);
            r.SetPropertyBlock(propBlock);
        }

        void OnGUI()
        {
            UiTheme.Apply();
            if (SideViewCamera.ViewingField) return;

            barTitleStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14 };
            barSubStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            tooltipStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

            var bench = Workbench.Instance;
            int unplacedTotal = bench != null ? bench.TotalUnplacedCount : 0;

            // Yerleştirme modu kapalıysa ama kuruluma hazır eşya varsa veya oyuncu düzenlemek isterse üst/alt bildirim butonu
            if (!IsActive)
            {
                if (unplacedTotal > 0)
                {
                    var alertRect = new Rect(UiTheme.Width * 0.5f - 190f, 52f, 380f, 34f);
                    UIBlocker.Add(alertRect);
                    GUI.color = new Color(0.95f, 0.85f, 0.3f);
                    if (GUI.Button(alertRect, $"📦 Kuruluma Hazır {unplacedTotal} Yapı Var! — Eve Yerleştir [B]"))
                    {
                        ToggleBuildMode();
                    }
                    GUI.color = Color.white;
                }
                return;
            }

            // Yerleştirme Modu Açıkken Alt/Üst Kontrol Paneli
            float panelW = 720f;
            float panelH = 122f;
            var panelRect = new Rect((UiTheme.Width - panelW) * 0.5f, UiTheme.Height - panelH - 14f, panelW, panelH);
            UIBlocker.Add(panelRect);
            GUI.Box(panelRect, GUIContent.none);

            GUILayout.BeginArea(new Rect(panelRect.x + 12f, panelRect.y + 8f, panelRect.width - 24f, panelRect.height - 16f));
            GUILayout.BeginHorizontal();

            var grid = ShelterGrid.Instance;
            string floorName = grid != null && grid.Building != null ? grid.Building.GetFloorName(hoverFloor) : $"Kat {hoverFloor}";
            string rowName = ShelterGrid.GetRowName(hoverCellZ);

            GUILayout.Label($"🏗 EV YERLEŞİM & IZGARASI (GRID) — {floorName} | Hücre ({hoverCellX}, {rowName}) | Yön: {rotationSteps * 90}°", barTitleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Döndür [R]", GUILayout.Width(95f), GUILayout.Height(24f)))
                rotationSteps = (rotationSteps + 1) % 4;
            if (GUILayout.Button("Kapat [B / Sağ Tık]", GUILayout.Width(135f), GUILayout.Height(24f)))
                ExitPlacementMode();
            GUILayout.EndHorizontal();

            // Kuruluma hazır envanter butonları
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (MovingStructure != null)
            {
                GUILayout.Label($"Taşınıyor: {MovingStructure.Info.Name} — Yeni hücreye Sol Tıkla bırak ya da [X] ile depoya kaldır.", barSubStyle);
                if (GUILayout.Button("Depoya Kaldır [X]", GUILayout.Width(130f), GUILayout.Height(28f)))
                    HandleStoreHoveredStructure();
            }
            else if (bench != null && bench.TotalUnplacedCount > 0)
            {
                GUILayout.Label("Kuruluma Hazır:", barTitleStyle, GUILayout.Width(110f));
                foreach (var recipe in RecipeCatalog.All)
                {
                    if (!recipe.IsPlaceable) continue;
                    int count = bench.GetUnplacedCount(recipe.Id);
                    if (count <= 0) continue;

                    bool isSelected = SelectedRecipe == recipe.Id;
                    GUI.color = isSelected ? new Color(0.4f, 1f, 0.55f) : Color.white;
                    if (GUILayout.Button($"{recipe.Name} (x{count}) [{recipe.GridWidth}x{recipe.GridDepth}]", GUILayout.Height(30f)))
                    {
                        SelectedRecipe = recipe.Id;
                        RebuildGhost(SelectedRecipe);
                    }
                    GUI.color = Color.white;
                }
            }
            else
            {
                GUILayout.Label("Kuruluma hazır yeni yapı yok (Çalışma Tezgahında üretebilirsin). Kurulu bir yapıya Sol Tıklayarak yerini değiştirebilirsin.", barSubStyle);
            }
            GUILayout.EndHorizontal();

            string statusLine;
            if (SelectedRecipe.HasValue)
            {
                var info = RecipeCatalog.Get(SelectedRecipe.Value);
                statusLine = hoverValid
                    ? $"✓ {info.Name} buraya yerleştirilebilir! (Sol Tık: Kur | [R]: Döndür)"
                    : $"✗ Yerleştirilemez: {hoverReason}";
            }
            else
            {
                var existing = FindPlacedAt(hoverFloor, hoverCellX, hoverCellZ);
                statusLine = existing != null
                    ? $"Seçili Hücredeki Yapı: {existing.Info.Name} (Sol Tık: Taşı | [X]: Depoya Kaldır)"
                    : "İpucu: Kurulu bir eşyayı taşımak için üzerine tıkla; yeni eşya üretmek için Çalışma Tezgahını kullan.";
            }

            GUILayout.Label(statusLine, barSubStyle);
            GUILayout.EndArea();
        }
    }
}
