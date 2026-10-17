using System.Collections.Generic;
using System.IO;
using AftershockIsle.Core;
using AftershockIsle.Skills;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// K / D-pad sağ ile açılan dairesel düğüm ağacı (uGUI): Assets/Models/UI/skilltree.png görselindeki
    /// 9 dairesel yetenek düğümü, bağlantı çizgileri ve yay üzerinde etkileşimli olarak gösterilir.
    /// Her düğüm kendi ikonunu taşır; açık (yeşil), açılabilir (altın) ve kilitli (gri) durumlarına göre
    /// renklenir. Üzerine gelinen/seçilen düğümün detayları alt bilgi çubuğunda gösterilir; düğüme tıklamak
    /// veya alt çubuktaki butona basmak yeteneği açar.
    /// </summary>
    public class SkillTreeWindow : MonoBehaviour
    {
        const float RefreshSeconds = 0.2f;
        const float SourceWidth = 1024f;
        const float SourceHeight = 576f;
        const float SourceCircleRadius = 51.5f;

        const float GraphWidth = 940f;
        const float GraphHeight = 528.75f; // 16:9 (1024x576 ile birebir oran)

        static readonly Color UnlockedTint = new Color(0.50f, 0.94f, 0.56f, 1f);
        static readonly Color AvailableTint = new Color(1.00f, 0.90f, 0.46f, 1f);
        static readonly Color LockedTint = new Color(0.46f, 0.50f, 0.56f, 1f);

        static readonly Color UnlockedRing = new Color(0.28f, 0.85f, 0.38f, 0.95f);
        static readonly Color AvailableRing = new Color(1.00f, 0.80f, 0.22f, 0.95f);
        static readonly Color LockedRing = new Color(0.26f, 0.29f, 0.34f, 0.85f);
        static readonly Color SelectedRing = new Color(1.00f, 0.98f, 0.82f, 1f);

        readonly struct NodeCoord
        {
            public readonly SkillId Id;
            public readonly float PixelX;
            public readonly float PixelYFromTop;

            public NodeCoord(SkillId id, float pixelX, float pixelYFromTop)
            {
                Id = id;
                PixelX = pixelX;
                PixelYFromTop = pixelYFromTop;
            }
        }

        // skilltree.png (1024x576) içindeki 9 dairesel düğümün piksel merkezleri.
        static readonly NodeCoord[] NodeCoords =
        {
            // İnşaat Dalı (Sol)
            new NodeCoord(SkillId.QuickHammer, 210.0f, 219.5f),
            new NodeCoord(SkillId.SteelBracing, 377.5f, 389.5f),
            new NodeCoord(SkillId.LifeTriangleArchitect, 141.5f, 466.5f),

            // Keşif Dalı (Orta)
            new NodeCoord(SkillId.NimbleStep, 527.5f, 115.5f),
            new NodeCoord(SkillId.WideBag, 527.5f, 275.5f),
            new NodeCoord(SkillId.LifeSaver, 527.5f, 446.5f),

            // Mühendislik Dalı (Sağ)
            new NodeCoord(SkillId.Recycling, 842.5f, 219.5f),
            new NodeCoord(SkillId.AutomationMaster, 692.5f, 318.5f),
            new NodeCoord(SkillId.FrequencyLock, 793.5f, 473.5f),
        };

        class NodeView
        {
            public SkillInfo Skill;
            public Button Button;
            public Image IconImage;
            public Image RingImage;
            public Image LabelPlate;
            public Text LabelText;
        }

        UiWindow window;
        readonly List<NodeView> nodes = new List<NodeView>();
        readonly Dictionary<SkillBranch, Text> branchLabels = new Dictionary<SkillBranch, Text>();

        SkillId selectedSkill = SkillId.QuickHammer;
        Image detailIcon;
        Text detailTitle;
        Text detailDescription;
        Text detailState;
        Button detailUnlockButton;
        Text detailUnlockLabel;

        float refreshAt;

        static Sprite cachedGraphBgSprite;
        static Sprite cachedRingSprite;
        static readonly Dictionary<SkillId, Sprite> cachedNodeSprites = new Dictionary<SkillId, Sprite>();

        void Update()
        {
            if (GameInput.SkillTreeTogglePressed && (IsOpen || !ModalMenu.IsOpen))
            {
                if (IsOpen) Close();
                else Open();
            }
            if (!IsOpen) return;

            window.Tick(nodes.Count > 0 ? nodes[0].Button.gameObject : null);
            if (GameInput.MenuBackPressed) Close();
            else if (Time.unscaledTime >= refreshAt) Refresh();
        }

        bool IsOpen => window != null && window.IsOpen;

        void Open()
        {
            if (SkillTree.Instance == null) return;
            if (window == null) Build();
            window.Show(true);
            Refresh();
            if (EventSystem.current != null && nodes.Count > 0)
            {
                var target = nodes.Find(n => n.Skill.Id == selectedSkill) ?? nodes[0];
                EventSystem.current.SetSelectedGameObject(target.Button.gameObject);
            }
        }

        void Close()
        {
            if (window != null) window.Show(false);
        }

        void Build()
        {
            EnsureSpritesExtracted();

            window = new UiWindow("Yetenek Ağacı", new Vector2(1040f, 756f), Close, false);
            window.FooterButton($"Kapat ({GameInput.Prompt(InputPrompt.SkillTree)} / {GameInput.Prompt(InputPrompt.Back)})", Close, null, 260f);

            var note = UiFactory.Label(window.Footer,
                "Her yeni gün +1 puan. Daireye tıklayarak veya seçip alt butona basarak yeteneği açabilirsin.",
                14, new Color(0.78f, 0.78f, 0.74f), TextAnchor.MiddleLeft, FontStyle.Normal, false);
            note.rectTransform.sizeDelta = new Vector2(690f, 44f);
            note.transform.SetAsFirstSibling();

            // --- Grafik Alanı (16:9)
            var graphFrame = UiFactory.Panel(window.Content, "Ağaç Çerçevesi", new Color(0.06f, 0.07f, 0.09f, 0.95f));
            var graphRect = UiFactory.Place(graphFrame.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -2f),
                new Vector2(GraphWidth, GraphHeight));

            if (cachedGraphBgSprite != null)
            {
                var bgImg = UiFactory.Image(graphRect, "Ağaç Çizgileri", cachedGraphBgSprite, Color.white, false);
                UiFactory.Stretch(bgImg.rectTransform);
            }

            // Üç dal başlığı (Sol: İnşaat, Orta-Sol/Üst: Keşif, Sağ: Mühendislik)
            CreateBranchHeader(graphRect, SkillBranch.Construction, new Vector2(24f, -14f), TextAnchor.UpperLeft);
            CreateBranchHeader(graphRect, SkillBranch.Exploration, new Vector2(280f, -14f), TextAnchor.UpperLeft);
            CreateBranchHeader(graphRect, SkillBranch.Engineering, new Vector2(GraphWidth - 284f, -14f), TextAnchor.UpperRight);

            // 9 Dairesel Düğüm
            float nodeDiameter = (SourceCircleRadius * 2f / SourceWidth) * GraphWidth; // ~94.5 px
            foreach (var coord in NodeCoords)
            {
                var skill = SkillCatalog.Get(coord.Id);
                float uiX = (coord.PixelX / SourceWidth) * GraphWidth;
                float uiY = -(coord.PixelYFromTop / SourceHeight) * GraphHeight;
                nodes.Add(BuildNode(graphRect, skill, new Vector2(uiX, uiY), nodeDiameter));
            }

            // --- Alt Seçili Yetenek Bilgi Çubuğu
            BuildDetailBar();
        }

        void CreateBranchHeader(RectTransform parent, SkillBranch branch, Vector2 pos, TextAnchor align)
        {
            var header = UiFactory.Label(parent, "", 17, UiFactory.Accent, align, FontStyle.Bold, false);
            UiFactory.Place(header.rectTransform, new Vector2(0f, 1f), pos, new Vector2(260f, 40f));
            branchLabels[branch] = header;
        }

        NodeView BuildNode(RectTransform graphRect, SkillInfo skill, Vector2 centerFromTopLeft, float diameter)
        {
            var holder = UiFactory.Rect($"Düğüm_{skill.Id}", graphRect);
            holder.anchorMin = holder.anchorMax = new Vector2(0f, 1f);
            holder.pivot = new Vector2(0.5f, 0.5f);
            holder.anchoredPosition = centerFromTopLeft;
            holder.sizeDelta = new Vector2(diameter, diameter);

            // Dış durum ve seçim halkası
            var ring = UiFactory.Image(holder, "Halka", cachedRingSprite ?? UiFactory.Circle, LockedRing, false);
            ring.type = Image.Type.Simple;
            ring.rectTransform.anchorMin = ring.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            ring.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            ring.rectTransform.anchoredPosition = Vector2.zero;
            ring.rectTransform.sizeDelta = new Vector2(diameter + 14f, diameter + 14f);

            // Tıklanabilir dairesel düğüm butonu
            cachedNodeSprites.TryGetValue(skill.Id, out var iconSprite);
            var btnObj = UiFactory.Rect("DaireButon", holder);
            btnObj.anchorMin = btnObj.anchorMax = new Vector2(0.5f, 0.5f);
            btnObj.pivot = new Vector2(0.5f, 0.5f);
            btnObj.anchoredPosition = Vector2.zero;
            btnObj.sizeDelta = new Vector2(diameter, diameter);

            var iconImg = btnObj.gameObject.AddComponent<Image>();
            iconImg.sprite = iconSprite ?? UiFactory.Circle;
            iconImg.type = Image.Type.Simple;
            iconImg.preserveAspect = true;
            iconImg.color = LockedTint;

            var button = btnObj.gameObject.AddComponent<Button>();
            button.targetGraphic = iconImg;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.selectedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            colors.fadeDuration = 0.06f;
            button.colors = colors;

            button.onClick.AddListener(() => OnNodeClicked(skill.Id));
            btnObj.gameObject.AddComponent<UiPress>();

            var hover = btnObj.gameObject.AddComponent<SkillNodeHover>();
            hover.Initialize(() =>
            {
                if (selectedSkill != skill.Id)
                {
                    selectedSkill = skill.Id;
                    Refresh();
                }
            });

            // Dairenin hemen altında kompakt isim etiketi
            var labelPlate = UiFactory.Panel(holder, "İsimPlaka", new Color(0.08f, 0.09f, 0.12f, 0.90f));
            labelPlate.rectTransform.anchorMin = labelPlate.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            labelPlate.rectTransform.pivot = new Vector2(0.5f, 1f);
            labelPlate.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            labelPlate.rectTransform.sizeDelta = new Vector2(154f, 22f);

            var labelText = UiFactory.Label(labelPlate.rectTransform, $"{skill.Tier}. {skill.Name}", 12,
                UiFactory.Text, TextAnchor.MiddleCenter, FontStyle.Bold, false);
            UiFactory.Stretch(labelText.rectTransform, 2f);

            return new NodeView
            {
                Skill = skill,
                Button = button,
                IconImage = iconImg,
                RingImage = ring,
                LabelPlate = labelPlate,
                LabelText = labelText
            };
        }

        void BuildDetailBar()
        {
            var bar = UiFactory.Panel(window.Content, "SeçiliYetenekÇubuğu", new Color(0.09f, 0.10f, 0.13f, 0.98f));
            var barRect = UiFactory.Place(bar.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 0f),
                new Vector2(GraphWidth, 88f));

            detailIcon = UiFactory.Image(barRect, "Detayİkon", cachedRingSprite, AvailableTint, false);
            UiFactory.Place(detailIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(64f, 64f));

            detailTitle = UiFactory.Label(barRect, "", 19, UiFactory.Accent, TextAnchor.UpperLeft, FontStyle.Bold, false);
            UiFactory.Place(detailTitle.rectTransform, new Vector2(0f, 1f), new Vector2(92f, -10f), new Vector2(560f, 26f));

            detailDescription = UiFactory.Label(barRect, "", 15, new Color(0.88f, 0.88f, 0.84f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(detailDescription.rectTransform, new Vector2(0f, 1f), new Vector2(92f, -36f), new Vector2(560f, 44f));

            detailState = UiFactory.Label(barRect, "", 14, UiFactory.Text, TextAnchor.MiddleRight, FontStyle.Bold, false);
            UiFactory.Place(detailState.rectTransform, new Vector2(1f, 0.5f), new Vector2(-216f, 0f), new Vector2(240f, 48f));

            detailUnlockButton = UiFactory.TextButton(barRect, "YETENEĞİ AÇ (1 Puan)", new Vector2(194f, 52f), () =>
            {
                if (SkillTree.Instance != null && SkillTree.Instance.TryUnlock(selectedSkill))
                    Refresh();
            }, new Color(0.42f, 0.36f, 0.14f, 1f), 15);
            UiFactory.Place((RectTransform)detailUnlockButton.transform, new Vector2(1f, 0.5f), new Vector2(-12f, 0f),
                new Vector2(194f, 52f));
            detailUnlockLabel = detailUnlockButton.GetComponentInChildren<Text>();
        }

        void OnNodeClicked(SkillId id)
        {
            selectedSkill = id;
            var tree = SkillTree.Instance;
            if (tree != null && tree.CanUnlock(id, out _))
            {
                tree.TryUnlock(id);
            }
            Refresh();
        }

        void Refresh()
        {
            refreshAt = Time.unscaledTime + RefreshSeconds;
            var tree = SkillTree.Instance;
            if (tree == null) return;

            window.Title.text = $"YETENEK AĞACI   <size=18><color=#cfcfc8>puan: {tree.Points}</color></size>";

            foreach (var pair in branchLabels)
            {
                bool avail = SkillTree.IsBranchAvailable(pair.Key);
                pair.Value.text = avail
                    ? $"{pair.Key.ToDisplayName().ToUpperInvariant()} DALI\n<size=12><color=#9be37a>Uzman aktif</color></size>"
                    : $"{pair.Key.ToDisplayName().ToUpperInvariant()} DALI\n<size=12><color=#9a9a94>(Uzman kazazede yok)</color></size>";
            }

            float pulse = 0.88f + 0.12f * Mathf.Sin(Time.unscaledTime * 5f);

            foreach (var node in nodes)
            {
                bool unlocked = tree.IsUnlocked(node.Skill.Id);
                bool canUnlock = tree.CanUnlock(node.Skill.Id, out _);
                bool isSelected = node.Skill.Id == selectedSkill;

                node.IconImage.color = unlocked
                    ? UnlockedTint
                    : canUnlock
                        ? new Color(AvailableTint.r * pulse, AvailableTint.g * pulse, AvailableTint.b, 1f)
                        : LockedTint;

                if (isSelected)
                {
                    node.RingImage.color = SelectedRing;
                    node.RingImage.rectTransform.localScale = new Vector3(1.06f, 1.06f, 1f);
                }
                else
                {
                    node.RingImage.color = unlocked ? UnlockedRing : canUnlock ? AvailableRing : LockedRing;
                    node.RingImage.rectTransform.localScale = Vector3.one;
                }

                node.LabelText.color = unlocked
                    ? new Color(0.65f, 0.96f, 0.68f)
                    : canUnlock
                        ? new Color(1f, 0.88f, 0.45f)
                        : new Color(0.72f, 0.75f, 0.80f);
            }

            // Alt detay çubuğunu güncelle
            var info = SkillCatalog.Get(selectedSkill);
            bool selUnlocked = tree.IsUnlocked(selectedSkill);
            bool selCanUnlock = tree.CanUnlock(selectedSkill, out string selReason);

            if (cachedNodeSprites.TryGetValue(selectedSkill, out var selSprite) && selSprite != null)
                detailIcon.sprite = selSprite;
            detailIcon.color = selUnlocked ? UnlockedTint : selCanUnlock ? AvailableTint : LockedTint;

            detailTitle.text = $"{info.Name.ToUpperInvariant()}   <size=14><color=#b8b8b0>({info.Branch.ToDisplayName()} Dalı • Kademe {info.Tier})</color></size>";
            detailDescription.text = info.Description;

            if (selUnlocked)
            {
                detailState.text = "<color=#9be37a>BU YETENEK AÇIK</color>";
                detailUnlockButton.interactable = false;
                detailUnlockButton.GetComponent<Image>().color = new Color(0.20f, 0.36f, 0.22f, 0.9f);
                if (detailUnlockLabel != null) detailUnlockLabel.text = "AÇILDI ✓";
            }
            else if (selCanUnlock)
            {
                detailState.text = "<color=#ffd75a>Açılmaya hazır</color>";
                detailUnlockButton.interactable = true;
                detailUnlockButton.GetComponent<Image>().color = new Color(0.52f, 0.42f, 0.14f, 1f);
                if (detailUnlockLabel != null) detailUnlockLabel.text = "YETENEĞİ AÇ (1 Puan)";
            }
            else
            {
                detailState.text = $"<color=#c0827a>{selReason}</color>";
                detailUnlockButton.interactable = false;
                detailUnlockButton.GetComponent<Image>().color = new Color(0.20f, 0.21f, 0.24f, 0.9f);
                if (detailUnlockLabel != null) detailUnlockLabel.text = "KİLİTLİ";
            }
        }

        // ---------------------------------------------------------------- Görsel & Düğüm İkonu Çıkarımı

        static void EnsureSpritesExtracted()
        {
            if (cachedRingSprite == null)
                cachedRingSprite = BuildRingSprite(128, 0.84f, 1.0f);

            if (cachedGraphBgSprite != null && cachedNodeSprites.Count == NodeCoords.Length) return;

            Texture2D src = LoadSourceTexture();
            if (src == null) return;

            int w = src.width;
            int h = src.height;
            Color32[] pixels = src.GetPixels32();

            // 1) Her bir dairesel düğümü (r=52) kendi dairesel Sprite'ı olarak kes
            int cropRadius = Mathf.CeilToInt(SourceCircleRadius + 1.5f); // 53 px
            int cropSize = cropRadius * 2;
            foreach (var coord in NodeCoords)
            {
                int cx = Mathf.RoundToInt(coord.PixelX);
                int cy = Mathf.RoundToInt(h - 1f - coord.PixelYFromTop);

                var nodeTex = new Texture2D(cropSize, cropSize, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                var nodePixels = new Color32[cropSize * cropSize];

                for (int y = 0; y < cropSize; y++)
                {
                    for (int x = 0; x < cropSize; x++)
                    {
                        float dx = (x + 0.5f) - cropRadius;
                        float dy = (y + 0.5f) - cropRadius;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        if (dist > SourceCircleRadius + 0.8f)
                        {
                            nodePixels[y * cropSize + x] = new Color32(0, 0, 0, 0);
                            continue;
                        }

                        int sx = Mathf.Clamp(cx - cropRadius + x, 0, w - 1);
                        int sy = Mathf.Clamp(cy - cropRadius + y, 0, h - 1);
                        Color32 c = pixels[sy * w + sx];

                        // Kenar yumuşatma (antialiasing)
                        float edgeAlpha = Mathf.Clamp01((SourceCircleRadius + 0.6f) - dist);
                        byte a = (byte)Mathf.RoundToInt(255f * edgeAlpha);
                        nodePixels[y * cropSize + x] = new Color32(c.r, c.g, c.b, a);
                    }
                }

                nodeTex.SetPixels32(nodePixels);
                nodeTex.Apply(false, false);
                cachedNodeSprites[coord.Id] = Sprite.Create(
                    nodeTex,
                    new Rect(0f, 0f, cropSize, cropSize),
                    new Vector2(0.5f, 0.5f),
                    100f);
            }

            // 2) Arka plan grafiğinde dairelerin içini (r < 50.5) koyu arka planla doldur ki butonlar küçülüp/renklenince
            // alttan beyaz daire taşmasın; tüm bağlantı çizgileri ve dış yay olduğu gibi korunsun.
            var bgTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var bgPixels = (Color32[])pixels.Clone();
            Color32 bgClear = pixels[0]; // (0, 0, 0, 138)

            foreach (var coord in NodeCoords)
            {
                float cx = coord.PixelX;
                float cy = (h - 1f) - coord.PixelYFromTop;
                int minX = Mathf.Max(0, Mathf.FloorToInt(cx - SourceCircleRadius));
                int maxX = Mathf.Min(w - 1, Mathf.CeilToInt(cx + SourceCircleRadius));
                int minY = Mathf.Max(0, Mathf.FloorToInt(cy - SourceCircleRadius));
                int maxY = Mathf.Min(h - 1, Mathf.CeilToInt(cy + SourceCircleRadius));

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float dx = x - cx;
                        float dy = y - cy;
                        if (dx * dx + dy * dy <= (SourceCircleRadius - 1.0f) * (SourceCircleRadius - 1.0f))
                        {
                            bgPixels[y * w + x] = bgClear;
                        }
                    }
                }
            }

            bgTex.SetPixels32(bgPixels);
            bgTex.Apply(false, false);
            cachedGraphBgSprite = Sprite.Create(bgTex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        static Texture2D LoadSourceTexture()
        {
            var skin = GameAssets.Ui;
            if (skin != null && skin.skillTreeBackground != null && skin.skillTreeBackground.texture != null &&
                skin.skillTreeBackground.texture.isReadable)
            {
                return skin.skillTreeBackground.texture;
            }

            string[] candidates =
            {
                Path.Combine(Application.dataPath, "Models/UI/skilltree.png"),
            };

            foreach (string fullPath in candidates)
            {
                if (!File.Exists(fullPath)) continue;
                byte[] bytes = File.ReadAllBytes(fullPath);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(bytes, false)) return tex;
            }
            return null;
        }

        static Sprite BuildRingSprite(int size, float innerNormalized, float outerNormalized)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var cols = new Color32[size * size];
            float half = size * 0.5f;
            float rIn = half * innerNormalized;
            float rOut = half * outerNormalized;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) - half;
                    float dy = (y + 0.5f) - half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float aIn = Mathf.Clamp01((d - rIn) * 1.5f);
                    float aOut = Mathf.Clamp01((rOut - d) * 1.5f);
                    byte a = (byte)Mathf.RoundToInt(255f * aIn * aOut);
                    cols[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(cols);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    /// <summary>Dairesel yetenek düğümünün üzerine fareyle gelindiğinde veya gamepad ile seçildiğinde tetiklenir.</summary>
    public class SkillNodeHover : MonoBehaviour, IPointerEnterHandler, ISelectHandler
    {
        System.Action onFocus;

        public void Initialize(System.Action callback) => onFocus = callback;
        public void OnPointerEnter(PointerEventData eventData) => onFocus?.Invoke();
        public void OnSelect(BaseEventData eventData) => onFocus?.Invoke();
    }
}
