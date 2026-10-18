using System.Collections.Generic;
using System.IO;
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
    /// Kapıdan sefere çıkarken ya da 'M' ile açılan Şehir Haritası (uGUI).
    /// Assets/Models/UI/city_map.png parşömen görseli üzerinde Yıkık Klinik, İnşaat Sahası,
    /// Enkaz Alanı ve Sığınak taşları etkileşimlidir. Lokasyona tıklamak seçer; sağ detay
    /// panelinde ganimet, risk, süre ve yoldaş seçimi incelenip sefere çıkılır.
    /// </summary>
    public class ExpeditionWindow : MonoBehaviour
    {
        const float InputGraceSeconds = 0.4f;
        const float MapWidth = 570f;
        const float MapHeight = 500f; // 1024x898 aspect

        class Node
        {
            public ExpeditionSite Site;
            public Button Button;
            public Image Highlight;
            public Text Label;
            /// <summary>Haritada hazır çizimi olmayan lokasyonlarda kodla çizilen plakanın yazısı (null: görselde çizili).</summary>
            public string PlateName;
        }

        static Sprite cachedMapSprite;
        static Sprite cachedHighlightSprite;

        public static ExpeditionWindow Instance { get; private set; }
        public static bool IsOpen => Instance != null && ((Instance.window != null && Instance.window.IsOpen) || (Instance.loadoutPanel != null && Instance.loadoutPanel.IsOpen));

        UiWindow window;
        readonly List<Node> nodes = new List<Node>();
        Button senButton;
        Image senHighlight;

        Text detailTitle;
        Text detailBadge;
        Text detailDesc;
        Text detailStats;
        Text detailLoot;
        Text detailHazards;

        Button depart;
        Button cancel;
        Button partner;
        Text partnerLabel;

        ExpeditionSite selectedSite;
        bool selectedSen;
        float openedAt;
        bool inspectionMode; // Kapıdan değil de 'M' ile sadece haritayı inceleme

        LoadoutPanel loadoutPanel;
        ExpeditionLoadout lastLoadout;
        bool loadoutPage;

        void Awake()
        {
            if (Instance == null) Instance = this;
            EnsureSprites();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            var expeditions = ExpeditionSystem.Instance;
            bool choosing = expeditions != null && expeditions.Stage == ExpeditionStage.ChoosingSite;

            // 'M' tuşu ile sığınaktayken haritayı inceleme
            if (GameInput.MapPressed && !ModalMenu.IsOpen && !PauseMenu.IsOpen && !choosing && !AftershockIsle.CameraControl.SideViewCamera.ViewingField &&
                (window == null || !window.IsOpen))
            {
                inspectionMode = true;
                Open(expeditions);
                return;
            }

            if (!choosing && !inspectionMode)
            {
                if (window != null && window.IsOpen) window.Show(false);
                if (loadoutPanel != null && loadoutPanel.IsOpen) loadoutPanel.Close();
                loadoutPage = false;
                return;
            }

            if (loadoutPage)
            {
                loadoutPanel.Tick();
                if (GameInput.MenuBackPressed) BackToMap();
                return;
            }

            if (window == null || !window.IsOpen) Open(expeditions);

            window.Tick(nodes.Count > 0 ? nodes[0].Button.gameObject : null);

            if (GameInput.MenuBackPressed || (inspectionMode && GameInput.MapPressed))
            {
                CancelMap();
                return;
            }

            Refresh(expeditions);
        }

        bool InGracePeriod => Time.unscaledTime - openedAt < InputGraceSeconds;

        public void OpenInspection()
        {
            inspectionMode = true;
            Open(ExpeditionSystem.Instance);
        }

        void Open(ExpeditionSystem expeditions)
        {
            if (window == null) Build(expeditions);
            selectedSite = null;
            selectedSen = false;
            openedAt = Time.unscaledTime;
            window.Show(true);

            // Varsayılan olarak ilk açık lokasyonu seç
            if (expeditions != null && expeditions.Sites != null && expeditions.Sites.Count > 0)
            {
                selectedSite = expeditions.Sites[0];
            }

            if (EventSystem.current != null && nodes.Count > 0)
                EventSystem.current.SetSelectedGameObject(nodes[0].Button.gameObject);
        }

        void Build(ExpeditionSystem expeditions)
        {
            window = new UiWindow("Şehir Haritası", new Vector2(1040f, 740f), CancelMap, false);

            // Sol Taraf: Şehir Haritası Parşömeni (city_map.png)
            var mapObj = UiFactory.Image(window.Content, "HaritaGorseli", cachedMapSprite, Color.white, true);
            UiFactory.Place(mapObj.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -10f), new Vector2(MapWidth, MapHeight));
            mapObj.preserveAspect = false;

            // Harita üzerindeki Taş Butonları (Normalleştirilmiş Koordinatlar)
            BuildStoneButtons(mapObj.rectTransform, expeditions);

            // Sağ Taraf: Detay Kartı
            BuildDetailPanel(window.Content);

            // Alt Buton Çubuğu (Footer)
            partner = window.FooterButton("", () => ExpeditionSystem.Instance?.CycleCompanion(), null, 320f);
            partnerLabel = partner.GetComponentInChildren<Text>();

            var spacer = UiFactory.Rect("Boşluk", window.Footer);
            spacer.sizeDelta = new Vector2(120f, 10f);

            depart = window.FooterButton("Sefer Hazırlığı", OpenLoadout, UiFactory.Good, 220f);
            cancel = window.FooterButton($"Kapat ({GameInput.Prompt(InputPrompt.Back)})", CancelMap, null, 180f);
        }

        void BuildStoneButtons(RectTransform mapParent, ExpeditionSystem expeditions)
        {
            nodes.Clear();

            // 1. SEN (Sığınak Merkezi) Butonu
            senButton = CreateMapButton(mapParent, "SEN_Btn", new Vector2(292.4f, -314.5f), new Vector2(125f, 125f), out senHighlight, out var senLbl);
            senLbl.text = "";
            senButton.onClick.AddListener(() =>
            {
                selectedSen = true;
                selectedSite = null;
                AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.UiClick, 0.4f);
            });

            if (expeditions == null || expeditions.Sites == null) return;

            foreach (var site in expeditions.Sites)
            {
                Vector2 pos;
                Vector2 size;
                Color? plate = null;

                string sName = site.name != null ? site.name.ToLowerInvariant() : "";
                if (sName.Contains("klinik"))
                {
                    pos = new Vector2(161.3f, -197.5f);
                    size = new Vector2(170f, 95f);
                }
                else if (sName.Contains("inşaat") || sName.Contains("insaat"))
                {
                    pos = new Vector2(450.9f, -172.5f);
                    size = new Vector2(150f, 85f);
                }
                else if (sName.Contains("enkaz"))
                {
                    pos = new Vector2(364.8f, -462.0f);
                    size = new Vector2(175f, 90f);
                }
                else if (sName.Contains("atölye") || sName.Contains("atolye"))
                {
                    pos = new Vector2(88f, -405.0f);
                    size = new Vector2(150f, 56f);
                    plate = new Color(0.45f, 0.72f, 0.48f);
                }
                else if (sName.Contains("market"))
                {
                    pos = new Vector2(305f, -100f);
                    size = new Vector2(140f, 50f);
                    plate = new Color(0.78f, 0.6f, 0.86f);
                }
                else if (sName.Contains("istasyon") || sName.Contains("benzin"))
                {
                    pos = new Vector2(190f, -468f);
                    size = new Vector2(150f, 50f);
                    plate = new Color(0.9f, 0.82f, 0.4f);
                }
                else
                {
                    pos = new Vector2(site.mapPosition.x * MapWidth, -site.mapPosition.y * MapHeight);
                    size = new Vector2(140f, 56f);
                    plate = new Color(0.7f, 0.74f, 0.8f);
                }

                var btn = CreateMapButton(mapParent, site.name, pos, size, out var hl, out var lbl);
                lbl.text = "";
                string plateName = null;
                if (plate.HasValue)
                {
                    // Bu lokasyonun haritada hazır çizimi yok: harita taşlarına benzeyen renkli plaka çizilir.
                    var backing = btn.GetComponent<Image>();
                    backing.sprite = cachedHighlightSprite;
                    backing.type = Image.Type.Sliced;
                    backing.color = plate.Value;
                    plateName = site.name;
                    lbl.alignment = TextAnchor.MiddleCenter;
                    lbl.fontSize = 14;
                    lbl.color = new Color(0.1f, 0.08f, 0.05f);
                    lbl.text = plateName;
                }

                var currentSite = site;
                btn.onClick.AddListener(() => SelectSite(currentSite));

                nodes.Add(new Node
                {
                    Site = currentSite,
                    Button = btn,
                    Highlight = hl,
                    Label = lbl,
                    PlateName = plateName,
                });
            }
        }

        Button CreateMapButton(RectTransform parent, string name, Vector2 pos, Vector2 size, out Image highlight, out Text label)
        {
            var btnObj = new GameObject(name, typeof(RectTransform), typeof(Button), typeof(Image));
            btnObj.transform.SetParent(parent, false);

            var rect = btnObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var img = btnObj.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.001f); // Görsel zaten haritada çizili

            // Seçim Vurgu Çerçevesi
            var hlObj = new GameObject("Highlight", typeof(RectTransform), typeof(Image));
            hlObj.transform.SetParent(btnObj.transform, false);
            var hlRect = hlObj.GetComponent<RectTransform>();
            UiFactory.Stretch(hlRect, -2f);
            highlight = hlObj.GetComponent<Image>();
            highlight.sprite = cachedHighlightSprite;
            highlight.color = new Color(1f, 0.85f, 0.3f, 0f);
            highlight.type = Image.Type.Sliced;

            // Küçük rozet yazısı (süre veya kilit)
            var lblObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            lblObj.transform.SetParent(btnObj.transform, false);
            label = lblObj.GetComponent<Text>();
            label.font = UiFactory.Font;
            label.fontSize = 13;
            label.alignment = TextAnchor.LowerCenter;
            label.fontStyle = FontStyle.Bold;
            label.color = Color.white;
            var lRect = lblObj.GetComponent<RectTransform>();
            UiFactory.Stretch(lRect, 4f);

            return btnObj.GetComponent<Button>();
        }

        void BuildDetailPanel(Transform parent)
        {
            var card = UiFactory.Panel(parent, "DetailCard", new Color(0.12f, 0.13f, 0.17f, 0.98f));
            UiFactory.Place(card.rectTransform, new Vector2(1f, 1f), new Vector2(-16f, -10f), new Vector2(420f, MapHeight));

            detailTitle = UiFactory.Label(card.transform, "LOKASYON SEÇ", 22, UiFactory.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            UiFactory.Place(detailTitle.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -14f), new Vector2(380f, 30f));

            detailBadge = UiFactory.Label(card.transform, "", 14, new Color(0.6f, 0.85f, 1f), TextAnchor.UpperLeft, FontStyle.Bold);
            UiFactory.Place(detailBadge.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -44f), new Vector2(380f, 22f));

            detailDesc = UiFactory.Label(card.transform, "", 14, new Color(0.85f, 0.85f, 0.82f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(detailDesc.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -70f), new Vector2(380f, 90f));

            var div1 = UiFactory.Panel(card.transform, "Div1", new Color(0.25f, 0.27f, 0.32f, 0.8f));
            UiFactory.Place(div1.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -165f), new Vector2(380f, 2f));

            detailStats = UiFactory.Label(card.transform, "", 14, Color.white, TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(detailStats.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -175f), new Vector2(380f, 65f));

            detailLoot = UiFactory.Label(card.transform, "", 14, new Color(1f, 0.9f, 0.5f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(detailLoot.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -245f), new Vector2(380f, 75f));

            detailHazards = UiFactory.Label(card.transform, "", 14, new Color(1f, 0.5f, 0.45f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(detailHazards.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -325f), new Vector2(380f, 65f));

            var goDepart = UiFactory.TextButton(card.transform, "Sefer Hazırlığı & Yola Çık", new Vector2(380f, 48f), OpenLoadout, UiFactory.Good, 17);
            UiFactory.Place((RectTransform)goDepart.transform, new Vector2(0.5f, 0f), new Vector2(0f, 32f), new Vector2(380f, 48f));
        }

        void OpenLoadout()
        {
            if (InGracePeriod) return;
            if (inspectionMode)
            {
                // İnceleme modundan çıkıp sığınak kapısına gitme tavsiyesi
                AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.EmptyClick, 0.5f);
                return;
            }
            if (selectedSite == null) return;
            loadoutPanel ??= new LoadoutPanel(BackToMap, Depart);
            loadoutPage = true;
            window.Show(false);
            loadoutPanel.Open(selectedSite, lastLoadout);
        }

        void BackToMap()
        {
            loadoutPage = false;
            loadoutPanel?.Close();
            openedAt = Time.unscaledTime;
            window.Show(true);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(depart.gameObject);
        }

        void Depart(ExpeditionLoadout loadout)
        {
            lastLoadout = loadout;
            loadoutPage = false;
            loadoutPanel?.Close();
            ExpeditionSystem.Instance?.Depart(selectedSite, loadout);
        }

        void CancelMap()
        {
            if (inspectionMode)
            {
                inspectionMode = false;
                if (window != null) window.Show(false);
                return;
            }
            if (ExpeditionSystem.Instance != null) ExpeditionSystem.Instance.CancelMap();
        }

        void SelectSite(ExpeditionSite site)
        {
            if (InGracePeriod) return;
            selectedSen = false;
            var expeditions = ExpeditionSystem.Instance;
            if (expeditions != null && !site.IsUnlocked(expeditions.Day))
            {
                AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.EmptyClick, 0.6f);
                return;
            }
            selectedSite = site;
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.UiClick, 0.4f);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(depart.gameObject);
        }

        void Refresh(ExpeditionSystem expeditions)
        {
            bool grace = InGracePeriod;
            int currentDay = expeditions != null ? expeditions.Day : 1;
            string explorerName = expeditions != null && expeditions.Explorer != null ? expeditions.Explorer.DisplayName : "Henüz Seçilmedi";

            window.Title.text = inspectionMode
                ? $"ŞEHİR KEŞİF HARİTASI   <size=15><color=#cfcfc8>(İnceleme Modu · Gün {currentDay})</color></size>"
                : $"ŞEHİR HARİTASI   <size=16><color=#cfcfc8>kâşif: {explorerName}</color></size>";

            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 5f);

            // SEN vurgusu
            if (senHighlight != null)
            {
                senHighlight.color = selectedSen
                    ? new Color(0.35f, 0.65f, 1f, 0.85f * pulse)
                    : new Color(0.2f, 0.5f, 0.9f, 0.15f);
            }

            foreach (var node in nodes)
            {
                bool locked = !node.Site.IsUnlocked(currentDay);
                bool isSel = node.Site == selectedSite && !selectedSen;

                node.Highlight.color = isSel
                    ? new Color(1f, 0.85f, 0.25f, 0.9f * pulse)
                    : locked
                        ? new Color(0.1f, 0.1f, 0.1f, 0.5f)
                        : new Color(1f, 1f, 1f, 0.12f);

                string lockText = $"Gün {node.Site.unlockDay}";
                node.Label.text = node.PlateName != null
                    ? (locked ? $"{node.PlateName}\n<size=11>{lockText}'te açılır</size>" : node.PlateName)
                    : (locked ? lockText : "");
                node.Button.interactable = !grace;
            }

            // Sağ Detay Paneli Güncellemesi
            if (selectedSen)
            {
                detailTitle.text = "SIĞINAK (MERKEZ)";
                detailBadge.text = "GÜVENLİ BÖLGE · ANA ÜS";
                detailDesc.text = "Tüm kazazedelerin yaşadığı, çöken çok katlı bina. Çatıda telsiz kulesi ve tahliye alanı bulunur. Sefere çıkmak için haritadaki renkli dış bölgelerden birine tıkla.";
                detailStats.text = $"• Güncel Gün: <b>{currentDay}. Gün</b>\n• Sığınak Kapasitesi: Sınırsız\n• Çıkış Kapısı: Zemin Kat";
                detailLoot.text = "• Dış bölgelerden erzak, hurda ve metal toplayıp sığınağa getir.";
                detailHazards.text = "";
                depart.interactable = false;
            }
            else if (selectedSite != null)
            {
                bool locked = !selectedSite.IsUnlocked(currentDay);
                detailTitle.text = selectedSite.name.ToUpperInvariant();
                detailBadge.text = locked ? $"<color=#ff7560>KİLİTLİ · GÜN {selectedSite.unlockDay}'TE AÇILIR</color>" : "<color=#80e575>AÇIK KEŞİF BÖLGESİ</color>";
                detailDesc.text = selectedSite.description;

                int enemyCount = selectedSite.dogs + selectedSite.raiders + selectedSite.brutes + selectedSite.throwers;
                detailStats.text = $"• Saha Süresi: <b>{selectedSite.duration:0} sn</b>\n• Keşif Noktaları: <b>{selectedSite.lootSpots} arama noktası</b>\n• Tahmini Tehdit: <b>{enemyCount}+ Yağmacı/Köpek</b>";

                var lootSb = new StringBuilder("• Olası Ganimetler:\n  ");
                if (selectedSite.loot != null)
                {
                    for (int i = 0; i < selectedSite.loot.Length; i++)
                    {
                        if (i > 0) lootSb.Append(", ");
                        lootSb.Append($"<b>{selectedSite.loot[i].type.ToDisplayName()}</b>");
                    }
                }
                detailLoot.text = lootSb.ToString();

                var hazSb = new StringBuilder();
                if (selectedSite.hazard != ExpeditionHazard.None)
                {
                    hazSb.Append($"Tehlike: %{selectedSite.hazardChance * 100f:0} ihtimalle {HazardName(selectedSite.hazard)} (-{selectedSite.hazardDamage * 0.7f:0} HP)\n");
                }
                if (selectedSite.LockedCrates + selectedSite.BuriedCaches > 0)
                {
                    hazSb.Append($"Özel: {selectedSite.LockedCrates} Kilitli Kasa (Levye) · {selectedSite.BuriedCaches} Gömülü Sandık (Kazma)");
                }
                detailHazards.text = hazSb.ToString();

                depart.interactable = !locked && !grace && !inspectionMode;
            }
            else
            {
                detailTitle.text = "BİR BÖLGE SEÇ";
                detailBadge.text = "ŞEHİR KEŞİF PLANI";
                detailDesc.text = "Haritadaki bölgelerden birine tıklayarak bölge tehlikelerini, süresini ve ganimet dağılımını incele.";
                detailStats.text = "";
                detailLoot.text = "";
                detailHazards.text = "";
                depart.interactable = false;
            }

            var companion = expeditions != null ? expeditions.Companion : null;
            bool anyone = expeditions != null && expeditions.CompanionCandidates().Count > 0;
            partnerLabel.text = companion != null ? $"Yoldaş: {companion.DisplayName} ({companion.Health:0} HP)"
                : anyone ? "Yoldaş: Yok (Değiştir)" : "Yoldaş: Uygun kimse yok";

            partner.interactable = anyone && !grace && !inspectionMode;
            cancel.interactable = !grace;
        }

        static string HazardName(ExpeditionHazard hazard) => hazard switch
        {
            ExpeditionHazard.Fire => "yangın",
            ExpeditionHazard.Collapse => "göçük",
            _ => "tuzak",
        };

        static void EnsureSprites()
        {
            // Önce derleme güvenli yol: arayüz görselleri kaydı (UiSkin). Dosyadan okuma yalnızca yedek (editör / geliştirme).
            if (cachedMapSprite == null && GameAssets.Ui != null) cachedMapSprite = GameAssets.Ui.cityMap;

            if (cachedMapSprite == null)
            {
                string path = Path.Combine(Application.dataPath, "Models/UI/city_map.png");
                if (File.Exists(path))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(bytes, false))
                    {
                        tex.filterMode = FilterMode.Bilinear;
                        cachedMapSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    }
                }
            }

            if (cachedHighlightSprite == null)
            {
                int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        bool isBorder = x < 4 || x >= size - 4 || y < 4 || y >= size - 4;
                        tex.SetPixel(x, y, isBorder ? Color.white : new Color(1f, 1f, 1f, 0.12f));
                    }
                }
                tex.Apply();
                cachedHighlightSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(8, 8, 8, 8));
            }
        }
    }
}
