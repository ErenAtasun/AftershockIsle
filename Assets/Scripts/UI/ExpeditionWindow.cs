using System.Text;
using AftershockIsle.Economy;
using AftershockIsle.Expedition;
using AftershockIsle.Skills;
using UnityEngine;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Geçici IMGUI: nodlu şehir haritası, Enkaz Alanı kurtarma kararı ve sefer durum paneli.
    /// Haritada lokasyona tıklamak sadece seçer; yola çıkmak ayrı onay ister (yanlışlıkla seçimi önler).
    /// </summary>
    public class ExpeditionWindow : MonoBehaviour
    {
        const int MapWindowId = 4301;
        const int RescueWindowId = 4302;
        // Pencere açılınca imlecin altında kalan butona yanlışlıkla basılmasın.
        const float InputGraceSeconds = 0.5f;

        Rect mapRect = new Rect(0, 0, 620, 480);
        Rect rescueRect = new Rect(0, 0, 440, 190);
        GUIStyle wrapStyle;
        GUIStyle nodeStyle;
        GUIStyle titleStyle;
        ExpeditionStage lastStage;
        float stageOpenedAt;
        ExpeditionSite selectedSite;

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
                if (lastStage == ExpeditionStage.ChoosingSite) selectedSite = null;
            }

            switch (expeditions.Stage)
            {
                case ExpeditionStage.ChoosingSite:
                    Center(ref mapRect);
                    UIBlocker.Add(mapRect);
                    GUI.Window(MapWindowId, mapRect, DrawMap, $"Şehir Haritası — kâşif: {expeditions.Explorer.DisplayName}");
                    break;
                case ExpeditionStage.RescueDecision:
                    Center(ref rescueRect);
                    UIBlocker.Add(rescueRect);
                    GUI.Window(RescueWindowId, rescueRect, DrawRescue, "Enkaz Alanı — Karar Anı");
                    DrawStatusPanel(expeditions);
                    break;
                case ExpeditionStage.Departing:
                case ExpeditionStage.Away:
                case ExpeditionStage.Returning:
                    DrawStatusPanel(expeditions);
                    break;
            }
        }

        /// <summary>Sağ üstte: hangi lokasyonda, ne kadar kaldı.</summary>
        void DrawStatusPanel(ExpeditionSystem expeditions)
        {
            var rect = new Rect(Screen.width - 312, 12, 300, 92);
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
                    GUILayout.Label($"SEFERDE: {explorer?.DisplayName}", titleStyle);
                    GUILayout.Label($"{expeditions.CurrentSite?.name} — kalan {Mathf.CeilToInt(expeditions.Remaining)} sn" +
                                    (expeditions.IsRescuing ? " (kazazede çıkarılıyor)" : ""));
                    var bar = GUILayoutUtility.GetRect(rect.width - 16, 10);
                    GUI.Box(bar, GUIContent.none);
                    GUI.DrawTexture(new Rect(bar.x + 1, bar.y + 1, (bar.width - 2) * expeditions.Progress01, bar.height - 2), Texture2D.whiteTexture);
                    break;
            }

            GUILayout.EndArea();
        }

        bool InGracePeriod => Time.unscaledTime - stageOpenedAt < InputGraceSeconds;

        // IMGUI butonları sağ tıka da tepki verir; sadece sol tık kabul edilir.
        bool LeftClickButton(Rect rect, GUIContent content, GUIStyle style)
        {
            bool isLeft = Event.current.button == 0;
            return GUI.Button(rect, content, style) && isLeft && !InGracePeriod;
        }

        bool LeftClickLayoutButton(string text)
        {
            bool isLeft = Event.current.button == 0;
            return GUILayout.Button(text) && isLeft && !InGracePeriod;
        }

        void DrawMap(int id)
        {
            var expeditions = ExpeditionSystem.Instance;
            var area = new Rect(10, 25, mapRect.width - 20, mapRect.height - 160);
            GUI.Box(area, GUIContent.none);

            var home = new Vector2(area.x + area.width * 0.5f, area.y + area.height * 0.5f);
            var nodeSize = new Vector2(150, 48);

            foreach (var site in expeditions.Sites)
            {
                var center = new Vector2(area.x + area.width * site.mapPosition.x, area.y + area.height * site.mapPosition.y);
                DrawRoute(home, center);

                var rect = new Rect(center - nodeSize * 0.5f, nodeSize);
                string marker = site == selectedSite ? ">> " : "";
                var content = new GUIContent($"{marker}{site.name}\n{site.duration:0} sn");
                if (LeftClickButton(rect, content, nodeStyle)) selectedSite = site;
            }

            GUI.Box(new Rect(home - new Vector2(45, 16), new Vector2(90, 32)), "Sığınak");

            var info = new Rect(10, mapRect.height - 130, mapRect.width - 20, 90);
            GUILayout.BeginArea(info);
            if (selectedSite == null)
            {
                GUILayout.Label("Bir lokasyon seç. Oyun bu ekranda duraklatıldı.", wrapStyle);
                GUILayout.Label($"Sefer çantası: {expeditions.BagSlots} slot. Dönüşte çanta otomatik olarak sandığa boşaltılır.", wrapStyle);
            }
            else
            {
                GUILayout.Label(selectedSite.name, titleStyle);
                GUILayout.Label(Details(selectedSite), wrapStyle);
            }
            GUILayout.EndArea();

            GUI.enabled = selectedSite != null;
            if (LeftClickButton(new Rect(mapRect.width - 230, mapRect.height - 32, 110, 24), new GUIContent("Yola Çık"), GUI.skin.button))
            {
                GUI.enabled = true;
                expeditions.Depart(selectedSite);
                return;
            }
            GUI.enabled = true;

            if (LeftClickButton(new Rect(mapRect.width - 112, mapRect.height - 32, 100, 24), new GUIContent("Vazgeç"), GUI.skin.button))
                expeditions.CancelMap();
        }

        void DrawRescue(int id)
        {
            var expeditions = ExpeditionSystem.Instance;
            float extraction = SkillTree.Has(SkillId.LifeSaver) ? 10f : 20f;

            GUILayout.Label(
                $"{expeditions.Explorer.DisplayName} enkaz altında mahsur kalmış birini buldu.\n\n" +
                $"Kurtarırsan: +1 iş gücü ve yeni bir yetenek dalı, ama gece tüketimi +1 Yaşam Kiti artar. " +
                $"Çıkarma seferi {extraction:0} sn uzatır.", wrapStyle);

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (LeftClickLayoutButton("Kurtar")) expeditions.DecideRescue(true);
            if (LeftClickLayoutButton("Bırak")) expeditions.DecideRescue(false);
            GUILayout.EndHorizontal();
        }

        static string Details(ExpeditionSite site)
        {
            var text = new StringBuilder(site.description);
            text.Append($"\nSüre: {site.duration:0} sn.  Ganimet: ");
            for (int i = 0; i < site.loot.Length; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(site.loot[i].type.ToDisplayName());
            }

            if (site.hazard != ExpeditionHazard.None)
                text.Append($"   Risk: %{site.hazardChance * 100f:0} ihtimalle -{site.hazardDamage:0} HP");

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
