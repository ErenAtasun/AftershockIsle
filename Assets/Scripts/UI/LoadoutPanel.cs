using System;
using System.Collections.Generic;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Economy;
using AftershockIsle.Expedition;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Sefer hazırlığı ve Sığınak Envanteri sayfası (This War of Mine iki panelli slotlu çanta & depo mimarisi):
    /// Sol Panel: EVDEKİ EŞYALARIMIZ (SIĞINAK DEPOSU) - Silahlar, cephane, ilk yardım, özel keşif aletleri ve erzak slotları.
    /// Sağ Panel: ENVANTER SLOTLARIMIZ (KÂŞİFİN ÇANTASI) - 12 açık envanter slotu (kuşanılan eşyalar ve boş ganimet yuvaları).
    /// Sol ve sağ slotlara tıklanarak eşyalar iki yönlü taşınır. Ağırlık anında hesaplanır ve canlı yük çubuğuna yansır.
    /// </summary>
    public class LoadoutPanel
    {
        class SlotView
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Background;
            public Text Icon;
            public Text Name;
            public Text Weight;
            public GameObject QtyPlate;
            public Text QtyText;
            public Text Status;
            public Button Button;
        }

        struct ShelterSlotDef
        {
            public string Id;
            public string Name;
            public string Icon;
            public string Category;
            public string WeightLabel;
            public string Description;
            public Func<int> GetStock;
            public Func<bool> IsAvailable;
            public Func<bool> IsEquipped;
            public Action OnClick;
        }

        struct BagSlotData
        {
            public bool IsOccupied;
            public string Icon;
            public string Name;
            public string Weight;
            public string Quantity;
            public string Subtitle;
            public string FullDescription;
            public Color BgColor;
            public Action OnClick;
        }

        readonly UiWindow window;
        readonly Action onBack;
        readonly Action<ExpeditionLoadout> onDepart;

        ExpeditionLoadout loadout;
        ExpeditionSite site;

        // Üst Alan: Yük & Kapasite Çubuğu
        Image loadFill;
        Text loadLabel;
        Text warningText;

        // Sol Panel: Sığınak Deposu
        RectTransform leftPanel;
        readonly List<SlotView> shelterSlots = new List<SlotView>();
        readonly List<ShelterSlotDef> shelterDefs = new List<ShelterSlotDef>();

        // Sağ Panel: Envanter Slotları
        RectTransform rightPanel;
        readonly List<SlotView> bagSlots = new List<SlotView>();
        Text inspectorTitle;
        Text inspectorCategory;
        Text inspectorStats;
        Text inspectorDesc;
        Text siteReconText;

        // Alt Butonlar
        Button backButton;
        Button clearButton;
        Button autoButton;
        Button departButton;
        Text departButtonLabel;

        public bool IsOpen => window.IsOpen;

        public LoadoutPanel(Action back, Action<ExpeditionLoadout> depart)
        {
            onBack = back;
            onDepart = depart;
            window = new UiWindow("Envanter & Sefer Hazırlığı", new Vector2(1280f, 860f), () => onBack?.Invoke(), false, true);

            BuildTopBar();
            BuildLeftPanel();
            BuildRightPanel();
            BuildFooter();
        }

        void BuildTopBar()
        {
            var barContainer = UiFactory.Rect("TopBar", window.Content);
            UiFactory.Place(barContainer, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(1220f, 40f));

            loadFill = UiFactory.Bar(barContainer, "YukCubugu", new Vector2(1220f, 32f), UiFactory.Good, out loadLabel, 16);
            UiFactory.Place((RectTransform)loadFill.transform.parent, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1220f, 32f));

            warningText = UiFactory.Label(window.Content, "", 14, UiFactory.Danger, TextAnchor.MiddleCenter, FontStyle.Bold, false);
            UiFactory.Place(warningText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(1220f, 20f));
        }

        void BuildLeftPanel()
        {
            var panelObj = UiFactory.Panel(window.Content, "ShelterPanel", new Color(0.12f, 0.13f, 0.17f, 0.98f));
            leftPanel = panelObj.rectTransform;
            UiFactory.Place(leftPanel, new Vector2(0f, 1f), new Vector2(20f, -68f), new Vector2(595f, 690f));

            var header = UiFactory.Label(leftPanel, "🏠 EVDEKİ EŞYALARIMIZ (SIĞINAK DEPOSU)", 18, UiFactory.Accent, TextAnchor.UpperLeft);
            UiFactory.Place(header.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -12f), new Vector2(560f, 24f));

            var subHeader = UiFactory.Label(leftPanel, "Eşya kutucuğuna tıklayarak kâşifin envanter slotuna gönder", 12, new Color(0.75f, 0.77f, 0.82f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(subHeader.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -36f), new Vector2(560f, 18f));

            var div = UiFactory.Panel(leftPanel, "Div", new Color(0.24f, 0.26f, 0.32f, 0.8f));
            UiFactory.Place(div.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -56f), new Vector2(563f, 2f));

            // 16 Sığınak Yuvası (4 Sütun × 4 Satır)
            var gridRoot = UiFactory.Rect("ShelterSlotGrid", leftPanel);
            UiFactory.Place(gridRoot, new Vector2(0f, 1f), new Vector2(16f, -66f), new Vector2(563f, 440f));

            DefineShelterSlots();

            const int cols = 4;
            const float slotW = 133f;
            const float slotH = 98f;
            const float gapX = 10f;
            const float gapY = 10f;

            for (int i = 0; i < shelterDefs.Count; i++)
            {
                int c = i % cols;
                int r = i / cols;
                var pos = new Vector2(c * (slotW + gapX), -r * (slotH + gapY));
                int index = i;

                var slotView = CreateSlotView(gridRoot, $"ShelterSlot_{i}", pos, new Vector2(slotW, slotH), () =>
                {
                    if (index >= 0 && index < shelterDefs.Count)
                    {
                        var def = shelterDefs[index];
                        def.OnClick?.Invoke();
                        InspectShelterItem(def);
                    }
                });

                shelterSlots.Add(slotView);
            }

            // Sığınak Bilgi Kartı
            var infoCard = UiFactory.Panel(leftPanel, "ShelterInfoCard", new Color(0.15f, 0.17f, 0.22f, 0.95f));
            UiFactory.Place(infoCard.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(563f, 150f));

            var infoTitle = UiFactory.Label(infoCard.transform, "💡 SIĞINAK DEPOSU VE KEŞİF KURALLARI", 14, UiFactory.Accent, TextAnchor.UpperLeft);
            UiFactory.Place(infoTitle.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -10f), new Vector2(535f, 20f));

            var infoBody = UiFactory.Label(infoCard.transform,
                "• <b>Mermi & Yaşam Kiti:</b> Sığınak stoğundan alınır; sefere gitmeyen kâşifler sığınakta kalır. Harcanmayan mermi/kit dönüşte depoya geri aktarılır.\n" +
                "• <b>Özel Aletler:</b> Kilitli sandık ve kasalar sahadayken <b>Levye</b>, gömülü enkazlar ise <b>Kazma</b> olmadan açılamaz.\n" +
                "• <b>Taşıma Sınırı:</b> Çanta ne kadar ağır olursa sahadan toplanabilecek ganimet o kadar azalır.",
                12, new Color(0.85f, 0.87f, 0.92f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(infoBody.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -34f), new Vector2(535f, 105f));
        }

        void BuildRightPanel()
        {
            var panelObj = UiFactory.Panel(window.Content, "BagPanel", new Color(0.13f, 0.15f, 0.20f, 0.98f));
            rightPanel = panelObj.rectTransform;
            UiFactory.Place(rightPanel, new Vector2(1f, 1f), new Vector2(-20f, -68f), new Vector2(595f, 690f));

            var header = UiFactory.Label(rightPanel, "🎒 ENVANTER SLOTLARIMIZ (KÂŞİFİN ÇANTASI)", 18, UiFactory.Accent, TextAnchor.UpperLeft);
            UiFactory.Place(header.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -12f), new Vector2(560f, 24f));

            var subHeader = UiFactory.Label(rightPanel, "Kâşifin yanında taşıyacağı eşyalar. Çıkarmak için slota tıkla", 12, new Color(0.75f, 0.77f, 0.82f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(subHeader.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -36f), new Vector2(560f, 18f));

            var div = UiFactory.Panel(rightPanel, "Div", new Color(0.24f, 0.26f, 0.32f, 0.8f));
            UiFactory.Place(div.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -56f), new Vector2(563f, 2f));

            // 12 Çanta Slotu (4 Sütun × 3 Satır)
            var gridRoot = UiFactory.Rect("BagSlotGrid", rightPanel);
            UiFactory.Place(gridRoot, new Vector2(0f, 1f), new Vector2(16f, -66f), new Vector2(563f, 320f));

            const int cols = 4;
            const float slotW = 133f;
            const float slotH = 98f;
            const float gapX = 10f;
            const float gapY = 10f;

            for (int i = 0; i < 12; i++)
            {
                int c = i % cols;
                int r = i / cols;
                var pos = new Vector2(c * (slotW + gapX), -r * (slotH + gapY));
                int slotIndex = i;

                var slotView = CreateSlotView(gridRoot, $"BagSlot_{i}", pos, new Vector2(slotW, slotH), () => OnBagSlotClicked(slotIndex));
                bagSlots.Add(slotView);
            }

            // Alt Alan: Eşya İnceleme & Saha Keşif Kartı
            var inspCard = UiFactory.Panel(rightPanel, "InspectorCard", new Color(0.16f, 0.19f, 0.26f, 0.95f));
            UiFactory.Place(inspCard.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(563f, 270f));

            inspectorTitle = UiFactory.Label(inspCard.transform, "EŞYA DETAYLARI", 16, UiFactory.Accent, TextAnchor.UpperLeft);
            UiFactory.Place(inspectorTitle.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -10f), new Vector2(535f, 22f));

            inspectorCategory = UiFactory.Label(inspCard.transform, "", 12, new Color(0.72f, 0.75f, 0.82f), TextAnchor.UpperLeft);
            UiFactory.Place(inspectorCategory.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -32f), new Vector2(535f, 18f));

            inspectorStats = UiFactory.Label(inspCard.transform, "", 13, new Color(1f, 0.88f, 0.5f), TextAnchor.UpperLeft);
            UiFactory.Place(inspectorStats.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -52f), new Vector2(535f, 20f));

            inspectorDesc = UiFactory.Label(inspCard.transform, "Sol veya sağdaki bir eşyaya tıklayarak özelliklerini inceleyin veya taşıyın.", 13, UiFactory.Text, TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(inspectorDesc.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -74f), new Vector2(535f, 66f));

            var sep = UiFactory.Panel(inspCard.transform, "Sep", new Color(0.28f, 0.31f, 0.38f, 0.8f));
            UiFactory.Place(sep.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -145f), new Vector2(535f, 1f));

            siteReconText = UiFactory.Label(inspCard.transform, "", 13, new Color(0.85f, 0.88f, 0.95f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(siteReconText.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -152f), new Vector2(535f, 110f));
        }

        SlotView CreateSlotView(Transform parent, string name, Vector2 pos, Vector2 size, Action onClick)
        {
            var btn = UiFactory.TextButton(parent, "", size, onClick, new Color(0.18f, 0.20f, 0.26f, 0.95f), 12);
            var rect = (RectTransform)btn.transform;
            UiFactory.Place(rect, new Vector2(0f, 1f), pos, size);

            var bg = btn.GetComponent<Image>();
            bg.sprite = UiFactory.Plate;
            bg.type = Image.Type.Sliced;

            // İkon
            var iconLbl = UiFactory.Label(btn.transform, "", 26, Color.white, TextAnchor.MiddleCenter);
            UiFactory.Place(iconLbl.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(size.x, 34f));

            // İsim
            var nameLbl = UiFactory.Label(btn.transform, "", 11, UiFactory.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.Place(nameLbl.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(size.x - 6f, 22f));

            // Durum / Alt Yazı
            var statusLbl = UiFactory.Label(btn.transform, "", 10, new Color(0.7f, 0.72f, 0.78f), TextAnchor.MiddleCenter, FontStyle.Normal, false);
            UiFactory.Place(statusLbl.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -68f), new Vector2(size.x - 6f, 16f));

            // Ağırlık Rozeti (Sol Alt)
            var weightLbl = UiFactory.Label(btn.transform, "", 10, UiFactory.Accent, TextAnchor.LowerLeft, FontStyle.Bold);
            UiFactory.Place(weightLbl.rectTransform, new Vector2(0f, 0f), new Vector2(6f, 5f), new Vector2(50f, 16f));

            // Miktar Plakası (Sağ Alt)
            var qtyPlate = UiFactory.Panel(btn.transform, "QtyPlate", new Color(0.08f, 0.09f, 0.12f, 0.95f));
            UiFactory.Place(qtyPlate.rectTransform, new Vector2(1f, 0f), new Vector2(-6f, 5f), new Vector2(40f, 18f));

            var qtyLbl = UiFactory.Label(qtyPlate.transform, "", 11, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.Stretch(qtyLbl.rectTransform);

            return new SlotView
            {
                Root = btn.gameObject,
                Rect = rect,
                Background = bg,
                Icon = iconLbl,
                Name = nameLbl,
                Weight = weightLbl,
                QtyPlate = qtyPlate.gameObject,
                QtyText = qtyLbl,
                Status = statusLbl,
                Button = btn
            };
        }

        void BuildFooter()
        {
            backButton = window.FooterButton($"Geri ({GameInput.Prompt(InputPrompt.Back)})", () => onBack?.Invoke(), null, 200f);
            clearButton = window.FooterButton("Tümünü Depoya Bırak", ClearAllToShelter, new Color(0.42f, 0.22f, 0.20f, 0.95f), 200f);
            autoButton = window.FooterButton("Önerilen Donanım", ApplyRecommended, UiFactory.SlateLight, 200f);
            departButton = window.FooterButton("Seferi Başlat & Yola Çık", OnDepartClicked, UiFactory.Good, 280f);
            departButtonLabel = departButton.GetComponentInChildren<Text>();
        }

        // ---------------------------------------------------------------- Sığınak Slot Tanımları

        void DefineShelterSlots()
        {
            shelterDefs.Clear();

            // 1. Sapan
            shelterDefs.Add(new ShelterSlotDef
            {
                Id = "slingshot",
                Name = "Sapan",
                Icon = "🏹",
                Category = "Temel Menzilli Silah",
                WeightLabel = "0 kg",
                Description = "Temel kâşif silahı. Sınırsız çakıl taşı fırlatır, cephane harcamaz ve ağırlık yapmaz.",
                GetStock = () => 1,
                IsAvailable = () => true,
                IsEquipped = () => loadout.Weapon == WeaponKind.Slingshot,
                OnClick = () => EquipWeapon(WeaponKind.Slingshot)
            });

            // 2. Tabanca
            shelterDefs.Add(new ShelterSlotDef
            {
                Id = "pistol",
                Name = "Tabanca",
                Icon = "🔫",
                Category = "Ateşli Silah (9mm)",
                WeightLabel = "1 kg",
                Description = "Yarı otomatik tabanca. Hasar: 20, Menzil: 12m. 10 Mermi = 1 kg.",
                GetStock = () => FieldWeapons.IsAvailable(WeaponKind.Pistol) ? 1 : 0,
                IsAvailable = () => FieldWeapons.IsAvailable(WeaponKind.Pistol),
                IsEquipped = () => loadout.Weapon == WeaponKind.Pistol,
                OnClick = () => EquipWeapon(WeaponKind.Pistol)
            });

            // 3. SMG
            shelterDefs.Add(new ShelterSlotDef
            {
                Id = "smg",
                Name = "Hafif Makineli",
                Icon = "💥",
                Category = "Otomatik Silah",
                WeightLabel = "2 kg",
                Description = "Hafif makineli tüfek (SMG). Yüksek atış hızı. Hasar: 14, Menzil: 10m.",
                GetStock = () => FieldWeapons.IsAvailable(WeaponKind.Smg) ? 1 : 0,
                IsAvailable = () => FieldWeapons.IsAvailable(WeaponKind.Smg),
                IsEquipped = () => loadout.Weapon == WeaponKind.Smg,
                OnClick = () => EquipWeapon(WeaponKind.Smg)
            });

            // 4. Pompalı
            shelterDefs.Add(new ShelterSlotDef
            {
                Id = "shotgun",
                Name = "Pompalı Tüfek",
                Icon = "🎯",
                Category = "Ağır Ateşli Silah",
                WeightLabel = "2 kg",
                Description = "Saçmalı av tüfeği. Yakın mesafede ölümcül hasar (48). Menzil: 7m.",
                GetStock = () => FieldWeapons.IsAvailable(WeaponKind.Shotgun) ? 1 : 0,
                IsAvailable = () => FieldWeapons.IsAvailable(WeaponKind.Shotgun),
                IsEquipped = () => loadout.Weapon == WeaponKind.Shotgun,
                OnClick = () => EquipWeapon(WeaponKind.Shotgun)
            });

            // 5. Piyade Tüfeği
            shelterDefs.Add(new ShelterSlotDef
            {
                Id = "ar",
                Name = "Piyade Tüfeği",
                Icon = "💥",
                Category = "Askeri Silah",
                WeightLabel = "3 kg",
                Description = "Yüksek etkili muharebe tüfeği. Hasar: 32, Menzil: 16m.",
                GetStock = () => FieldWeapons.IsAvailable(WeaponKind.AssaultRifle) ? 1 : 0,
                IsAvailable = () => FieldWeapons.IsAvailable(WeaponKind.AssaultRifle),
                IsEquipped = () => loadout.Weapon == WeaponKind.AssaultRifle,
                OnClick = () => EquipWeapon(WeaponKind.AssaultRifle)
            });

            // 6. Keskin Nişancı
            shelterDefs.Add(new ShelterSlotDef
            {
                Id = "sniper",
                Name = "Keskin Nişancı",
                Icon = "🔭",
                Category = "Uzun Menzilli Silah",
                WeightLabel = "3 kg",
                Description = "Dürbünlü yüksek kalibre tüfek. Uzaktan tek atışta kritik hasar verir. Hasar: 55, Menzil: 22m.",
                GetStock = () => FieldWeapons.IsAvailable(WeaponKind.SniperRifle) ? 1 : 0,
                IsAvailable = () => FieldWeapons.IsAvailable(WeaponKind.SniperRifle),
                IsEquipped = () => loadout.Weapon == WeaponKind.SniperRifle,
                OnClick = () => EquipWeapon(WeaponKind.SniperRifle)
            });

            // 7. Mermi (+10)
            shelterDefs.Add(new ShelterSlotDef
            {
                Id = "ammo10",
                Name = "Mermi (+10)",
                Icon = "🔴",
                Category = "Ateşli Silah Cephanesi",
                WeightLabel = "1 kg",
                Description = "10 adet mermi çantaya aktarılır. Her 10 mermi çantada 1 kg yer kaplar.",
                GetStock = () => ExpeditionLoadout.AmmoInShelter,
                IsAvailable = () => ExpeditionLoadout.AmmoInShelter > 0,
                IsEquipped = () => loadout.Ammo > 0,
                OnClick = () => AddAmmo(10)
            });

            // 8. Mermi (+5)
            shelterDefs.Add(new ShelterSlotDef
            {
                Id = "ammo5",
                Name = "Mermi (+5)",
                Icon = "🔴",
                Category = "Ateşli Silah Cephanesi",
                WeightLabel = "0.5 kg",
                Description = "Hassas cephane ayarı için 5 adet mermi çantaya aktarılır.",
                GetStock = () => ExpeditionLoadout.AmmoInShelter,
                IsAvailable = () => ExpeditionLoadout.AmmoInShelter > 0,
                IsEquipped = () => loadout.Ammo > 0,
                OnClick = () => AddAmmo(5)
            });

            // 9. Yaşam Kiti (+1)
            shelterDefs.Add(new ShelterSlotDef
            {
                Id = "kit",
                Name = "Yaşam Kiti (+1)",
                Icon = "🩹",
                Category = "Tıbbi İlk Yardım & Erzak",
                WeightLabel = "1 kg",
                Description = "Saha keşfinde ağır yaraları sarar ve canı tazeler (Azami 3 adet taşınabilir).",
                GetStock = () => ExpeditionLoadout.KitsInShelter,
                IsAvailable = () => ExpeditionLoadout.KitsInShelter > 0,
                IsEquipped = () => loadout.Kits > 0,
                OnClick = () => AddKit(1)
            });

            // 10. Kazma
            shelterDefs.Add(new ShelterSlotDef
            {
                Id = "pickaxe",
                Name = "Kazma",
                Icon = "⛏️",
                Category = "Özel Keşif Aleti",
                WeightLabel = "2 kg",
                Description = "Harabelerdeki gömülü sandıkları ve molozları kazar. Yanınızda kazma yoksa gömülü ganimetler alınamaz!",
                GetStock = () => ToolKit.HasPickaxe ? 1 : 0,
                IsAvailable = () => ToolKit.HasPickaxe,
                IsEquipped = () => loadout.Pickaxe,
                OnClick = TogglePickaxe
            });

            // 11. Levye
            shelterDefs.Add(new ShelterSlotDef
            {
                Id = "crowbar",
                Name = "Levye",
                Icon = "🔧",
                Category = "Özel Keşif Aleti",
                WeightLabel = "1 kg",
                Description = "Kilitli çelik kasaları sessizce açar. Yanınızda levye yoksa kilitli kasalar açılamaz!",
                GetStock = () => ToolKit.HasCrowbar ? 1 : 0,
                IsAvailable = () => ToolKit.HasCrowbar,
                IsEquipped = () => loadout.Crowbar,
                OnClick = ToggleCrowbar
            });

            // 12. Sırt Çantası
            shelterDefs.Add(new ShelterSlotDef
            {
                Id = "backpack",
                Name = "Büyük Çanta",
                Icon = "🎒",
                Category = "Pasif Teçhizat",
                WeightLabel = "+3 kg",
                Description = "Tezgahta üretildiğinde kâşifin taşıma kapasitesini kalıcı olarak +3 kg arttırır.",
                GetStock = () => ToolKit.HasBackpack ? 1 : 0,
                IsAvailable = () => ToolKit.HasBackpack,
                IsEquipped = () => ToolKit.HasBackpack,
                OnClick = () => ShowWarning(ToolKit.HasBackpack ? "Büyük Sırt Çantası aktif (+3 kg taşıma kapasitesi sağlar)." : "Büyük Sırt Çantası henüz tezgahta üretilmedi.")
            });

            // 13. Konserve & Yaşam Erzağı
            shelterDefs.Add(new ShelterSlotDef
            {
                Id = "supplies_depot",
                Name = "Erzak & Kit Stoğu",
                Icon = "🥫",
                Category = "Sığınak Erzağı",
                WeightLabel = "Depo",
                Description = "Sığınaktaki yaşam kiti ve erzak stoğu. Kazazedelerin aç kalmaması için gün sonunda dağıtılır.",
                GetStock = () => ResourceStorage.Instance != null ? ResourceStorage.Instance.Get(ResourceType.Supplies) : 0,
                IsAvailable = () => true,
                IsEquipped = () => false,
                OnClick = () => ShowWarning("Erzak ve kitler sığınakta saklanır; sefere Yaşam Kiti olarak götürülür.")
            });

            // 14. Temiz Su
            shelterDefs.Add(new ShelterSlotDef
            {
                Id = "water",
                Name = "Temiz Su",
                Icon = "💧",
                Category = "Sığınak Yaşam Kaynağı",
                WeightLabel = "Depo",
                Description = "Sığınaktaki temiz su rezervi. Kazazedelerin susuz kalmaması için sığınakta tutulur.",
                GetStock = () => ResourceStorage.Instance != null ? ResourceStorage.Instance.Get(ResourceType.Water) : 0,
                IsAvailable = () => true,
                IsEquipped = () => false,
                OnClick = () => ShowWarning("Su sığınakta tüketilir; arıtma ve pompa ile üretilir.")
            });

            // 15. Hurda Metal
            shelterDefs.Add(new ShelterSlotDef
            {
                Id = "scrap",
                Name = "Hurda Metal",
                Icon = "🔩",
                Category = "İnşaat Malzemesi",
                WeightLabel = "Depo",
                Description = "Sığınak inşaatlarında, barikatlarda ve tezgah üretiminde temel hammadde.",
                GetStock = () => ResourceStorage.Instance != null ? ResourceStorage.Instance.Get(ResourceType.Scrap) : 0,
                IsAvailable = () => true,
                IsEquipped = () => false,
                OnClick = () => ShowWarning("Hurda metal sığınakta tutulur; sahada bulunan hurdalar çantada taşınır.")
            });

            // 16. Elektronik Parça
            shelterDefs.Add(new ShelterSlotDef
            {
                Id = "electronic",
                Name = "Elektronik",
                Icon = "⚡",
                Category = "İleri Teknoloji Malzemesi",
                WeightLabel = "Depo",
                Description = "Jeneratör, telsiz ve gelişmiş silah üretiminde kullanılan kıymetli devre elemanları.",
                GetStock = () => ResourceStorage.Instance != null ? ResourceStorage.Instance.Get(ResourceType.Electronics) : 0,
                IsAvailable = () => true,
                IsEquipped = () => false,
                OnClick = () => ShowWarning("Elektronik parçalar sığınak projelerinde kullanılır.")
            });
        }

        // ---------------------------------------------------------------- Çanta Slotları Verisi

        List<BagSlotData> BuildBagSlotDataList(int capacity, int gearWeight, int freeKg)
        {
            var list = new List<BagSlotData>();

            // 1. Silah Slotu
            if (loadout.Weapon != WeaponKind.Slingshot)
            {
                var info = FieldWeapons.Get(loadout.Weapon);
                int weight = ExpeditionLoadout.WeaponWeight(loadout.Weapon);
                string icon = loadout.Weapon switch
                {
                    WeaponKind.Pistol => "🔫",
                    WeaponKind.Smg => "💥",
                    WeaponKind.Shotgun => "🎯",
                    WeaponKind.AssaultRifle => "💥",
                    WeaponKind.SniperRifle => "🔭",
                    _ => "🔫"
                };

                list.Add(new BagSlotData
                {
                    IsOccupied = true,
                    Icon = icon,
                    Name = info.Name,
                    Weight = $"{weight} kg",
                    Quantity = "1",
                    Subtitle = "Kuşanıldı ✓",
                    FullDescription = $"{info.Name} — Hasar: {info.Damage * info.Pellets:0} · Menzil: {info.Range:0}m · Mermi harcar. Depoya geri göndermek için tıklayın.",
                    BgColor = new Color(0.18f, 0.42f, 0.28f, 0.98f),
                    OnClick = () => EquipWeapon(WeaponKind.Slingshot)
                });
            }
            else
            {
                list.Add(new BagSlotData
                {
                    IsOccupied = true,
                    Icon = "🏹",
                    Name = "Sapan",
                    Weight = "0 kg",
                    Quantity = "∞",
                    Subtitle = "Temel Silah",
                    FullDescription = "Sapan her zaman kâşifin cebindedir (0 kg). Sınırsız çakıl taşı fırlatır, cephane gerektirmez.",
                    BgColor = new Color(0.18f, 0.22f, 0.28f, 0.95f),
                    OnClick = () => ShowWarning("Sapan temel ekipmandır, cebinizde kalır.")
                });
            }

            // 2. Cephane Slotu
            if (loadout.Ammo > 0)
            {
                int weight = ExpeditionLoadout.AmmoWeight(loadout.Ammo);
                list.Add(new BagSlotData
                {
                    IsOccupied = true,
                    Icon = "🔴",
                    Name = "Cephane",
                    Weight = $"{weight} kg",
                    Quantity = $"x{loadout.Ammo}",
                    Subtitle = "(-5 Mermi)",
                    FullDescription = $"{loadout.Ammo} adet mermi ({weight} kg). Tıklayarak 5 adet mermiyi depoya geri gönderin.",
                    BgColor = new Color(0.24f, 0.22f, 0.35f, 0.98f),
                    OnClick = () => RemoveAmmo(5)
                });
            }

            // 3. Yaşam Kitleri (Her biri için ayrı slot)
            for (int k = 0; k < loadout.Kits; k++)
            {
                list.Add(new BagSlotData
                {
                    IsOccupied = true,
                    Icon = "🩹",
                    Name = "Yaşam Kiti",
                    Weight = "1 kg",
                    Quantity = "x1",
                    Subtitle = "İlk Yardım",
                    FullDescription = "Ağır yaraları sarar ve canı yeniler. Tıklayarak depoya geri gönderin.",
                    BgColor = new Color(0.22f, 0.35f, 0.26f, 0.98f),
                    OnClick = () => RemoveKit(1)
                });
            }

            // 4. Kazma
            if (loadout.Pickaxe)
            {
                list.Add(new BagSlotData
                {
                    IsOccupied = true,
                    Icon = "⛏️",
                    Name = "Kazma",
                    Weight = "2 kg",
                    Quantity = "x1",
                    Subtitle = "Gömülü Kazar",
                    FullDescription = "Gömülü sandıkları ve molozları kazar. Tıklayarak depoya geri gönderin.",
                    BgColor = new Color(0.35f, 0.26f, 0.18f, 0.98f),
                    OnClick = TogglePickaxe
                });
            }

            // 5. Levye
            if (loadout.Crowbar)
            {
                list.Add(new BagSlotData
                {
                    IsOccupied = true,
                    Icon = "🔧",
                    Name = "Levye",
                    Weight = "1 kg",
                    Quantity = "x1",
                    Subtitle = "Kilit Açar",
                    FullDescription = "Kilitli çelik kasaları sessizce açar. Tıklayarak depoya geri gönderin.",
                    BgColor = new Color(0.28f, 0.26f, 0.22f, 0.98f),
                    OnClick = ToggleCrowbar
                });
            }

            // Kalan Tüm Yuvalar: Boş Ganimet Yuvası
            while (list.Count < 12)
            {
                list.Add(new BagSlotData
                {
                    IsOccupied = false,
                    Icon = "+",
                    Name = "BOŞ SLOT",
                    Weight = freeKg > 0 ? $"{freeKg} kg" : "0 kg",
                    Quantity = "",
                    Subtitle = "Ganimet Payı",
                    FullDescription = "Boş Ganimet Yuvası. Sahadan toplanacak hurda, metal, erzak ve elektronik eşyalar bu yuvalarda taşınır.",
                    BgColor = new Color(0.11f, 0.13f, 0.16f, 0.55f),
                    OnClick = () => ShowWarning("Sol taraftaki sığınak eşyalarına tıklayarak buraya eşya ekleyebilirsiniz.")
                });
            }

            return list;
        }

        void OnBagSlotClicked(int index)
        {
            var expeditions = ExpeditionSystem.Instance;
            bool companion = expeditions != null && expeditions.Companion != null;
            int capacity = ExpeditionLoadout.Capacity(companion);
            int gear = loadout.GearWeight();
            int free = capacity - gear;

            var bagData = BuildBagSlotDataList(capacity, gear, free);
            if (index >= 0 && index < bagData.Count)
            {
                var item = bagData[index];
                item.OnClick?.Invoke();
                InspectBagItem(item);
            }
        }

        // ---------------------------------------------------------------- İnceleme & Detay Kartı

        void InspectShelterItem(ShelterSlotDef def)
        {
            inspectorTitle.text = $"{def.Icon} {def.Name.ToUpperInvariant()}";
            inspectorCategory.text = $"KATEGORİ: {def.Category.ToUpperInvariant()}   •   AĞIRLIK: {def.WeightLabel}";
            inspectorStats.text = $"Depodaki Stok: <b>{def.GetStock()} adet</b>   {(def.IsEquipped() ? "<color=#88e070>[Çantada ✓]</color>" : "")}";
            inspectorDesc.text = $"{def.Description}\n<size=12><color=#cfcfc8>Sol Tık: Kâşifin sırt çantasına aktar</color></size>";
        }

        void InspectBagItem(BagSlotData item)
        {
            inspectorTitle.text = $"{item.Icon} {item.Name.ToUpperInvariant()}";
            inspectorCategory.text = item.IsOccupied ? $"ÇANTADAKİ EKİPMAN   •   AĞIRLIK: {item.Weight}" : "BOŞ GANİMET YUVASI";
            inspectorStats.text = item.IsOccupied ? $"Miktar: <b>{item.Quantity}</b>   •   Durum: {item.Subtitle}" : "Sahadan toplanacak ganimetler için ayrılmıştır.";
            inspectorDesc.text = item.IsOccupied
                ? $"{item.FullDescription}\n<size=12><color=#ff8f7a>Sol Tık: Çantadan çıkar ve depoya geri gönder</color></size>"
                : "Sol taraftaki ev eşyalarına tıklayarak çantaya eşya alabilirsiniz.";
        }

        void ShowWarning(string msg)
        {
            warningText.text = msg;
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.EmptyClick, 0.45f);
        }

        // ---------------------------------------------------------------- Açma / Kapama

        public void Open(ExpeditionSite target, ExpeditionLoadout previous)
        {
            site = target;
            loadout = previous ?? ExpeditionLoadout.Default();
            loadout.Clamp();
            warningText.text = "";
            window.Show(true);

            // Varsayılan inceleme
            if (shelterDefs.Count > 0) InspectShelterItem(shelterDefs[0]);

            Refresh();

            if (EventSystem.current != null && shelterSlots.Count > 0)
                EventSystem.current.SetSelectedGameObject(shelterSlots[0].Button.gameObject);
        }

        public void Close() => window.Show(false);

        public void Tick()
        {
            window.Tick(shelterSlots.Count > 0 ? shelterSlots[0].Button.gameObject : departButton.gameObject);
            Refresh();
        }

        // ---------------------------------------------------------------- Eşya Taşıma Mantığı

        void EquipWeapon(WeaponKind kind)
        {
            if (!FieldWeapons.IsAvailable(kind))
            {
                ShowWarning("Bu silah henüz tezgahta üretilmedi!");
                return;
            }

            int diffWeight = ExpeditionLoadout.WeaponWeight(kind) - ExpeditionLoadout.WeaponWeight(loadout.Weapon);
            var expeditions = ExpeditionSystem.Instance;
            bool companion = expeditions != null && expeditions.Companion != null;
            int capacity = ExpeditionLoadout.Capacity(companion);

            if (loadout.GearWeight() + diffWeight > capacity)
            {
                ShowWarning($"Çanta kapasitesi doldu! En fazla {capacity} kg taşınabilir.");
                return;
            }

            loadout.Weapon = kind;
            if (!FieldWeapons.Get(kind).UsesAmmo)
            {
                loadout.Ammo = 0;
            }
            else if (loadout.Ammo == 0)
            {
                // Silah seçildiğinde otomatik 10 mermi yükle (varsa ve yer varsa)
                int freeWeight = capacity - loadout.GearWeight();
                if (freeWeight >= 1 && ExpeditionLoadout.AmmoInShelter >= 10)
                    loadout.Ammo = 10;
            }

            warningText.text = "";
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.UiClick, 0.5f);
            Refresh();
        }

        void AddAmmo(int amount)
        {
            var weapon = FieldWeapons.Get(loadout.Weapon);
            if (!weapon.UsesAmmo)
            {
                ShowWarning("Sapan mermi kullanmaz! Önce ateşli bir silah kuşanın.");
                return;
            }

            int shelterAmmo = ExpeditionLoadout.AmmoInShelter;
            if (loadout.Ammo >= shelterAmmo)
            {
                ShowWarning("Sığınak deposundaki tüm mermiler zaten çantada!");
                return;
            }

            int newAmmo = Mathf.Min(loadout.Ammo + amount, shelterAmmo);
            int newWeight = loadout.GearWeight(newAmmo, loadout.Kits);
            var expeditions = ExpeditionSystem.Instance;
            bool companion = expeditions != null && expeditions.Companion != null;
            int capacity = ExpeditionLoadout.Capacity(companion);

            if (newWeight > capacity)
            {
                ShowWarning($"Çanta kapasitesi doldu! En fazla {capacity} kg taşınabilir.");
                return;
            }

            loadout.Ammo = newAmmo;
            warningText.text = "";
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.Pickup, 0.45f);
            Refresh();
        }

        void RemoveAmmo(int amount)
        {
            loadout.Ammo = Mathf.Max(0, loadout.Ammo - amount);
            warningText.text = "";
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.UiClick, 0.5f);
            Refresh();
        }

        void AddKit(int amount)
        {
            int shelterKits = ExpeditionLoadout.KitsInShelter;
            if (loadout.Kits >= shelterKits)
            {
                ShowWarning("Sığınak deposunda başka Yaşam Kiti kalmadı!");
                return;
            }

            if (loadout.Kits >= ExpeditionLoadout.MaxKits)
            {
                ShowWarning($"Kâşif çantasına en fazla {ExpeditionLoadout.MaxKits} Yaşam Kiti alabilir.");
                return;
            }

            int newKits = Mathf.Min(loadout.Kits + amount, Mathf.Min(ExpeditionLoadout.MaxKits, shelterKits));
            int newWeight = loadout.GearWeight(loadout.Ammo, newKits);
            var expeditions = ExpeditionSystem.Instance;
            bool companion = expeditions != null && expeditions.Companion != null;
            int capacity = ExpeditionLoadout.Capacity(companion);

            if (newWeight > capacity)
            {
                ShowWarning($"Çanta kapasitesi doldu! En fazla {capacity} kg taşınabilir.");
                return;
            }

            loadout.Kits = newKits;
            warningText.text = "";
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.Pickup, 0.45f);
            Refresh();
        }

        void RemoveKit(int amount)
        {
            loadout.Kits = Mathf.Max(0, loadout.Kits - amount);
            warningText.text = "";
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.UiClick, 0.5f);
            Refresh();
        }

        void TogglePickaxe()
        {
            if (!ToolKit.HasPickaxe)
            {
                ShowWarning("Kazma henüz sığınak tezgahında üretilmedi!");
                return;
            }

            var expeditions = ExpeditionSystem.Instance;
            bool companion = expeditions != null && expeditions.Companion != null;
            int capacity = ExpeditionLoadout.Capacity(companion);

            if (!loadout.Pickaxe && (loadout.GearWeight() + ExpeditionLoadout.PickaxeWeight > capacity))
            {
                ShowWarning($"Kazma (+2 kg) için çantada yeterli yer yok! (Kapasite: {capacity} kg)");
                return;
            }

            loadout.Pickaxe = !loadout.Pickaxe;
            warningText.text = "";
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.UiClick, 0.5f);
            Refresh();
        }

        void ToggleCrowbar()
        {
            if (!ToolKit.HasCrowbar)
            {
                ShowWarning("Levye henüz sığınak tezgahında üretilmedi!");
                return;
            }

            var expeditions = ExpeditionSystem.Instance;
            bool companion = expeditions != null && expeditions.Companion != null;
            int capacity = ExpeditionLoadout.Capacity(companion);

            if (!loadout.Crowbar && (loadout.GearWeight() + ExpeditionLoadout.CrowbarWeight > capacity))
            {
                ShowWarning($"Levye (+1 kg) için çantada yeterli yer yok! (Kapasite: {capacity} kg)");
                return;
            }

            loadout.Crowbar = !loadout.Crowbar;
            warningText.text = "";
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.UiClick, 0.5f);
            Refresh();
        }

        void ClearAllToShelter()
        {
            loadout.Weapon = WeaponKind.Slingshot;
            loadout.Ammo = 0;
            loadout.Kits = 0;
            loadout.Pickaxe = false;
            loadout.Crowbar = false;
            warningText.text = "Çantadaki tüm eşyalar sığınak deposuna geri bırakıldı.";
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.Pickup, 0.5f);
            Refresh();
        }

        void ApplyRecommended()
        {
            loadout.Weapon = FieldWeapons.Best();
            int shelterAmmo = ExpeditionLoadout.AmmoInShelter;
            loadout.Ammo = FieldWeapons.Get(loadout.Weapon).UsesAmmo ? Mathf.Min(15, shelterAmmo) : 0;
            loadout.Kits = Mathf.Min(1, ExpeditionLoadout.KitsInShelter);

            var expeditions = ExpeditionSystem.Instance;
            bool companion = expeditions != null && expeditions.Companion != null;
            int capacity = ExpeditionLoadout.Capacity(companion);

            // Sahada kasa varsa ve yer varsa levye al
            if (site != null && site.LockedCrates > 0 && ToolKit.HasCrowbar && loadout.GearWeight() + 1 <= capacity)
                loadout.Crowbar = true;
            else
                loadout.Crowbar = false;

            // Sahada gömülü sandık varsa ve yer varsa kazma al
            if (site != null && site.BuriedCaches > 0 && ToolKit.HasPickaxe && loadout.GearWeight() + 2 <= capacity)
                loadout.Pickaxe = true;
            else
                loadout.Pickaxe = false;

            warningText.text = "Önerilen sefer donanımı hazırlandı.";
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.Pickup, 0.5f);
            Refresh();
        }

        void OnDepartClicked()
        {
            if (site != null)
            {
                onDepart?.Invoke(loadout);
            }
            else
            {
                onBack?.Invoke();
            }
        }

        // ---------------------------------------------------------------- Görsel Yenileme

        void Refresh()
        {
            var expeditions = ExpeditionSystem.Instance;
            bool companion = expeditions != null && expeditions.Companion != null;
            int capacity = ExpeditionLoadout.Capacity(companion);
            int gear = loadout.GearWeight();
            int free = capacity - gear;

            string explorerName = expeditions != null && expeditions.Explorer != null ? expeditions.Explorer.DisplayName : "Seçilmedi";
            window.Title.text = site != null
                ? $"SEFER HAZIRLIĞI — {site.name.ToUpperInvariant()}   <size=16><color=#cfcfc8>(Kâşif: {explorerName})</color></size>"
                : $"SIĞINAK ENVANTERİ & SEFER ÇANTASI   <size=16><color=#cfcfc8>(Kâşif: {explorerName})</color></size>";

            // Yük Çubuğu
            loadFill.fillAmount = capacity > 0 ? Mathf.Clamp01(gear / (float)capacity) : 1f;
            loadFill.color = free < 0 ? UiFactory.Danger : free <= 3 ? new Color(0.95f, 0.75f, 0.25f) : UiFactory.Good;

            var bagData = BuildBagSlotDataList(capacity, gear, free);
            int occupiedCount = 0;
            for (int i = 0; i < bagData.Count; i++) if (bagData[i].IsOccupied) occupiedCount++;

            loadLabel.text = free < 0
                ? $"AŞIRI YÜK: {gear} / {capacity} kg (Ganimet toplanamaz!)"
                : $"Taşınan Ekipman: {gear} kg / Kapasite: {capacity} kg{(companion ? " (Yoldaş +3 kg)" : "")}   •   Ganimete Kalan Boş: <b>{free} kg</b>   •   Dolu Slot: {occupiedCount}/12";

            // Sol Panel: Sığınak Yuvaları
            for (int i = 0; i < shelterSlots.Count && i < shelterDefs.Count; i++)
            {
                var view = shelterSlots[i];
                var def = shelterDefs[i];
                int stock = def.GetStock();
                bool available = def.IsAvailable();
                bool equipped = def.IsEquipped();

                view.Icon.text = def.Icon;
                view.Name.text = def.Name;
                view.Weight.text = def.WeightLabel;

                if (equipped)
                {
                    view.Background.color = new Color(0.18f, 0.42f, 0.28f, 0.98f);
                    view.Status.text = "<color=#88e070>[Çantada ✓]</color>";
                    view.QtyText.text = stock > 0 ? $"x{stock}" : "✓";
                    view.QtyPlate.SetActive(true);
                    view.Button.interactable = true;
                }
                else if (available && stock > 0)
                {
                    view.Background.color = new Color(0.18f, 0.20f, 0.26f, 0.95f);
                    view.Status.text = "<color=#cfcfc8>Depoda</color>";
                    view.QtyText.text = $"x{stock}";
                    view.QtyPlate.SetActive(true);
                    view.Button.interactable = true;
                }
                else if (available)
                {
                    view.Background.color = new Color(0.14f, 0.15f, 0.19f, 0.7f);
                    view.Status.text = "<color=#888>Tükendi</color>";
                    view.QtyPlate.SetActive(false);
                    view.Button.interactable = false;
                }
                else
                {
                    view.Background.color = new Color(0.10f, 0.11f, 0.13f, 0.45f);
                    view.Status.text = "<color=#666>Üretilmedi</color>";
                    view.QtyPlate.SetActive(false);
                    view.Button.interactable = false;
                }
            }

            // Sağ Panel: Çanta Yuvaları
            for (int i = 0; i < bagSlots.Count && i < bagData.Count; i++)
            {
                var view = bagSlots[i];
                var data = bagData[i];

                view.Background.color = data.BgColor;
                view.Icon.text = data.Icon;
                view.Name.text = data.Name;
                view.Weight.text = data.Weight;
                view.Status.text = data.Subtitle;

                if (!string.IsNullOrEmpty(data.Quantity))
                {
                    view.QtyPlate.SetActive(true);
                    view.QtyText.text = data.Quantity;
                }
                else
                {
                    view.QtyPlate.SetActive(false);
                }
            }

            // Saha İnceleme / Özel Noktalar
            if (site != null)
            {
                int locked = site.LockedCrates;
                int buried = site.BuriedCaches;
                int threats = site.dogs + site.raiders + site.brutes + site.throwers;
                string hazard = site.hazard switch
                {
                    ExpeditionHazard.Collapse => "<color=#ffcc33>Göçük / Tuzak</color>",
                    ExpeditionHazard.Fire => "<color=#ff5544>Yangın / Patlama</color>",
                    _ => threats > 2 ? "<color=#ff5544>Düşman / Yağmacı</color>" : "<color=#88e070>Düşük Risk</color>"
                };

                siteReconText.text =
                    $"<b>🎯 HEDEF SAHA İSTİHBARATI ({site.name}):</b>\n" +
                    $"• Tehlike / Yağmacı Riski: {hazard}\n" +
                    $"• Kilitli Çelik Kasalar: <b>{locked} adet</b> {(loadout.Crowbar ? "<color=#88e070>(Levye Hazır ✓)</color>" : "<color=#ff8f7a>(Levye Yok - Kasalar Açılamaz!)</color>")}\n" +
                    $"• Gömülü Sandıklar: <b>{buried} adet</b> {(loadout.Pickaxe ? "<color=#88e070>(Kazma Hazır ✓)</color>" : "<color=#ff8f7a>(Kazma Yok - Sandıklar Kazılamaz!)</color>")}";
            }
            else
            {
                siteReconText.text =
                    "<b>🗺️ GENEL SEFER HAZIRLIĞI:</b>\n" +
                    "Haritadan bir lokasyon seçip sefere çıkmadan önce donanımınızı buradan planlayabilirsiniz.\n" +
                    "Kâşifiniz sefere çıkarken yanında götüreceği bu eşyalarla hayatta kalacak ve ganimet toplayacaktır.";
            }

            departButtonLabel.text = site != null ? "Seferi Başlat & Yola Çık" : "Kaydet & Kapat";
            departButton.interactable = free >= 0;
        }
    }
}
