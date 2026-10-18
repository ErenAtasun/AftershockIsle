using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Interaction;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AftershockIsle.Crafting
{
    /// <summary>
    /// İnşaat Masası: eve kurulan yapıların (soba, pompa, revir, jeneratör...) hazırlandığı yer. Çalışma Tezgahı ise silah,
    /// mermi, ekipman ve yükseltme gibi doğrudan etkili şeyleri üretir. Malzeme, kademe ve sonuç mantığı tezgahla ortaktır
    /// (Workbench.Produce); iki istasyonun ayrı sırası ve ayrı karakteri olur. Hazır yapı "kuruluma hazır" envantere girer
    /// ve yerleşim modunda (B / D-pad ↑) eve kurulur.
    /// </summary>
    public class BuildTable : Interactable
    {
        RecipeInfo? pending;

        public static BuildTable Instance { get; private set; }
        public RecipeInfo? PendingOrder => pending;

        public override string ActionLabel => pending.HasValue ? $"Hazırla: {pending.Value.Name}" : "İnşaat Menüsü";
        public override WorkCategory Category => WorkCategory.Repair;
        public override float WorkDuration => pending.HasValue ? pending.Value.Duration : base.WorkDuration;
        public override bool RequiresEmptyHands => true;

        public override string StatusText
        {
            get
            {
                var bench = Workbench.Instance;
                int ready = bench != null ? bench.TotalUnplacedCount : 0;
                return "Eve kurulan yapılar buradan hazırlanır." +
                       (ready > 0 ? $"\nKuruluma hazır: {ready} ({GameInput.Prompt(InputPrompt.Build)} ile yerleştir)" : "") +
                       (pending.HasValue ? $"\nSipariş: {pending.Value.Name}" : "");
            }
        }

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
        }

        void OnEnable() => Instance = this;

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        public override void HandleCommand(Survivor worker, bool append) =>
            EventBus.Publish(new WorkbenchMenuRequested(worker, true));

        public bool Order(RecipeInfo recipe, Survivor worker, bool append)
        {
            var bench = Workbench.Instance;
            if (bench == null || worker == null || !recipe.IsPlaceable || !bench.CanOrder(recipe, out _)) return false;

            pending = recipe;
            worker.Tasks.Assign(this, append);
            return true;
        }

        public override bool CanStart(Survivor worker, out string reason)
        {
            var bench = Workbench.Instance;
            if (bench == null)
            {
                reason = "Çalışma Tezgahı yok.";
                return false;
            }
            if (!pending.HasValue)
            {
                reason = "Önce inşaat menüsünden bir yapı seç.";
                return false;
            }
            return bench.CanOrder(pending.Value, out reason);
        }

        public override bool CanContinue(Survivor worker) => pending.HasValue;

        public override void Complete(Survivor worker)
        {
            if (!pending.HasValue || Workbench.Instance == null) return;

            var recipe = pending.Value;
            pending = null;
            Workbench.Instance.Produce(recipe);
        }

        // ---------------------------------------------------------------- Kendini kurma

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        // Sahne yeniden kurulmadan da gelsin: sahnede İnşaat Masası yoksa tezgahın yanına koyulur.
        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var building = FindFirstObjectByType<BuildingLayout>();
            if (building == null || FindFirstObjectByType<BuildTable>() != null) return;

            var workbench = FindFirstObjectByType<Workbench>();
            float z = workbench != null ? workbench.transform.position.z : building.WalkPlaneZ + building.Depth * 0.25f;
            CreateDefault(building, new Vector3(3.6f, building.FloorY(1), z));
        }

        public static BuildTable CreateDefault(BuildingLayout building, Vector3 floorPoint)
        {
            var root = new GameObject("İnşaat Masası");
            root.transform.position = floorPoint;
            if (building != null) root.transform.SetParent(building.transform, true);

            var wood = new Color(0.55f, 0.4f, 0.24f);
            var dark = new Color(0.26f, 0.28f, 0.31f);
            var paper = new Color(0.78f, 0.86f, 0.95f);

            Part(root.transform, PrimitiveType.Cube, new Vector3(0f, 0.9f, 0f), new Vector3(1.9f, 0.1f, 0.95f), wood);
            Part(root.transform, PrimitiveType.Cube, new Vector3(-0.8f, 0.43f, -0.35f), new Vector3(0.1f, 0.86f, 0.1f), dark);
            Part(root.transform, PrimitiveType.Cube, new Vector3(0.8f, 0.43f, -0.35f), new Vector3(0.1f, 0.86f, 0.1f), dark);
            Part(root.transform, PrimitiveType.Cube, new Vector3(-0.8f, 0.43f, 0.35f), new Vector3(0.1f, 0.86f, 0.1f), dark);
            Part(root.transform, PrimitiveType.Cube, new Vector3(0.8f, 0.43f, 0.35f), new Vector3(0.1f, 0.86f, 0.1f), dark);
            // Masa üstünde plan kâğıdı ve alet kutusu.
            Part(root.transform, PrimitiveType.Cube, new Vector3(-0.2f, 0.96f, 0f), new Vector3(1.0f, 0.02f, 0.7f), paper);
            Part(root.transform, PrimitiveType.Cube, new Vector3(0.65f, 1.0f, 0.1f), new Vector3(0.4f, 0.12f, 0.28f), dark);

            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.55f, 0f);
            box.size = new Vector3(1.95f, 1.1f, 1.0f);

            var table = root.AddComponent<BuildTable>();
            table.ConfigureRuntime("İnşaat Masası", 5f);
            return table;
        }

        static void Part(Transform parent, PrimitiveType type, Vector3 localPosition, Vector3 localScale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;

            var renderer = go.GetComponent<Renderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            renderer.sharedMaterial = material;
        }
    }
}
