using System.Text;
using AftershockIsle.CameraControl;
using AftershockIsle.Core;
using AftershockIsle.Economy;
using AftershockIsle.Expedition;
using UnityEngine;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Geçici IMGUI: nodlu şehir haritası ve sığınak görünümündeyken sefer durum paneli (saha HUD'u FieldExpedition'da).
    /// Haritada lokasyona tıklamak sadece seçer; yola çıkmak ayrı onay ister (yanlışlıkla seçimi önler).
    /// Gamepad: yukarı/aşağı lokasyonlar arasında gez, A seç (odak "Yola Çık"a geçer), B vazgeç.
    /// </summary>
    public class ExpeditionWindow : MonoBehaviour
    {
        const int MapWindowId = 4301;
        // Pencere açılınca imlecin altında kalan butona yanlışlıkla basılmasın.
        const float InputGraceSeconds = 0.5f;

        Rect mapRect = new Rect(0, 0, 620, 480);
        GUIStyle wrapStyle;
        GUIStyle nodeStyle;
        GUIStyle titleStyle;
        ExpeditionStage lastStage;
        float stageOpenedAt;
        ExpeditionSite selectedSite;
        readonly MenuNav nav = new MenuNav();

        void Update()
        {
            var expeditions = ExpeditionSystem.Instance;
            if (expeditions == null || expeditions.Stage != ExpeditionStage.ChoosingSite) return;

            nav.Tick();
            if (GameInput.MenuBackPressed) expeditions.CancelMap();
        }

        void OnGUI()
        {
            var expeditions = ExpeditionSystem.Instance;
            if (expeditions == null) return;

            wrapStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
            nodeStyle ??= new GUIStyle(GUI.skin.button) { wordWrap = true, fontSize = 12 };
            titleStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14 };

            if (expeditions.Stage != lastStage)
            {
                lastStage = expeditions.Stage;
                stageOpenedAt = Time.unscaledTime;
                if (lastStage == ExpeditionStage.ChoosingSite)
                {
                    selectedSite = null;
                    nav.Reset();
                }
            }

            switch (expeditions.Stage)
            {
                case ExpeditionStage.ChoosingSite:
                    Center(ref mapRect);
                    UIBlocker.Add(mapRect);
                    GUI.Window(MapWindowId, mapRect, DrawMap, $"Şehir Haritası — kâşif: {expeditions.Explorer.DisplayName}");
                    break;
                case ExpeditionStage.Departing:
                case ExpeditionStage.Away:
                case ExpeditionStage.Returning:
                    // Saha görünümünde FieldExpedition kendi panelini çizer.
                    if (!SideViewCamera.ViewingField) DrawStatusPanel(expeditions);
                    break;
            }
        }

        /// <summary>Sağ üstte: hangi lokasyonda, ne kadar kaldı.</summary>
        void DrawStatusPanel(ExpeditionSystem expeditions)
        {
            var rect = new Rect(Screen.width - 312, 12, 300, 84);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12));

            switch (expeditions.Stage)
            {
                case ExpeditionStage.Departing:
                    GUILayout.Label("SEFER: kâşif kapıdan çıkıyor...", titleStyle);
                    break;
                case ExpeditionStage.Returning:
                    GUILayout.Label("SEFER: kâşif geri dönüyor!", titleStyle);
                    break;
                default:
                    var explorer = ExpeditionSystem.AwayExplorer;
                    var field = expeditions.Field;
                    GUILayout.Label($"SEFERDE: {explorer?.DisplayName} — {expeditions.CurrentSite?.name}", titleStyle);
                    if (explorer != null && field != null)
                        GUILayout.Label($"Can {explorer.Health:0}/{explorer.MaxHealth:0}   Çanta {field.UsedSlots}/{field.BagSlots}   Kalan {Mathf.CeilToInt(expeditions.Remaining)} sn" +
                                        (expeditions.IsRescuing ? "   [kazazede kurtarıldı]" : ""));
                    GUILayout.Label($"{GameInput.Prompt(InputPrompt.SwitchCharacter)} / Select: sahaya geç ve kâşifi yönet.");
                    break;
            }

            GUILayout.EndArea();
        }

        bool InGracePeriod => Time.unscaledTime - stageOpenedAt < InputGraceSeconds;

        void DrawMap(int id)
        {
            var expeditions = ExpeditionSystem.Instance;
            var sites = expeditions.Sites;
            nav.BeginDraw();

            var area = new Rect(10, 25, mapRect.width - 20, mapRect.height - 160);
            GUI.Box(area, GUIContent.none);

            var home = new Vector2(area.x + area.width * 0.5f, area.y + area.height * 0.5f);
            var nodeSize = new Vector2(150, 48);

            for (int i = 0; i < sites.Count; i++)
            {
                var site = sites[i];
                var center = new Vector2(area.x + area.width * site.mapPosition.x, area.y + area.height * site.mapPosition.y);
                DrawRoute(home, center);

                var rect = new Rect(center - nodeSize * 0.5f, nodeSize);
                string marker = site == selectedSite ? ">> " : "";
                var content = new GUIContent($"{marker}{site.name}\n{site.duration:0} sn");
                nav.Button(rect, content, () => SelectSite(site, sites.Count), !InGracePeriod, nodeStyle);
            }

            GUI.Box(new Rect(home - new Vector2(45, 16), new Vector2(90, 32)), "Sığınak");

            // Gamepad ile gezerken odaktaki lokasyonun bilgisi seçmeden de görünür.
            var shown = selectedSite;
            if (shown == null && GameInput.UsingGamepad && nav.Focus < sites.Count) shown = sites[nav.Focus];

            var info = new Rect(10, mapRect.height - 130, mapRect.width - 20, 90);
            GUILayout.BeginArea(info);
            if (shown == null)
            {
                GUILayout.Label("Bir lokasyon seç. Oyun bu ekranda duraklatıldı.", wrapStyle);
                GUILayout.Label($"Sefer çantası: {expeditions.BagSlots} slot (yoldaşla +1). Sahada kâşifi sen yönetirsin; yoldaş seni takip edip ateş eder ama o gün sığınakta çalışamaz. Sığınak çalışmaya devam eder.", wrapStyle);
            }
            else
            {
                GUILayout.Label(shown == selectedSite ? shown.name : $"{shown.name}   ({GameInput.Prompt(InputPrompt.Confirm)}: seç)", titleStyle);
                GUILayout.Label(Details(shown), wrapStyle);
            }
            GUILayout.EndArea();

            nav.Button(new Rect(mapRect.width - 230, mapRect.height - 32, 110, 24), new GUIContent("Yola Çık"),
                () => expeditions.Depart(selectedSite), selectedSite != null && !InGracePeriod);
            nav.Button(new Rect(mapRect.width - 112, mapRect.height - 32, 100, 24),
                new GUIContent($"Vazgeç ({GameInput.Prompt(InputPrompt.Back)})"), expeditions.CancelMap, !InGracePeriod);

            var partner = expeditions.Companion;
            bool anyone = expeditions.CompanionCandidates().Count > 0;
            string partnerText = partner != null ? $"Yoldaş: {partner.DisplayName} ({partner.Health:0} HP)"
                : anyone ? "Yoldaş: yok (seç)" : "Yoldaş: uygun kimse yok";
            nav.Button(new Rect(10, mapRect.height - 32, 260, 24), new GUIContent(partnerText),
                expeditions.CycleCompanion, anyone && !InGracePeriod);

            nav.EndDraw();
        }

        void SelectSite(ExpeditionSite site, int departIndex)
        {
            selectedSite = site;
            nav.SetFocus(departIndex);
        }

        static string Details(ExpeditionSite site)
        {
            var text = new StringBuilder(site.description);
            text.Append($"\nSaha süresi: {site.duration:0} sn.  Düşman: {site.dogs + site.raiders + site.brutes + site.throwers}+.  Ganimet: ");
            for (int i = 0; i < site.loot.Length; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(site.loot[i].type.ToDisplayName());
            }

            if (site.hazard != ExpeditionHazard.None)
                text.Append($"   Arama tuzağı: %{site.hazardChance * 100f:0} ihtimalle -{site.hazardDamage * 0.7f:0} HP");

            if (site.isRescueSite && !ExpeditionSystem.RescueVictimAvailable)
                text.Append("   (Kurtarılacak kimse yok ya da sığınak dolu.)");

            return text.ToString();
        }

        // Düğümler arası noktalı yol.
        static void DrawRoute(Vector2 from, Vector2 to)
        {
            const int dots = 14;
            var previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            for (int i = 1; i < dots; i++)
            {
                var point = Vector2.Lerp(from, to, i / (float)dots);
                GUI.DrawTexture(new Rect(point.x - 2, point.y - 2, 4, 4), Texture2D.whiteTexture);
            }
            GUI.color = previous;
        }

        static void Center(ref Rect rect)
        {
            rect.x = (Screen.width - rect.width) * 0.5f;
            rect.y = (Screen.height - rect.height) * 0.5f;
        }
    }
}
