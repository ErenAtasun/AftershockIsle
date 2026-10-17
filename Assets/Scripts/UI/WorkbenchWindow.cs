using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Economy;
using UnityEngine;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Tezgahla etkileşince (sağ tık ya da F / A) açılan geçici IMGUI üretim menüsü. Seçilen tarif, menüyü açan karaktere
    /// iş olarak verilir. Üretilen yerleşilebilir yapılar buradan ya da [B] tuşuyla sığınak ızgarasına (Grid) yerleştirilir.
    /// </summary>
    public class WorkbenchWindow : MonoBehaviour
    {
        const int WindowId = 4401;

        Rect windowRect = new Rect(0, 0, 560, 510);
        Survivor worker;
        bool open;
        Vector2 scroll;
        GUIStyle wrapStyle;
        GUIStyle headerStyle;
        readonly MenuNav nav = new MenuNav();

        void OnEnable() => EventBus.Subscribe<WorkbenchMenuRequested>(OnMenuRequested);
        void OnDisable() => EventBus.Unsubscribe<WorkbenchMenuRequested>(OnMenuRequested);

        void OnMenuRequested(WorkbenchMenuRequested evt)
        {
            worker = evt.Worker;
            open = true;
            nav.Reset();
        }

        void Update()
        {
            if (!open) return;
            nav.Tick();
            if (GameInput.MenuBackPressed || GameInput.BuildModeTogglePressed) open = false;
        }

        void OnGUI()
        {
            var bench = Workbench.Instance;
            if (!open || bench == null) return;
            if (worker == null || worker.IsDead || !worker.isActiveAndEnabled)
            {
                open = false;
                return;
            }

            wrapStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 };
            headerStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

            windowRect.x = (Screen.width - windowRect.width) * 0.5f;
            windowRect.y = (Screen.height - windowRect.height) * 0.5f;
            UIBlocker.Add(windowRect);
            GUI.Window(WindowId, windowRect, DrawWindow, $"Çalışma Tezgahı — Kademe {bench.Tier}   (üretecek: {worker.DisplayName})");
        }

        void DrawWindow(int id)
        {
            var bench = Workbench.Instance;
            nav.BeginDraw();

            if (bench.TotalUnplacedCount > 0)
            {
                GUI.color = new Color(0.55f, 1f, 0.65f);
                nav.Button($"📦 Kuruluma Hazır {bench.TotalUnplacedCount} Yapıyı Eve Yerleştir [B]", () =>
                {
                    open = false;
                    if (PlacementController.Instance != null)
                        PlacementController.Instance.ToggleBuildMode();
                });
                GUI.color = Color.white;
                GUILayout.Space(4);
            }

            scroll = GUILayout.BeginScrollView(scroll);

            for (int tier = 1; tier <= 3; tier++)
            {
                GUILayout.Label(tier == 1 ? "Kademe 1 — İlkel Afet Hazırlığı & Ev Konforu"
                    : tier == 2 ? "Kademe 2 — Mekanik, Revir & Altyapı"
                    : "Kademe 3 — İletişim, Jeneratör & Kurtarma", headerStyle);

                if (tier == 1)
                    GUILayout.Label("Ahşap Destek (çatlak kolona sağ tık) ve Manuel Tulumba başlangıçtan beri kullanılabilir.", wrapStyle);

                foreach (var recipe in RecipeCatalog.All)
                    if (recipe.RequiredTier == tier) DrawRecipe(bench, recipe);

                GUILayout.Space(6);
            }

            GUILayout.EndScrollView();
            nav.KeepFocusVisible(ref scroll, windowRect.height - 140f);

            if (bench.PendingOrder.HasValue)
                GUILayout.Label($"Bekleyen sipariş: {bench.PendingOrder.Value.Name}", wrapStyle);

            GUILayout.BeginHorizontal();
            nav.Button("🏗 Ev Yerleşim Modu [B]", () =>
            {
                open = false;
                if (PlacementController.Instance != null)
                    PlacementController.Instance.ToggleBuildMode();
            });
            nav.Button($"Kapat ({GameInput.Prompt(InputPrompt.Back)})", () => open = false);
            GUILayout.EndHorizontal();
            nav.EndDraw();
        }

        void DrawRecipe(Workbench bench, RecipeInfo recipe)
        {
            bool canOrder = bench.CanOrder(recipe, out string reason);
            bool done = !recipe.Repeatable && Workbench.IsCrafted(recipe.Id);
            int unplacedCount = recipe.IsPlaceable ? bench.GetUnplacedCount(recipe.Id) : 0;

            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.BeginVertical();
            string suffix = done && unplacedCount == 0 ? "  (KURULDU / TAMAM)"
                : unplacedCount > 0 ? $"  (Kuruluma Hazır: {unplacedCount})"
                : recipe.Id == RecipeId.SteelBeam ? $"  (stok {bench.SteelBeams})"
                : recipe.Id == RecipeId.AmmoPack ? $"  (stok {bench.Ammo})"
                : "";

            string gridBadge = recipe.IsPlaceable
                ? $"   [Grid: {recipe.GridWidth}x{recipe.GridDepth} • {recipe.AllowedFloorsText()}]"
                : "";

            GUILayout.Label($"{recipe.Name}{suffix}{gridBadge}", headerStyle);
            GUILayout.Label(recipe.Description, wrapStyle);
            GUILayout.Label($"Maliyet: {ResourceStorage.FormatCosts(recipe.Costs)}   Süre: {recipe.Duration:0} sn" +
                            (canOrder || done ? "" : $"   → {reason}"), wrapStyle);
            GUILayout.EndVertical();

            if (unplacedCount > 0)
            {
                nav.Button("Kur [B]", () =>
                {
                    open = false;
                    if (PlacementController.Instance != null)
                        PlacementController.Instance.EnterPlacementForRecipe(recipe.Id);
                }, true, null, GUILayout.Width(68), GUILayout.Height(40));
            }

            nav.Button(recipe.IsUpgrade ? "Yükselt" : "Üret", () =>
            {
                bench.Order(recipe, worker, GameInput.QueueModifierHeld);
                open = false;
            }, canOrder, null, GUILayout.Width(78), GUILayout.Height(40));
            GUILayout.EndHorizontal();
        }
    }
}
