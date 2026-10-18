using System.Collections.Generic;
using AftershockIsle.CameraControl;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.UI;
using UnityEngine;
using UnityEngine.UI;

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

        // uGUI: alt ortada yerleşim paneli, üstte "kuruluma hazır" uyarısı.
        GameObject panelRoot;
        Text panelTitle;
        Text panelStatus;
        RectTransform recipeRow;
        GameObject alertButton;
        Text alertLabel;
        string rowSignature;

        // Klavye ile hücre seçimi: ok tuşlarına basılınca fare konumu yok sayılır, fare kımıldayınca fare geri alır.
        bool keyboardDriven;
        Vector2 lastPointer;
        // Modu açan tuş (ör. pencerede A / Enter) aynı karede yerleşimi de onaylamasın.
        int enteredFrame = -1;

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
            BuildUi();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            TimeManager.Release(this);
        }

        void OnEnable()
        {
            EventBus.Subscribe<PlaceableItemReady>(OnPlaceableItemReady);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<PlaceableItemReady>(OnPlaceableItemReady);
            TimeManager.Release(this);
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
            enteredFrame = Time.frameCount;
            MovingStructure = null;
            SelectedRecipe = recipe;
            rotationSteps = 0;

            var info = RecipeCatalog.Get(recipe);
            if (!info.AllowsFloor(hoverFloor))
            {
                var grid = ShelterGrid.Instance;
                int floorCount = grid != null && grid.Building != null ? grid.Building.FloorCount : 5;
                for (int f = 0; f < floorCount; f++)
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
            enteredFrame = Time.frameCount;
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
                enteredFrame = Time.frameCount;
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

            if (GameInput.BuildModeKeyPressed || (GameInput.BuildModePadPressed && !ModalMenu.IsOpen))
            {
                ToggleBuildMode();
                return;
            }

            if (!IsActive || Time.frameCount == enteredFrame) return;

            if (GameInput.EscapePressed || GameInput.CommandPressed || GameInput.PlacementBackPressed)
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

            int recipeStep = GameInput.PlacementRecipeStep;
            if (recipeStep != 0) CycleRecipe(recipeStep);

            ReadKeyboardCell();
            UpdateHoveredGridCell();
            UpdateGridAndGhostVisuals();

            if (GameInput.PlacementConfirmPressed)
            {
                HandleLeftClick();
            }
            else if (!UIBlocker.IsPointerOver(GameInput.PointerPosition))
            {
                if (GameInput.SelectPressed)
                {
                    HandleLeftClick();
                }
            }

            if (GameInput.PlacementStorePressed) HandleStoreHoveredStructure();
        }

        // LT/RT ve Tab: kuruluma hazır yapılar arasında dolaşır.
        void CycleRecipe(int step)
        {
            var bench = Workbench.Instance;
            if (bench == null || MovingStructure != null) return;

            var ready = new List<RecipeId>();
            foreach (var recipe in RecipeCatalog.All)
                if (recipe.IsPlaceable && bench.GetUnplacedCount(recipe.Id) > 0) ready.Add(recipe.Id);
            if (ready.Count == 0) return;

            int index = SelectedRecipe.HasValue ? ready.IndexOf(SelectedRecipe.Value) : -1;
            int next = index < 0 ? (step > 0 ? 0 : ready.Count - 1) : ((index + step) % ready.Count + ready.Count) % ready.Count;
            SelectedRecipe = ready[next];
            RebuildGhost(SelectedRecipe);
        }

        // Ok tuşları hücreyi, Q/E katı değiştirir; seçili yapının ayak izi kat sınırlarının dışına taşmaz.
        void ReadKeyboardCell()
        {
            var pointer = GameInput.PointerPosition;
            if ((pointer - lastPointer).sqrMagnitude > 9f) keyboardDriven = false;
            lastPointer = pointer;

            var nudge = GameInput.PlacementNudge;
            int floorStep = GameInput.PlacementFloorStep;
            if (nudge == Vector2Int.zero && floorStep == 0) return;

            keyboardDriven = true;
            var grid = ShelterGrid.Instance;
            int maxFloor = grid != null && grid.Building != null ? grid.Building.FloorCount - 1 : 4;
            hoverFloor = Mathf.Clamp(hoverFloor + floorStep, 0, maxFloor);

            ClampHoverToFootprint(hoverCellX + nudge.x, hoverCellZ + nudge.y);
        }

        void ClampHoverToFootprint(int cellX, int cellZ)
        {
            int w = 1;
            int d = 1;
            if (SelectedRecipe.HasValue)
            {
                var info = RecipeCatalog.Get(SelectedRecipe.Value);
                int rot = ((rotationSteps % 4) + 4) % 4;
                w = (rot % 2 == 0) ? info.GridWidth : info.GridDepth;
                d = (rot % 2 == 0) ? info.GridDepth : info.GridWidth;
            }
            hoverCellX = Mathf.Clamp(cellX, 0, ShelterGrid.Columns - w);
            hoverCellZ = Mathf.Clamp(cellZ, 0, ShelterGrid.Rows - d);
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

            // 1. Fare doğrudan bir kat zeminini (görünen ızgara karelerini) gösteriyorsa hücre tam o noktadan okunur.
            // Işının, bina ayak izinin içinde kalan EN YAKIN kat zemini seçilir; böylece ızgara kareleri üstüne gelmek
            // katı ya da şeridi kaydırmaz (eski yöntem ön şerit karelerini bir alt kat sayıyordu).
            if (!keyboardDriven && TryPickFloorCell(ray, b, out int pickedFloor, out int pickedX, out int pickedZ))
            {
                hoverFloor = pickedFloor;
                ClampHoverToFootprint(pickedX, pickedZ);
            }
            // 2. Fare odanın boşluğunu / duvarı gösteriyorsa: z = 0 düzlemindeki yükseklikten kat, dikey orandan şerit.
            else if (!keyboardDriven && new Plane(Vector3.forward, new Vector3(0f, 0f, b.WalkPlaneZ)).Raycast(ray, out float distWalk))
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

        /// <summary>Işın, binanın iç ayak izinde bir kat zeminini keserse en yakın olanın katını ve hücresini verir.</summary>
        bool TryPickFloorCell(Ray ray, BuildingLayout b, out int floor, out int cellX, out int cellZ)
        {
            floor = 0;
            cellX = 0;
            cellZ = 0;
            if (ray.direction.y >= -0.0001f) return false;

            float bestDistance = float.MaxValue;
            Vector3 bestHit = default;
            for (int f = 0; f < b.FloorCount; f++)
            {
                float distance = (b.FloorY(f) - ray.origin.y) / ray.direction.y;
                if (distance <= 0f || distance >= bestDistance) continue;

                Vector3 hit = ray.GetPoint(distance);
                float localZ = hit.z - b.transform.position.z;
                float localX = hit.x - b.transform.position.x;
                if (Mathf.Abs(localZ) > b.Depth * 0.5f || Mathf.Abs(localX) > b.Width * 0.5f) continue;

                bestDistance = distance;
                bestHit = hit;
                floor = f;
            }

            if (bestDistance == float.MaxValue) return false;

            var grid = ShelterGrid.Instance;
            grid.WorldToCell(new Vector3(bestHit.x, b.FloorY(floor) + 0.1f, bestHit.z), out _, out cellX, out cellZ);
            return true;
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

        // ---------------------------------------------------------------- Arayüz (uGUI)

        void BuildUi()
        {
            UiFactory.EnsureEventSystem();
            var canvas = UiFactory.Canvas("Yerleşim", 11, transform).transform;

            // Kuruluma hazır yapı varken (mod kapalıyken) üstte uyarı düğmesi.
            var alert = UiFactory.TextButton(canvas, "", new Vector2(640f, 52f), ToggleBuildMode, new Color(0.62f, 0.5f, 0.12f, 0.97f), 22);
            alertButton = alert.gameObject;
            alertLabel = alert.GetComponentInChildren<Text>();
            UiFactory.Place((RectTransform)alertButton.transform, new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(640f, 52f));
            alertButton.SetActive(false);

            var panel = UiFactory.Panel(canvas, "Yerleşim Paneli", UiFactory.Slate);
            panelRoot = panel.gameObject;
            UiFactory.Place(panel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(1240f, 0f));
            var layout = panelRoot.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(22, 22, 12, 14);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panelRoot.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var top = UiFactory.Rect("Üst Satır", panelRoot.transform);
            top.gameObject.AddComponent<LayoutElement>().preferredHeight = 46f;
            var topRow = top.gameObject.AddComponent<HorizontalLayoutGroup>();
            topRow.spacing = 12f;
            topRow.childControlWidth = false;
            topRow.childControlHeight = false;
            topRow.childForceExpandWidth = false;
            topRow.childAlignment = TextAnchor.MiddleLeft;

            panelTitle = UiFactory.Label(top, "", 21, UiFactory.Accent, TextAnchor.MiddleLeft);
            panelTitle.rectTransform.sizeDelta = new Vector2(740f, 44f);
            UiFactory.TextButton(top, "Döndür [R]", new Vector2(190f, 42f), () => rotationSteps = (rotationSteps + 1) % 4, null, 19);
            UiFactory.TextButton(top, "Kapat [B / Sağ Tık]", new Vector2(250f, 42f), ExitPlacementMode, null, 19);

            recipeRow = UiFactory.Rect("Yapı Satırı", panelRoot.transform);
            recipeRow.gameObject.AddComponent<LayoutElement>().minHeight = 46f;
            var rowLayout = recipeRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 10f;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;

            panelStatus = UiFactory.Label(panelRoot.transform, "", 19, UiFactory.Text, TextAnchor.UpperLeft, FontStyle.Normal, false);
            panelStatus.gameObject.AddComponent<LayoutElement>().minHeight = 62f;

            panelRoot.SetActive(false);
        }

        // Her kare: görünürlük, başlık ve durum satırı güncellenir; yapı düğmeleri yalnızca içerik değişince yeniden kurulur.
        void LateUpdate()
        {
            if (panelRoot == null) return;

            var bench = Workbench.Instance;
            int unplacedTotal = bench != null ? bench.TotalUnplacedCount : 0;
            bool field = SideViewCamera.ViewingField;

            // Yerleşim planlama işidir: mod açıkken zaman durur, kapanınca kaldığı hızdan devam eder.
            if (IsActive && !field) TimeManager.Hold(this);
            else TimeManager.Release(this);

            bool showAlert = !field && !IsActive && unplacedTotal > 0 && !ModalMenu.IsOpen;
            if (alertButton.activeSelf != showAlert) alertButton.SetActive(showAlert);
            if (showAlert) alertLabel.text = $"Kuruluma Hazır {unplacedTotal} Yapı Var! — Eve Yerleştir [{GameInput.Prompt(InputPrompt.Build)}]";

            bool showPanel = !field && IsActive;
            if (panelRoot.activeSelf != showPanel) panelRoot.SetActive(showPanel);
            if (!showPanel) return;

            var grid = ShelterGrid.Instance;
            string floorName = grid != null && grid.Building != null ? grid.Building.GetFloorName(hoverFloor) : $"Kat {hoverFloor}";
            string rowName = ShelterGrid.GetRowName(hoverCellZ);
            panelTitle.text = $"EV YERLEŞİMİ (zaman durdu) — {floorName} | Hücre ({hoverCellX}, {rowName}) | Yön: {rotationSteps * 90}°";

            RefreshRecipeRow(bench);

            string statusLine;
            if (SelectedRecipe.HasValue)
            {
                var info = RecipeCatalog.Get(SelectedRecipe.Value);
                statusLine = hoverValid
                    ? $"<color=#7bdc6e>{info.Name} buraya yerleştirilebilir!</color> (Sol Tık: Kur | [R]: Döndür)"
                    : $"<color=#ff8f7a>Yerleştirilemez:</color> {hoverReason}";
            }
            else
            {
                var existing = FindPlacedAt(hoverFloor, hoverCellX, hoverCellZ);
                statusLine = existing != null
                    ? $"Seçili Hücredeki Yapı: {existing.Info.Name} (Sol Tık: Taşı | [X]: Depoya Kaldır)"
                    : "İpucu: Kurulu bir eşyayı taşımak için üzerine tıkla; yeni yapı hazırlamak için İnşaat Masası'nı kullan.";
            }
            panelStatus.text = statusLine + "\n<color=#b8b8b0>" +
                $"{GameInput.Prompt(InputPrompt.PlaceCell)}: hücre   ·   {GameInput.Prompt(InputPrompt.PlaceFloor)}: kat   ·   " +
                $"{GameInput.Prompt(InputPrompt.PlaceRecipe)}: yapı seç   ·   {GameInput.Prompt(InputPrompt.PlaceConfirm)}: kur   ·   " +
                $"{GameInput.Prompt(InputPrompt.PlaceRotate)}: döndür   ·   {GameInput.Prompt(InputPrompt.PlaceStore)}: depoya kaldır   ·   " +
                $"{GameInput.Prompt(InputPrompt.PlaceClose)}: kapat</color>";
        }

        void RefreshRecipeRow(Workbench bench)
        {
            var signature = new System.Text.StringBuilder();
            signature.Append(MovingStructure != null ? "move:" + MovingStructure.Info.Name : "").Append('|').Append(SelectedRecipe.HasValue ? (int)SelectedRecipe.Value : -1);
            if (bench != null)
                foreach (var recipe in RecipeCatalog.All)
                    if (recipe.IsPlaceable) signature.Append('|').Append(bench.GetUnplacedCount(recipe.Id));
            if (signature.ToString() == rowSignature) return;
            rowSignature = signature.ToString();

            for (int i = recipeRow.childCount - 1; i >= 0; i--) Destroy(recipeRow.GetChild(i).gameObject);

            if (MovingStructure != null)
            {
                RowLabel($"Taşınıyor: {MovingStructure.Info.Name} — Yeni hücreye Sol Tıkla bırak ya da [X] ile depoya kaldır.", 800f);
                UiFactory.TextButton(recipeRow, "Depoya Kaldır [X]", new Vector2(250f, 42f), HandleStoreHoveredStructure, null, 19);
            }
            else if (bench != null && bench.TotalUnplacedCount > 0)
            {
                RowLabel("Kuruluma Hazır:", 190f);
                foreach (var recipe in RecipeCatalog.All)
                {
                    if (!recipe.IsPlaceable) continue;
                    int count = bench.GetUnplacedCount(recipe.Id);
                    if (count <= 0) continue;

                    var id = recipe.Id;
                    bool selected = SelectedRecipe == id;
                    UiFactory.TextButton(recipeRow, $"{recipe.Name} (x{count}) [{recipe.GridWidth}x{recipe.GridDepth}]",
                        new Vector2(300f, 42f), () =>
                        {
                            SelectedRecipe = id;
                            RebuildGhost(id);
                        }, selected ? UiFactory.Good : null, 18);
                }
            }
            else
            {
                RowLabel("Kuruluma hazır yeni yapı yok (İnşaat Masası'nda hazırlayabilirsin). Kurulu bir yapıya Sol Tıklayarak yerini değiştirebilirsin.", 1180f);
            }
        }

        void RowLabel(string text, float width)
        {
            var label = UiFactory.Label(recipeRow, text, 19, UiFactory.Text, TextAnchor.MiddleLeft, FontStyle.Normal, false);
            label.rectTransform.sizeDelta = new Vector2(width, 42f);
        }
    }
}
