using System;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Expedition;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Sefer hazırlığı sayfası (harita sonrası): silah, mermi, Yaşam Kiti ve alet seçimi; yük çubuğu çantada ganimete
    /// ne kadar yer kalacağını gösterir. Seçimler bir sonraki sefer için hatırlanır.
    /// </summary>
    public class LoadoutPanel
    {
        readonly UiWindow window;
        readonly Action onBack;
        readonly Action<ExpeditionLoadout> onDepart;

        ExpeditionLoadout loadout;
        ExpeditionSite site;

        Text weaponValue, ammoValue, kitValue, pickValue, crowValue, loadLabel, tips;
        Image loadFill;
        Button weaponButton, ammoMinus, ammoPlus, kitMinus, kitPlus, pickButton, crowButton, departButton;

        public bool IsOpen => window.IsOpen;

        public LoadoutPanel(Action back, Action<ExpeditionLoadout> depart)
        {
            onBack = back;
            onDepart = depart;
            window = new UiWindow("Sefer Hazırlığı", new Vector2(980f, 720f), () => onBack?.Invoke());

            var loadRow = window.Row(60f, new Color(0f, 0f, 0f, 0f));
            loadFill = UiFactory.Bar(loadRow, "Yük", new Vector2(880f, 32f), UiFactory.Good, out loadLabel, 17);
            UiFactory.Place((RectTransform)loadFill.transform.parent, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(880f, 32f));

            window.Heading("Silah ve cephane");
            weaponButton = Option("Silah", out weaponValue, "Değiştir", CycleWeapon);
            Stepper("Mermi", out ammoValue, out ammoMinus, out ammoPlus, () => StepAmmo(-1), () => StepAmmo(1));

            window.Heading("İlk yardım");
            Stepper("Yaşam Kiti", out kitValue, out kitMinus, out kitPlus, () => StepKits(-1), () => StepKits(1));

            window.Heading("Aletler (sahadaki özel noktalar için)");
            pickButton = Option("Kazma", out pickValue, "", () => { loadout.Pickaxe = !loadout.Pickaxe; Click(); });
            crowButton = Option("Levye", out crowValue, "", () => { loadout.Crowbar = !loadout.Crowbar; Click(); });

            tips = UiFactory.Label(window.Content, "", 16, new Color(0.84f, 0.84f, 0.8f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            tips.gameObject.AddComponent<LayoutElement>().minHeight = 70f;

            window.FooterButton($"Geri ({GameInput.Prompt(InputPrompt.Back)})", () => onBack?.Invoke(), null, 200f);
            departButton = window.FooterButton("Yola Çık", () => onDepart?.Invoke(loadout), UiFactory.Good, 220f);
        }

        public void Open(ExpeditionSite target, ExpeditionLoadout previous)
        {
            site = target;
            loadout = previous ?? ExpeditionLoadout.Default();
            loadout.Clamp();
            window.Show(true);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(weaponButton.gameObject);
        }

        public void Close() => window.Show(false);

        public void Tick()
        {
            window.Tick(weaponButton.gameObject);
            Refresh();
        }

        // ---------------------------------------------------------------- Satırlar

        Button Option(string title, out Text value, string buttonText, Action onClick)
        {
            var row = window.Row(62f);
            var name = UiFactory.Label(row, title, 19, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(170f, 40f));
            value = UiFactory.Label(row, "", 17, UiFactory.Text, TextAnchor.MiddleLeft, FontStyle.Normal, false);
            UiFactory.Place(value.rectTransform, new Vector2(0f, 0.5f), new Vector2(190f, 0f), new Vector2(560f, 56f));
            var button = UiFactory.TextButton(row, buttonText, new Vector2(170f, 46f), onClick, null, 18);
            UiFactory.Place((RectTransform)button.transform, new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(170f, 46f));
            return button;
        }

        void Stepper(string title, out Text value, out Button minus, out Button plus, Action onMinus, Action onPlus)
        {
            var row = window.Row(62f);
            var name = UiFactory.Label(row, title, 19, UiFactory.Accent, TextAnchor.MiddleLeft);
            UiFactory.Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(170f, 40f));
            value = UiFactory.Label(row, "", 17, UiFactory.Text, TextAnchor.MiddleLeft, FontStyle.Normal, false);
            UiFactory.Place(value.rectTransform, new Vector2(0f, 0.5f), new Vector2(190f, 0f), new Vector2(560f, 56f));
            minus = UiFactory.TextButton(row, "−", new Vector2(80f, 46f), onMinus, null, 26);
            UiFactory.Place((RectTransform)minus.transform, new Vector2(1f, 0.5f), new Vector2(-102f, 0f), new Vector2(80f, 46f));
            plus = UiFactory.TextButton(row, "+", new Vector2(80f, 46f), onPlus, null, 26);
            UiFactory.Place((RectTransform)plus.transform, new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(80f, 46f));
        }

        // ---------------------------------------------------------------- Seçimler

        void CycleWeapon()
        {
            var weapons = ExpeditionLoadout.AvailableWeapons();
            int index = weapons.IndexOf(loadout.Weapon);
            loadout.Weapon = weapons[(index + 1) % weapons.Count];
            if (!FieldWeapons.Get(loadout.Weapon).UsesAmmo) loadout.Ammo = 0;
            else if (loadout.Ammo == 0) loadout.Ammo = Mathf.Min(20, ExpeditionLoadout.AmmoInShelter);
            Click();
        }

        void StepAmmo(int direction)
        {
            loadout.Ammo = Mathf.Clamp(loadout.Ammo + direction * ExpeditionLoadout.AmmoStep, 0, ExpeditionLoadout.AmmoInShelter);
            Click();
        }

        void StepKits(int direction)
        {
            loadout.Kits = Mathf.Clamp(loadout.Kits + direction, 0, Mathf.Min(ExpeditionLoadout.MaxKits, ExpeditionLoadout.KitsInShelter));
            Click();
        }

        static void Click() => AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.UiClick, 0.6f);

        // ---------------------------------------------------------------- Görünüm

        void Refresh()
        {
            var expeditions = ExpeditionSystem.Instance;
            bool companion = expeditions != null && expeditions.Companion != null;
            int capacity = ExpeditionLoadout.Capacity(companion);
            int gear = loadout.GearWeight();
            int free = capacity - gear;

            window.Title.text = $"SEFER HAZIRLIĞI — {(site != null ? site.name : "")}   <size=16><color=#cfcfc8>kâşif: {(expeditions != null && expeditions.Explorer != null ? expeditions.Explorer.DisplayName : "")}</color></size>";

            loadFill.fillAmount = capacity > 0 ? Mathf.Clamp01(gear / (float)capacity) : 1f;
            loadFill.color = free < 0 ? UiFactory.Danger : free <= 3 ? new Color(0.95f, 0.75f, 0.25f) : UiFactory.Good;
            loadLabel.text = free < 0
                ? $"EKİPMAN ÇOK AĞIR: {gear} / {capacity} kg"
                : $"Ekipman {gear} kg / çanta {capacity} kg{(companion ? " (yoldaş +3)" : "")}   ·   ganimete <b>{free} kg</b> yer kalıyor";

            var weapon = FieldWeapons.Get(loadout.Weapon);
            weaponValue.text = $"<b>{weapon.Name}</b> ({ExpeditionLoadout.WeaponWeight(loadout.Weapon)} kg)   <color=#cfcfc8>hasar {weapon.Damage * weapon.Pellets:0} · menzil {weapon.Range:0} m{(weapon.UsesAmmo ? " · mermi harcar" : " · sessiz, sınırsız")}</color>\n<size=14><color=#a8a8a0>Sapan her zaman yanında (0 kg).</color></size>";
            weaponButton.interactable = ExpeditionLoadout.AvailableWeapons().Count > 1;

            bool usesAmmo = weapon.UsesAmmo;
            ammoValue.text = usesAmmo
                ? $"<b>{loadout.Ammo}</b> mermi ({ExpeditionLoadout.AmmoWeight(loadout.Ammo)} kg)   <color=#cfcfc8>sığınakta {ExpeditionLoadout.AmmoInShelter} · kalan geri döner</color>"
                : "<color=#a8a8a0>Sapan mermi harcamaz.</color>";
            ammoMinus.interactable = usesAmmo && loadout.Ammo > 0;
            ammoPlus.interactable = usesAmmo && loadout.Ammo < ExpeditionLoadout.AmmoInShelter;

            kitValue.text = $"<b>{loadout.Kits}</b> kit ({loadout.Kits} kg)   <color=#cfcfc8>sığınakta {ExpeditionLoadout.KitsInShelter} · sahada {GameInput.Prompt(InputPrompt.Heal)} ile +35 can · kullanılmayan geri döner</color>";
            kitMinus.interactable = loadout.Kits > 0;
            kitPlus.interactable = loadout.Kits < Mathf.Min(ExpeditionLoadout.MaxKits, ExpeditionLoadout.KitsInShelter);

            ToolRow(pickButton, pickValue, ToolKit.HasPickaxe, loadout.Pickaxe, ExpeditionLoadout.PickaxeWeight,
                "Gömülü sandıkları kazar (bol ganimet).", "Tezgahta Kazma üret (Kademe 2).");
            ToolRow(crowButton, crowValue, ToolKit.HasCrowbar, loadout.Crowbar, ExpeditionLoadout.CrowbarWeight,
                "Kilitli kasaları açar (bol ganimet).", "Tezgahta Levye üret.");

            string spots = site == null ? "" :
                $"Bu sahada: <b>{site.LockedCrates}</b> kilitli kasa (Levye), <b>{site.BuriedCaches}</b> gömülü sandık (Kazma).";
            tips.text = $"{spots}\n{ExpeditionLoadout.WeightLegend}\nÇanta dolunca sahada {GameInput.Prompt(InputPrompt.Inventory)} ile çantayı açıp eşya bırakabilirsin.";

            departButton.interactable = free >= 0;
        }

        static void ToolRow(Button button, Text value, bool crafted, bool taken, int weight, string effect, string howTo)
        {
            button.interactable = crafted;
            button.GetComponentInChildren<Text>().text = !crafted ? "Yok" : taken ? "Bırak" : "Yanına al";
            value.text = !crafted
                ? $"<color=#a8a8a0>Üretilmedi. {howTo}</color>"
                : taken ? $"<b>Yanında</b> ({weight} kg)   <color=#cfcfc8>{effect}</color>"
                : $"Sığınakta kalıyor   <color=#cfcfc8>{effect} ({weight} kg)</color>";
        }
    }
}
