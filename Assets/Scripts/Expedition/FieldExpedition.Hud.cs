using System.Text;
using AftershockIsle.CameraControl;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Economy;
using AftershockIsle.UI;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    /// <summary>Saha arayüzü (geçici IMGUI): panel, dünya etiketleri, etkileşim istemi, kontroller.</summary>
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
            if (!IsActive || !SideViewCamera.ViewingField || ModalMenu.IsOpen || cam == null) return;

            centered ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };
            small ??= new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            prompt ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 13, fontStyle = FontStyle.Bold };

            DrawHurtOverlay();
            DrawWorldLabels();
            DrawInteractPrompt();
            DrawPanel();
            DrawControls();
        }

        // Hasar alınca ekran kenarları kısa süre kızarır.
        void DrawHurtOverlay()
        {
            if (hurtFlash <= 0f) return;
            var previous = GUI.color;
            GUI.color = new Color(0.8f, 0f, 0f, hurtFlash * 0.6f);
            const float edge = 60f;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, edge), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, Screen.height - edge, Screen.width, edge), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, 0, edge, Screen.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(Screen.width - edge, 0, edge, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;
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
                string label = spot.Done ? "(boş)" : spot.Kind == FieldSpotKind.Rescue ? "MAHSUR KALAN" : "Ara";
                DrawLabel(spot.Position + Vector3.up * 0.9f, label, spot.Done ? Color.gray : Color.white);
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
                text = $"[{key}] Üsse dön";
            }
            else
            {
                var spot = NearestSpot();
                if (spot == null) return;
                anchor = spot.Position;
                text = spot.Kind == FieldSpotKind.Rescue ? $"[{key}] Kurtar"
                    : usedSlots >= bagSlots ? "Çanta dolu"
                    : $"[{key}] Ara";
            }

            var screen = cam.WorldToScreenPoint(anchor + Vector3.up * 1.7f);
            if (screen.z < 0f) return;
            GUI.Box(new Rect(screen.x - 60f, Screen.height - screen.y - 13f, 120f, 26f), text, prompt);
        }

        void DrawLabel(Vector3 world, string text, Color color)
        {
            var screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f) return;

            var previous = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(new Rect(screen.x - 80f, Screen.height - screen.y - 10f, 160f, 20f), text, centered);
            GUI.contentColor = previous;
        }

        void DrawBar(Vector3 world, float fraction, Color color, float width)
        {
            var screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f) return;
            DrawBarRect(new Rect(screen.x - width * 0.5f, Screen.height - screen.y, width, 5f), fraction, color);
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

        void DrawPanel()
        {
            const float width = 440f;
            var rect = new Rect((Screen.width - width) * 0.5f, 10, width, companion != null ? 130 : 112);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12));

            string place = InsideBuilding ? "   [BİNA İÇİ]" : "";
            GUILayout.Label($"SAHA: {site.name} — {explorer.DisplayName}   Kalan: {Mathf.CeilToInt(Remaining)} sn{place}", centered);
            float health = explorer.Health / explorer.MaxHealth;
            DrawBarRect(GUILayoutUtility.GetRect(width - 16f, 10f), health, Color.Lerp(Color.red, Color.green, health));
            GUILayout.Label($"Can {explorer.Health:0}/{explorer.MaxHealth:0}   Çanta {usedSlots}/{bagSlots}: {BagText()}" +
                            (RescueSecured ? "   [Kazazede kurtarıldı]" : ""), small);
            var info = CurrentWeapon;
            string ammo = info.UsesAmmo ? $"   Mermi: {(Workbench.Instance != null ? Workbench.Instance.Ammo : 0)}" : "   (sessiz, sınırsız)";
            string stance = leanSide != 0 ? "Köşeden ateş" : CrouchedLow ? "Çömelik" : crouched ? "Doğruldu (açıkta)" : "Ayakta";
            GUILayout.Label($"Silah: {info.Name}{ammo}   Duruş: {stance}{(inCover ? "  [SİPERDE]" : "")}", small);
            if (companion != null)
                GUILayout.Label(companion.IsDead ? $"Yoldaş: {companion.Survivor.DisplayName} öldü"
                    : $"Yoldaş: {companion.Survivor.DisplayName}   Can {companion.Survivor.Health:0}/{companion.Survivor.MaxHealth:0}", small);
            if (messageTimer > 0f && message != null) GUILayout.Label(message, small);

            GUILayout.EndArea();
        }

        void DrawControls()
        {
            var storage = ResourceStorage.Instance;
            int kits = storage != null ? storage.Get(ResourceType.Supplies) : 0;
            string text =
                $"{GameInput.Prompt(InputPrompt.Move)}: yürü    {GameInput.Prompt(InputPrompt.Aim)}: nişan    " +
                $"{GameInput.Prompt(InputPrompt.Fire)}: ateş    {GameInput.Prompt(InputPrompt.SwitchWeapon)}: silah    " +
                $"{GameInput.Prompt(InputPrompt.Crouch)}: çömel    {GameInput.Prompt(InputPrompt.Dodge)}: kaçış    " +
                $"{GameInput.Prompt(InputPrompt.Interact)}: etkileşim    {GameInput.Prompt(InputPrompt.Heal)}: ilk yardım ({kits} kit)    " +
                $"{GameInput.Prompt(InputPrompt.SwitchCharacter)}: sığınağa dön";
            GUI.Label(new Rect(0, Screen.height - 30, Screen.width, 24), text, centered);
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
