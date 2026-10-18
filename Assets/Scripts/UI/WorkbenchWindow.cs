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
        // True: İnşaat Masası (eve kurulan yapılar), false: Çalışma Tezgahı (silah, ekipman, yükseltme).
        bool buildMode;
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
            if (Workbench.Instance == null || (evt.Build && BuildTable.Instance == null)) return;
            worker = evt.Worker;
            buildMode = evt.Build;
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

            placeAll = null;
            placeAllLabel = null;
            if (buildMode)
            {
                placeAll = UiFactory.TextButton(window.Content, "", new Vector2(0f, 48f), () => OpenPlacement(null), UiFactory.Good, 18);
                placeAll.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
                placeAllLabel = placeAll.GetComponentInChildren<Text>();
            }

            // Tezgahta önce alet dolabı: aletler işleri hızlandırır, herkes kullanır.
            if (!buildMode)
            {
                window.Heading("Aletler — üretilince herkes kullanır, işler hızlanır");
                foreach (var recipe in RecipeCatalog.All)
                    if (recipe.IsTool) rows.Add(BuildRow(recipe));
            }

            for (int tier = 1; tier <= 3; tier++)
            {
                bool any = false;
                foreach (var recipe in RecipeCatalog.All)
                    if (recipe.RequiredTier == tier && recipe.IsPlaceable == buildMode && !recipe.IsTool) any = true;
                if (!any) continue;

                window.Heading(buildMode
                    ? (tier == 1 ? "Kademe 1 — Ev Konforu & Güvenlik"
                        : tier == 2 ? "Kademe 2 — Altyapı, Revir & Su"
                        : "Kademe 3 — Enerji & Koruma")
                    : (tier == 1 ? "Kademe 1 — Temel Ekipman & Mermi"
                        : tier == 2 ? "Kademe 2 — Silah, Zırh & Yükseltme"
                        : "Kademe 3 — İletişim, Ağır Silah & Kurtarma"));

                foreach (var recipe in RecipeCatalog.All)
                    if (recipe.RequiredTier == tier && recipe.IsPlaceable == buildMode && !recipe.IsTool) rows.Add(BuildRow(recipe));
            }

            pendingLabel = UiFactory.Label(window.Footer, "", 15, new Color(0.8f, 0.8f, 0.75f), TextAnchor.MiddleLeft, FontStyle.Normal, false);
            pendingLabel.rectTransform.sizeDelta = new Vector2(470f, 46f);
            if (buildMode) window.FooterButton($"Yerleşim Modu ({GameInput.Prompt(InputPrompt.Build)})", () => OpenPlacement(null));
            window.FooterButton($"Kapat ({GameInput.Prompt(InputPrompt.Back)})", Close, null, 180f);
        }

        RecipeRow BuildRow(RecipeInfo recipe)
        {
            var row = window.Row(98f);
            var entry = new RecipeRow { Recipe = recipe };

            entry.Title = UiFactory.Label(row, "", 18, UiFactory.Text, TextAnchor.UpperLeft);
            entry.Title.horizontalOverflow = HorizontalWrapMode.Overflow;
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

            string station = buildMode ? "İNŞAAT MASASI" : "ÇALIŞMA TEZGAHI";
            window.Title.text = $"{station} — KADEME {bench.Tier}   <size=16><color=#cfcfc8>çalışacak: {worker.DisplayName}</color></size>";

            int unplaced = bench.TotalUnplacedCount;
            if (placeAll != null)
            {
                placeAll.gameObject.SetActive(unplaced > 0);
                placeAllLabel.text = $"Kuruluma hazır {unplaced} yapıyı yerleştir ({GameInput.Prompt(InputPrompt.Build)})";
            }

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
                row.Title.text = recipe.Name + suffix + grid + "   " + RecipeTags(recipe);

                // Eksik malzeme kırmızı (elde/gereken), yeterli olanlar yeşil.
                string blocked = canOrder || done ? "" : $"   <color=#ff9a80>→ {reason}</color>";
                row.Status.text = $"Maliyet: {CostText(recipe.Costs)}   ·   Süre: {recipe.Duration:0} sn{blocked}";

                row.Order.interactable = canOrder && !grace;
                row.Order.gameObject.SetActive(!(done && !recipe.Repeatable));
                if (row.Place != null) row.Place.gameObject.SetActive(ready > 0);
            }

            var pending = buildMode ? (BuildTable.Instance != null ? BuildTable.Instance.PendingOrder : null) : bench.PendingOrder;
            pendingLabel.text = pending.HasValue ? $"Bekleyen sipariş: {pending.Value.Name}" : "";
        }

        static string CostText(ResourceAmount[] costs)
        {
            var storage = ResourceStorage.Instance;
            var text = new System.Text.StringBuilder();
            foreach (var cost in costs)
            {
                int have = storage != null ? storage.Get(cost.type) : 0;
                if (text.Length > 0) text.Append(", ");
                text.Append(have >= cost.amount
                    ? $"<color=#9be37a>{cost.amount} {cost.type.ToDisplayName()}</color>"
                    : $"<color=#ff8f7a>{cost.type.ToDisplayName()} {have}/{cost.amount}</color>");
            }
            return text.ToString();
        }

        /// <summary>Tarifin ne için olduğunu söyleyen renkli etiket (ne işe yaradığı bir bakışta anlaşılsın).</summary>
        static string RecipeTags(RecipeInfo recipe)
        {
            string tag;
            string color;
            switch (recipe.Id)
            {
                case RecipeId.UpgradeTier2:
                case RecipeId.UpgradeTier3:
                case RecipeId.RadioModule:
                case RecipeId.SteelBeam:
                    tag = "TAHLİYE İÇİN"; color = "#ffd75a"; break;
                case RecipeId.ElectricPump:
                case RecipeId.BypassValve:
                case RecipeId.LightningRod:
                case RecipeId.FireSuppressor:
                case RecipeId.GasMask:
                    tag = "FELAKETE KARŞI"; color = "#ff9a80"; break;
                case RecipeId.ReinforcedDoor:
                    tag = "GECE SAVUNMASI"; color = "#c9a0ff"; break;
                case RecipeId.ComfortCorner:
                case RecipeId.ShelterHeater:
                case RecipeId.MedStation:
                case RecipeId.WaterPurifier:
                case RecipeId.StorageRack:
                case RecipeId.BackupGenerator:
                    tag = "KONFOR / ALTYAPI"; color = "#9be37a"; break;
                case RecipeId.AmmoPack:
                case RecipeId.Pistol:
                case RecipeId.Shotgun:
                case RecipeId.Smg:
                case RecipeId.AssaultRifle:
                case RecipeId.SniperRifle:
                case RecipeId.ArmorVest:
                    tag = "SEFER"; color = "#8fc7ff"; break;
                default:
                    if (recipe.IsTool) { tag = "ALET"; color = "#cfe3a0"; break; }
                    return "";
            }
            return $"<size=13><color={color}>[{tag}]</color></size>";
        }

        void OrderRecipe(RecipeInfo recipe)
        {
            if (buildMode) BuildTable.Instance?.Order(recipe, worker, GameInput.QueueModifierHeld);
            else Workbench.Instance.Order(recipe, worker, GameInput.QueueModifierHeld);
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
