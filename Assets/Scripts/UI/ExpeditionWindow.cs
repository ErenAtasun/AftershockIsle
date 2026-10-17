using System.Collections.Generic;
using System.Text;
using AftershockIsle.Core;
using AftershockIsle.Economy;
using AftershockIsle.Expedition;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Kapıdan sefere çıkarken açılan şehir haritası (uGUI). Lokasyona basmak sadece seçer; yola çıkmak ayrı onay ister
    /// (yanlışlıkla seçimi önler). Gamepad ile odaktaki lokasyonun bilgisi seçmeden de görünür. B vazgeçer.
    /// Seferdeki durum (kalan süre, can, çanta) oyun göstergesinde (GameHud) gösterilir.
    /// </summary>
    public class ExpeditionWindow : MonoBehaviour
    {
        // Pencere açılınca imlecin altında kalan butona yanlışlıkla basılmasın.
        const float InputGraceSeconds = 0.5f;
        static readonly Vector2 MapSize = new Vector2(900f, 360f);
        static readonly Vector2 NodeSize = new Vector2(210f, 64f);

        class Node
        {
            public ExpeditionSite Site;
            public Button Button;
            public Image Plate;
        }

        UiWindow window;
        readonly List<Node> nodes = new List<Node>();
        Text info;
        Button depart;
        Button cancel;
        Button partner;
        Text partnerLabel;
        ExpeditionSite selectedSite;
        float openedAt;

        void Update()
        {
            var expeditions = ExpeditionSystem.Instance;
            bool choosing = expeditions != null && expeditions.Stage == ExpeditionStage.ChoosingSite;

            if (choosing && (window == null || !window.IsOpen)) Open(expeditions);
            else if (!choosing && window != null && window.IsOpen) window.Show(false);
            if (!choosing) return;

            window.Tick(nodes.Count > 0 ? nodes[0].Button.gameObject : null);
            if (GameInput.MenuBackPressed)
            {
                expeditions.CancelMap();
                return;
            }
            Refresh(expeditions);
        }

        bool InGracePeriod => Time.unscaledTime - openedAt < InputGraceSeconds;

        void Open(ExpeditionSystem expeditions)
        {
            if (window == null) Build(expeditions);
            selectedSite = null;
            openedAt = Time.unscaledTime;
            window.Show(true);
            if (EventSystem.current != null && nodes.Count > 0) EventSystem.current.SetSelectedGameObject(nodes[0].Button.gameObject);
        }

        void Build(ExpeditionSystem expeditions)
        {
            window = new UiWindow("Şehir Haritası", new Vector2(980f, 720f), CancelMap, false);

            var map = UiFactory.Image(window.Content, "Harita", null, new Color(0.07f, 0.08f, 0.1f, 0.95f), true);
            UiFactory.Place(map.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -4f), MapSize);

            var home = MapPoint(new Vector2(0.5f, 0.5f));
            foreach (var site in expeditions.Sites)
            {
                var center = MapPoint(site.mapPosition);
                DrawRoute(map.rectTransform, home, center);

                var button = UiFactory.TextButton(map.rectTransform, $"{site.name}\n<size=14>{site.duration:0} sn</size>", NodeSize,
                    () => SelectSite(site), UiFactory.SlateLight, 17);
                UiFactory.Place((RectTransform)button.transform, new Vector2(0.5f, 0.5f), center, NodeSize);
                nodes.Add(new Node { Site = site, Button = button, Plate = button.GetComponent<Image>() });
            }

            var shelter = UiFactory.Image(map.rectTransform, "Sığınak", null, UiFactory.Accent);
            UiFactory.Place(shelter.rectTransform, new Vector2(0.5f, 0.5f), home, new Vector2(120f, 40f));
            var shelterLabel = UiFactory.Label(shelter.transform, "SIĞINAK", 16, UiFactory.Ink, TextAnchor.MiddleCenter, FontStyle.Bold, false);
            UiFactory.Stretch(shelterLabel.rectTransform);

            info = UiFactory.Label(window.Content, "", 16, UiFactory.Text, TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(info.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -MapSize.y - 18f), new Vector2(MapSize.x, 170f));

            partner = window.FooterButton("", () => ExpeditionSystem.Instance.CycleCompanion(), null, 330f);
            partnerLabel = partner.GetComponentInChildren<Text>();
            var spacer = UiFactory.Rect("Boşluk", window.Footer);
            spacer.sizeDelta = new Vector2(170f, 10f);
            depart = window.FooterButton("Yola Çık", () => ExpeditionSystem.Instance.Depart(selectedSite), UiFactory.Good, 190f);
            cancel = window.FooterButton($"Vazgeç ({GameInput.Prompt(InputPrompt.Back)})", CancelMap, null, 190f);
        }

        static void CancelMap()
        {
            if (ExpeditionSystem.Instance != null) ExpeditionSystem.Instance.CancelMap();
        }

        // Harita koordinatı (0–1, y aşağı) → harita plakasının merkezine göre yerel konum.
        static Vector2 MapPoint(Vector2 normalized) =>
            new Vector2((normalized.x - 0.5f) * MapSize.x, (0.5f - normalized.y) * MapSize.y);

        static void DrawRoute(RectTransform parent, Vector2 from, Vector2 to)
        {
            const int dots = 16;
            for (int i = 1; i < dots; i++)
            {
                var dot = UiFactory.Image(parent, "Yol", UiFactory.Circle, new Color(1f, 1f, 1f, 0.3f));
                dot.type = Image.Type.Simple;
                UiFactory.Place(dot.rectTransform, new Vector2(0.5f, 0.5f), Vector2.Lerp(from, to, i / (float)dots), new Vector2(7f, 7f));
            }
        }

        void SelectSite(ExpeditionSite site)
        {
            if (InGracePeriod) return;
            selectedSite = site;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(depart.gameObject);
        }

        void Refresh(ExpeditionSystem expeditions)
        {
            bool grace = InGracePeriod;
            window.Title.text = $"ŞEHİR HARİTASI   <size=16><color=#cfcfc8>kâşif: {expeditions.Explorer.DisplayName}</color></size>";

            // Gamepad ile odaktaki lokasyon seçmeden de anlatılır.
            var shown = selectedSite;
            var focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            foreach (var node in nodes)
            {
                node.Plate.color = node.Site == selectedSite ? UiFactory.Accent : UiFactory.SlateLight;
                node.Button.GetComponentInChildren<Text>().color = node.Site == selectedSite ? UiFactory.Ink : UiFactory.Text;
                if (shown == null && focused == node.Button.gameObject) shown = node.Site;
            }

            if (shown == null)
                info.text = "Bir lokasyon seç. Oyun bu ekranda duraklatıldı.\n" +
                            $"Sefer çantası: {expeditions.BagSlots} slot (yoldaşla +1). Sahada kâşifi sen yönetirsin; yoldaş seni takip edip ateş eder " +
                            "ama o gün sığınakta çalışamaz.";
            else
                info.text = $"<b><size=19>{shown.name}</size></b>{(shown == selectedSite ? "" : $"   <color=#cfcfc8>({GameInput.Prompt(InputPrompt.Confirm)}: seç)</color>")}\n{Details(shown)}";

            var companion = expeditions.Companion;
            bool anyone = expeditions.CompanionCandidates().Count > 0;
            partnerLabel.text = companion != null ? $"Yoldaş: {companion.DisplayName} ({companion.Health:0} HP)"
                : anyone ? "Yoldaş: yok (değiştir)" : "Yoldaş: uygun kimse yok";

            partner.interactable = anyone && !grace;
            depart.interactable = selectedSite != null && !grace;
            cancel.interactable = !grace;
            foreach (var node in nodes) node.Button.interactable = !grace;
        }

        static string Details(ExpeditionSite site)
        {
            var text = new StringBuilder(site.description);
            text.Append($"\nSaha süresi: {site.duration:0} sn   ·   Düşman: {site.dogs + site.raiders + site.brutes + site.throwers}+   ·   Ganimet: ");
            for (int i = 0; i < site.loot.Length; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(site.loot[i].type.ToDisplayName());
            }

            if (site.hazard != ExpeditionHazard.None)
                text.Append($"\nArama tuzağı: %{site.hazardChance * 100f:0} ihtimalle -{site.hazardDamage * 0.7f:0} HP");

            if (site.isRescueSite && !ExpeditionSystem.RescueVictimAvailable)
                text.Append("\n<color=#ff9a80>Kurtarılacak kimse yok ya da sığınak dolu.</color>");

            return text.ToString();
        }
    }
}
