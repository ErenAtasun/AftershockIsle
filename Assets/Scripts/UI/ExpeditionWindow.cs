using System.Collections.Generic;
using System.Text;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Economy;
using AftershockIsle.Expedition;
using AftershockIsle.Skills;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Kapıdan sefere çıkarken ya da 'M' ile açılan tam ekran Şehir Haritası (CityMapView üstünde).
    /// Solda EKİP paneli: kim kâşif, kim yoldaş, kim sığınakta kalıyor (yoldaş satırdaki düğmeyle seçilir).
    /// Haritada Yıkık Klinik, İnşaat Sahası, Enkaz Alanı ve Sığınak taşları etkileşimlidir; çizimi olmayan lokasyonlar
    /// taş görünümlü levha olarak eklenir. Lokasyona tıklamak seçer ve sağdaki detay kartını açar (ganimet, risk, süre).
    /// Akış: Harita → lokasyon seç → Sefer Hazırlığı (envanter) → Yola Çık.
    /// </summary>
    public class ExpeditionWindow : MonoBehaviour
    {
        const float InputGraceSeconds = 0.4f;
        const float RosterWidth = 420f;

        class Node
        {
            public ExpeditionSite Site;
            public Button Button;
            public Image Highlight;
            public Text Label;
            public UiHoverFlag Hover;
            public Color? Plate;
            public Image Backing;
            /// <summary>Haritada hazır çizimi olmayan lokasyonlarda kodla çizilen levhanın yazısı (null: görselde çizili).</summary>
            public string PlateName;
            public Text LockBadge;
            public GameObject LockPlate;
        }

        class RosterRow
        {
            public Survivor Survivor;
            public GameObject Root;
            public Text Info;
            public Button Role;
            public Text RoleLabel;
            public Image RolePlate;
        }

        public static ExpeditionWindow Instance { get; private set; }
        public static bool IsOpen => Instance != null && ((Instance.view != null && Instance.view.IsOpen) || (Instance.loadoutPanel != null && Instance.loadoutPanel.IsOpen));

        CityMapView view;
        readonly List<Node> nodes = new List<Node>();
        Button senButton;
        Image senHighlight;
        UiHoverFlag senHover;

        RectTransform rosterContent;
        readonly List<RosterRow> rosterRows = new List<RosterRow>();
        Text rosterFooter;
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
            BuildRoster();
            view.ShowDetail(false);
            view.Open();

            if (EventSystem.current != null && nodes.Count > 0)
                EventSystem.current.SetSelectedGameObject(nodes[0].Button.gameObject);
        }

        void Build(ExpeditionSystem expeditions)
        {
            view = new CityMapView("Şehir Haritası", CancelMap, true);
            view.SetSidePanel(true, RosterWidth);
            BuildRosterSkeleton();
            BuildNodes(expeditions);

            cancel = view.Window.FooterButton($"Kapat ({GameInput.Prompt(InputPrompt.Back)})", CancelMap, null, 220f);
        }

        // ---------------------------------------------------------------- Harita düğümleri

        void BuildNodes(ExpeditionSystem expeditions)
        {
            nodes.Clear();

            // Sığınak (SEN) taşı.
            senButton = view.AddNode("Sığınak", CityMapView.ShelterRect, null, out senHighlight, out var senLabel);
            senLabel.text = "";
            senHover = senButton.GetComponent<UiHoverFlag>();
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
                string plateName = plate.HasValue ? site.name.ToUpperInvariant() : null;
                label.text = plateName ?? "";

                var lockText = CityMapView.AddBadge(button, out var lockPlate, new Color(0.35f, 0.08f, 0.06f, 0.95f));
                lockText.color = new Color(1f, 0.82f, 0.7f);

                var current = site;
                button.onClick.AddListener(() => SelectSite(current));
                nodes.Add(new Node
                {
                    Site = current,
                    Button = button,
                    Highlight = highlight,
                    Label = label,
                    Hover = button.GetComponent<UiHoverFlag>(),
                    Plate = plate,
                    Backing = button.GetComponent<Image>(),
                    PlateName = plateName,
                    LockBadge = lockText,
                    LockPlate = lockPlate,
                });
            }
        }

        // ---------------------------------------------------------------- Ekip paneli

        void BuildRosterSkeleton()
        {
            var side = view.SidePanel;

            var header = UiFactory.Label(side, "EKİP", 22, UiFactory.Accent, TextAnchor.UpperLeft);
            UiFactory.Place(header.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -14f), new Vector2(RosterWidth - 36f, 30f));
            var sub = UiFactory.Label(side, "Kim sefere çıkıyor, kim sığınakta kalıyor? Yoldaş seçmek için satırdaki düğmeye bas.", 14,
                new Color(0.8f, 0.8f, 0.76f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(sub.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -46f), new Vector2(RosterWidth - 36f, 42f));

            var list = UiFactory.Rect("EkipListesi", side);
            list.anchorMin = new Vector2(0f, 0f);
            list.anchorMax = new Vector2(1f, 1f);
            list.offsetMin = new Vector2(12f, 150f);
            list.offsetMax = new Vector2(-12f, -96f);
            var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperCenter;
            rosterContent = list;

            var card = UiFactory.Panel(side, "EkipOzeti", new Color(0.06f, 0.07f, 0.09f, 0.98f));
            UiFactory.Place(card.rectTransform, new Vector2(0f, 0f), new Vector2(12f, 12f), new Vector2(RosterWidth - 24f, 128f));
            rosterFooter = UiFactory.Label(card.transform, "", 14, new Color(0.88f, 0.88f, 0.84f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Stretch(rosterFooter.rectTransform, 12f);
        }

        void BuildRoster()
        {
            foreach (var row in rosterRows)
                if (row.Root != null) Destroy(row.Root);
            rosterRows.Clear();

            var people = new List<Survivor>();
            foreach (var survivor in Survivor.All)
                if (survivor != null && !survivor.IsDead) people.Add(survivor);
            if (ExpeditionSystem.AwayExplorer != null && !people.Contains(ExpeditionSystem.AwayExplorer)) people.Add(ExpeditionSystem.AwayExplorer);
            if (ExpeditionSystem.AwayCompanion != null && !people.Contains(ExpeditionSystem.AwayCompanion)) people.Add(ExpeditionSystem.AwayCompanion);

            foreach (var survivor in people)
            {
                var plate = UiFactory.Panel(rosterContent, survivor.DisplayName, new Color(0.15f, 0.17f, 0.22f, 0.98f));
                var element = plate.gameObject.AddComponent<LayoutElement>();
                element.preferredHeight = 74f;
                element.minHeight = 74f;

                var name = UiFactory.Label(plate.transform, survivor.DisplayName, 18, UiFactory.Text, TextAnchor.UpperLeft);
                name.rectTransform.anchorMin = new Vector2(0f, 1f);
                name.rectTransform.anchorMax = new Vector2(1f, 1f);
                name.rectTransform.offsetMin = new Vector2(14f, -34f);
                name.rectTransform.offsetMax = new Vector2(-160f, -8f);

                var info = UiFactory.Label(plate.transform, "", 13, new Color(0.78f, 0.8f, 0.84f), TextAnchor.UpperLeft, FontStyle.Normal, false);
                info.rectTransform.anchorMin = new Vector2(0f, 0f);
                info.rectTransform.anchorMax = new Vector2(1f, 0f);
                info.rectTransform.offsetMin = new Vector2(14f, 8f);
                info.rectTransform.offsetMax = new Vector2(-160f, 34f);

                var current = survivor;
                var role = UiFactory.TextButton(plate.transform, "", new Vector2(140f, 44f), () => OnRoleClicked(current), UiFactory.SlateLight, 15);
                UiFactory.Place((RectTransform)role.transform, new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(140f, 44f));
                var press = role.GetComponent<UiPress>();
                if (press != null) press.ScaleOnHover = false;

                rosterRows.Add(new RosterRow
                {
                    Survivor = survivor,
                    Root = plate.gameObject,
                    Info = info,
                    Role = role,
                    RoleLabel = role.GetComponentInChildren<Text>(),
                    RolePlate = role.GetComponent<Image>(),
                });
            }
        }

        void OnRoleClicked(Survivor survivor)
        {
            var expeditions = ExpeditionSystem.Instance;
            if (InGracePeriod || inspectionMode || expeditions == null || expeditions.Stage != ExpeditionStage.ChoosingSite) return;
            if (survivor == expeditions.Explorer) return;

            if (expeditions.Companion == survivor) expeditions.SetCompanion(null);
            else if (expeditions.CompanionCandidates().Contains(survivor)) expeditions.SetCompanion(survivor);
            else return;
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.UiClick, 0.5f);
        }

        void RefreshRoster(ExpeditionSystem expeditions)
        {
            bool choosing = expeditions != null && expeditions.Stage == ExpeditionStage.ChoosingSite;
            var candidates = choosing ? expeditions.CompanionCandidates() : null;

            foreach (var row in rosterRows)
            {
                var survivor = row.Survivor;
                if (survivor == null) continue;

                string stress = survivor.Condition.Stress >= 60f ? $"<color=#ff8f7a>Stres {survivor.Condition.Stress:0}</color>" : $"Stres {survivor.Condition.Stress:0}";
                string health = survivor.Health < survivor.MaxHealth * 0.5f ? $"<color=#ff8f7a>Can {survivor.Health:0}</color>" : $"Can {survivor.Health:0}";
                row.Info.text = $"{survivor.Branch.ToDisplayName()}  ·  {health}  ·  {stress}";

                string label;
                Color color;
                bool interactable = false;
                bool away = survivor == ExpeditionSystem.AwayExplorer || survivor == ExpeditionSystem.AwayCompanion || !survivor.isActiveAndEnabled;

                if (choosing && survivor == expeditions.Explorer)
                {
                    label = "KÂŞİF";
                    color = new Color(0.85f, 0.62f, 0.15f, 1f);
                }
                else if (choosing && survivor == expeditions.Companion)
                {
                    label = "YOLDAŞ";
                    color = UiFactory.Good;
                    interactable = !inspectionMode;
                }
                else if (away)
                {
                    label = "SEFERDE";
                    color = new Color(0.25f, 0.4f, 0.62f, 1f);
                }
                else if (choosing && candidates.Contains(survivor))
                {
                    label = "Sığınakta";
                    color = UiFactory.SlateLight;
                    interactable = !inspectionMode;
                }
                else
                {
                    label = survivor.IsTrapped ? "Mahsur" : "Sığınakta";
                    color = new Color(0.2f, 0.21f, 0.25f, 1f);
                }

                row.RoleLabel.text = label;
                row.RolePlate.color = color;
                row.Role.interactable = interactable;
            }

            var companion = choosing ? expeditions.Companion : null;
            int capacity = ExpeditionLoadout.Capacity(companion != null);
            var footer = new StringBuilder();
            if (choosing)
            {
                footer.Append($"Kâşif: <b>{expeditions.Explorer?.DisplayName}</b>");
                footer.Append(companion != null ? $"   Yoldaş: <b>{companion.DisplayName}</b>" : "   Yoldaş: yok");
                footer.Append($"\nÇanta kapasitesi: <b>{capacity} kg</b>{(companion == null ? $"  (yoldaşla +{ExpeditionLoadout.CompanionCapacity} kg)" : "")}");
                footer.Append("\n<color=#a8a8a0>Sığınakta kalanlar işlere devam eder. Gece nöbetçisi akşam planında seçilir.</color>");
            }
            else
            {
                footer.Append("Harita inceleme: sefere çıkmak için bir kazazedeyi sığınağın kapısına gönder (öğle vakti).");
                footer.Append("\n<color=#a8a8a0>Gece nöbetçisi ve gece keşfi akşam planında seçilir.</color>");
            }
            rosterFooter.text = footer.ToString();
        }

        // ---------------------------------------------------------------- Envanter / sefer hazırlığı

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
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.InventoryOpen, 0.8f, 0.02f);
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
            // Geri dönerken seçilen donanım kaybolmasın (sığınak envanteri ve haritaya dönüş).
            if (loadoutPanel != null && loadoutPanel.Loadout != null) lastLoadout = loadoutPanel.Loadout;
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

        // ---------------------------------------------------------------- Görünüm

        static Color Grey(Color color)
        {
            float lum = color.r * 0.3f + color.g * 0.59f + color.b * 0.11f;
            return new Color(lum * 0.75f, lum * 0.75f, lum * 0.78f, 1f);
        }

        void Refresh(ExpeditionSystem expeditions)
        {
            bool grace = InGracePeriod;
            int currentDay = expeditions != null ? expeditions.Day : 1;
            string explorerName = expeditions != null && expeditions.Explorer != null ? expeditions.Explorer.DisplayName : "—";

            view.Window.Title.text = inspectionMode
                ? $"ŞEHİR HARİTASI   <size=16><color=#cfcfc8>inceleme · Gün {currentDay}</color></size>"
                : $"ŞEHİR HARİTASI   <size=16><color=#cfcfc8>kâşif: {explorerName} · Gün {currentDay}</color></size>";

            float pulse = CityMapView.Pulse;
            bool senHovered = senHover != null && senHover.Hovered;
            if (senHighlight != null)
                senHighlight.color = selectedSen ? new Color(0.45f, 0.75f, 1f, pulse) : senHovered ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0f);

            foreach (var node in nodes)
            {
                bool locked = !node.Site.IsUnlocked(currentDay);
                bool isSel = node.Site == selectedSite && !selectedSen;
                bool hovered = node.Hover != null && node.Hover.Hovered;

                // Vurgu yalnızca seçili ya da üstüne gelinen düğümde görünür (kutular haritada sürekli durmasın).
                node.Highlight.color = isSel ? new Color(1f, 0.85f, 0.25f, pulse) : hovered ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0f);

                string lockText = $"GÜN {node.Site.unlockDay}'DE AÇILIR";
                if (node.PlateName != null)
                {
                    node.Label.text = locked ? $"{node.PlateName}\n<size=12>{lockText}</size>" : node.PlateName;
                    if (node.Plate.HasValue) node.Backing.color = locked ? Grey(node.Plate.Value) : node.Plate.Value;
                    if (node.LockPlate.activeSelf) node.LockPlate.SetActive(false);
                }
                else
                {
                    node.Label.text = "";
                    if (node.LockPlate.activeSelf != locked) node.LockPlate.SetActive(locked);
                    if (locked) node.LockBadge.text = lockText;
                }
                node.Button.interactable = !grace;
            }

            if (selectedSen) ShowShelter(currentDay);
            else if (selectedSite != null) ShowSite(selectedSite, currentDay, grace);
            else
            {
                view.ShowDetail(false);
                view.SetBanner(inspectionMode ? "Lokasyonları incelemek için bir taşa tıkla" : "Sefere çıkmak için haritadan bir bölge seç");
            }

            RefreshRoster(expeditions);
            cancel.interactable = !grace;
        }

        void ShowShelter(int currentDay)
        {
            view.SetBanner(null);
            view.ShowDetail(true);
            view.DetailTitle.text = "SIĞINAK";
            view.DetailBadge.text = "GÜVENLİ BÖLGE · ANA ÜS";
            view.DetailText.text =
                "Bütün kazazedelerin yaşadığı, çökmekte olan çok katlı bina. Çatıda telsiz istasyonu ve tahliye alanı var.\n\n" +
                $"• Gün: <b>{currentDay}</b>\n• Sefere çıkış: zemin kattaki kapı (öğle vakti)\n\n" +
                "Dış bölgelerden erzak, hurda, metal ve elektronik toplayıp sığınağa getir.";
            view.SetDetailAction(null, null);
        }

        void ShowSite(ExpeditionSite site, int currentDay, bool grace)
        {
            view.SetBanner(null);
            view.ShowDetail(true);
            bool locked = !site.IsUnlocked(currentDay);
            view.DetailTitle.text = site.name.ToUpperInvariant();
            view.DetailBadge.text = locked ? $"<color=#ff7560>KİLİTLİ · GÜN {site.unlockDay}'DE AÇILIR</color>" : "<color=#80e575>KEŞFE AÇIK</color>";

            var sb = new StringBuilder();
            sb.Append(site.description).Append("\n\n");
            int enemies = site.dogs + site.raiders + site.brutes + site.throwers;
            sb.Append($"• Sahada kalma süresi: <b>{site.duration:0} sn</b>\n");
            sb.Append($"• Aranacak nokta: <b>{site.lootSpots}</b>\n");
            sb.Append($"• Tehdit: <b>{(enemies <= 1 ? "az" : enemies <= 3 ? "orta" : "yüksek")}</b> ({enemies} düşman)\n\n");

            sb.Append("<color=#ffe680>Olası ganimet: ");
            if (site.loot != null)
                for (int i = 0; i < site.loot.Length; i++)
                    sb.Append(i > 0 ? ", " : "").Append($"<b>{site.loot[i].type.ToDisplayName()}</b>");
            sb.Append("</color>\n");

            if (site.hazard != ExpeditionHazard.None)
                sb.Append($"\n<color=#ff8073>Tehlike: %{site.hazardChance * 100f:0} ihtimalle {HazardName(site.hazard)} (-{site.hazardDamage * 0.7f:0} can)</color>");
            if (site.LockedCrates + site.BuriedCaches > 0)
                sb.Append($"\n<color=#ffb070>Özel: {site.LockedCrates} kilitli kasa (Levye) · {site.BuriedCaches} gömülü sandık (Kazma)</color>");
            view.DetailText.text = sb.ToString();

            if (inspectionMode) view.SetDetailAction(null, null);
            else
            {
                view.SetDetailAction(locked ? "Kilitli" : "Sefer Hazırlığı  >", OpenLoadout);
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
