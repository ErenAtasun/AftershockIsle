using AftershockIsle.CameraControl;
using AftershockIsle.Core;
using AftershockIsle.Economy;
using AftershockIsle.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AftershockIsle.Expedition
{
    /// <summary>
    /// Sahada çanta (envanter) penceresi: yük çubuğu, yanındaki ekipman, çantadaki ganimet ve ağırlıkları.
    /// Eşya bırakılabilir (yere konur, sonra geri alınabilir); yakında bekleyen ganimet buradan alınabilir.
    /// Açıkken zaman durur. Klavye I, gamepad D-pad ↑ ile açılır; B / I / Kapat ile kapanır.
    /// </summary>
    public partial class FieldExpedition
    {
        UiWindow inventoryWindow;
        Button inventoryFirst;
        bool inventoryOpen;
        bool inventoryDirty;

        void UpdateInventory()
        {
            if (!IsActive)
            {
                if (inventoryOpen) CloseInventory();
                return;
            }

            if (!inventoryOpen)
            {
                if (SideViewCamera.ViewingField && !ModalMenu.IsOpen && !PauseMenu.IsOpen && searchTarget == null && GameInput.InventoryPressed)
                    OpenInventory();
                return;
            }

            TimeManager.Hold(this);
            if (inventoryDirty) BuildInventory();
            inventoryWindow.Tick(inventoryFirst != null ? inventoryFirst.gameObject : null);

            // Gamepad'de D-pad ↑ pencerede gezinmek için kullanılır; kapatmak B ile.
            if (GameInput.MenuBackPressed || (!GameInput.UsingGamepad && GameInput.InventoryPressed) || !SideViewCamera.ViewingField)
                CloseInventory();
        }

        void OpenInventory()
        {
            inventoryOpen = true;
            velocity = Vector3.zero;
            BuildInventory();
            inventoryWindow.Show(true);
            TimeManager.Hold(this);
            if (GameInput.UsingGamepad && EventSystem.current != null && inventoryFirst != null)
                EventSystem.current.SetSelectedGameObject(inventoryFirst.gameObject);
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.Search, 0.5f);
        }

        void CloseInventory()
        {
            inventoryOpen = false;
            if (inventoryWindow != null) inventoryWindow.Show(false);
            TimeManager.Release(this);
        }

        void BuildInventory()
        {
            inventoryDirty = false;
            if (inventoryWindow == null) inventoryWindow = new UiWindow("Çanta", new Vector2(900f, 720f), CloseInventory);
            inventoryWindow.ClearContent();
            inventoryWindow.ClearFooter();
            inventoryFirst = null;

            inventoryWindow.Title.text = $"ÇANTA — {explorer.DisplayName}";

            // Yük çubuğu
            var loadRow = inventoryWindow.Row(56f, new Color(0f, 0f, 0f, 0f));
            var fill = UiFactory.Bar(loadRow, "Yük", new Vector2(820f, 30f), FreeKg <= 0 ? UiFactory.Danger : UiFactory.Good, out var loadLabel, 17);
            UiFactory.Place((RectTransform)fill.transform.parent, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 30f));
            fill.fillAmount = capacityKg > 0 ? Mathf.Clamp01(LoadKg / (float)capacityKg) : 1f;
            loadLabel.text = $"YÜK {LoadKg} / {capacityKg} kg   ·   ekipman {GearKg} kg, ganimet {LootKg} kg   ·   boş {FreeKg} kg";

            // Ekipman
            inventoryWindow.Heading("Ekipman (sefer hazırlığında seçildi)");
            var weaponInfo = FieldWeapons.Get(loadout.Weapon);
            Line($"Silah: <b>{weaponInfo.Name}</b> ({ExpeditionLoadout.WeaponWeight(loadout.Weapon)} kg) + Sapan (0 kg)");
            if (weaponInfo.UsesAmmo) Line($"Mermi: <b>{ammo}</b> ({ExpeditionLoadout.AmmoWeight(ammo)} kg)");
            Line($"Yaşam Kiti (ilk yardım, {GameInput.Prompt(InputPrompt.Heal)}): <b>{kits}</b> ({kits} kg)");
            if (loadout.Pickaxe) Line($"Kazma ({ExpeditionLoadout.PickaxeWeight} kg): gömülü sandıkları kazar");
            if (loadout.Crowbar) Line($"Levye ({ExpeditionLoadout.CrowbarWeight} kg): kilitli kasaları açar");

            // Yerde bekleyen
            var near = NearestSpot();
            if (near != null && near.Searched && near.HasPending)
            {
                inventoryWindow.Heading("Yakında");
                var row = inventoryWindow.Row(58f);
                var label = UiFactory.Label(row, $"{near.Pending} — {ExpeditionLoadout.Weight(near.Pending)} kg", 18, UiFactory.Text, TextAnchor.MiddleLeft, FontStyle.Normal, false);
                UiFactory.Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(520f, 40f));
                var take = UiFactory.TextButton(row, "Al", new Vector2(150f, 44f), () =>
                {
                    TakePending(near);
                    inventoryDirty = true;
                }, UiFactory.Good, 18);
                UiFactory.Place((RectTransform)take.transform, new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(150f, 44f));
                take.interactable = FreeKg >= ExpeditionLoadout.ResourceWeight(near.Pending.type);
                inventoryFirst ??= take;
            }

            // Ganimet
            inventoryWindow.Heading("Ganimet (dönüşte depoya girer)");
            if (bag.Count == 0) Line("<color=#a8a8a0>Çanta boş.</color>");
            foreach (var item in bag.ToArray())
            {
                var type = item.type;
                var row = inventoryWindow.Row(58f);
                var label = UiFactory.Label(row, $"<b>{item.amount} {type.ToDisplayName()}</b>   <color=#cfcfc8>{ExpeditionLoadout.ResourceWeight(type)} kg/adet · toplam {ExpeditionLoadout.Weight(item)} kg</color>",
                    18, type.ToColor(), TextAnchor.MiddleLeft, FontStyle.Normal, false);
                UiFactory.Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(480f, 40f));

                var dropOne = UiFactory.TextButton(row, "1 bırak", new Vector2(130f, 44f), () => Drop(type, 1), null, 17);
                UiFactory.Place((RectTransform)dropOne.transform, new Vector2(1f, 0.5f), new Vector2(-156f, 0f), new Vector2(130f, 44f));
                var dropAll = UiFactory.TextButton(row, "Hepsini bırak", new Vector2(140f, 44f), () => Drop(type, int.MaxValue), new Color(0.55f, 0.2f, 0.16f, 0.97f), 17);
                UiFactory.Place((RectTransform)dropAll.transform, new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(140f, 44f));
                inventoryFirst ??= dropOne;
            }

            var legend = UiFactory.Label(inventoryWindow.Footer, ExpeditionLoadout.WeightLegend + "\nBırakılan eşya yere konur, sonra geri alınabilir.",
                14, new Color(0.8f, 0.8f, 0.75f), TextAnchor.MiddleLeft, FontStyle.Normal, false);
            legend.rectTransform.sizeDelta = new Vector2(600f, 46f);
            var close = inventoryWindow.FooterButton($"Kapat ({GameInput.Prompt(InputPrompt.Back)})", CloseInventory, null, 200f);
            inventoryFirst ??= close;
        }

        void Drop(ResourceType type, int amount)
        {
            DropFromBag(type, amount);
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.Pickup, 0.5f, 0.15f);
            inventoryDirty = true;
        }

        void Line(string text)
        {
            var label = UiFactory.Label(inventoryWindow.Content, text, 18, UiFactory.Text, TextAnchor.UpperLeft, FontStyle.Normal, false);
            label.gameObject.AddComponent<LayoutElement>().minHeight = 28f;
        }
    }
}
