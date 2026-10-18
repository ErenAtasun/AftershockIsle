using System;
using System.IO;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Economy;
using AftershockIsle.Effects;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Sığınak Envanteri ve Çanta Penceresi (uGUI): Assets/Models/UI/inventory_sheet.png parşömen görseli
    /// üzerindeki 11 yuva üzerinde sığınağın erzak, cephane, silah, alet ve inşaat kaynaklarını gösterir.
    /// 'I' veya HUD üzerindeki çanta butonuna basılarak açılır/kapanır.
    /// </summary>
    public class InventoryWindow : MonoBehaviour
    {
        public static InventoryWindow Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance.isOpen;

        const float BaseWidth = 960f;
        const float BaseHeight = 789.4f; // 1024x842 aspect

        struct SlotCoord
        {
            public int Index;
            public string Name;
            public string Category;
            public string Description;
            public float NormX;
            public float NormY; // from top (0..1)
            public ResourceType? ResType;
            public Func<int> GetCount;
            public Action OnUse;
            public string ActionLabel;

            public SlotCoord(int index, string name, string category, string desc, float nx, float ny, ResourceType? res, Func<int> countGetter, Action onUse = null, string actionLabel = null)
            {
                Index = index;
                Name = name;
                Category = category;
                Description = desc;
                NormX = nx;
                NormY = ny;
                ResType = res;
                GetCount = countGetter;
                OnUse = onUse;
                ActionLabel = actionLabel;
            }
        }

        class SlotView
        {
            public SlotCoord Data;
            public Button Button;
            public Image Highlight;
            public Text QuantityText;
            public GameObject QuantityPlate;
        }

        static Sprite cachedSheetSprite;
        static Sprite cachedHighlightSprite;

        GameObject root;
        Canvas canvas;
        RectTransform sheetTransform;
        readonly System.Collections.Generic.List<SlotView> slots = new System.Collections.Generic.List<SlotView>();

        int selectedSlotIndex = 0;
        Text detailTitle;
        Text detailCategory;
        Text detailCount;
        Text detailDescription;
        Button detailActionButton;
        Text detailActionLabel;
        bool isOpen;

        void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            EnsureSprites();
            Build();
            Close();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            // Dış sahadayken sahanın kendi çantası (ağırlıklı çanta) açılır; sığınak envanteri açılmaz ve açıksa kapanır.
            bool inField = AftershockIsle.CameraControl.SideViewCamera.ViewingField || AftershockIsle.Building.PlacementController.IsPlacementActive;
            if (inField)
            {
                if (isOpen) Close();
                return;
            }

            // Sığınakta sabah envanteri açılmaz (This War of Mine mantığı; sadece sefer hazırlığında çanta açılır).
            if (!isOpen) return;

            ModalMenu.MarkOpen();

            if (GameInput.MenuBackPressed || (!GameInput.UsingGamepad && GameInput.InventoryPressed))
            {
                Close();
                return;
            }

            Refresh();
        }

        public void Toggle()
        {
            ExpeditionWindow.Instance?.OpenShelterInventory();
        }

        public void Open()
        {
            ExpeditionWindow.Instance?.OpenShelterInventory();
        }

        public void Close()
        {
            if (!isOpen && root != null && !root.activeSelf) return;
            isOpen = false;
            if (root != null) root.SetActive(false);
            SoundManager.Play(SoundId.EmptyClick, 0.4f);
        }

        void Build()
        {
            root = new GameObject("Inventory_Root", typeof(RectTransform));
            root.transform.SetParent(transform, false);

            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90; // HUD üstü

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            root.AddComponent<GraphicRaycaster>();

            // Karartma arka planı (modal tıklandığında kapatır)
            var dimObj = new GameObject("Dimmer", typeof(RectTransform), typeof(Image), typeof(Button));
            dimObj.transform.SetParent(root.transform, false);
            var dimImg = dimObj.GetComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.65f);
            var dimRect = dimObj.GetComponent<RectTransform>();
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.sizeDelta = Vector2.zero;
            dimObj.GetComponent<Button>().onClick.AddListener(Close);

            // Parşömen Çanta Paneli
            var sheetObj = new GameObject("InventorySheet", typeof(RectTransform), typeof(Image));
            sheetObj.transform.SetParent(root.transform, false);
            sheetTransform = sheetObj.GetComponent<RectTransform>();
            sheetTransform.anchorMin = new Vector2(0.5f, 0.5f);
            sheetTransform.anchorMax = new Vector2(0.5f, 0.5f);
            sheetTransform.anchoredPosition = new Vector2(0f, 25f);
            sheetTransform.sizeDelta = new Vector2(BaseWidth, BaseHeight);

            var sheetImage = sheetObj.GetComponent<Image>();
            sheetImage.sprite = cachedSheetSprite;
            sheetImage.color = Color.white;
            sheetImage.preserveAspect = true;

            // Kapat [X] Butonu (Parşömenin sol üstünde)
            var closeBtn = UiFactory.SpriteButton(sheetTransform, "Kapat", GameAssets.Ui != null ? GameAssets.Ui.closeButton : null,
                new Vector2(46f, 46f), Close, "X");
            UiFactory.Place((RectTransform)closeBtn.transform, new Vector2(0f, 1f), new Vector2(28f, -28f), new Vector2(46f, 46f));

            // Parşömen Başlık Etiketi
            var titleLabel = UiFactory.Label(sheetTransform, "SIĞINAK ENVANTERİ & ÇANTA", 20, new Color(0.24f, 0.20f, 0.16f, 0.95f), TextAnchor.MiddleLeft, FontStyle.Bold);
            UiFactory.Place(titleLabel.rectTransform, new Vector2(0f, 1f), new Vector2(85f, -32f), new Vector2(420f, 38f));

            // Sırt Çantası Rozeti Tıklama Alanı (Sağ üst yuvarlak)
            var bagBtnObj = new GameObject("BackpackBadge", typeof(RectTransform), typeof(Button), typeof(Image));
            bagBtnObj.transform.SetParent(sheetTransform, false);
            var bagRect = bagBtnObj.GetComponent<RectTransform>();
            bagRect.anchorMin = new Vector2(0f, 1f);
            bagRect.anchorMax = new Vector2(0f, 1f);
            bagRect.anchoredPosition = new Vector2(815f, -135f);
            bagRect.sizeDelta = new Vector2(175f, 175f);
            var bagImg = bagBtnObj.GetComponent<Image>();
            bagImg.color = new Color(1f, 1f, 1f, 0.001f); // Şeffaf ama tıklanabilir
            bagBtnObj.GetComponent<Button>().onClick.AddListener(() =>
            {
                selectedSlotIndex = -1;
                SoundManager.Play(SoundId.UiClick, 0.4f);
                RefreshDetails();
            });

            // 11 Yuvanın Tanımları
            var slotDefs = GetSlotDefinitions();

            float slotW = 154f;
            float slotH = 156f;

            foreach (var def in slotDefs)
            {
                var slotObj = new GameObject($"Slot_{def.Index}", typeof(RectTransform), typeof(Button), typeof(Image));
                slotObj.transform.SetParent(sheetTransform, false);

                var sRect = slotObj.GetComponent<RectTransform>();
                sRect.anchorMin = new Vector2(0f, 1f);
                sRect.anchorMax = new Vector2(0f, 1f);
                sRect.anchoredPosition = new Vector2(def.NormX * BaseWidth, -def.NormY * BaseHeight);
                sRect.sizeDelta = new Vector2(slotW, slotH);

                var btnImg = slotObj.GetComponent<Image>();
                btnImg.color = new Color(1f, 1f, 1f, 0.001f); // Görsel zaten arkada parşömende çizili

                // Seçim / Vurgu Çerçevesi
                var hlObj = new GameObject("Highlight", typeof(RectTransform), typeof(Image));
                hlObj.transform.SetParent(slotObj.transform, false);
                var hlRect = hlObj.GetComponent<RectTransform>();
                UiFactory.Stretch(hlRect, -4f);
                var hlImg = hlObj.GetComponent<Image>();
                hlImg.sprite = cachedHighlightSprite;
                hlImg.color = new Color(1f, 0.85f, 0.35f, 0f);
                hlImg.type = Image.Type.Sliced;

                // Miktar Plakası (Sağ alt köşe)
                var qtyPlate = UiFactory.Panel(slotObj.transform, "QtyPlate", new Color(0.12f, 0.13f, 0.16f, 0.92f));
                UiFactory.Place(qtyPlate.rectTransform, new Vector2(1f, 0f), new Vector2(-12f, 12f), new Vector2(62f, 26f));

                var qtyText = UiFactory.Label(qtyPlate.transform, "0", 16, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiFactory.Stretch(qtyText.rectTransform);

                int index = def.Index;
                var btn = slotObj.GetComponent<Button>();
                btn.onClick.AddListener(() =>
                {
                    selectedSlotIndex = index;
                    SoundManager.Play(SoundId.UiClick, 0.4f);
                    RefreshDetails();
                });

                slots.Add(new SlotView
                {
                    Data = def,
                    Button = btn,
                    Highlight = hlImg,
                    QuantityText = qtyText,
                    QuantityPlate = qtyPlate.gameObject
                });
            }

            // Alt Bilgi / Detay Kartı
            BuildDetailCard();
        }

        void BuildDetailCard()
        {
            var card = UiFactory.Panel(sheetTransform, "DetailCard", new Color(0.13f, 0.14f, 0.18f, 0.96f));
            UiFactory.Place(card.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 32f), new Vector2(BaseWidth - 60f, 96f));

            detailTitle = UiFactory.Label(card.transform, "", 18, UiFactory.Accent, TextAnchor.UpperLeft, FontStyle.Bold);
            UiFactory.Place(detailTitle.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(480f, 26f));

            detailCategory = UiFactory.Label(card.transform, "", 13, new Color(0.72f, 0.74f, 0.80f), TextAnchor.UpperLeft);
            UiFactory.Place(detailCategory.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -34f), new Vector2(480f, 18f));

            detailDescription = UiFactory.Label(card.transform, "", 13, new Color(0.88f, 0.88f, 0.85f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(detailDescription.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -54f), new Vector2(560f, 38f));

            detailCount = UiFactory.Label(card.transform, "", 17, Color.white, TextAnchor.MiddleRight, FontStyle.Bold);
            UiFactory.Place(detailCount.rectTransform, new Vector2(1f, 1f), new Vector2(-220f, -10f), new Vector2(180f, 30f));

            detailActionButton = UiFactory.TextButton(card.transform, "Kullan", new Vector2(190f, 42f), OnClickAction, UiFactory.Good, 15);
            UiFactory.Place((RectTransform)detailActionButton.transform, new Vector2(1f, 0.5f), new Vector2(-110f, 0f), new Vector2(190f, 42f));
            detailActionLabel = detailActionButton.GetComponentInChildren<Text>();
        }

        void Refresh()
        {
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 5f);

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                int count = slot.Data.GetCount != null ? slot.Data.GetCount() : 0;
                slot.QuantityText.text = count > 0 ? count.ToString() : "-";

                bool isSel = selectedSlotIndex == slot.Data.Index;
                if (isSel)
                {
                    slot.Highlight.color = new Color(1f, 0.82f, 0.28f, 0.85f * pulse);
                    slot.QuantityText.color = UiFactory.Accent;
                }
                else
                {
                    slot.Highlight.color = new Color(1f, 1f, 1f, count > 0 ? 0.15f : 0f);
                    slot.QuantityText.color = count > 0 ? Color.white : new Color(0.6f, 0.6f, 0.6f);
                }
            }

            RefreshDetails();
        }

        void RefreshDetails()
        {
            if (selectedSlotIndex == -1)
            {
                // Çanta & Kapasite Bilgisi
                detailTitle.text = "SAHA SIRT ÇANTASI & SIĞINAK DEPOSU";
                detailCategory.text = "TAŞIMA KAPASİTESİ VE LOJİSTİK";
                detailCount.text = "";
                detailDescription.text = $"Sığınak deposu sınırsızdır. Dış sefere çıkarken çanta kapasitesi {Expedition.ExpeditionLoadout.Capacity(false)} kg (yoldaşla +{Expedition.ExpeditionLoadout.CompanionCapacity}); silah, mermi, kit ve aletler de yer kaplar.";
                detailActionButton.gameObject.SetActive(false);
                return;
            }

            if (selectedSlotIndex >= 0 && selectedSlotIndex < slots.Count)
            {
                var slot = slots[selectedSlotIndex];
                int count = slot.Data.GetCount != null ? slot.Data.GetCount() : 0;

                detailTitle.text = slot.Data.Name.ToUpperInvariant();
                detailCategory.text = slot.Data.Category.ToUpperInvariant();
                detailCount.text = $"Depoda: <color=#ffcc33>{count}</color>";
                detailDescription.text = slot.Data.Description;

                if (!string.IsNullOrEmpty(slot.Data.ActionLabel) && slot.Data.OnUse != null)
                {
                    detailActionButton.gameObject.SetActive(true);
                    detailActionLabel.text = slot.Data.ActionLabel;
                    detailActionButton.interactable = count > 0;
                }
                else
                {
                    detailActionButton.gameObject.SetActive(false);
                }
            }
        }

        void OnClickAction()
        {
            if (selectedSlotIndex >= 0 && selectedSlotIndex < slots.Count)
            {
                var slot = slots[selectedSlotIndex];
                slot.Data.OnUse?.Invoke();
                Refresh();
            }
        }

        static SlotCoord[] GetSlotDefinitions()
        {
            // Normalleştirilmiş koordinatlar (inventory_sheet.png 1024x842)
            // Row 0: Y=0.2435 (Cols: 0.1758, 0.3794, 0.5825)
            // Row 1: Y=0.4792 (Cols: 0.1758, 0.3794, 0.5825, 0.7856)
            // Row 2: Y=0.7150 (Cols: 0.1758, 0.3794, 0.5825, 0.7856)

            return new[]
            {
                // Slot 0: Yaşam Kiti (İlk Yardım)
                new SlotCoord(0, "Yaşam Kiti (İlk Yardım & Erzak)", "Tıbbi & Gıda",
                    "Kazazedelerin canını yeniler, açlığı sıfırlar ve hastalık krizlerini önler. Seferde yaralanınca kullanılır.",
                    0.1758f, 0.2435f, ResourceType.Supplies,
                    () => ResourceStorage.Instance != null ? ResourceStorage.Instance.Get(ResourceType.Supplies) : 0,
                    UseLifeKitOnMostNeedy, "Kazazedeyi İyileştir (1 Kit)"),

                // Slot 1: Cephane & Mermi
                new SlotCoord(1, "Ateşli Silah Mermisi", "Mühimmat",
                    "Tabanca ve av tüfeğinde kullanılan 9mm / av fişeği cephanesi. Gece nöbetinde ve dış keşifte yağmacıları savuşturur.",
                    0.3794f, 0.2435f, null,
                    () => Crafting.Workbench.Instance != null ? Crafting.Workbench.Instance.Ammo : 0, null, null),

                // Slot 2: Ateşli Silah (Tabanca)
                new SlotCoord(2, "Hizmet Tabancası & Tüfek", "Silah",
                    "Gece nöbetçisi kapı başında kuşanır; yağmacı baskınını kan dökülmeden püskürtür. Seferde menzilli çatışma sağlar.",
                    0.5825f, 0.2435f, null,
                    () => CraftedWeaponCount(), null, null),

                // Slot 3: Hurda & Enkaz Parçası
                new SlotCoord(3, "Hurda & Enkaz Parçaları", "Temel İnşaat",
                    "Yıkıntı ve molozlardan toplanır. Sütun tamirleri, barikat örme, takviye kirişleri ve tezgah üretiminde harcanır.",
                    0.1758f, 0.4792f, ResourceType.Scrap,
                    () => ResourceStorage.Instance != null ? ResourceStorage.Instance.Get(ResourceType.Scrap) : 0),

                // Slot 4: Metal & Kalas
                new SlotCoord(4, "Metal Plaka & Ahşap Kalas", "Güçlendirme",
                    "Çelik sütun dikme, çökme emniyet kirişleri, su pompası ve korunaklı depo inşasında kullanılan sağlam malzeme.",
                    0.3794f, 0.4792f, ResourceType.Metal,
                    () => ResourceStorage.Instance != null ? ResourceStorage.Instance.Get(ResourceType.Metal) : 0),

                // Slot 5: Elektronik & Mikroçip
                new SlotCoord(5, "Elektronik Devre & Akü", "Mühendislik",
                    "Atölyeden ve yıkık kliniklerden çıkar. Elektrikli su pompası, telsiz sinyali ve yedek jeneratör için gereklidir.",
                    0.5825f, 0.4792f, ResourceType.Electronics,
                    () => ResourceStorage.Instance != null ? ResourceStorage.Instance.Get(ResourceType.Electronics) : 0),

                // Slot 6: Aletler (Levye & Kazma)
                new SlotCoord(6, "Keşif Aletleri (Levye & Kazma)", "Ekipman",
                    "Dış seferde kilitli kasaları (Levye) ve enkaz altına gömülü sandıkları (Kazma) açmayı sağlar.",
                    0.7856f, 0.4792f, null,
                    () => CraftedToolCount()),

                // Slot 7: Temiz Su Deposu
                new SlotCoord(7, "Çelik Kiriş", "Güçlendirme",
                    "Bir kolonu çelikle güçlendirir: sarsıntılardan etkilenmez. Çatıyı taşıyan kolonların hepsi güçlendirilmeden tahliye olunamaz.",
                    0.1758f, 0.7150f, null,
                    () => Crafting.Workbench.Instance != null ? Crafting.Workbench.Instance.SteelBeams : 0),

                // Slot 8: Yedek Parça & Jeneratör Donanımı
                new SlotCoord(8, "Telsiz Modülü", "Özel Görev",
                    "Çatıdaki telsiz istasyonunu tamir etmek için gerekir (Kademe 3). Tamirden sonra kesintisiz 60 sn sinyal gönderilir.",
                    0.3794f, 0.7150f, null,
                    () => Crafting.Workbench.IsCrafted(Crafting.RecipeId.RadioModule) ? 1 : 0),

                // Slot 9: Kuruluma Hazır Yapılar
                new SlotCoord(9, "Hazır İmalat Mobilyaları", "İnşaat Masası",
                    "İnşaat masasında önceden üretilmiş, katlara yerleştirilmeyi bekleyen depo rafları ve sedirler.",
                    0.5825f, 0.7150f, null,
                    () => Crafting.Workbench.Instance != null ? Crafting.Workbench.Instance.UnplacedInventory.Count : 0),

                // Slot 10: Sefer Ganimeti & Çanta
                new SlotCoord(10, "Sefer Yükü", "Lojistik",
                    "Seferdeki kâşifin çantasındaki ganimetin ağırlığı (kg). Dönünce doğrudan depoya geçer.",
                    0.7856f, 0.7150f, null,
                    () => Expedition.ExpeditionSystem.Instance != null && Expedition.ExpeditionSystem.Instance.Field != null
                        ? Expedition.ExpeditionSystem.Instance.Field.LootKg : 0),
            };
        }

        static int CraftedWeaponCount()
        {
            int count = 0;
            foreach (Expedition.WeaponKind kind in Enum.GetValues(typeof(Expedition.WeaponKind)))
                if (kind != Expedition.WeaponKind.Slingshot && Expedition.FieldWeapons.IsAvailable(kind)) count++;
            return count;
        }

        static int CraftedToolCount()
        {
            int count = 0;
            foreach (var recipe in Crafting.RecipeCatalog.All)
                if (recipe.IsTool && Crafting.Workbench.IsCrafted(recipe.Id)) count++;
            return count;
        }

        static void UseLifeKitOnMostNeedy()
        {
            var storage = ResourceStorage.Instance;
            if (storage == null || !storage.TrySpend(new ResourceAmount(ResourceType.Supplies, 1)))
            {
                SoundManager.Play(SoundId.EmptyClick, 0.5f);
                return;
            }

            // En çok hasar almış veya en aç kazazedeyi bul
            Survivor target = null;
            float maxNeed = -1f;
            foreach (var s in Survivor.All)
            {
                if (s == null || s.IsDead) continue;
                float need = (100f - s.Health) + (s.Condition.Hunger * 35f);
                if (need > maxNeed)
                {
                    maxNeed = need;
                    target = s;
                }
            }

            if (target != null)
            {
                target.Heal(35f);
                target.Condition.Feed(15f);
                target.Condition.AddStress(-15f);
                var chatter = target.GetComponent<SurvivorChatter>();
                if (chatter != null) chatter.Say("İlaç ve yemek çok iyi geldi... Teşekkürler!", 3.5f, Color.green);
                SoundManager.Play(SoundId.CraftSuccess, 0.6f);
            }
        }

        static void EnsureSprites()
        {
            // Önce derleme güvenli yol: arayüz görselleri kaydı (UiSkin); dosyadan okuma yalnızca yedek.
            if (cachedSheetSprite == null && GameAssets.Ui != null) cachedSheetSprite = GameAssets.Ui.inventorySheet;

            if (cachedSheetSprite == null)
            {
                string path = Path.Combine(Application.dataPath, "Models/UI/inventory_sheet.png");
                if (File.Exists(path))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(bytes, false))
                    {
                        tex.filterMode = FilterMode.Bilinear;
                        cachedSheetSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    }
                }
            }

            if (cachedHighlightSprite == null)
            {
                // Yuvarlak köşeli vurgu kutusu
                int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        bool isBorder = x < 4 || x >= size - 4 || y < 4 || y >= size - 4;
                        tex.SetPixel(x, y, isBorder ? Color.white : new Color(1f, 1f, 1f, 0.08f));
                    }
                }
                tex.Apply();
                cachedHighlightSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(8, 8, 8, 8));
            }
        }
    }
}
