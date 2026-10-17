using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Economy;
using UnityEngine;

namespace AftershockIsle.UI
{
    /// <summary>Tezgaha sağ tıklayınca açılan geçici IMGUI üretim menüsü. Seçilen tarif, menüyü açan karaktere iş olarak verilir.</summary>
    public class WorkbenchWindow : MonoBehaviour
    {
        const int WindowId = 4401;

        Rect windowRect = new Rect(0, 0, 520, 470);
        Survivor worker;
        bool open;
        Vector2 scroll;
        GUIStyle wrapStyle;
        GUIStyle headerStyle;

        void OnEnable() => EventBus.Subscribe<WorkbenchMenuRequested>(OnMenuRequested);
        void OnDisable() => EventBus.Unsubscribe<WorkbenchMenuRequested>(OnMenuRequested);

        void OnMenuRequested(WorkbenchMenuRequested evt)
        {
            worker = evt.Worker;
            open = true;
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
            scroll = GUILayout.BeginScrollView(scroll);

            for (int tier = 1; tier <= 3; tier++)
            {
                GUILayout.Label(tier == 1 ? "Kademe 1 — İlkel Afet Hazırlığı"
                    : tier == 2 ? "Kademe 2 — Mekanik & Altyapı"
                    : "Kademe 3 — İletişim & Kurtarma", headerStyle);

                if (tier == 1)
                    GUILayout.Label("Ahşap Destek (çatlak kolona sağ tık) ve Manuel Tulumba başlangıçtan beri kullanılabilir.", wrapStyle);

                foreach (var recipe in RecipeCatalog.All)
                    if (recipe.RequiredTier == tier) DrawRecipe(bench, recipe);

                GUILayout.Space(6);
            }

            GUILayout.EndScrollView();

            if (bench.PendingOrder.HasValue)
                GUILayout.Label($"Bekleyen sipariş: {bench.PendingOrder.Value.Name}", wrapStyle);

            if (GUILayout.Button("Kapat")) open = false;
        }

        void DrawRecipe(Workbench bench, RecipeInfo recipe)
        {
            bool canOrder = bench.CanOrder(recipe, out string reason);
            bool done = !recipe.Repeatable && Workbench.Has(recipe.Id);

            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.BeginVertical();
            string suffix = done ? "  (TAMAM)" : recipe.Id == RecipeId.SteelBeam ? $"  (stok {bench.SteelBeams})" : "";
            GUILayout.Label($"{recipe.Name}{suffix}", headerStyle);
            GUILayout.Label(recipe.Description, wrapStyle);
            GUILayout.Label($"Maliyet: {ResourceStorage.FormatCosts(recipe.Costs)}   Süre: {recipe.Duration:0} sn" +
                            (canOrder || done ? "" : $"   → {reason}"), wrapStyle);
            GUILayout.EndVertical();

            GUI.enabled = canOrder;
            // IMGUI butonları sağ tıka da tepki verir; sadece sol tık kabul edilir.
            bool isLeft = Event.current.button == 0;
            if (GUILayout.Button(recipe.IsUpgrade ? "Yükselt" : "Üret", GUILayout.Width(80), GUILayout.Height(40)) && isLeft)
            {
                bench.Order(recipe, worker, GameInput.QueueModifierHeld);
                open = false;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }
    }
}
