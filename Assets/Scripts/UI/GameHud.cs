using System.Collections.Generic;
using System.Text;
using AftershockIsle.Building;
using AftershockIsle.CameraControl;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Crisis;
using AftershockIsle.Economy;
using AftershockIsle.Endgame;
using AftershockIsle.Expedition;
using AftershockIsle.Interaction;
using AftershockIsle.Player;
using AftershockIsle.Save;
using AftershockIsle.Skills;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Oyun içi gösterge (uGUI). Oyun sahnesi her yüklendiğinde kendini kurar; sahneye kaydedilmesi gerekmez.
    /// Sade düzen: ekranda yalnızca kritik bilgi durur (gün, bütünlük, kaynaklar, seçili kazazede, tehlikeler, bildirimler).
    /// Ayrıntılar (tahliye şartları, kontrol listesi) zaman durdurulunca açılır: plan yapılan an bilgi en çok o zaman gerekir.
    /// Saha görünümünde yalnızca bildirimler kalır (sahanın kendi göstergesi var).
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        const float TextRefresh = 0.1f;
        const float NoticeDuration = 4f;
        const int NoticeSlots = 3;
        const float Margin = 14f;

        static readonly Color Muted = new Color(0.78f, 0.78f, 0.74f, 1f);

        // ---------------------------------------------------------------- Kendini kurma

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindFirstObjectByType<BuildingLayout>() == null || FindFirstObjectByType<GameHud>() != null) return;

            var go = new GameObject("[Arayüz]");
            go.AddComponent<GameHud>();
            go.AddComponent<WorldOverlays>();
            go.AddComponent<WorldMarkers>();
            go.AddComponent<InfoCard>();
        }

        // ---------------------------------------------------------------- Durum

        struct Notice
        {
            public string Text;
            public float Until;
        }

        TimeManager timeManager;
        DayCycle dayCycle;
        Text evacHeading;
        CommandController commands;
        BuildingLayout building;
        GameSession session;
        ManualPump[] pumps;
        UiSkin skin;

        readonly List<Notice> notices = new List<Notice>();
        readonly StringBuilder text = new StringBuilder();
        float refreshAt;

        GameObject topGroup;
        GameObject bottomGroup;
        Text dayTitle;
        Text dayClock;
        Text daySub;
        Image integrityFill;
        Text integrityLabel;
        Text warnings;
        readonly Dictionary<ResourceType, Text> resourceCounts = new Dictionary<ResourceType, Text>();
        GameObject powerChip;
        GameObject expeditionChip;
        Text expeditionLabel;
        GameObject pausedBanner;

        GameObject card;
        Text cardName;
        Text cardInfo;
        Image healthFill;
        Text healthLabel;
        Image hungerGauge;
        Image stressGauge;
        Text cardTask;
        Text cardTraits;
        GameObject selectHint;

        GameObject evacChip;
        Text evacChipLabel;
        GameObject evacPanel;
        Text checklist;
        Text hint;
        readonly Image[] noticePlates = new Image[NoticeSlots];
        readonly Text[] noticeTexts = new Text[NoticeSlots];

        GameObject endPanel;
        Text endTitle;
        Text endReason;

        void Awake()
        {
            timeManager = FindFirstObjectByType<TimeManager>();
            dayCycle = FindFirstObjectByType<DayCycle>();
            commands = FindFirstObjectByType<CommandController>();
            building = FindFirstObjectByType<BuildingLayout>();
            session = FindFirstObjectByType<GameSession>();
            pumps = FindObjectsByType<ManualPump>(FindObjectsSortMode.None);
            skin = GameAssets.Ui;
        }

        void Start()
        {
            UiFactory.EnsureEventSystem();
            Build();
        }

        bool Planning => timeManager != null && timeManager.IsPaused;

        // ---------------------------------------------------------------- Kurulum

        void Build()
        {
            var canvas = UiFactory.Canvas("Oyun Göstergesi", 10, transform).transform;
            topGroup = UiFactory.Rect("Üst", canvas).gameObject;
            UiFactory.Stretch((RectTransform)topGroup.transform);
            bottomGroup = UiFactory.Rect("Alt", canvas).gameObject;
            UiFactory.Stretch((RectTransform)bottomGroup.transform);

            BuildDay(topGroup.transform);
            BuildIntegrity(topGroup.transform);
            BuildResources(topGroup.transform);
            BuildCard(bottomGroup.transform);
            BuildEvacuation(bottomGroup.transform);
            BuildNotices(canvas);
            BuildEndPanel(canvas);

            hint = UiFactory.Label(bottomGroup.transform, "", 15, Muted, TextAnchor.MiddleCenter, FontStyle.Normal);
            UiFactory.Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(1400f, 24f));
        }

        // Sol üst: kenarda dönen gece-gündüz çarkı, yanında gün sayısı ve fazın kalan süresi; altında küçük hava/sefer satırı.
        void BuildDay(Transform parent)
        {
            float sunWidth = BuildDayDial(parent);
            float x = DayDialEdgeOffset + sunWidth + 14f;
            float centerY = -(Margin + DayDialHeight * 0.5f);

            dayTitle = UiFactory.Label(parent, "", 30, UiFactory.Accent, TextAnchor.LowerLeft);
            UiFactory.Place(dayTitle.rectTransform, new Vector2(0f, 1f), new Vector2(x, centerY + 38f), new Vector2(260f, 36f));
            dayClock = UiFactory.Label(parent, "", 24, UiFactory.Text, TextAnchor.UpperLeft);
            UiFactory.Place(dayClock.rectTransform, new Vector2(0f, 1f), new Vector2(x, centerY + 2f), new Vector2(260f, 30f));
            daySub = UiFactory.Label(parent, "", 15, Muted, TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(daySub.rectTransform, new Vector2(0f, 1f), new Vector2(x, centerY - 30f), new Vector2(420f, 20f));

            // Panel yok: yazılar karanlık sahnede okunsun diye gölgeli.
            foreach (var label in new[] { dayTitle, dayClock, daySub })
            {
                var shadow = label.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
                shadow.effectDistance = new Vector2(1.5f, -1.5f);
            }
        }

        // Sol kenar: yarısı ekran dışında kalan gece-gündüz çarkı (Resources/gecegunduz.png: sol yarı ay, sağ yarı güneş).
        // Gündüz güneş yarısı görünür; gece çark 180° dönüp ay yarısını gösterir, sabah dönmeye devam edip güneşe geçer.
        const string DayDialResource = "gecegunduz";
        // Görselde ay ile güneşin birleştiği çizgi (piksel): çark buranın etrafında döner ve tam ekran kenarına oturur.
        static readonly Vector2 DayDialSplitPixel = new Vector2(210.5f, 277f);
        const float DayDialHeight = 170f;
        const float DayDialTurnSpeed = 140f;
        // Merkezin ekranın sol kenarına göre yeri (eksi = sola, daha çok ekran dışında).
        const float DayDialEdgeOffset = -16f;

        RectTransform dayDial;
        float dayDialAngle;
        float dayDialTarget;
        bool dayDialNight;
        bool dayDialPlaced;

        /// <summary>Çarkı kurar; ekranda görünen güneş yarısının genişliğini döndürür (yazılar onun yanına dizilir).</summary>
        float BuildDayDial(Transform parent)
        {
            var texture = Resources.Load<Texture2D>(DayDialResource);
            if (texture == null) return 0f;

            var pivot = new Vector2(DayDialSplitPixel.x / texture.width, 1f - DayDialSplitPixel.y / texture.height);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), pivot, 100f);
            sprite.name = DayDialResource;

            var image = UiFactory.Image(parent, "Gece Gündüz", sprite, Color.white);
            dayDial = image.rectTransform;
            dayDial.anchorMin = dayDial.anchorMax = new Vector2(0f, 1f);
            dayDial.pivot = pivot;
            dayDial.sizeDelta = new Vector2(DayDialHeight * texture.width / texture.height, DayDialHeight);
            // Sol üst köşe, merkezi sol kenarın biraz dışında.
            dayDial.anchoredPosition = new Vector2(DayDialEdgeOffset, -(Margin + DayDialHeight * 0.5f));
            return DayDialHeight * (texture.width - DayDialSplitPixel.x) / texture.height;
        }

        void UpdateDayDial()
        {
            if (dayDial == null || dayCycle == null) return;

            bool night = dayCycle.Phase == DayPhase.Night;
            if (!dayDialPlaced)
            {
                dayDialPlaced = true;
                dayDialNight = night;
                dayDialAngle = dayDialTarget = night ? 180f : 0f;
            }
            else if (night != dayDialNight)
            {
                // Hep aynı yöne döner: güneş batar, ay doğar; sabah ay batar, güneş doğar.
                dayDialNight = night;
                dayDialTarget += 180f;
            }

            dayDialAngle = Mathf.MoveTowards(dayDialAngle, dayDialTarget, DayDialTurnSpeed * Time.unscaledDeltaTime);
            if (dayDialAngle >= 360f && dayDialTarget >= 360f)
            {
                dayDialAngle -= 360f;
                dayDialTarget -= 360f;
            }
            // Saat yönünde: görünen yarı aşağı iner, diğeri üstten gelir.
            dayDial.localRotation = Quaternion.Euler(0f, 0f, -dayDialAngle);
        }

        void BuildIntegrity(Transform parent)
        {
            integrityFill = UiFactory.Bar(parent, "Bütünlük", new Vector2(420f, 28f), UiFactory.Good, out integrityLabel, 15);
            UiFactory.Place((RectTransform)integrityFill.transform.parent, new Vector2(0.5f, 1f), new Vector2(0f, -Margin), new Vector2(420f, 28f));

            UiTooltip.Attach(integrityFill.transform.parent.gameObject, IntegrityTip);

            warnings = UiFactory.Label(parent, "", 19, UiFactory.Text, TextAnchor.UpperCenter);
            UiFactory.Place(warnings.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(900f, 200f));

            if (skin != null && skin.pausedTitle != null)
            {
                var banner = UiFactory.Image(parent, "Duraklatıldı", skin.pausedTitle, Color.white);
                UiFactory.Place(banner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(340f, 66f));
                pausedBanner = banner.gameObject;
            }
            else
            {
                var label = UiFactory.Label(parent, "DURAKLATILDI", 44, UiFactory.Text);
                UiFactory.Place(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(600f, 70f));
                pausedBanner = label.gameObject;
            }
        }

        // Sağ üst: dört küçük rozet; elektrik yalnızca kesikken uyarı olarak görünür.
        void BuildResources(Transform parent)
        {
            const float width = 128f;
            const float height = 63f;
            const float gap = 6f;

            var row = UiFactory.Rect("Kaynaklar", parent);
            UiFactory.Place(row, new Vector2(1f, 1f), new Vector2(-Margin, -Margin), new Vector2(4 * (width + gap) - gap, height));

            float x = 0f;
            foreach (var type in ResourceTypes.All)
            {
                Sprite badge = skin == null ? null : type switch
                {
                    ResourceType.Scrap => skin.scrapBadge,
                    ResourceType.Metal => skin.metalBadge,
                    ResourceType.Electronics => skin.electricBadge,
                    _ => null,
                };

                var holder = UiFactory.Rect(type.ToString(), row);
                UiFactory.Place(holder, new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(width, height));
                x += width + gap;

                if (badge != null)
                {
                    var image = UiFactory.Image(holder, "Rozet", badge, Color.white, true);
                    UiFactory.Stretch(image.rectTransform);
                }
                else
                {
                    // Yaşam Kiti için rozet yok: koyu kırmızı plaka ve açlık simgesi.
                    var plate = UiFactory.Panel(holder, "Rozet", new Color(0.55f, 0.18f, 0.18f, 0.95f));
                    UiFactory.Place(plate.rectTransform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(width - 26f, height - 18f));
                    var iconSprite = skin != null ? UiSkin.Frame(skin.hungerFrames, 0) : null;
                    var icon = UiFactory.Image(holder, "Simge", iconSprite != null ? iconSprite : UiFactory.Circle,
                        iconSprite != null ? Color.white : type.ToColor());
                    icon.type = Image.Type.Simple;
                    icon.preserveAspect = true;
                    UiFactory.Place(icon.rectTransform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(height, height));
                }

                var count = UiFactory.Label(holder, "0", 24, UiFactory.Text);
                UiFactory.Place(count.rectTransform, new Vector2(0f, 0.5f), new Vector2(width * 0.42f, 1f), new Vector2(width * 0.52f, height));
                resourceCounts[type] = count;
                UiTooltip.Attach(holder.gameObject, () => ResourceTip(type));
            }

            var chip = UiFactory.Panel(parent, "Elektrik Kesik", new Color(0.55f, 0.16f, 0.12f, 0.95f));
            UiFactory.Place(chip.rectTransform, new Vector2(1f, 1f), new Vector2(-Margin, -Margin - height - 6f), new Vector2(190f, 30f));
            var chipLabel = UiFactory.Label(chip.transform, "ELEKTRİK KESİK", 15, UiFactory.Text);
            UiFactory.Stretch(chipLabel.rectTransform);
            powerChip = chip.gameObject;

            // Sefer sürerken: kim, nerede, kalan süre, can ve çanta; sahaya geçme kısayolu.
            var expedition = UiFactory.Panel(parent, "Sefer", UiFactory.Slate);
            UiFactory.Place(expedition.rectTransform, new Vector2(1f, 1f), new Vector2(-Margin, -Margin - height - 42f), new Vector2(430f, 56f));
            expeditionLabel = UiFactory.Label(expedition.transform, "", 15, UiFactory.Text, TextAnchor.MiddleLeft, FontStyle.Normal, false);
            UiFactory.Stretch(expeditionLabel.rectTransform, 12f);
            expeditionChip = expedition.gameObject;
            expeditionChip.SetActive(false);
        }

        // Sol alt: seçili kazazede. Ad ve konum, can çubuğu, açlık ve stres göstergesi, tek satır görev.
        void BuildCard(Transform parent)
        {
            var panel = UiFactory.Panel(parent, "Seçili Karakter", UiFactory.Slate);
            UiFactory.Place(panel.rectTransform, new Vector2(0f, 0f), new Vector2(Margin, 34f), new Vector2(420f, 124f));
            card = panel.gameObject;

            cardName = UiFactory.Label(panel.transform, "", 22, UiFactory.Accent, TextAnchor.UpperLeft);
            UiFactory.Place(cardName.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -8f), new Vector2(270f, 28f));
            cardInfo = UiFactory.Label(panel.transform, "", 14, Muted, TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(cardInfo.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -36f), new Vector2(270f, 18f));

            healthFill = UiFactory.Bar(panel.transform, "Can", new Vector2(270f, 18f), UiFactory.Good, out healthLabel, 13);
            UiFactory.Place((RectTransform)healthFill.transform.parent, new Vector2(0f, 1f), new Vector2(14f, -58f), new Vector2(270f, 18f));

            cardTask = UiFactory.Label(panel.transform, "", 14, UiFactory.Text, TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(cardTask.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -80f), new Vector2(392f, 18f));
            cardTask.horizontalOverflow = HorizontalWrapMode.Overflow;
            cardTraits = UiFactory.Label(panel.transform, "", 13, Muted, TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(cardTraits.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -100f), new Vector2(392f, 18f));
            cardTraits.horizontalOverflow = HorizontalWrapMode.Overflow;

            hungerGauge = Gauge(panel.transform, skin != null ? skin.hungerFrames : null, new Vector2(-70f, -8f), "Açlık");
            stressGauge = Gauge(panel.transform, skin != null ? skin.stressFrames : null, new Vector2(-12f, -8f), "Stres");
            UiTooltip.Attach(hungerGauge.gameObject, HungerTip);
            UiTooltip.Attach(stressGauge.gameObject, StressTip);

            var chip = UiFactory.Panel(parent, "Seçim İpucu", UiFactory.Slate);
            UiFactory.Place(chip.rectTransform, new Vector2(0f, 0f), new Vector2(Margin, 34f), new Vector2(300f, 34f));
            var chipLabel = UiFactory.Label(chip.transform, "", 15, Muted, TextAnchor.MiddleCenter, FontStyle.Normal, false);
            UiFactory.Stretch(chipLabel.rectTransform);
            selectHint = chip.gameObject;
        }

        static Image Gauge(Transform parent, Sprite[] frames, Vector2 position, string caption)
        {
            var sprite = UiSkin.Frame(frames, 0);
            var image = UiFactory.Image(parent, caption, sprite != null ? sprite : UiFactory.Circle, sprite != null ? Color.white : UiFactory.Good);
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            UiFactory.Place(image.rectTransform, new Vector2(1f, 1f), position, new Vector2(54f, 54f));
            return image;
        }

        // Sağ alt: normalde küçük bir ilerleme etiketi, zaman durunca tam liste.
        void BuildEvacuation(Transform parent)
        {
            var chip = UiFactory.Panel(parent, "Tahliye Özeti", UiFactory.Slate);
            UiFactory.Place(chip.rectTransform, new Vector2(1f, 0f), new Vector2(-Margin, 34f), new Vector2(480f, 86f));
            evacChipLabel = UiFactory.Label(chip.transform, "", 17, UiFactory.Text, TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Stretch(evacChipLabel.rectTransform, 10f);
            evacChip = chip.gameObject;

            var panel = UiFactory.Panel(parent, "Tahliye", UiFactory.Slate);
            UiFactory.Place(panel.rectTransform, new Vector2(1f, 0f), new Vector2(-Margin, 128f), new Vector2(340f, 176f));
            evacHeading = UiFactory.Label(panel.transform, "TAHLİYE ŞARTLARI", 17, UiFactory.Accent, TextAnchor.UpperLeft);
            UiFactory.Place(evacHeading.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -8f), new Vector2(312f, 24f));
            checklist = UiFactory.Label(panel.transform, "", 15, UiFactory.Text, TextAnchor.UpperLeft, FontStyle.Normal, false);
            UiFactory.Place(checklist.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -34f), new Vector2(316f, 140f));
            evacPanel = panel.gameObject;
        }

        void BuildNotices(Transform canvas)
        {
            for (int i = 0; i < NoticeSlots; i++)
            {
                var plate = UiFactory.Image(canvas, $"Bildirim {i}", null, new Color(0.08f, 0.08f, 0.1f, 0.85f));
                UiFactory.Place(plate.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 205f + i * 40f), new Vector2(760f, 34f));
                var label = UiFactory.Label(plate.transform, "", 16, UiFactory.Text, TextAnchor.MiddleCenter, FontStyle.Normal);
                UiFactory.Stretch(label.rectTransform, 6f);
                plate.gameObject.SetActive(false);
                noticePlates[i] = plate;
                noticeTexts[i] = label;
            }
        }

        void BuildEndPanel(Transform canvas)
        {
            var dim = UiFactory.Panel(canvas, "Oyun Sonu", new Color(0f, 0f, 0f, 0.6f));
            UiFactory.Stretch(dim.rectTransform);
            endPanel = dim.gameObject;

            var panel = UiFactory.Panel(dim.transform, "Panel", UiFactory.Slate);
            UiFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 290f));
            endTitle = UiFactory.Label(panel.transform, "", 48, UiFactory.Accent);
            UiFactory.Place(endTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(680f, 64f));
            endReason = UiFactory.Label(panel.transform, "", 20, UiFactory.Text, TextAnchor.MiddleCenter, FontStyle.Normal, false);
            UiFactory.Place(endReason.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(660f, 80f));

            var restart = UiFactory.TextButton(panel.transform, "Yeniden Başla (R)", new Vector2(280f, 54f),
                () => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex), UiFactory.Good, 20);
            UiFactory.Place((RectTransform)restart.transform, new Vector2(0.5f, 0f), new Vector2(-150f, 24f), new Vector2(280f, 54f));
            var menu = UiFactory.TextButton(panel.transform, "Ana Menü", new Vector2(280f, 54f),
                () => SceneManager.LoadScene(PauseMenu.MainMenuScene), null, 20);
            UiFactory.Place((RectTransform)menu.transform, new Vector2(0.5f, 0f), new Vector2(150f, 24f), new Vector2(280f, 54f));

            endPanel.SetActive(false);
        }

        // ---------------------------------------------------------------- Güncelleme

        void Update()
        {
            if (topGroup == null) return;

            bool field = SideViewCamera.ViewingField;
            bool ended = session != null && session.Outcome != GameOutcome.None;
            topGroup.SetActive(!field && !ended);
            bottomGroup.SetActive(!field && !ended);
            UpdateEnd(ended);
            UpdateDayDial();
            UpdateNotices();

            if (field || ended) return;

            // Çubuklar her kare, yazılar saniyede on kez (gereksiz metin üretmemek için).
            UpdateBars();
            pausedBanner.SetActive(Planning && !ModalMenu.IsOpen && !PauseMenu.IsOpen);
            hint.gameObject.SetActive(Planning);

            if (Time.unscaledTime < refreshAt) return;
            refreshAt = Time.unscaledTime + TextRefresh;

            UpdateDay();
            UpdateResources();
            UpdateWarnings();
            UpdateCard();
            UpdateEvacuation();
            if (Planning) UpdateHint();
        }

        void UpdateBars()
        {
            var integrity = StructuralIntegrity.Instance;
            if (integrity == null) return;
            integrityFill.fillAmount = integrity.Normalized;
            integrityFill.color = Color.Lerp(UiFactory.Danger, UiFactory.Good, integrity.Normalized);
        }

        void UpdateDay()
        {
            if (dayCycle == null) return;

            dayTitle.text = $"GÜN {dayCycle.Day}";
            int seconds = Mathf.CeilToInt(dayCycle.PhaseRemaining);
            dayClock.text = $"{seconds / 60}:{seconds % 60:00}";

            text.Clear();
            text.Append(WeatherSystem.ToDisplayName(WeatherSystem.Current));
            if (timeManager != null && timeManager.Speed == TimeSpeed.Fast) text.Append("  ·  2x hız");

            var away = ExpeditionSystem.AwayExplorer;
            if (away != null && ExpeditionSystem.Instance.CurrentSite != null)
                text.Append($"  ·  Seferde: {away.DisplayName} {Mathf.CeilToInt(ExpeditionSystem.Instance.Remaining)} sn");
            daySub.text = text.ToString();

            var integrity = StructuralIntegrity.Instance;
            if (integrity == null) return;
            float drain = integrity.TotalDrainPerSecond;
            integrityLabel.text = drain > 0f
                ? $"BÜTÜNLÜK %{integrity.Current:0}  <color=#ffb0a0>-%{drain:0.0}/sn</color>"
                : $"BÜTÜNLÜK %{integrity.Current:0}";
        }

        void UpdateResources()
        {
            var storage = ResourceStorage.Instance;
            if (storage == null) return;
            foreach (var pair in resourceCounts) pair.Value.text = storage.Get(pair.Key).ToString();
            powerChip.SetActive(!PowerGrid.IsOn);
            UpdateExpedition();
        }

        void UpdateExpedition()
        {
            var expeditions = ExpeditionSystem.Instance;
            var stage = expeditions != null ? expeditions.Stage : ExpeditionStage.None;
            bool visible = stage == ExpeditionStage.Departing || stage == ExpeditionStage.Away || stage == ExpeditionStage.Returning;
            if (expeditionChip.activeSelf != visible) expeditionChip.SetActive(visible);
            if (!visible) return;

            if (stage == ExpeditionStage.Departing) expeditionLabel.text = "<b>SEFER</b>  ·  kâşif kapıdan çıkıyor...";
            else if (stage == ExpeditionStage.Returning) expeditionLabel.text = "<b>SEFER</b>  ·  kâşif geri dönüyor!";
            else
            {
                var explorer = ExpeditionSystem.AwayExplorer;
                var field = expeditions.Field;
                string site = expeditions.CurrentSite != null ? expeditions.CurrentSite.name : "";
                string state = explorer != null && field != null
                    ? $"Can {explorer.Health:0}  ·  Yük {field.LoadKg}/{field.CapacityKg} kg  ·  {Mathf.CeilToInt(expeditions.Remaining)} sn"
                    : "";
                expeditionLabel.text = $"<b>SEFERDE:</b> {(explorer != null ? explorer.DisplayName : "")} → {site}\n" +
                                       $"<size=13>{state}   <color=#cfcfc8>{GameInput.Prompt(InputPrompt.SwitchCharacter)}: sahaya geç</color></size>";
            }
        }

        // Tehlikeler önem sırasıyla: sarsıntı geri sayımı, temel kayması, elektrik kaçağı, gaz, yangın, bodrum suyu, pompa.
        void UpdateWarnings()
        {
            text.Clear();
            var storage = ResourceStorage.Instance;

            var quake = AftershockSystem.Instance;
            if (quake != null && quake.Phase == QuakePhase.Warning)
                Line($"<size=28><color=#ff6a50>ARTÇI SARSINTI: {Mathf.CeilToInt(quake.PhaseRemaining)}</color></size>\n" +
                     $"<size=16>{GameInput.Prompt(InputPrompt.Pause)} ile durdur, karakterleri yeşil Yaşam Üçgenlerine gönder.</size>");

            var flood = FloodSystem.Instance;
            if (flood != null)
            {
                if (flood.IsFull) Line($"<color=#ff6a50>TEMEL KAYIYOR: {Mathf.CeilToInt(flood.CollapseCountdown)} sn — suyu pompala!</color>");
                if (flood.IsElectrified) Line("<color=#ffe36a>Bodrumdaki su elektrikli — önce şalteri indir.</color>");
                if (flood.Level > 0.01f || flood.IsPipeBurst)
                    Line($"<color=#8fc7ff>Bodrum suyu %{flood.Level:0}{(flood.IsPipeBurst ? " · boru patlak" : "")}</color>");
            }

            foreach (var leak in GasLeak.All)
            {
                if (!leak.IsLeaking && leak.Concentration < 1f) continue;
                string danger = leak.IsExplosive ? (PowerGrid.IsOn ? " · PATLAMA RİSKİ: şalteri indir" : " · patlayıcı")
                    : leak.IsBlockingHere ? " · girilemiyor, pencereyi kır" : "";
                Line($"<color=#b5e66a>{(leak.IsLeaking ? "GAZ KAÇAĞI" : "Gaz")}: {building.GetFloorName(leak.Floor)} %{leak.Concentration:0}{danger}</color>");
            }

            foreach (var fire in FireSpot.All)
            {
                if (!fire.IsBurning && !fire.IsSmokeBlockedHere) continue;
                string collapse = fire.IsAtFullBurn ? $" · TAVAN ÇÖKÜYOR {Mathf.CeilToInt(fire.CollapseCountdown)} sn" : "";
                string smoke = fire.IsSmokeBlockedHere ? " · dumandan girilemiyor" : "";
                string label = fire.IsBurning ? $"YANGIN: {building.GetFloorName(fire.Floor)} %{fire.Intensity:0}" : $"Duman: {building.GetFloorName(fire.Floor)}";
                Line($"<color=#ffa050>{label}{collapse}{smoke}</color>");
            }

            foreach (var pump in pumps)
            {
                if (pump == null || !pump.IsBeingOperated) continue;
                Line($"<size=16>Pompala: {GameInput.Prompt(InputPrompt.Pump)} tuşlarına sırayla bas</size>");
                break;
            }

            // Akşam yemeği: gün içinde kit eksikse önceden uyar (gece akşam planında kimin yiyeceği seçilir).
            if (dayCycle != null && dayCycle.Phase != DayPhase.Night && storage != null)
            {
                int need = Survivor.AliveCount + ExpeditionSystem.AwayCount;
                int kits = storage.Get(ResourceType.Supplies);
                if (kits < need)
                    Line($"<size=17><color=#ffb070>Akşam yemeği: {kits}/{need} Yaşam Kiti — {need - kits} kişi aç kalacak. Kit bul (sefer, arama, enkaz).</color></size>");
            }

            warnings.text = text.ToString();
        }

        void Line(string line)
        {
            if (text.Length > 0) text.Append('\n');
            text.Append(line);
        }

        void UpdateCard()
        {
            var selected = commands != null ? commands.Selected : null;
            bool has = selected != null && !selected.IsDead;
            if (card.activeSelf != has) card.SetActive(has);
            if (selectHint.activeSelf == has) selectHint.SetActive(!has);
            if (!has)
            {
                selectHint.GetComponentInChildren<Text>().text = $"{GameInput.Prompt(InputPrompt.SwitchCharacter)}: karakter seç";
                return;
            }

            var condition = selected.Condition;
            cardName.text = selected.DisplayName;
            cardName.color = BranchColor(selected.Branch);
            cardInfo.text = $"{selected.Branch.ToDisplayName()} · {building.GetFloorName(selected.Mover.CurrentFloor)}" +
                            (condition.IsExhausted ? $" · <color=#ff8f7a>Aç ({condition.Hunger}/{SurvivorCondition.MaxHunger}): yarı hız</color>" : "");

            float health = selected.Health / selected.MaxHealth;
            healthFill.fillAmount = health;
            healthFill.color = Color.Lerp(UiFactory.Danger, UiFactory.Good, health);
            healthLabel.text = $"{selected.Health:0} / {selected.MaxHealth:0}";

            SetGauge(hungerGauge, skin != null ? skin.hungerFrames : null, condition.Hunger);
            float stress = condition.MaxStress > 0f ? condition.Stress / condition.MaxStress : 0f;
            SetGauge(stressGauge, skin != null ? skin.stressFrames : null, stress < 0.25f ? 0 : stress < 0.5f ? 1 : stress < 0.8f ? 2 : 3);

            string state = selected.IsTrapped ? $"<color=#ff8f7a>Enkaz altında {Mathf.CeilToInt(selected.TrappedRemaining)} sn</color>"
                : condition.IsPanicking ? $"<color=#ff8f7a>Panik {Mathf.CeilToInt(condition.PanicRemaining)} sn</color>"
                : TaskLabel(selected.Tasks);
            string carried = selected.IsCarrying ? $"  ·  Elinde: {selected.Carried}" : "";
            cardTask.text = state + carried;
            cardTraits.text = selected.TraitsSummary();
        }

        static void SetGauge(Image gauge, Sprite[] frames, int level)
        {
            var sprite = UiSkin.Frame(frames, level);
            if (sprite != null)
            {
                gauge.sprite = sprite;
                return;
            }
            gauge.color = level switch { 0 => UiFactory.Good, 1 => new Color(0.75f, 0.78f, 0.2f), 2 => UiFactory.Danger, _ => Color.gray };
        }

        void UpdateEvacuation()
        {
            var radio = RadioStation.Instance;
            var goal = GoalAdvisor.Current(building);
            evacChip.SetActive(true);
            evacPanel.SetActive(radio != null && Planning);
            evacChipLabel.text = GoalLabel(goal, null);
            if (radio == null) return;

            var conditions = EvacuationConditions.Evaluate(building, radio);
            int tier = Workbench.Instance != null ? Workbench.Instance.Tier : 1;
            bool columns = conditions.RoofColumnsTotal > 0 && conditions.RoofColumnsReinforced == conditions.RoofColumnsTotal;
            bool[] done = { conditions.WorkbenchTier3, conditions.RadioRepaired, conditions.RoofRubbleLeft == 0, columns, conditions.PowerOn };
            int count = 0;
            foreach (bool d in done) if (d) count++;

            string signal = radio.SignalComplete ? "ulaştı" : $"%{radio.Signal01 * 100f:0}";
            if (GameMode.IsEndless && radio.SignalComplete) signal = $"Gün {EvacuationSystem.NextDropDay}'de";
            evacChipLabel.text = GoalLabel(goal, $"{(GameMode.IsEndless ? "Yardım" : "Tahliye")} {count}/{done.Length}  ·  Sinyal {signal}");
            if (evacHeading != null) evacHeading.text = GameMode.IsEndless ? "ERZAK YARDIMI ŞARTLARI" : "TAHLİYE ŞARTLARI";

            if (!Planning) return;
            text.Clear();
            text.Append($"{Check(done[0])} Tezgah Kademe 3 (şu an {tier})\n");
            text.Append($"{Check(done[1])} Çatıdaki telsiz tamir\n");
            text.Append($"{Check(done[2])} Çatı molozu temiz ({conditions.RoofRubbleLeft} kaldı)\n");
            text.Append($"{Check(done[3])} Çatı kolonları çelik ({conditions.RoofColumnsReinforced}/{conditions.RoofColumnsTotal})\n");
            text.Append($"{Check(done[4])} Ana şalter açık\n");
            text.Append($"Kesintisiz sinyal: {signal}");
            checklist.text = text.ToString();
        }

        static string GoalLabel(GoalAdvisor.Goal goal, string progress)
        {
            string color = goal.Urgent ? "#ff8f7a" : "#ffd75a";
            string head = goal.Urgent ? "ACİL" : "HEDEF";
            string tail = string.IsNullOrEmpty(progress) ? "" : $"   <color=#a8a8a0>{progress}</color>";
            return $"<color={color}><b>{head}: {goal.Title}</b></color>{tail}\n<size=14><color=#d9d9d2>{goal.Detail}</color></size>";
        }

        // ---------------------------------------------------------------- İpucu balonları

        static string ResourceTip(ResourceType type)
        {
            var storage = ResourceStorage.Instance;
            int have = storage != null ? storage.Get(type) : 0;
            var tip = new StringBuilder($"<b><size=19><color=#ffcc33>{type.ToDisplayName()}</color></size></b>   depoda <b>{have}</b>");
            tip.Append($"\n{GoalAdvisor.UseHint(type)}");
            tip.Append($"\n<color=#cfe3a0>Nereden: {GoalAdvisor.SourceHint(type)}</color>");
            if (type == ResourceType.Supplies)
            {
                int need = Survivor.AliveCount + ExpeditionSystem.AwayCount;
                tip.Append($"\n<color={(have >= need ? "#9be37a" : "#ff8f7a")}>Bu gece {need} kit gerekir.</color>");
            }
            return tip.ToString();
        }

        string HungerTip()
        {
            var selected = commands != null ? commands.Selected : null;
            int level = selected != null ? selected.Condition.Hunger : 0;
            return "<b><size=19><color=#ffcc33>Açlık</color></size></b>\n" +
                   "Her gece kişi başı 1 Yaşam Kiti yenir. Yiyemeyen acıkır (0–3): aç karakter yarı hızda yürür ve çalışır, son kademede bir gece daha aç kalan can kaybeder.\n" +
                   $"<color=#cfe3a0>Şu an: {(level == 0 ? "tok" : $"{level}/{SurvivorCondition.MaxHunger} aç")}. Akşam Planı'nda kimin yiyeceğini sen seçersin.</color>";
        }

        string StressTip()
        {
            var selected = commands != null ? commands.Selected : null;
            string now = selected != null ? $"Şu an: %{selected.Condition.Stress / selected.Condition.MaxStress * 100f:0}" : "";
            return "<b><size=19><color=#ffcc33>Stres</color></size></b>\n" +
                   "Karanlıkta çalışmak, felaketler, açlık ve baskınlar stresi artırır. %100 olunca karakter 15 sn panikler ve emir dinlemez.\n" +
                   $"<color=#cfe3a0>Azaltmak: ışıkta boş bekle, sandalyede otur, yemek ye, Dinlenme Köşesi kur. {now}</color>";
        }

        static string IntegrityTip()
        {
            var integrity = StructuralIntegrity.Instance;
            string drain = integrity != null && integrity.TotalDrainPerSecond > 0f ? $"\n<color=#ff8f7a>Şu an her saniye -%{integrity.TotalDrainPerSecond:0.0} eriyor.</color>" : "";
            return "<b><size=19><color=#ffcc33>Bina Bütünlüğü</color></size></b>\n" +
                   "Sarsıntı, su baskını, yangın ve gaz binayı zayıflatır; %0 olursa bina çöker (yenilgi).\n" +
                   "<color=#cfe3a0>Koru: kolonları çelikle güçlendir, suyu pompala, yangını söndür, gaz kaçağını kapat.</color>" + drain;
        }

        static string Check(bool done) => done ? "<color=#9be37a>●</color>" : "<color=#6f6f6f>○</color>";

        void UpdateHint()
        {
            hint.text = $"{GameInput.Prompt(InputPrompt.Move)} yürü   {GameInput.Prompt(InputPrompt.Interact)} etkileş   " +
                        $"{GameInput.Prompt(InputPrompt.SwitchCharacter)} karakter   {GameInput.Prompt(InputPrompt.Pause)} devam   " +
                        $"B yerleşim   {GameInput.Prompt(InputPrompt.SkillTree)} yetenekler   {GameInput.Prompt(InputPrompt.Help)} rehber   " +
                        $"{GameInput.Prompt(InputPrompt.Menu)} menü";
        }

        void UpdateEnd(bool ended)
        {
            if (endPanel.activeSelf != ended)
            {
                endPanel.SetActive(ended);
                if (ended && UnityEngine.EventSystems.EventSystem.current != null)
                    UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(endPanel.GetComponentInChildren<Button>().gameObject);
            }
            if (!ended) return;

            endTitle.text = session.Outcome == GameOutcome.Victory ? "TAHLİYE BAŞARILI" : "OYUN BİTTİ";
            endTitle.color = session.Outcome == GameOutcome.Victory ? UiFactory.Good : UiFactory.Danger;
            endReason.text = session.EndReason;
        }

        // ---------------------------------------------------------------- Bildirimler

        void ShowNotice(string message, float duration = NoticeDuration)
        {
            // Aynı mesaj zaten görünüyorsa yalnızca süresini uzat.
            for (int i = 0; i < notices.Count; i++)
            {
                if (notices[i].Text != message) continue;
                notices[i] = new Notice { Text = message, Until = Time.unscaledTime + duration };
                return;
            }

            notices.Insert(0, new Notice { Text = message, Until = Time.unscaledTime + duration });
            if (notices.Count > NoticeSlots) notices.RemoveAt(notices.Count - 1);
        }

        void UpdateNotices()
        {
            float now = Time.unscaledTime;
            notices.RemoveAll(n => n.Until <= now);

            for (int i = 0; i < NoticeSlots; i++)
            {
                bool visible = i < notices.Count;
                if (noticePlates[i].gameObject.activeSelf != visible) noticePlates[i].gameObject.SetActive(visible);
                if (!visible) continue;

                float alpha = Mathf.Clamp01((notices[i].Until - now) / 0.4f);
                noticeTexts[i].text = notices[i].Text;
                noticeTexts[i].color = new Color(UiFactory.Text.r, UiFactory.Text.g, UiFactory.Text.b, alpha);
                noticePlates[i].color = new Color(0.08f, 0.08f, 0.1f, 0.85f * alpha);
            }
        }

        void OnEnable()
        {
            EventBus.Subscribe<TaskNotice>(OnTaskNotice);
            EventBus.Subscribe<AftershockImpact>(OnAftershockImpact);
            EventBus.Subscribe<SurvivorDied>(OnSurvivorDied);
            EventBus.Subscribe<PipeBurst>(OnPipeBurst);
            EventBus.Subscribe<ElectrocutionStarted>(OnElectrocution);
            EventBus.Subscribe<PowerChanged>(OnPowerChanged);
            EventBus.Subscribe<FireStarted>(OnFireStarted);
            EventBus.Subscribe<FireExtinguished>(OnFireExtinguished);
            EventBus.Subscribe<CeilingCollapsed>(OnCeilingCollapsed);
            EventBus.Subscribe<SmokeEvacuation>(OnSmokeEvacuation);
            EventBus.Subscribe<GasLeakStarted>(OnGasLeak);
            EventBus.Subscribe<GasLeakStopped>(OnGasStopped);
            EventBus.Subscribe<GasExploded>(OnGasExploded);
            EventBus.Subscribe<UpkeepResolved>(OnUpkeep);
            EventBus.Subscribe<SurvivorPanicked>(OnPanicked);
            EventBus.Subscribe<SurvivorJoined>(OnJoined);
            EventBus.Subscribe<SkillPointsGained>(OnSkillPoints);
            EventBus.Subscribe<SkillUnlocked>(OnSkillUnlocked);
            EventBus.Subscribe<ExpeditionStarted>(OnExpeditionStarted);
            EventBus.Subscribe<ExpeditionReturned>(OnExpeditionReturned);
            EventBus.Subscribe<RecipeCrafted>(OnRecipeCrafted);
            EventBus.Subscribe<PumpBroke>(OnPumpBroke);
            EventBus.Subscribe<RadioRepaired>(OnRadioRepaired);
            EventBus.Subscribe<SignalInterrupted>(OnSignalInterrupted);
            EventBus.Subscribe<HelicopterInbound>(OnHelicopterInbound);
            EventBus.Subscribe<SaveCompleted>(OnSaveCompleted);
            EventBus.Subscribe<GameLoaded>(OnGameLoaded);
            EventBus.Subscribe<StructurePlaced>(OnStructurePlaced);
            EventBus.Subscribe<WeatherChanged>(OnWeatherChanged);
            EventBus.Subscribe<NarrativeNotice>(OnNarrativeNotice);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<TaskNotice>(OnTaskNotice);
            EventBus.Unsubscribe<AftershockImpact>(OnAftershockImpact);
            EventBus.Unsubscribe<SurvivorDied>(OnSurvivorDied);
            EventBus.Unsubscribe<PipeBurst>(OnPipeBurst);
            EventBus.Unsubscribe<ElectrocutionStarted>(OnElectrocution);
            EventBus.Unsubscribe<PowerChanged>(OnPowerChanged);
            EventBus.Unsubscribe<FireStarted>(OnFireStarted);
            EventBus.Unsubscribe<FireExtinguished>(OnFireExtinguished);
            EventBus.Unsubscribe<CeilingCollapsed>(OnCeilingCollapsed);
            EventBus.Unsubscribe<SmokeEvacuation>(OnSmokeEvacuation);
            EventBus.Unsubscribe<GasLeakStarted>(OnGasLeak);
            EventBus.Unsubscribe<GasLeakStopped>(OnGasStopped);
            EventBus.Unsubscribe<GasExploded>(OnGasExploded);
            EventBus.Unsubscribe<UpkeepResolved>(OnUpkeep);
            EventBus.Unsubscribe<SurvivorPanicked>(OnPanicked);
            EventBus.Unsubscribe<SurvivorJoined>(OnJoined);
            EventBus.Unsubscribe<SkillPointsGained>(OnSkillPoints);
            EventBus.Unsubscribe<SkillUnlocked>(OnSkillUnlocked);
            EventBus.Unsubscribe<ExpeditionStarted>(OnExpeditionStarted);
            EventBus.Unsubscribe<ExpeditionReturned>(OnExpeditionReturned);
            EventBus.Unsubscribe<RecipeCrafted>(OnRecipeCrafted);
            EventBus.Unsubscribe<PumpBroke>(OnPumpBroke);
            EventBus.Unsubscribe<RadioRepaired>(OnRadioRepaired);
            EventBus.Unsubscribe<SignalInterrupted>(OnSignalInterrupted);
            EventBus.Unsubscribe<HelicopterInbound>(OnHelicopterInbound);
            EventBus.Unsubscribe<SaveCompleted>(OnSaveCompleted);
            EventBus.Unsubscribe<GameLoaded>(OnGameLoaded);
            EventBus.Unsubscribe<StructurePlaced>(OnStructurePlaced);
            EventBus.Unsubscribe<WeatherChanged>(OnWeatherChanged);
            EventBus.Unsubscribe<NarrativeNotice>(OnNarrativeNotice);
        }

        void OnTaskNotice(TaskNotice evt) => ShowNotice($"{evt.Survivor.DisplayName}: {evt.Message}");

        void OnAftershockImpact(AftershockImpact evt)
        {
            string columns = evt.BrokenColumns > 0 ? $", {evt.BrokenColumns} kırık kolon" : "";
            string exposed = evt.ExposedSurvivors > 0 ? $", {evt.ExposedSurvivors} kişi açıkta yakalandı" : "";
            ShowNotice($"Artçı sarsıntı! Bina -%{evt.Damage:0.#}{columns}{exposed}");
        }

        void OnSurvivorDied(SurvivorDied evt) => ShowNotice($"{evt.Survivor.DisplayName} öldü. {evt.Cause}", 6f);
        void OnPipeBurst(PipeBurst evt) => ShowNotice("Bodrumdaki şebeke borusu patladı! Su dolmaya başladı.");
        void OnElectrocution(ElectrocutionStarted evt) => ShowNotice($"{evt.Survivor.DisplayName} elektrik çarpıyor! Sudan çıkar ya da şalteri indir.");
        void OnPowerChanged(PowerChanged evt) => ShowNotice(evt.IsOn ? "Şalter kaldırıldı, elektrik geldi." : "Şalter indirildi, bina karanlıkta.");
        void OnFireStarted(FireStarted evt) => ShowNotice($"YANGIN çıktı: {building.GetFloorName(evt.Fire.Floor)}! Kova suyuyla söndür.");
        void OnFireExtinguished(FireExtinguished evt) => ShowNotice($"{building.GetFloorName(evt.Fire.Floor)} yangını söndürüldü.");
        void OnCeilingCollapsed(CeilingCollapsed evt) => ShowNotice($"{building.GetFloorName(evt.Fire.Floor)} tavanı yangından çöktü!");
        void OnSmokeEvacuation(SmokeEvacuation evt) => ShowNotice($"{evt.Survivor.DisplayName} öksürerek dumandan kaçıyor.");
        void OnGasLeak(GasLeakStarted evt) => ShowNotice(
            $"GAZ KAÇAĞI: {building.GetFloorName(evt.Leak.Floor)}! Şalteri indir (kıvılcım patlatır), pencereyi kır, vanayı kapat.", 7f);
        void OnGasStopped(GasLeakStopped evt) => ShowNotice("Gaz vanası kapatıldı. Kalan gaz dağılıyor.");
        void OnGasExploded(GasExploded evt) => ShowNotice($"GAZ PATLADI: {building.GetFloorName(evt.Leak.Floor)} alev aldı!", 6f);

        void OnUpkeep(UpkeepResolved evt) => ShowNotice(evt.Hungry == 0
            ? $"Gece: {evt.Fed} kişi Yaşam Kiti ile doydu."
            : $"Gece: {evt.Fed} kişi doydu, {evt.Hungry} kişi aç kaldı (Tükenmiş: yarı hız).", 5f);
        void OnPanicked(SurvivorPanicked evt) => ShowNotice($"{evt.Survivor.DisplayName} PANİKLEDİ! Bir süre emir dinlemeyecek.");
        void OnJoined(SurvivorJoined evt) => ShowNotice($"{evt.Survivor.DisplayName} sığınağa katıldı ({evt.Survivor.Branch.ToDisplayName()} uzmanı).");
        void OnSkillPoints(SkillPointsGained evt) =>
            ShowNotice($"Yeni gün: +1 yetenek puanı (toplam {evt.Total}). {GameInput.Prompt(InputPrompt.SkillTree)} ile yetenek ağacını aç.");
        void OnSkillUnlocked(SkillUnlocked evt) => ShowNotice($"Yetenek açıldı: {SkillCatalog.Get(evt.Id).Name}");

        void OnExpeditionStarted(ExpeditionStarted evt) => ShowNotice($"{evt.Explorer.DisplayName} {evt.Site.name} için yola çıktı.");

        void OnExpeditionReturned(ExpeditionReturned evt)
        {
            string message = $"{evt.Explorer.DisplayName} döndü. Çanta: {LootText(evt.Loot)}.";
            if (evt.HazardReport != null) message += $" {evt.HazardReport}";
            if (evt.Rescued != null) message += $" {evt.Rescued.DisplayName} kurtarıldı!";
            ShowNotice(message, 6f);
        }

        void OnRecipeCrafted(RecipeCrafted evt)
        {
            var info = RecipeCatalog.Get(evt.Id);
            ShowNotice(info.IsPlaceable
                ? $"Tezgah: {info.Name} tamamlandı! Sığınak ızgarasında istediğin hücreye yerleştir."
                : $"Tezgah: {info.Name} tamamlandı.");
        }

        void OnPumpBroke(PumpBroke evt) => ShowNotice("Elektrikli pompa bozuldu! Tamir için 1 Boru & Metal gerekli.");
        void OnRadioRepaired(RadioRepaired evt) => ShowNotice("Telsiz tamir edildi. Şartlar sağlanınca sinyal gönderilebilir.");
        void OnSignalInterrupted(SignalInterrupted evt) => ShowNotice(evt.Reason, 5f);
        void OnHelicopterInbound(HelicopterInbound evt) => ShowNotice("SİNYAL ULAŞTI! Tahliye helikopteri yolda.", 6f);

        void OnSaveCompleted(SaveCompleted evt)
        {
            if (evt.Success) ShowNotice(evt.SlotName == SaveManager.AutoSaveFile ? "Otomatik kayıt alındı." : "Oyun kaydedildi.");
        }

        void OnGameLoaded(GameLoaded evt)
        {
            if (evt.Success) ShowNotice("Kayıtlı oyun yüklendi.");
        }

        void OnStructurePlaced(StructurePlaced evt)
        {
            if (evt.Structure != null)
                ShowNotice($"{evt.Structure.Info.Name} {building.GetFloorName(evt.Structure.GridFloor)} katına yerleştirildi.");
        }

        void OnWeatherChanged(WeatherChanged evt)
        {
            if (evt.Weather != WeatherType.Clear)
                ShowNotice($"Hava: {WeatherSystem.ToDisplayName(evt.Weather)} — {WeatherSystem.ToEffectSummary(evt.Weather)}", 5f);
        }

        void OnNarrativeNotice(NarrativeNotice evt) => ShowNotice(evt.Message, 6f);

        // ---------------------------------------------------------------- Yardımcılar

        string SpeedLabel() => timeManager == null ? "1x" : timeManager.Speed switch
        {
            TimeSpeed.Paused => "Durdu",
            TimeSpeed.Fast => "2x",
            _ => "1x",
        };

        static Color BranchColor(SkillBranch branch) => branch switch
        {
            SkillBranch.Construction => new Color(0.98f, 0.66f, 0.32f),
            SkillBranch.Exploration => new Color(0.52f, 0.9f, 0.5f),
            _ => new Color(0.5f, 0.75f, 1f),
        };

        static string TaskLabel(TaskRunner tasks)
        {
            if (tasks.Current == null) return tasks.QueuedCount > 0 ? $"sırada {tasks.QueuedCount}" : "boşta";

            string action = tasks.State == TaskState.Moving ? "yolda" : $"%{tasks.Progress01 * 100f:0}";
            string queued = tasks.QueuedCount > 0 ? $" (+{tasks.QueuedCount})" : "";
            return $"{tasks.Current.ActionLabel}: {tasks.Current.DisplayName} — {action}{queued}";
        }

        static string LootText(IReadOnlyList<ResourceAmount> items)
        {
            if (items.Count == 0) return "boş";
            var parts = new string[items.Count];
            for (int i = 0; i < items.Count; i++) parts[i] = items[i].ToString();
            return string.Join(", ", parts);
        }
    }
}
