using System.Text;
using AftershockIsle.CameraControl;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Economy;
using AftershockIsle.UI;
using UnityEngine;
using UnityEngine.UI;

namespace AftershockIsle.Expedition
{
    /// <summary>Saha arayüzü: üst panel, kontrol satırı ve hasar kızarması uGUI; dünya etiketleri ve etkileşim istemi imleci izlediği için IMGUI.</summary>
    public partial class FieldExpedition
    {
        // ---------------------------------------------------------------- HUD

        void Say(string text)
        {
            message = text;
            messageTimer = 6f;
        }

        void Float(string text, Vector3 position, Color color) =>
            floaters.Add(new FloatingText { Text = text, Position = position, Color = color });

        void UpdateFloaters(float dt)
        {
            for (int i = floaters.Count - 1; i >= 0; i--)
            {
                var item = floaters[i];
                item.Age += dt;
                item.Position += Vector3.up * dt * 0.8f;
                if (item.Age > 1.4f) floaters.RemoveAt(i);
                else floaters[i] = item;
            }
        }

        void OnGUI()
        {
            UiTheme.Apply();
            if (!IsActive || !SideViewCamera.ViewingField || ModalMenu.IsOpen || cam == null) return;

            centered ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold };
            prompt ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold };

            DrawWorldLabels();
            DrawInteractPrompt();
        }

        // ---------------------------------------------------------------- uGUI paneli

        GameObject hudRoot;
        Text hudTitle;
        Text hudInfo;
        Image hudHealth;
        Text hudControls;
        Image hurtVeil;
        bool hudShown;

        void BuildHud()
        {
            var canvas = UiFactory.Canvas("Saha Göstergesi", 11, transform).transform;
            hudRoot = UiFactory.Rect("Saha", canvas).gameObject;
            UiFactory.Stretch((RectTransform)hudRoot.transform);

            // Hasar alınca ekran kızarır; tıklamaları engellemez.
            hurtVeil = UiFactory.Image(hudRoot.transform, "Hasar", null, new Color(0.8f, 0f, 0f, 0f));
            hurtVeil.sprite = null;
            hurtVeil.type = Image.Type.Simple;
            UiFactory.Stretch(hurtVeil.rectTransform);

            var panel = UiFactory.Panel(hudRoot.transform, "Panel", UiFactory.Slate);
            panel.raycastTarget = false;
            UiFactory.Place(panel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(900f, 0f));
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(22, 22, 12, 14);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            hudTitle = UiFactory.Label(panel.transform, "", 23, UiFactory.Accent);
            hudTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

            hudHealth = UiFactory.Bar(panel.transform, "Can", new Vector2(0f, 22f), UiFactory.Good, out _, 16);
            hudHealth.transform.parent.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            hudInfo = UiFactory.Label(panel.transform, "", 19, UiFactory.Text, TextAnchor.UpperLeft, FontStyle.Normal, false);

            var bar = UiFactory.Panel(hudRoot.transform, "Kontroller", new Color(0.08f, 0.08f, 0.1f, 0.78f));
            bar.raycastTarget = false;
            UiFactory.Place(bar.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(1800f, 40f));
            hudControls = UiFactory.Label(bar.transform, "", 19, new Color(0.9f, 0.9f, 0.85f), TextAnchor.MiddleCenter, FontStyle.Normal, false);
            UiFactory.Stretch(hudControls.rectTransform, 6f);

            hudRoot.SetActive(false);
        }

        void RefreshHud()
        {
            bool show = IsActive && SideViewCamera.ViewingField && !inventoryOpen && !ModalMenu.IsOpen && !PauseMenu.IsOpen;
            if (hudRoot == null)
            {
                if (!show) return;
                BuildHud();
            }

            if (show != hudShown)
            {
                hudShown = show;
                hudRoot.SetActive(show);
            }
            if (!show) return;

            var veil = hurtVeil.color;
            veil.a = Mathf.Clamp01(hurtFlash * 0.35f);
            hurtVeil.color = veil;

            string place = InsideBuilding ? "   [BİNA İÇİ]" : "";
            hudTitle.text = $"SAHA: {site.name} — {explorer.DisplayName}   Kalan: {Mathf.CeilToInt(Remaining)} sn{place}";

            float health = explorer.Health / explorer.MaxHealth;
            hudHealth.fillAmount = Mathf.Clamp01(health);
            hudHealth.color = Color.Lerp(UiFactory.Danger, UiFactory.Good, health);

            var info = CurrentWeapon;
            string ammoText = info.UsesAmmo ? $"   Mermi: {ammo}" : "   (sessiz, sınırsız)";
            string stance = leanSide != 0 ? "Köşeden ateş" : CrouchedLow ? "Çömelik" : crouched ? "Doğruldu (açıkta)" : "Ayakta";
            var text = new StringBuilder();
            string loadColor = FreeKg <= 0 ? "#ff8f7a" : FreeKg <= 2 ? "#ffd75a" : "#9be37a";
            text.Append($"Can {explorer.Health:0}/{explorer.MaxHealth:0}   <color={loadColor}>Yük {LoadKg}/{CapacityKg} kg</color>   Çanta: {BagText()}");
            if (RescueSecured) text.Append("   [Kazazede kurtarıldı]");
            text.Append($"\nSilah: {info.Name}{ammoText}   Kit: {kits}{ToolsText()}   Duruş: {stance}{(inCover ? "  [SİPERDE]" : "")}");
            if (companion != null)
                text.Append(companion.IsDead ? $"\nYoldaş: {companion.Survivor.DisplayName} öldü"
                    : $"\nYoldaş: {companion.Survivor.DisplayName}   Can {companion.Survivor.Health:0}/{companion.Survivor.MaxHealth:0}");
            if (messageTimer > 0f && message != null) text.Append($"\n<color=#ffd75a>{message}</color>");
            hudInfo.text = text.ToString();

            hudControls.text =
                $"{GameInput.Prompt(InputPrompt.Move)}: yürü    {GameInput.Prompt(InputPrompt.Aim)}: nişan    " +
                $"{GameInput.Prompt(InputPrompt.Fire)}: ateş    {GameInput.Prompt(InputPrompt.SwitchWeapon)}: silah    " +
                $"{GameInput.Prompt(InputPrompt.Crouch)}: çömel    {GameInput.Prompt(InputPrompt.Dodge)}: kaçış    " +
                $"{GameInput.Prompt(InputPrompt.Interact)}: etkileşim    {GameInput.Prompt(InputPrompt.Heal)}: ilk yardım ({kits} kit)    " +
                $"{GameInput.Prompt(InputPrompt.Inventory)}: çanta    {GameInput.Prompt(InputPrompt.Retreat)}: çıkışa koş    " +
                $"{GameInput.Prompt(InputPrompt.SwitchCharacter)}: sığınağa dön";
        }

        void DrawWorldLabels()
        {
            DrawLabel(exitPoint + Vector3.up * 2.9f, "ÇIKIŞ", new Color(0.5f, 1f, 0.6f));

            foreach (var enemy in enemies)
            {
                if (enemy.IsDead) continue;
                string icon = enemy.IsAlerted ? "! " : enemy.IsInvestigating ? "? " : "";
                var top = enemy.Position + Vector3.up * enemy.LabelHeight;
                DrawLabel(top + Vector3.up * 0.25f, icon + enemy.Label, enemy.IsAlerted ? new Color(1f, 0.5f, 0.4f) : new Color(0.9f, 0.85f, 0.75f));
                DrawBar(top, enemy.Health01, Color.red, 44);
            }

            foreach (var spot in spots)
            {
                string label = spot.Done ? "(boş)"
                    : spot.Kind == FieldSpotKind.Rescue ? "MAHSUR KALAN"
                    : spot.HasPending ? $"{spot.Pending} ({ExpeditionLoadout.Weight(spot.Pending)} kg)"
                    : spot.Lock == FieldSpotLock.Locked ? (loadout.Crowbar ? "Kilitli kasa (Levye)" : "Kilitli kasa — Levye gerek")
                    : spot.Lock == FieldSpotLock.Buried ? (loadout.Pickaxe ? "Gömülü sandık (Kazma)" : "Gömülü sandık — Kazma gerek")
                    : "Ara";
                var labelColor = spot.Done ? Color.gray
                    : spot.HasPending ? new Color(1f, 0.9f, 0.45f)
                    : spot.Lock != FieldSpotLock.None ? new Color(0.75f, 0.85f, 1f)
                    : Color.white;
                DrawLabel(spot.Position + Vector3.up * 0.9f, label, labelColor);
                if (searchTarget == spot) DrawBar(spot.Position + Vector3.up * 1.3f, searchProgress / spot.Duration, Color.white, 54);
            }

            DrawBar(avatar.position + Vector3.up * 1.3f, explorer.Health / explorer.MaxHealth, Color.green, 56);
            if (companion != null && !companion.IsDead)
            {
                DrawLabel(companion.LabelPoint + Vector3.up * 0.25f, companion.Survivor.DisplayName, new Color(0.7f, 0.9f, 1f));
                DrawBar(companion.LabelPoint, companion.Survivor.Health / companion.Survivor.MaxHealth, Color.green, 44);
            }

            foreach (var item in floaters)
            {
                var color = item.Color;
                color.a = 1f - item.Age / 1.4f;
                DrawLabel(item.Position, item.Text, color);
            }
        }

        void DrawInteractPrompt()
        {
            if (searchTarget != null) return;

            string key = GameInput.Prompt(InputPrompt.Interact);
            string text;
            Vector3 anchor = avatar.position;

            if (NearExit)
            {
                text = exitArmed ? $"[{key}] Üsse dön  ({Mathf.Max(0f, ExitAutoSeconds - exitDwell):0.0} sn)" : $"[{key}] Üsse dön";
            }
            else
            {
                var spot = NearestSpot();
                if (spot == null) return;
                anchor = spot.Position;
                text = spot.Kind == FieldSpotKind.Rescue ? $"[{key}] Kurtar"
                    : spot.HasPending ? (FreeKg >= ExpeditionLoadout.ResourceWeight(spot.Pending.type) ? $"[{key}] Al" : "Çanta dolu")
                    : spot.Lock == FieldSpotLock.Locked && !loadout.Crowbar ? "Levye gerek"
                    : spot.Lock == FieldSpotLock.Buried && !loadout.Pickaxe ? "Kazma gerek"
                    : FreeKg <= 0 ? "Çanta dolu"
                    : spot.Lock == FieldSpotLock.Locked ? $"[{key}] Levyeyle aç"
                    : spot.Lock == FieldSpotLock.Buried ? $"[{key}] Kazmayla kaz"
                    : $"[{key}] Ara";
            }

            var screen = cam.WorldToScreenPoint(anchor + Vector3.up * 1.7f);
            if (screen.z < 0f) return;
            var at = UiTheme.ToGui(screen);
            GUI.Box(new Rect(at.x - 80f, at.y - 13f, 160f, 26f), text, prompt);
        }

        void DrawLabel(Vector3 world, string text, Color color)
        {
            var screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f) return;

            var previous = GUI.contentColor;
            GUI.contentColor = color;
            var at = UiTheme.ToGui(screen);
            GUI.Label(new Rect(at.x - 80f, at.y - 10f, 160f, 20f), text, centered);
            GUI.contentColor = previous;
        }

        void DrawBar(Vector3 world, float fraction, Color color, float width)
        {
            var screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f) return;
            var at = UiTheme.ToGui(screen);
            DrawBarRect(new Rect(at.x - width * 0.5f, at.y, width, 5f), fraction, color);
        }

        static void DrawBarRect(Rect rect, float fraction, Color color)
        {
            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x + 1, rect.y + 1, (rect.width - 2) * Mathf.Clamp01(fraction), rect.height - 2), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        string ToolsText()
        {
            if (loadout.Pickaxe && loadout.Crowbar) return "   Alet: Kazma, Levye";
            if (loadout.Pickaxe) return "   Alet: Kazma";
            if (loadout.Crowbar) return "   Alet: Levye";
            return "";
        }

        string BagText()
        {
            if (bag.Count == 0) return "boş";
            var text = new StringBuilder();
            for (int i = 0; i < bag.Count; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(bag[i]);
            }
            return text.ToString();
        }
    }
}
