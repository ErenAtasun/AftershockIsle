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
    /// Kapıdan sefere çıkarken ya da 'M' ile açılan tam ekran Şehir Haritası (CityMapView üstünde).
    /// Assets/Models/UI/city_map.png üzerinde Yıkık Klinik, İnşaat Sahası, Enkaz Alanı ve Sığınak taşları etkileşimlidir;
    /// çizimi olmayan lokasyonlar renkli plaka olarak eklenir. Lokasyona tıklamak seçer ve haritanın üstüne binen detay
    /// kartını açar (ganimet, risk, süre). Akış: Harita → lokasyon seç → Sefer Hazırlığı (envanter) → Yola Çık.
    /// </summary>
    public class ExpeditionWindow : MonoBehaviour
    {
        const float InputGraceSeconds = 0.4f;

        class Node
        {
            public ExpeditionSite Site;
            public Button Button;
            public Image Highlight;
            public Text Label;
            /// <summary>Haritada hazır çizimi olmayan lokasyonlarda kodla çizilen plakanın yazısı (null: görselde çizili).</summary>
            public string PlateName;
        }

        public static ExpeditionWindow Instance { get; private set; }
        public static bool IsOpen => Instance != null && ((Instance.view != null && Instance.view.IsOpen) || (Instance.loadoutPanel != null && Instance.loadoutPanel.IsOpen));

        CityMapView view;
        readonly List<Node> nodes = new List<Node>();
        Button senButton;
        Image senHighlight;

        Button partner;
        Text partnerLabel;
        Button cancel;

        ExpeditionSite selectedSite;
        bool selectedSen;
        float openedAt;
        bool inspectionMode; // Kapıdan değil de 'M' ile sadece haritayı inceleme
        bool shelterInventoryMode; // 'I' veya HUD'dan sığınak envanteri / çanta açma

        LoadoutPanel loadoutPanel;
        ExpeditionLoadout lastLoadout;
        bool loadoutPage;

        void Awake()
        {
            if (Instance == null) Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            var expeditions = ExpeditionSystem.Instance;
            bool choosing = expeditions != null && expeditions.Stage == ExpeditionStage.ChoosingSite;
            bool open = view != null && view.IsOpen;

            // 'I' tuşu ile sığınaktayken envanter & çanta hazırlığı açma/kapama
            if (GameInput.InventoryPressed && !ModalMenu.IsOpen && !PauseMenu.IsOpen && !choosing && !EveningPlan.IsOpen &&
                !AftershockIsle.CameraControl.SideViewCamera.ViewingField)
            {
                if (loadoutPanel != null && loadoutPanel.IsOpen)
                {
                    BackToMap();
                    return;
                }
                OpenShelterInventory();
                return;
            }

            // 'M' tuşu ile sığınaktayken haritayı inceleme (akşam planı açıkken harita zaten onun içinde).
            if (GameInput.MapPressed && !ModalMenu.IsOpen && !PauseMenu.IsOpen && !choosing && !EveningPlan.IsOpen &&
                !AftershockIsle.CameraControl.SideViewCamera.ViewingField && !open && !loadoutPage)
            {
                OpenInspection();
                return;
            }

            if (!choosing && !inspectionMode && !shelterInventoryMode)
            {
                if (open) view.Close();
                if (loadoutPanel != null && loadoutPanel.IsOpen) loadoutPanel.Close();
                loadoutPage = false;
                return;
            }

            if (loadoutPage)
            {
                ModalMenu.MarkOpen();
                loadoutPanel.Tick();
                if (GameInput.MenuBackPressed || (shelterInventoryMode && GameInput.InventoryPressed))
                {
                    BackToMap();
                    return;
                }
                return;
            }

            if (!open) Open(expeditions);

            view.Window.Tick(nodes.Count > 0 ? nodes[0].Button.gameObject : null);

            if (GameInput.MenuBackPressed || (inspectionMode && GameInput.MapPressed && Time.unscaledTime - openedAt > 0.1f))
            {
                CancelMap();
                return;
            }

            Refresh(expeditions);
        }

        bool InGracePeriod => Time.unscaledTime - openedAt < InputGraceSeconds;

        public void OpenInspection()
        {
            if (EveningPlan.IsOpen) return;
            inspectionMode = true;
            Open(ExpeditionSystem.Instance);
        }

        void Open(ExpeditionSystem expeditions)
        {
            if (view == null) Build(expeditions);
            // Seçili yer yokken detay kartı kapalı: harita gereksiz yer kaybetmez.
            selectedSite = null;
            selectedSen = false;
            openedAt = Time.unscaledTime;
            view.ShowDetail(false);
            view.Open();

            if (EventSystem.current != null && nodes.Count > 0)
                EventSystem.current.SetSelectedGameObject(nodes[0].Button.gameObject);
        }

        void Build(ExpeditionSystem expeditions)
        {
            view = new CityMapView("Şehir Haritası", CancelMap, true);
            view.SetSidePanel(false, 0f);
            BuildNodes(expeditions);

            partner = view.Window.FooterButton("", () => ExpeditionSystem.Instance?.CycleCompanion(), null, 360f);
            partnerLabel = partner.GetComponentInChildren<Text>();
            cancel = view.Window.FooterButton($"Kapat ({GameInput.Prompt(InputPrompt.Back)})", CancelMap, null, 200f);
        }

        void BuildNodes(ExpeditionSystem expeditions)
        {
            nodes.Clear();

            // Sığınak (SEN) taşı.
            senButton = view.AddNode("Sığınak", CityMapView.ShelterRect, null, out senHighlight, out var senLabel);
            senLabel.text = "";
            senButton.onClick.AddListener(() =>
            {
                if (InGracePeriod) return;
                selectedSen = true;
                selectedSite = null;
                AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.UiClick, 0.4f);
            });

            if (expeditions == null || expeditions.Sites == null) return;

            foreach (var site in expeditions.Sites)
            {
                var norm = CityMapView.SiteLayout(site, out var plate);
                var button = view.AddNode(site.name, norm, plate, out var highlight, out var label);
                string plateName = plate.HasValue ? site.name : null;
                label.text = plateName ?? "";

                var current = site;
                button.onClick.AddListener(() => SelectSite(current));
                nodes.Add(new Node { Site = current, Button = button, Highlight = highlight, Label = label, PlateName = plateName });
            }
        }

        public void OpenShelterInventory()
        {
            if (EveningPlan.IsOpen || AftershockIsle.CameraControl.SideViewCamera.ViewingField) return;
            if (loadoutPanel != null && loadoutPanel.IsOpen)
            {
                BackToMap();
                return;
            }

            shelterInventoryMode = true;
            inspectionMode = false;
            selectedSite = null;
            loadoutPanel ??= new LoadoutPanel(BackToMap, Depart);
            loadoutPage = true;
            if (view != null && view.IsOpen) view.Close();
            loadoutPanel.Open(null, lastLoadout);
        }

        void OpenLoadout()
        {
            if (InGracePeriod || inspectionMode || selectedSite == null) return;
            var expeditions = ExpeditionSystem.Instance;
            if (expeditions != null && !selectedSite.IsUnlocked(expeditions.Day)) return;

            shelterInventoryMode = false;
            loadoutPanel ??= new LoadoutPanel(BackToMap, Depart);
            loadoutPage = true;
            if (view != null && view.IsOpen) view.Close();
            loadoutPanel.Open(selectedSite, lastLoadout);
        }

        void BackToMap()
        {
            loadoutPage = false;
            loadoutPanel?.Close();
            if (shelterInventoryMode)
            {
                shelterInventoryMode = false;
                return;
            }
            if (selectedSite != null && view != null)
            {
                openedAt = Time.unscaledTime;
                view.Open();
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(view.DetailAction.gameObject);
            }
        }

        void Depart(ExpeditionLoadout loadout)
        {
            lastLoadout = loadout;
            loadoutPage = false;
            bool wasShelter = shelterInventoryMode;
            shelterInventoryMode = false;
            loadoutPanel?.Close();
            if (!wasShelter && selectedSite != null)
            {
                ExpeditionSystem.Instance?.Depart(selectedSite, loadout);
            }
        }

        void CancelMap()
        {
            if (inspectionMode)
            {
                inspectionMode = false;
                view?.Close();
                return;
            }
            if (ExpeditionSystem.Instance != null) ExpeditionSystem.Instance.CancelMap();
        }

        void SelectSite(ExpeditionSite site)
        {
            if (InGracePeriod) return;
            selectedSen = false;
            selectedSite = site;
            var expeditions = ExpeditionSystem.Instance;
            bool locked = expeditions != null && !site.IsUnlocked(expeditions.Day);
            AftershockIsle.Effects.SoundManager.Play(locked ? AftershockIsle.Effects.SoundId.EmptyClick : AftershockIsle.Effects.SoundId.UiClick, 0.5f);
            if (!locked && !inspectionMode && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(view.DetailAction.gameObject);
        }

        void Refresh(ExpeditionSystem expeditions)
        {
            bool grace = InGracePeriod;
            int currentDay = expeditions != null ? expeditions.Day : 1;
            string explorerName = expeditions != null && expeditions.Explorer != null ? expeditions.Explorer.DisplayName : "Henüz Seçilmedi";

            view.Window.Title.text = inspectionMode
                ? $"ŞEHİR KEŞİF HARİTASI   <size=16><color=#cfcfc8>(İnceleme · Gün {currentDay} · sefere kapıdan çıkılır)</color></size>"
                : $"ŞEHİR HARİTASI   <size=17><color=#cfcfc8>kâşif: {explorerName}</color></size>";

            float pulse = CityMapView.Pulse;
            if (senHighlight != null)
                senHighlight.color = selectedSen ? new Color(0.35f, 0.65f, 1f, 0.85f * pulse) : new Color(0.2f, 0.5f, 0.9f, 0.15f);

            foreach (var node in nodes)
            {
                bool locked = !node.Site.IsUnlocked(currentDay);
                bool isSel = node.Site == selectedSite && !selectedSen;
                node.Highlight.color = isSel ? new Color(1f, 0.85f, 0.25f, 0.9f * pulse)
                    : locked ? new Color(0.1f, 0.1f, 0.1f, 0.5f)
                    : new Color(1f, 1f, 1f, 0.12f);

                string lockText = $"Gün {node.Site.unlockDay}";
                node.Label.text = node.PlateName != null
                    ? (locked ? $"{node.PlateName}\n<size=13>{lockText}'te açılır</size>" : node.PlateName)
                    : (locked ? lockText : "");
                node.Button.interactable = !grace;
            }

            if (selectedSen) ShowShelter(currentDay);
            else if (selectedSite != null) ShowSite(selectedSite, currentDay, grace);
            else
            {
                view.ShowDetail(false);
                view.SetBanner(inspectionMode ? null : "Sefere çıkmak için haritadan bir bölge seç");
            }

            var companion = expeditions != null ? expeditions.Companion : null;
            bool anyone = expeditions != null && expeditions.CompanionCandidates().Count > 0;
            partner.gameObject.SetActive(!inspectionMode);
            partnerLabel.text = companion != null ? $"Yoldaş: {companion.DisplayName} ({companion.Health:0} HP, +{ExpeditionLoadout.CompanionCapacity} kg)"
                : anyone ? "Yoldaş: Yok (Değiştir)" : "Yoldaş: Uygun kimse yok";
            partner.interactable = anyone && !grace;
            cancel.interactable = !grace;
        }

        void ShowShelter(int currentDay)
        {
            view.SetBanner(null);
            view.ShowDetail(true);
            view.DetailTitle.text = "SIĞINAK (MERKEZ)";
            view.DetailBadge.text = "GÜVENLİ BÖLGE · ANA ÜS";
            view.DetailText.text =
                "Tüm kazazedelerin yaşadığı, çöken çok katlı bina. Çatıda telsiz kulesi ve tahliye alanı bulunur.\n\n" +
                $"• Güncel Gün: <b>{currentDay}. Gün</b>\n• Çıkış Kapısı: Zemin Kat\n\n" +
                "Sefere çıkmak için haritadaki dış bölgelerden birine tıkla. Dış bölgelerden erzak, hurda ve metal toplayıp sığınağa getir.";
            view.SetDetailAction(null, null);
        }

        void ShowSite(ExpeditionSite site, int currentDay, bool grace)
        {
            view.SetBanner(null);
            view.ShowDetail(true);
            bool locked = !site.IsUnlocked(currentDay);
            view.DetailTitle.text = site.name.ToUpperInvariant();
            view.DetailBadge.text = locked ? $"<color=#ff7560>KİLİTLİ · GÜN {site.unlockDay}'TE AÇILIR</color>" : "<color=#80e575>AÇIK KEŞİF BÖLGESİ</color>";

            var sb = new StringBuilder();
            sb.Append(site.description).Append("\n\n");
            int enemies = site.dogs + site.raiders + site.brutes + site.throwers;
            sb.Append($"• Saha Süresi: <b>{site.duration:0} sn</b>\n");
            sb.Append($"• Arama Noktası: <b>{site.lootSpots}</b>\n");
            sb.Append($"• Tahmini Tehdit: <b>{enemies}+ Yağmacı/Köpek</b>\n\n");

            sb.Append("<color=#ffe680>Olası Ganimet: ");
            if (site.loot != null)
                for (int i = 0; i < site.loot.Length; i++)
                    sb.Append(i > 0 ? ", " : "").Append($"<b>{site.loot[i].type.ToDisplayName()}</b>");
            sb.Append("</color>\n");

            if (site.hazard != ExpeditionHazard.None)
                sb.Append($"\n<color=#ff8073>Tehlike: %{site.hazardChance * 100f:0} ihtimalle {HazardName(site.hazard)} (-{site.hazardDamage * 0.7f:0} HP)</color>");
            if (site.LockedCrates + site.BuriedCaches > 0)
                sb.Append($"\n<color=#ff8073>Özel: {site.LockedCrates} Kilitli Kasa (Levye) · {site.BuriedCaches} Gömülü Sandık (Kazma)</color>");
            view.DetailText.text = sb.ToString();

            if (inspectionMode) view.SetDetailAction(null, null);
            else
            {
                view.SetDetailAction(locked ? "Kilitli" : "Sefer Hazırlığı  →", OpenLoadout);
                view.DetailAction.interactable = !locked && !grace;
            }
        }

        static string HazardName(ExpeditionHazard hazard) => hazard switch
        {
            ExpeditionHazard.Fire => "yangın",
            ExpeditionHazard.Collapse => "göçük",
            _ => "tuzak",
        };
    }
}
