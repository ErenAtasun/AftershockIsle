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
    /// Sefer hazırlığı ve Sığınak Envanteri (This War of Mine mantığı, iki panel):
    /// Sol: SIĞINAK DEPOSU — silahlar, mermi, Yaşam Kiti, aletler (tıklayınca çantaya geçer) ve altta taşınmayan stok şeridi.
    /// Sağ: KÂŞİFİN ÇANTASI — 12 yuva; tıklayınca eşya depoya döner. Ağırlık anında hesaplanır, üstteki yük çubuğuna yansır.
    /// Üstüne gelince / gamepad ile seçince eşyanın ayrıntısı alttaki kartta görünür. Görünüm yalnızca durum değişince tazelenir.
    /// Yerleşim ekrana oranlı (çapa tabanlı); emoji yerine kodla çizilen ikonlar (UiIcons) kullanılır.
    /// </summary>
    public class LoadoutPanel
    {
        const int BagSlotCount = 12;
        const float SlotWidth = 164f;
        const float SlotHeight = 112f;
        const float SlotGap = 10f;

        class SlotView
        {
            public GameObject Root;
            public Image Background;
            public Image Icon;
            public Text Name;
            public Text Status;
            public Text Weight;
            public GameObject QtyPlate;
            public Text QtyText;
            public Button Button;
        }

        class CarryDef
        {
            public string Name;
            public IconKind Icon;
            public Color Tint;
            public string Category;
            public string Weight;
            public Func<string> Describe;
            public Func<bool> IsAvailable;
            public Func<bool> InBag;
            public Func<int> StockLeft;     // depoda kalan (çantadakiler düşülmüş); -1: gösterme
            public Func<string> InBagText;  // çantada olduğunda durum yazısı
            public Action OnClick;
        }

        struct BagSlotData
        {
            public bool Occupied;
            public IconKind Icon;
            public Color Tint;
            public string Name;
            public string Weight;
            public string Quantity;
            public string Status;
            public string Category;
            public string Description;
            public Color Background;
            public Action OnClick;
        }

        class StockChip
        {
            public GameObject Root;
            public Image Icon;
            public Text Label;
            public Text Count;
            public Func<int> Value;
        }

        static readonly Color WeaponTint = new Color(0.82f, 0.88f, 0.96f);
        static readonly Color AmmoTint = new Color(0.95f, 0.78f, 0.4f);
        static readonly Color KitTint = new Color(0.55f, 0.92f, 0.62f);
        static readonly Color ToolTint = new Color(0.98f, 0.68f, 0.38f);
        static readonly Color BagTint = new Color(0.78f, 0.82f, 0.7f);

        static readonly Color SlotNormal = new Color(0.19f, 0.21f, 0.27f, 0.98f);
        static readonly Color SlotActive = new Color(0.17f, 0.4f, 0.27f, 0.99f);
        static readonly Color SlotDim = new Color(0.12f, 0.13f, 0.16f, 0.85f);
        static readonly Color SlotEmpty = new Color(0.11f, 0.12f, 0.15f, 0.7f);

        readonly UiWindow window;
        readonly Action onBack;
        readonly Action<ExpeditionLoadout> onDepart;

        ExpeditionLoadout loadout;
        ExpeditionSite site;
        int lastKey = int.MinValue;
        float warningUntil;

        Image loadFill;
        Text loadLabel;
        Text warningText;

        readonly List<SlotView> carrySlots = new List<SlotView>();
        readonly List<CarryDef> carryDefs = new List<CarryDef>();
        readonly List<StockChip> stockChips = new List<StockChip>();
        readonly List<SlotView> bagSlots = new List<SlotView>();
        List<BagSlotData> bagData = new List<BagSlotData>();

        Text inspectorTitle;
        Text inspectorCategory;
        Text inspectorStats;
        Text inspectorDesc;
        Text siteReconText;
        int inspectCarry = 0;
        int inspectBag = -1;

        Button clearButton;
        Button autoButton;
        Button departButton;
        Text departButtonLabel;

        public bool IsOpen => window.IsOpen;

        /// <summary>Şu anki seçim; sığınak envanterinden çıkarken ya da haritaya dönerken saklanır.</summary>
        public ExpeditionLoadout Loadout => loadout;

        public LoadoutPanel(Action back, Action<ExpeditionLoadout> depart)
        {
            onBack = back;
            onDepart = depart;
            window = new UiWindow("Envanter & Sefer Hazırlığı", new Vector2(1500f, 900f), () => onBack?.Invoke(), false, true);

            DefineCarryItems();
            BuildTopBar();
            BuildLeftPanel();
            BuildRightPanel();
            BuildFooter();
        }

        // ---------------------------------------------------------------- Kurulum

        static RectTransform Anchored(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        // Üstten y kadar aşağıda, yatayda kenarlardan inset içeride, height yüksekliğinde.
        static RectTransform TopStrip(RectTransform rect, float y, float height, float inset)
        {
            return Anchored(rect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(inset, -y - height), new Vector2(-inset, -y));
        }

        void BuildTopBar()
        {
            var bar = UiFactory.Rect("YukSatiri", window.Content);
            TopStrip(bar, 2f, 34f, 6f);
            loadFill = UiFactory.Bar(bar, "YukCubugu", new Vector2(100f, 34f), UiFactory.Good, out loadLabel, 16);
            Anchored((RectTransform)loadFill.transform.parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            warningText = UiFactory.Label(window.Content, "", 15, new Color(1f, 0.6f, 0.5f), TextAnchor.MiddleCenter, FontStyle.Bold);
            TopStrip(warningText.rectTransform, 40f, 24f, 6f);
        }

        RectTransform BuildPanel(string name, Color color, bool left, string title, string subtitle)
        {
            var panel = UiFactory.Panel(window.Content, name, color).rectTransform;
            if (left) Anchored(panel, new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(6f, 0f), new Vector2(-6f, -70f));
            else Anchored(panel, new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(6f, 0f), new Vector2(-6f, -70f));

            var header = UiFactory.Label(panel, title, 20, UiFactory.Accent, TextAnchor.UpperLeft);
            TopStrip(header.rectTransform, 12f, 28f, 18f);
            var sub = UiFactory.Label(panel, subtitle, 14, new Color(0.78f, 0.8f, 0.85f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            TopStrip(sub.rectTransform, 42f, 22f, 18f);
            var divider = UiFactory.Panel(panel, "Cizgi", new Color(0.28f, 0.3f, 0.37f, 0.9f));
            divider.raycastTarget = false;
            TopStrip(divider.rectTransform, 68f, 2f, 18f);
            return panel;
        }

        RectTransform BuildGrid(RectTransform parent, string name, float y, int rows)
        {
            var grid = UiFactory.Rect(name, parent);
            float height = rows * SlotHeight + (rows - 1) * SlotGap;
            Anchored(grid, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -y - height), new Vector2(-18f, -y));
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(SlotWidth, SlotHeight);
            layout.spacing = new Vector2(SlotGap, SlotGap);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 4;
            layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            layout.childAlignment = TextAnchor.UpperLeft;
            return grid;
        }

        void BuildLeftPanel()
        {
            var panel = BuildPanel("DepoPaneli", new Color(0.12f, 0.13f, 0.17f, 0.98f), true, "SIĞINAK DEPOSU",
                "Bir eşyaya tıkla: kâşifin çantasına geçer. Üstüne gelince ayrıntısı görünür.");

            var grid = BuildGrid(panel, "DepoIzgara", 82f, 3);
            for (int i = 0; i < carryDefs.Count; i++)
            {
                int index = i;
                carrySlots.Add(CreateSlot(grid, $"Depo_{i}", () =>
                {
                    inspectCarry = index;
                    inspectBag = -1;
                    carryDefs[index].OnClick?.Invoke();
                    UpdateInspector();
                }, () =>
                {
                    inspectCarry = index;
                    inspectBag = -1;
                    UpdateInspector();
                }));
            }

            // Taşınmayan stok şeridi.
            float stockY = 82f + 3 * SlotHeight + 2 * SlotGap + 18f;
            var stockLabel = UiFactory.Label(panel, "SIĞINAK STOĞU  (sefere götürülmez)", 14, UiFactory.Accent, TextAnchor.UpperLeft);
            TopStrip(stockLabel.rectTransform, stockY, 20f, 18f);

            var strip = UiFactory.Rect("StokSeridi", panel);
            TopStrip(strip, stockY + 26f, 58f, 18f);
            var row = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 8f;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;
            AddStockChip(strip, "Hurda", IconKind.Gear, () => Stock(ResourceType.Scrap));
            AddStockChip(strip, "Metal", IconKind.Block, () => Stock(ResourceType.Metal));
            AddStockChip(strip, "Elektronik", IconKind.Bolt, () => Stock(ResourceType.Electronics));
            AddStockChip(strip, "Erzak", IconKind.Can, () => Stock(ResourceType.Supplies));
            AddStockChip(strip, "Çelik Kiriş", IconKind.Beam, () => Workbench.Instance != null ? Workbench.Instance.SteelBeams : 0);

            // Nasıl çalışır?
            var card = UiFactory.Panel(panel, "BilgiKarti", new Color(0.15f, 0.17f, 0.22f, 0.97f));
            Anchored(card.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(18f, 16f), new Vector2(-18f, 156f));
            var title = UiFactory.Label(card.transform, "NASIL ÇALIŞIR", 15, UiFactory.Accent, TextAnchor.UpperLeft);
            TopStrip(title.rectTransform, 10f, 22f, 14f);
            var body = UiFactory.Label(card.transform,
                "• <b>Mermi ve Yaşam Kiti</b> sığınak stoğundan alınır; harcanmayan kısmı dönüşte depoya geri konur.\n" +
                "• <b>Levye</b> kilitli kasaları, <b>Kazma</b> gömülü sandıkları açar. Yanında yoksa o noktalar açılmaz.\n" +
                "• Çanta ne kadar doluysa sahada o kadar az ganimet taşırsın. 10 mermi = 1 kg.",
                14, new Color(0.88f, 0.9f, 0.94f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            Anchored(body.rectTransform, Vector2.zero, Vector2.one, new Vector2(14f, 10f), new Vector2(-14f, -36f));
        }

        void AddStockChip(RectTransform parent, string label, IconKind icon, Func<int> value)
        {
            var chip = UiFactory.Panel(parent, "Stok_" + label, new Color(0.17f, 0.19f, 0.24f, 0.98f));
            var img = UiFactory.Image(chip.transform, "Ikon", UiIcons.Get(icon), new Color(0.82f, 0.85f, 0.9f));
            UiFactory.Place(img.rectTransform, new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(34f, 34f));
            var name = UiFactory.Label(chip.transform, label, 13, new Color(0.78f, 0.8f, 0.85f), TextAnchor.UpperLeft, FontStyle.Normal);
            Anchored(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(50f, 0f), new Vector2(-4f, -6f));
            var count = UiFactory.Label(chip.transform, "0", 20, Color.white, TextAnchor.LowerLeft, FontStyle.Bold);
            Anchored(count.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.5f), new Vector2(50f, 4f), new Vector2(-4f, 0f));
            stockChips.Add(new StockChip { Root = chip.gameObject, Icon = img, Label = name, Count = count, Value = value });
        }

        void BuildRightPanel()
        {
            var panel = BuildPanel("CantaPaneli", new Color(0.13f, 0.15f, 0.2f, 0.98f), false, "KÂŞİFİN ÇANTASI",
                "Yanında götüreceği eşyalar. Çıkarmak için yuvaya tıkla.");

            var grid = BuildGrid(panel, "CantaIzgara", 82f, 3);
            for (int i = 0; i < BagSlotCount; i++)
            {
                int index = i;
                bagSlots.Add(CreateSlot(grid, $"Canta_{i}", () => OnBagSlotClicked(index), () =>
                {
                    inspectBag = index;
                    UpdateInspector();
                }));
            }

            // İnceleme kartı
            var card = UiFactory.Panel(panel, "IncelemeKarti", new Color(0.16f, 0.19f, 0.26f, 0.97f));
            Anchored(card.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(18f, 16f), new Vector2(-18f, 266f));

            inspectorTitle = UiFactory.Label(card.transform, "", 18, UiFactory.Accent, TextAnchor.UpperLeft);
            TopStrip(inspectorTitle.rectTransform, 10f, 26f, 14f);
            inspectorCategory = UiFactory.Label(card.transform, "", 13, new Color(0.74f, 0.77f, 0.84f), TextAnchor.UpperLeft, FontStyle.Normal);
            TopStrip(inspectorCategory.rectTransform, 38f, 20f, 14f);
            inspectorStats = UiFactory.Label(card.transform, "", 15, new Color(1f, 0.88f, 0.5f), TextAnchor.UpperLeft, FontStyle.Normal);
            TopStrip(inspectorStats.rectTransform, 60f, 22f, 14f);
            inspectorDesc = UiFactory.Label(card.transform, "", 14, UiFactory.Text, TextAnchor.UpperLeft, FontStyle.Normal, false);
            TopStrip(inspectorDesc.rectTransform, 86f, 72f, 14f);

            var sep = UiFactory.Panel(card.transform, "Ayirac", new Color(0.3f, 0.33f, 0.4f, 0.9f));
            sep.raycastTarget = false;
            TopStrip(sep.rectTransform, 160f, 1f, 14f);

            siteReconText = UiFactory.Label(card.transform, "", 14, new Color(0.86f, 0.89f, 0.95f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            Anchored(siteReconText.rectTransform, Vector2.zero, Vector2.one, new Vector2(14f, 8f), new Vector2(-14f, -168f));
        }

        SlotView CreateSlot(Transform parent, string name, Action onClick, Action onHover)
        {
            var button = UiFactory.TextButton(parent, "", new Vector2(SlotWidth, SlotHeight), onClick, SlotNormal, 12);
            button.gameObject.name = name;
            var stray = button.GetComponentInChildren<Text>();
            if (stray != null) UnityEngine.Object.Destroy(stray.gameObject);
            var press = button.GetComponent<UiPress>();
            if (press != null) press.ScaleOnHover = false;
            button.gameObject.AddComponent<UiSlotHover>().OnHover = onHover;

            var rect = (RectTransform)button.transform;
            var background = button.GetComponent<Image>();

            var icon = UiFactory.Image(rect, "Ikon", UiIcons.Get(IconKind.Plus), Color.white);
            UiFactory.Place(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(50f, 50f));

            var label = UiFactory.Label(rect, "", 14, UiFactory.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 10;
            label.resizeTextMaxSize = 14;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiFactory.Place(label.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(SlotWidth - 12f, 22f));

            var status = UiFactory.Label(rect, "", 12, new Color(0.76f, 0.78f, 0.84f), TextAnchor.MiddleCenter, FontStyle.Normal);
            UiFactory.Place(status.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(SlotWidth - 12f, 20f));

            var weight = UiFactory.Label(rect, "", 12, UiFactory.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            UiFactory.Place(weight.rectTransform, new Vector2(0f, 1f), new Vector2(8f, -6f), new Vector2(70f, 18f));

            var qtyPlate = UiFactory.Panel(rect, "Miktar", new Color(0.07f, 0.08f, 0.11f, 0.96f));
            qtyPlate.raycastTarget = false;
            UiFactory.Place(qtyPlate.rectTransform, new Vector2(1f, 1f), new Vector2(-6f, -6f), new Vector2(46f, 22f));
            var qty = UiFactory.Label(qtyPlate.transform, "", 14, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.Stretch(qty.rectTransform);

            return new SlotView
            {
                Root = button.gameObject,
                Background = background,
                Icon = icon,
                Name = label,
                Status = status,
                Weight = weight,
                QtyPlate = qtyPlate.gameObject,
                QtyText = qty,
                Button = button,
            };
        }

        void BuildFooter()
        {
            window.FooterButton($"Geri ({GameInput.Prompt(InputPrompt.Back)})", () => onBack?.Invoke(), null, 200f);
            clearButton = window.FooterButton("Hepsini Depoya Bırak", ClearAllToShelter, new Color(0.42f, 0.22f, 0.2f, 0.95f), 250f);
            autoButton = window.FooterButton("Önerilen Donanım", ApplyRecommended, UiFactory.SlateLight, 230f);
            departButton = window.FooterButton("Yola Çık", OnDepartClicked, UiFactory.Good, 280f);
            departButtonLabel = departButton.GetComponentInChildren<Text>();
        }

        // ---------------------------------------------------------------- Depo eşyaları (çantaya alınabilenler)

        static int Stock(ResourceType type) => ResourceStorage.Instance != null ? ResourceStorage.Instance.Get(type) : 0;

        static string WeaponDescription(WeaponKind kind)
        {
            var info = FieldWeapons.Get(kind);
            string damage = info.Pellets > 1 ? $"{info.Damage:0} x {info.Pellets} saçma" : $"{info.Damage:0}";
            float rate = info.Interval > 0.01f ? 1f / info.Interval : 0f;
            string noise = info.Noise <= 0f ? "Sessiz" : info.Noise >= 22f ? "Çok gürültülü" : "Gürültülü";
            return $"Hasar: <b>{damage}</b>   Menzil: <b>{info.Range:0} m</b>   Atış: <b>{rate:0.0}/sn</b>\n" +
                   (info.UsesAmmo ? $"{noise}; mermi harcar. {ExpeditionLoadout.AmmoPerKg} mermi = 1 kg." : "Sınırsız çakıl taşı; sessiz, mermi harcamaz.");
        }

        void AddWeapon(WeaponKind kind, IconKind icon, string category)
        {
            var info = FieldWeapons.Get(kind);
            carryDefs.Add(new CarryDef
            {
                Name = kind == WeaponKind.Slingshot ? "Sapan" : info.Name,
                Icon = icon,
                Tint = WeaponTint,
                Category = category,
                Weight = $"{ExpeditionLoadout.WeaponWeight(kind)} kg",
                Describe = () => WeaponDescription(kind),
                IsAvailable = () => FieldWeapons.IsAvailable(kind),
                InBag = () => loadout != null && loadout.Weapon == kind,
                StockLeft = () => -1,
                InBagText = () => "Kuşanıldı",
                OnClick = () => EquipWeapon(kind),
            });
        }

        void DefineCarryItems()
        {
            carryDefs.Clear();
            AddWeapon(WeaponKind.Slingshot, IconKind.Slingshot, "Temel silah");
            AddWeapon(WeaponKind.Pistol, IconKind.Pistol, "Ateşli silah");
            AddWeapon(WeaponKind.Smg, IconKind.Smg, "Otomatik silah");
            AddWeapon(WeaponKind.Shotgun, IconKind.Shotgun, "Ağır silah");
            AddWeapon(WeaponKind.AssaultRifle, IconKind.Rifle, "Askeri silah");
            AddWeapon(WeaponKind.SniperRifle, IconKind.Sniper, "Uzun menzilli silah");

            carryDefs.Add(new CarryDef
            {
                Name = "Mermi",
                Icon = IconKind.Bullet,
                Tint = AmmoTint,
                Category = "Cephane",
                Weight = "10 mermi = 1 kg",
                Describe = () => $"Tıklayınca çantaya {ExpeditionLoadout.AmmoStep * 2} mermi daha geçer. Kuşandığın silah mermi kullanmıyorsa (sapan) alınamaz.\nHarcanmayan mermi dönüşte depoya geri konur.",
                IsAvailable = () => ExpeditionLoadout.AmmoInShelter > 0,
                InBag = () => loadout != null && loadout.Ammo > 0,
                StockLeft = () => Mathf.Max(0, ExpeditionLoadout.AmmoInShelter - (loadout != null ? loadout.Ammo : 0)),
                InBagText = () => $"Çantada: {loadout.Ammo}",
                OnClick = () => AddAmmo(ExpeditionLoadout.AmmoStep * 2),
            });

            carryDefs.Add(new CarryDef
            {
                Name = "Yaşam Kiti",
                Icon = IconKind.Kit,
                Tint = KitTint,
                Category = "Tıbbi malzeme",
                Weight = "1 kg",
                Describe = () => $"Sahada {GameInput.Prompt(InputPrompt.Heal)} tuşuyla +{35} can verir. En fazla {ExpeditionLoadout.MaxKits} kit taşınır; kullanılmayan kit dönüşte depoya geri konur.",
                IsAvailable = () => ExpeditionLoadout.KitsInShelter > 0,
                InBag = () => loadout != null && loadout.Kits > 0,
                StockLeft = () => Mathf.Max(0, ExpeditionLoadout.KitsInShelter - (loadout != null ? loadout.Kits : 0)),
                InBagText = () => $"Çantada: {loadout.Kits}",
                OnClick = () => AddKit(1),
            });

            carryDefs.Add(new CarryDef
            {
                Name = "Kazma",
                Icon = IconKind.Pickaxe,
                Tint = ToolTint,
                Category = "Keşif aleti",
                Weight = $"{ExpeditionLoadout.PickaxeWeight} kg",
                Describe = () => "Moloz altındaki gömülü sandıkları kazar (bol ganimet). Yanında yoksa o sandıklar açılmaz.\n" +
                                 (ToolKit.HasPickaxe ? "" : "Tezgahta üretilmedi (Kademe 2)."),
                IsAvailable = () => ToolKit.HasPickaxe,
                InBag = () => loadout != null && loadout.Pickaxe,
                StockLeft = () => -1,
                InBagText = () => "Çantada",
                OnClick = TogglePickaxe,
            });

            carryDefs.Add(new CarryDef
            {
                Name = "Levye",
                Icon = IconKind.Crowbar,
                Tint = ToolTint,
                Category = "Keşif aleti",
                Weight = $"{ExpeditionLoadout.CrowbarWeight} kg",
                Describe = () => "Kilitli çelik kasaları açar (bol ganimet). Yanında yoksa kilitli kasalar açılmaz.\n" +
                                 (ToolKit.HasCrowbar ? "" : "Tezgahta üretilmedi."),
                IsAvailable = () => ToolKit.HasCrowbar,
                InBag = () => loadout != null && loadout.Crowbar,
                StockLeft = () => -1,
                InBagText = () => "Çantada",
                OnClick = ToggleCrowbar,
            });

            carryDefs.Add(new CarryDef
            {
                Name = "Büyük Çanta",
                Icon = IconKind.Backpack,
                Tint = BagTint,
                Category = "Pasif teçhizat",
                Weight = "+3 kg yer",
                Describe = () => "Tezgahta üretilince taşıma kapasiteni kalıcı olarak +3 kg artırır; ayrıca taşınmaz, hep etkindir.",
                IsAvailable = () => ToolKit.HasBackpack,
                InBag = () => ToolKit.HasBackpack,
                StockLeft = () => -1,
                InBagText = () => "Etkin",
                OnClick = () => ShowWarning(ToolKit.HasBackpack ? "Büyük Çanta zaten etkin: kapasiteyi +3 kg artırıyor." : "Büyük Çanta henüz üretilmedi."),
            });
        }

        // ---------------------------------------------------------------- Çanta yuvaları

        void BuildBagData(int freeKg)
        {
            bagData.Clear();

            if (loadout.Weapon != WeaponKind.Slingshot)
            {
                var info = FieldWeapons.Get(loadout.Weapon);
                bagData.Add(new BagSlotData
                {
                    Occupied = true,
                    Icon = WeaponIcon(loadout.Weapon),
                    Tint = WeaponTint,
                    Name = info.Name,
                    Weight = $"{ExpeditionLoadout.WeaponWeight(loadout.Weapon)} kg",
                    Quantity = "",
                    Status = "Kuşanıldı",
                    Category = "Kuşanılan silah",
                    Description = WeaponDescription(loadout.Weapon) + "\n<color=#ff9f8a>Tıkla: çantadan çıkar (yerine sapan kalır).</color>",
                    Background = SlotActive,
                    OnClick = () => EquipWeapon(WeaponKind.Slingshot),
                });
            }
            else
            {
                bagData.Add(new BagSlotData
                {
                    Occupied = true,
                    Icon = IconKind.Slingshot,
                    Tint = WeaponTint,
                    Name = "Sapan",
                    Weight = "0 kg",
                    Quantity = "",
                    Status = "Temel silah",
                    Category = "Her zaman yanında",
                    Description = WeaponDescription(WeaponKind.Slingshot),
                    Background = SlotNormal,
                    OnClick = () => ShowWarning("Sapan her zaman cebinde; çıkarılamaz."),
                });
            }

            if (loadout.Ammo > 0)
            {
                int step = Mathf.Min(ExpeditionLoadout.AmmoStep * 2, loadout.Ammo);
                bagData.Add(new BagSlotData
                {
                    Occupied = true,
                    Icon = IconKind.Bullet,
                    Tint = AmmoTint,
                    Name = "Mermi",
                    Weight = $"{ExpeditionLoadout.AmmoWeight(loadout.Ammo)} kg",
                    Quantity = $"x{loadout.Ammo}",
                    Status = $"Tıkla: -{step}",
                    Category = "Cephane",
                    Description = $"{loadout.Ammo} mermi ({ExpeditionLoadout.AmmoWeight(loadout.Ammo)} kg).\n<color=#ff9f8a>Tıkla: {step} mermiyi depoya geri gönder.</color>",
                    Background = SlotNormal,
                    OnClick = () => RemoveAmmo(ExpeditionLoadout.AmmoStep * 2),
                });
            }

            if (loadout.Kits > 0)
            {
                bagData.Add(new BagSlotData
                {
                    Occupied = true,
                    Icon = IconKind.Kit,
                    Tint = KitTint,
                    Name = "Yaşam Kiti",
                    Weight = $"{loadout.Kits} kg",
                    Quantity = $"x{loadout.Kits}",
                    Status = "Tıkla: -1",
                    Category = "Tıbbi malzeme",
                    Description = $"Sahada {GameInput.Prompt(InputPrompt.Heal)} ile +35 can.\n<color=#ff9f8a>Tıkla: bir kiti depoya geri gönder.</color>",
                    Background = SlotNormal,
                    OnClick = () => RemoveKit(1),
                });
            }

            if (loadout.Pickaxe)
            {
                bagData.Add(new BagSlotData
                {
                    Occupied = true,
                    Icon = IconKind.Pickaxe,
                    Tint = ToolTint,
                    Name = "Kazma",
                    Weight = $"{ExpeditionLoadout.PickaxeWeight} kg",
                    Quantity = "",
                    Status = "Gömülü sandık",
                    Category = "Keşif aleti",
                    Description = "Moloz altındaki gömülü sandıkları kazar.\n<color=#ff9f8a>Tıkla: depoya geri gönder.</color>",
                    Background = SlotNormal,
                    OnClick = TogglePickaxe,
                });
            }

            if (loadout.Crowbar)
            {
                bagData.Add(new BagSlotData
                {
                    Occupied = true,
                    Icon = IconKind.Crowbar,
                    Tint = ToolTint,
                    Name = "Levye",
                    Weight = $"{ExpeditionLoadout.CrowbarWeight} kg",
                    Quantity = "",
                    Status = "Kilitli kasa",
                    Category = "Keşif aleti",
                    Description = "Kilitli çelik kasaları açar.\n<color=#ff9f8a>Tıkla: depoya geri gönder.</color>",
                    Background = SlotNormal,
                    OnClick = ToggleCrowbar,
                });
            }

            bool first = true;
            while (bagData.Count < BagSlotCount)
            {
                bagData.Add(new BagSlotData
                {
                    Occupied = false,
                    Icon = IconKind.Plus,
                    Tint = new Color(1f, 1f, 1f, 0.28f),
                    Name = first ? "Ganimet yeri" : "",
                    Weight = "",
                    Quantity = "",
                    Status = first ? (freeKg > 0 ? $"{freeKg} kg boş" : "Dolu") : "",
                    Category = "Boş yuva",
                    Description = $"Sahadan toplayacağın hurda, metal, erzak ve elektronik burada taşınır. Şu an {Mathf.Max(0, freeKg)} kg ganimete yer var.",
                    Background = SlotEmpty,
                    OnClick = () => ShowWarning("Soldaki depo eşyalarına tıklayarak çantaya eşya ekleyebilirsin."),
                });
                first = false;
            }
        }

        static IconKind WeaponIcon(WeaponKind kind) => kind switch
        {
            WeaponKind.Pistol => IconKind.Pistol,
            WeaponKind.Smg => IconKind.Smg,
            WeaponKind.Shotgun => IconKind.Shotgun,
            WeaponKind.AssaultRifle => IconKind.Rifle,
            WeaponKind.SniperRifle => IconKind.Sniper,
            _ => IconKind.Slingshot,
        };

        void OnBagSlotClicked(int index)
        {
            if (index < 0 || index >= bagData.Count) return;
            inspectBag = index;
            bagData[index].OnClick?.Invoke();
            UpdateInspector();
        }

        // ---------------------------------------------------------------- İnceleme kartı

        void UpdateInspector()
        {
            if (inspectBag >= 0 && inspectBag < bagData.Count)
            {
                var item = bagData[inspectBag];
                inspectorTitle.text = string.IsNullOrEmpty(item.Name) ? "BOŞ YUVA" : item.Name.ToUpperInvariant();
                inspectorCategory.text = item.Occupied ? $"ÇANTADA  ·  {item.Category}" : item.Category.ToUpperInvariant();
                inspectorStats.text = item.Occupied ? $"Ağırlık: <b>{item.Weight}</b>{(string.IsNullOrEmpty(item.Quantity) ? "" : $"   Miktar: <b>{item.Quantity}</b>")}" : "";
                inspectorDesc.text = item.Description;
                return;
            }

            if (inspectCarry < 0 || inspectCarry >= carryDefs.Count) return;
            var def = carryDefs[inspectCarry];
            int left = def.StockLeft();
            inspectorTitle.text = def.Name.ToUpperInvariant();
            inspectorCategory.text = $"DEPODA  ·  {def.Category}";
            inspectorStats.text = $"Ağırlık: <b>{def.Weight}</b>" + (left >= 0 ? $"   Depoda kalan: <b>{left}</b>" : "") +
                                  (def.InBag() ? "   <color=#88e070>Çantada</color>" : "");
            inspectorDesc.text = def.Describe() + (def.IsAvailable() ? "\n<color=#cfcfc8>Tıkla: çantaya al / çıkar.</color>" : "");
        }

        void ShowWarning(string message)
        {
            warningText.text = message;
            warningUntil = Time.unscaledTime + 4f;
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.EmptyClick, 0.45f);
        }

        void ClearWarning()
        {
            warningText.text = "";
            warningUntil = 0f;
        }

        // ---------------------------------------------------------------- Açma / Kapama

        public void Open(ExpeditionSite target, ExpeditionLoadout previous)
        {
            site = target;
            loadout = previous ?? ExpeditionLoadout.Default();
            loadout.Clamp();
            ClearWarning();
            inspectCarry = 0;
            inspectBag = -1;
            lastKey = int.MinValue;
            window.Show(true);
            Refresh();

            if (EventSystem.current != null && carrySlots.Count > 0)
                EventSystem.current.SetSelectedGameObject(carrySlots[0].Button.gameObject);
        }

        public void Close() => window.Show(false);

        public void Tick()
        {
            window.Tick(carrySlots.Count > 0 ? carrySlots[0].Button.gameObject : departButton.gameObject);
            if (warningUntil > 0f && Time.unscaledTime > warningUntil) ClearWarning();

            // Her kare değil, yalnızca durum değişince tazele (takılma ve çöp üretimi olmasın).
            int key = StateKey();
            if (key != lastKey) Refresh();
        }

        int StateKey()
        {
            var expeditions = ExpeditionSystem.Instance;
            unchecked
            {
                int h = 17;
                h = h * 31 + (int)loadout.Weapon;
                h = h * 31 + loadout.Ammo;
                h = h * 31 + loadout.Kits;
                h = h * 31 + (loadout.Pickaxe ? 1 : 0) + (loadout.Crowbar ? 2 : 0);
                h = h * 31 + ExpeditionLoadout.AmmoInShelter;
                h = h * 31 + ExpeditionLoadout.KitsInShelter;
                h = h * 31 + (expeditions != null && expeditions.Companion != null ? 1 : 0);
                h = h * 31 + (ToolKit.HasPickaxe ? 1 : 0) + (ToolKit.HasCrowbar ? 2 : 0) + (ToolKit.HasBackpack ? 4 : 0);
                h = h * 31 + (FieldWeapons.IsAvailable(WeaponKind.Pistol) ? 1 : 0) + (FieldWeapons.IsAvailable(WeaponKind.Smg) ? 2 : 0) +
                    (FieldWeapons.IsAvailable(WeaponKind.Shotgun) ? 4 : 0) + (FieldWeapons.IsAvailable(WeaponKind.AssaultRifle) ? 8 : 0) +
                    (FieldWeapons.IsAvailable(WeaponKind.SniperRifle) ? 16 : 0);
                foreach (var chip in stockChips) h = h * 31 + chip.Value();
                return h;
            }
        }

        // ---------------------------------------------------------------- Eşya taşıma

        static int Capacity()
        {
            var expeditions = ExpeditionSystem.Instance;
            return ExpeditionLoadout.Capacity(expeditions != null && expeditions.Companion != null);
        }

        void Changed(AftershockIsle.Effects.SoundId sound = AftershockIsle.Effects.SoundId.UiClick)
        {
            ClearWarning();
            AftershockIsle.Effects.SoundManager.Play(sound, 0.5f);
            Refresh();
        }

        void EquipWeapon(WeaponKind kind)
        {
            if (!FieldWeapons.IsAvailable(kind))
            {
                ShowWarning("Bu silah henüz tezgahta üretilmedi.");
                return;
            }
            if (loadout.Weapon == kind && kind != WeaponKind.Slingshot)
            {
                // Aynı silaha tekrar tıklamak onu çantadan çıkarır.
                kind = WeaponKind.Slingshot;
            }

            int capacity = Capacity();
            int diff = ExpeditionLoadout.WeaponWeight(kind) - ExpeditionLoadout.WeaponWeight(loadout.Weapon);
            if (loadout.GearWeight() + diff > capacity)
            {
                ShowWarning($"Çanta kapasitesi doldu: en fazla {capacity} kg taşınabilir.");
                return;
            }

            loadout.Weapon = kind;
            if (!FieldWeapons.Get(kind).UsesAmmo) loadout.Ammo = 0;
            else if (loadout.Ammo == 0 && ExpeditionLoadout.AmmoInShelter >= 10 && capacity - loadout.GearWeight() >= 1) loadout.Ammo = 10;
            Changed();
        }

        void AddAmmo(int amount)
        {
            if (!FieldWeapons.Get(loadout.Weapon).UsesAmmo)
            {
                ShowWarning("Sapan mermi kullanmaz: önce ateşli bir silah al.");
                return;
            }

            int shelter = ExpeditionLoadout.AmmoInShelter;
            if (loadout.Ammo >= shelter)
            {
                ShowWarning("Depodaki bütün mermiler zaten çantada.");
                return;
            }

            int newAmmo = Mathf.Min(loadout.Ammo + amount, shelter);
            if (loadout.GearWeight(newAmmo, loadout.Kits) > Capacity())
            {
                ShowWarning($"Çanta kapasitesi doldu: en fazla {Capacity()} kg taşınabilir.");
                return;
            }

            loadout.Ammo = newAmmo;
            Changed(AftershockIsle.Effects.SoundId.Pickup);
        }

        void RemoveAmmo(int amount)
        {
            loadout.Ammo = Mathf.Max(0, loadout.Ammo - amount);
            Changed();
        }

        void AddKit(int amount)
        {
            int shelter = ExpeditionLoadout.KitsInShelter;
            if (loadout.Kits >= shelter)
            {
                ShowWarning("Depoda başka Yaşam Kiti kalmadı.");
                return;
            }
            if (loadout.Kits >= ExpeditionLoadout.MaxKits)
            {
                ShowWarning($"Kâşif en fazla {ExpeditionLoadout.MaxKits} Yaşam Kiti taşıyabilir.");
                return;
            }

            int newKits = Mathf.Min(loadout.Kits + amount, Mathf.Min(ExpeditionLoadout.MaxKits, shelter));
            if (loadout.GearWeight(loadout.Ammo, newKits) > Capacity())
            {
                ShowWarning($"Çanta kapasitesi doldu: en fazla {Capacity()} kg taşınabilir.");
                return;
            }

            loadout.Kits = newKits;
            Changed(AftershockIsle.Effects.SoundId.Pickup);
        }

        void RemoveKit(int amount)
        {
            loadout.Kits = Mathf.Max(0, loadout.Kits - amount);
            Changed();
        }

        void TogglePickaxe()
        {
            if (!ToolKit.HasPickaxe)
            {
                ShowWarning("Kazma henüz tezgahta üretilmedi.");
                return;
            }
            if (!loadout.Pickaxe && loadout.GearWeight() + ExpeditionLoadout.PickaxeWeight > Capacity())
            {
                ShowWarning($"Kazma ({ExpeditionLoadout.PickaxeWeight} kg) için çantada yer yok (kapasite {Capacity()} kg).");
                return;
            }

            loadout.Pickaxe = !loadout.Pickaxe;
            Changed();
        }

        void ToggleCrowbar()
        {
            if (!ToolKit.HasCrowbar)
            {
                ShowWarning("Levye henüz tezgahta üretilmedi.");
                return;
            }
            if (!loadout.Crowbar && loadout.GearWeight() + ExpeditionLoadout.CrowbarWeight > Capacity())
            {
                ShowWarning($"Levye ({ExpeditionLoadout.CrowbarWeight} kg) için çantada yer yok (kapasite {Capacity()} kg).");
                return;
            }

            loadout.Crowbar = !loadout.Crowbar;
            Changed();
        }

        void ClearAllToShelter()
        {
            loadout.Weapon = WeaponKind.Slingshot;
            loadout.Ammo = 0;
            loadout.Kits = 0;
            loadout.Pickaxe = false;
            loadout.Crowbar = false;
            Changed(AftershockIsle.Effects.SoundId.Pickup);
            ShowWarning("Çantadaki her şey depoya geri bırakıldı.");
        }

        void ApplyRecommended()
        {
            loadout.Weapon = FieldWeapons.Best();
            loadout.Ammo = FieldWeapons.Get(loadout.Weapon).UsesAmmo ? Mathf.Min(15, ExpeditionLoadout.AmmoInShelter) : 0;
            loadout.Kits = Mathf.Min(1, ExpeditionLoadout.KitsInShelter);
            loadout.Pickaxe = false;
            loadout.Crowbar = false;

            int capacity = Capacity();
            if (site != null && site.LockedCrates > 0 && ToolKit.HasCrowbar && loadout.GearWeight() + ExpeditionLoadout.CrowbarWeight <= capacity) loadout.Crowbar = true;
            if (site != null && site.BuriedCaches > 0 && ToolKit.HasPickaxe && loadout.GearWeight() + ExpeditionLoadout.PickaxeWeight <= capacity) loadout.Pickaxe = true;

            // Ağırlık aşıldıysa mermiyi azalt.
            while (loadout.GearWeight() > capacity && loadout.Ammo > 0) loadout.Ammo = Mathf.Max(0, loadout.Ammo - 5);
            Changed(AftershockIsle.Effects.SoundId.Pickup);
            ShowWarning("Önerilen sefer donanımı hazırlandı.");
        }

        void OnDepartClicked()
        {
            // Sefer seçilmemişse (sığınak envanteri) yalnızca seçimi kaydedip kapatır; ExpeditionWindow kaydı üstlenir.
            onDepart?.Invoke(loadout);
        }

        // ---------------------------------------------------------------- Görünüm

        void Refresh()
        {
            var expeditions = ExpeditionSystem.Instance;
            bool companion = expeditions != null && expeditions.Companion != null;
            int capacity = ExpeditionLoadout.Capacity(companion);
            int gear = loadout.GearWeight();
            int free = capacity - gear;
            lastKey = StateKey();

            string explorerName = expeditions != null && expeditions.Explorer != null ? expeditions.Explorer.DisplayName : "Seçilmedi";
            window.Title.text = site != null
                ? $"SEFER HAZIRLIĞI — {site.name.ToUpperInvariant()}   <size=16><color=#cfcfc8>kâşif: {explorerName}</color></size>"
                : $"SIĞINAK ENVANTERİ   <size=16><color=#cfcfc8>kâşif: {explorerName}</color></size>";

            loadFill.fillAmount = capacity > 0 ? Mathf.Clamp01(gear / (float)capacity) : 1f;
            loadFill.color = free < 0 ? UiFactory.Danger : free <= 2 ? new Color(0.95f, 0.75f, 0.25f) : UiFactory.Good;

            BuildBagData(free);
            int occupied = 0;
            foreach (var slot in bagData) if (slot.Occupied) occupied++;

            loadLabel.text = free < 0
                ? $"AŞIRI YÜK: {gear} / {capacity} kg — ganimet taşınamaz"
                : $"Ekipman {gear} kg / çanta {capacity} kg{(companion ? " (yoldaş dahil)" : "")}   ·   ganimete <b>{free} kg</b> yer   ·   dolu yuva {occupied}/{BagSlotCount}";

            for (int i = 0; i < carrySlots.Count && i < carryDefs.Count; i++) RefreshCarrySlot(carrySlots[i], carryDefs[i]);

            for (int i = 0; i < bagSlots.Count && i < bagData.Count; i++)
            {
                var view = bagSlots[i];
                var data = bagData[i];
                view.Background.color = data.Background;
                view.Icon.sprite = UiIcons.Get(data.Icon);
                view.Icon.color = data.Tint;
                view.Name.text = data.Name;
                view.Status.text = data.Status;
                view.Weight.text = data.Weight;
                bool hasQty = !string.IsNullOrEmpty(data.Quantity);
                if (view.QtyPlate.activeSelf != hasQty) view.QtyPlate.SetActive(hasQty);
                if (hasQty) view.QtyText.text = data.Quantity;
                view.Button.interactable = true;
            }

            // Olmayan stok gösterilmez.
            foreach (var chip in stockChips)
            {
                int value = chip.Value();
                if (chip.Root.activeSelf != value > 0) chip.Root.SetActive(value > 0);
                chip.Count.text = value.ToString();
            }

            if (site != null)
            {
                string hazard = site.hazard switch
                {
                    ExpeditionHazard.Collapse => "<color=#ffcc33>göçük / tuzak</color>",
                    ExpeditionHazard.Fire => "<color=#ff6655>yangın / patlama</color>",
                    _ => (site.dogs + site.raiders + site.brutes + site.throwers) > 2 ? "<color=#ff6655>yağmacılar</color>" : "<color=#88e070>düşük risk</color>",
                };

                siteReconText.text =
                    $"<b>{site.name.ToUpperInvariant()} — saha bilgisi</b>\n" +
                    $"• Tehlike: {hazard}\n" +
                    $"• Kilitli kasa: <b>{site.LockedCrates}</b>  {(site.LockedCrates == 0 ? "" : loadout.Crowbar ? "<color=#88e070>(levye hazır)</color>" : "<color=#ff9f8a>(levye yok: açılamaz)</color>")}\n" +
                    $"• Gömülü sandık: <b>{site.BuriedCaches}</b>  {(site.BuriedCaches == 0 ? "" : loadout.Pickaxe ? "<color=#88e070>(kazma hazır)</color>" : "<color=#ff9f8a>(kazma yok: kazılamaz)</color>")}";
            }
            else
            {
                siteReconText.text =
                    "<b>SEFER HAZIRLIĞI</b>\nBir bölge seçip sefere çıkmadan önce donanımını buradan hazırla. " +
                    "Seçimin kaydedilir; sefere çıkarken bu eşyalar yanında olur.";
            }

            departButtonLabel.text = site != null ? "Yola Çık" : "Kaydet ve Kapat";
            departButton.interactable = free >= 0;
            UpdateInspector();
        }

        void RefreshCarrySlot(SlotView view, CarryDef def)
        {
            bool available = def.IsAvailable();
            bool inBag = def.InBag();
            int left = def.StockLeft();

            // Sahip olunmayan (üretilmemiş / tükenmiş) eşya gösterilmez; ızgara kendiliğinden toplanır.
            bool visible = available || inBag;
            if (view.Root.activeSelf != visible) view.Root.SetActive(visible);
            if (!visible) return;

            view.Icon.sprite = UiIcons.Get(def.Icon);
            view.Name.text = def.Name;
            view.Weight.text = def.Weight;

            string status;
            Color background;
            bool interactable = true;
            if (!available && left < 0)
            {
                status = "<color=#7a7e88>Üretilmedi</color>";
                background = SlotDim;
                interactable = false;
            }
            else if (!available)
            {
                status = inBag ? $"<color=#9be37a>{def.InBagText()}</color>" : "<color=#7a7e88>Tükendi</color>";
                background = inBag ? SlotActive : SlotDim;
                interactable = inBag;
            }
            else if (inBag)
            {
                status = $"<color=#9be37a>{def.InBagText()}</color>";
                background = SlotActive;
            }
            else
            {
                status = "Depoda";
                background = SlotNormal;
            }

            view.Background.color = background;
            view.Icon.color = available || inBag ? def.Tint : new Color(def.Tint.r, def.Tint.g, def.Tint.b, 0.35f);
            view.Status.text = status;
            view.Button.interactable = interactable;

            bool showQty = left >= 0 && (available || inBag);
            if (view.QtyPlate.activeSelf != showQty) view.QtyPlate.SetActive(showQty);
            if (showQty) view.QtyText.text = $"x{left}";
        }
    }

    /// <summary>Üstüne gelince ya da gamepad ile seçilince inceleme kartını günceller.</summary>
    public class UiSlotHover : MonoBehaviour, IPointerEnterHandler, ISelectHandler
    {
        public Action OnHover;
        public void OnPointerEnter(PointerEventData eventData) => OnHover?.Invoke();
        public void OnSelect(BaseEventData eventData) => OnHover?.Invoke();
    }
}
