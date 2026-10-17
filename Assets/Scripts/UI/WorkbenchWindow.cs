using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Economy;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Tezgahla etkileşince (sağ tık ya da F / A) açılan üretim penceresi (uGUI). Seçilen tarif, pencereyi açan karaktere
    /// iş olarak verilir. Üretilen yerleştirilebilir yapılar buradan ya da [B] tuşuyla sığınak ızgarasına yerleştirilir.
    /// Satırlar açılışta kurulur; maliyet, durum ve butonlar saniyede dört kez tazelenir (kaynak değişince anında görünür).
    /// </summary>
    public class WorkbenchWindow : MonoBehaviour
    {
        const float RefreshSeconds = 0.25f;

        class RecipeRow
        {
            public RecipeInfo Recipe;
            public Text Title;
            public Text Status;
            public Button Order;
            public Button Place;
        }

        UiWindow window;
        Survivor worker;
        Button placeAll;
        Text placeAllLabel;
        Text pendingLabel;
        readonly List<RecipeRow> rows = new List<RecipeRow>();
        float refreshAt;
        float openedAt;

        void OnEnable() => EventBus.Subscribe<WorkbenchMenuRequested>(OnMenuRequested);
        void OnDisable() => EventBus.Unsubscribe<WorkbenchMenuRequested>(OnMenuRequested);

        void OnMenuRequested(WorkbenchMenuRequested evt)
        {
            if (Workbench.Instance == null) return;
            worker = evt.Worker;
            Build();
            window.Show(true);
            openedAt = Time.unscaledTime;
            Refresh();
            Select(FirstSelectable());
        }

        void Update()
        {
            if (window == null || !window.IsOpen) return;

            if (worker == null || worker.IsDead || !worker.isActiveAndEnabled || Workbench.Instance == null)
            {
                Close();
                return;
            }

            window.Tick(FirstSelectable());
            if (GameInput.MenuBackPressed || GameInput.BuildModeKeyPressed)
            {
                Close();
                return;
            }

            if (Time.unscaledTime >= refreshAt) Refresh();
        }

        void Close()
        {
            if (window != null) window.Show(false);
        }

        // ---------------------------------------------------------------- Kurulum

        void Build()
        {
            if (window == null) window = new UiWindow("Çalışma Tezgahı", new Vector2(980f, 760f), Close);
            window.ClearContent();
            window.ClearFooter();
            rows.Clear();

            placeAll = UiFactory.TextButton(window.Content, "", new Vector2(0f, 48f), () => OpenPlacement(null), UiFactory.Good, 18);
            placeAll.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
            placeAllLabel = placeAll.GetComponentInChildren<Text>();

            for (int tier = 1; tier <= 3; tier++)
            {
                window.Heading(tier == 1 ? "Kademe 1 — İlkel Afet Hazırlığı & Ev Konforu"
                    : tier == 2 ? "Kademe 2 — Mekanik, Revir & Altyapı"
                    : "Kademe 3 — İletişim, Jeneratör & Kurtarma");

                foreach (var recipe in RecipeCatalog.All)
                    if (recipe.RequiredTier == tier) rows.Add(BuildRow(recipe));
            }

            pendingLabel = UiFactory.Label(window.Footer, "", 15, new Color(0.8f, 0.8f, 0.75f), TextAnchor.MiddleLeft, FontStyle.Normal, false);
            pendingLabel.rectTransform.sizeDelta = new Vector2(470f, 46f);
            window.FooterButton($"Yerleşim Modu ({GameInput.Prompt(InputPrompt.Build)})", () => OpenPlacement(null));
            window.FooterButton($"Kapat ({GameInput.Prompt(InputPrompt.Back)})", Close, null, 180f);
        }

        RecipeRow BuildRow(RecipeInfo recipe)
        {
            var row = window.Row(98f);
            var entry = new RecipeRow { Recipe = recipe };

            entry.Title = UiFactory.Label(row, "", 18, UiFactory.Text, TextAnchor.UpperLeft);
            UiFactory.Place(entry.Title.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -8f), new Vector2(700f, 24f));

            var description = UiFactory.Label(row, recipe.Description, 14, new Color(0.78f, 0.78f, 0.74f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(description.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -34f), new Vector2(700f, 38f));

            entry.Status = UiFactory.Label(row, "", 14, UiFactory.Text, TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(entry.Status.rectTransform, new Vector2(0f, 0f), new Vector2(14f, 6f), new Vector2(720f, 22f));

            entry.Order = UiFactory.TextButton(row, recipe.IsUpgrade ? "Yükselt" : "Üret", new Vector2(120f, 46f),
                () => OrderRecipe(recipe), UiFactory.Good, 18);
            UiFactory.Place((RectTransform)entry.Order.transform, new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(120f, 46f));

            if (recipe.IsPlaceable)
            {
                entry.Place = UiFactory.TextButton(row, "Kur", new Vector2(90f, 46f), () => OpenPlacement(recipe.Id), null, 18);
                UiFactory.Place((RectTransform)entry.Place.transform, new Vector2(1f, 0.5f), new Vector2(-142f, 0f), new Vector2(90f, 46f));
            }
            return entry;
        }

        // ---------------------------------------------------------------- Durum

        void Refresh()
        {
            refreshAt = Time.unscaledTime + RefreshSeconds;
            var bench = Workbench.Instance;
            bool grace = Time.unscaledTime - openedAt < 0.3f;

            window.Title.text = $"ÇALIŞMA TEZGAHI — KADEME {bench.Tier}   <size=16><color=#cfcfc8>üretecek: {worker.DisplayName}</color></size>";

            int unplaced = bench.TotalUnplacedCount;
            placeAll.gameObject.SetActive(unplaced > 0);
            placeAllLabel.text = $"Kuruluma hazır {unplaced} yapıyı yerleştir ({GameInput.Prompt(InputPrompt.Build)})";

            foreach (var row in rows)
            {
                var recipe = row.Recipe;
                bool canOrder = bench.CanOrder(recipe, out string reason);
                bool done = !recipe.Repeatable && Workbench.IsCrafted(recipe.Id);
                int ready = recipe.IsPlaceable ? bench.GetUnplacedCount(recipe.Id) : 0;

                string suffix = done && ready == 0 ? "  <color=#9be37a>(TAMAM)</color>"
                    : ready > 0 ? $"  <color=#9be37a>(kuruluma hazır: {ready})</color>"
                    : recipe.Id == RecipeId.SteelBeam ? $"  <color=#cfcfc8>(stok {bench.SteelBeams})</color>"
                    : recipe.Id == RecipeId.AmmoPack ? $"  <color=#cfcfc8>(stok {bench.Ammo})</color>"
                    : "";
                string grid = recipe.IsPlaceable ? $"   <size=13><color=#9fb4c8>{recipe.GridWidth}x{recipe.GridDepth} · {recipe.AllowedFloorsText()}</color></size>" : "";
                row.Title.text = recipe.Name + suffix + grid;

                string blocked = canOrder || done ? "" : $"   <color=#ff9a80>→ {reason}</color>";
                row.Status.text = $"Maliyet: {ResourceStorage.FormatCosts(recipe.Costs)}   ·   Süre: {recipe.Duration:0} sn{blocked}";

                row.Order.interactable = canOrder && !grace;
                row.Order.gameObject.SetActive(!(done && !recipe.Repeatable));
                if (row.Place != null) row.Place.gameObject.SetActive(ready > 0);
            }

            pendingLabel.text = bench.PendingOrder.HasValue ? $"Bekleyen sipariş: {bench.PendingOrder.Value.Name}" : "";
        }

        void OrderRecipe(RecipeInfo recipe)
        {
            Workbench.Instance.Order(recipe, worker, GameInput.QueueModifierHeld);
            Close();
        }

        void OpenPlacement(RecipeId? recipe)
        {
            Close();
            var placement = PlacementController.Instance;
            if (placement == null) return;
            if (recipe.HasValue) placement.EnterPlacementForRecipe(recipe.Value);
            else placement.ToggleBuildMode();
        }

        GameObject FirstSelectable()
        {
            if (placeAll != null && placeAll.gameObject.activeInHierarchy) return placeAll.gameObject;
            foreach (var row in rows)
                if (row.Order.gameObject.activeInHierarchy && row.Order.interactable) return row.Order.gameObject;
            return rows.Count > 0 ? rows[0].Order.gameObject : null;
        }

        static void Select(GameObject target)
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(target);
        }
    }
}
