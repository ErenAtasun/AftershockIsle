using System;
using System.IO;
using AftershockIsle.Core;
using AftershockIsle.Expedition;
using UnityEngine;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Tam ekran Şehir Haritası iskeleti (uGUI). Hem 'M' ile inceleme / kapıdan sefere çıkış (ExpeditionWindow) hem de
    /// gece başındaki akşam planı (EveningPlan) aynı görünümü kullanır.
    /// Pencere ekranı kaplar (kenarlarda birkaç piksel boşluk); city_map.png en-boy oranı bozulmadan sığabileceği en büyük
    /// boyda ortalanır. Düğümler (taşlar / plakalar) harita boyutuna oranlı yerleşir, harita büyüyünce kaymaz.
    /// Detay kartı haritanın üstüne yarı saydam biner ve seçili yer yoksa kapalıdır. İsteğe bağlı sol panel
    /// (akşam planındaki kazazede listesi) açılınca harita kalan alana sığdırılır.
    /// </summary>
    public class CityMapView
    {
        public const float ScreenInset = 6f;
        const float SideGap = 12f;
        const float DetailWidth = 430f;
        const float FallbackAspect = 1024f / 898f;

        static Sprite mapSprite;
        static Sprite frameSprite;

        public readonly UiWindow Window;
        /// <summary>Haritanın sığdırıldığı alan (sol panel açıkken daralır).</summary>
        public readonly RectTransform MapArea;
        /// <summary>Harita görselinin kendisi; düğümler bunun içine oranlı yerleşir.</summary>
        public readonly RectTransform Map;
        /// <summary>Sol panel (varsayılan kapalı).</summary>
        public readonly RectTransform SidePanel;
        public readonly RectTransform Detail;
        public readonly Text DetailTitle;
        public readonly Text DetailBadge;
        public readonly Text DetailText;
        public readonly Button DetailAction;

        readonly Text detailActionLabel;
        readonly GameObject bannerPlate;
        readonly Text bannerText;
        Action detailCallback;

        public bool IsOpen => Window.IsOpen;
        public static Sprite FrameSprite { get { EnsureSprites(); return frameSprite; } }

        /// <summary>Düğüm yanıp sönme çarpanı (seçili vurgular için).</summary>
        public static float Pulse => 0.92f + 0.08f * Mathf.Sin(Time.unscaledTime * 3f);

        public CityMapView(string name, Action onClose, bool closable)
        {
            EnsureSprites();
            Window = new UiWindow(name, new Vector2(1600f, 900f), onClose, false, closable);

            // Ekranı kapla: panel tuvale yayılır, başlık genişliği de panelle büyür.
            UiFactory.Stretch(Window.Panel, ScreenInset);
            var title = Window.Title.rectTransform;
            title.anchorMin = new Vector2(0f, 1f);
            title.anchorMax = new Vector2(1f, 1f);
            title.pivot = new Vector2(0.5f, 1f);
            title.offsetMin = new Vector2(22f, -50f);
            title.offsetMax = new Vector2(-90f, -10f);

            // Sol panel (akşam planı): içerik alanının solunda tam boy.
            SidePanel = UiFactory.Panel(Window.Content, "Sol Panel", new Color(0.09f, 0.1f, 0.13f, 0.96f)).rectTransform;
            SidePanel.anchorMin = new Vector2(0f, 0f);
            SidePanel.anchorMax = new Vector2(0f, 1f);
            SidePanel.pivot = new Vector2(0f, 0.5f);
            SidePanel.anchoredPosition = Vector2.zero;
            SidePanel.sizeDelta = new Vector2(500f, 0f);
            SidePanel.gameObject.SetActive(false);

            MapArea = UiFactory.Rect("Harita Alanı", Window.Content);
            UiFactory.Stretch(MapArea);

            var image = UiFactory.Image(MapArea, "Harita", mapSprite, Color.white, true);
            image.preserveAspect = false;
            Map = image.rectTransform;
            Map.pivot = new Vector2(0.5f, 0.5f);
            var fitter = image.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = mapSprite != null ? mapSprite.rect.width / mapSprite.rect.height : FallbackAspect;

            // Harita üstünde ipucu şeridi (ör. "Ali için keşif yeri seç").
            var banner = UiFactory.Panel(MapArea, "İpucu Şeridi", new Color(0.08f, 0.08f, 0.1f, 0.9f));
            // Görselin kendi başlığı üstte; ipucu şeridi altta durur, başlığı kapatmaz.
            UiFactory.Place(banner.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(760f, 46f));
            banner.raycastTarget = false;
            bannerText = UiFactory.Label(banner.transform, "", 19, UiFactory.Accent, TextAnchor.MiddleCenter);
            UiFactory.Stretch(bannerText.rectTransform, 6f);
            bannerPlate = banner.gameObject;
            bannerPlate.SetActive(false);

            // Detay kartı: haritanın sağ tarafına biner, yarı saydam.
            var card = UiFactory.Panel(Window.Content, "Detay Kartı", new Color(0.07f, 0.08f, 0.11f, 0.86f));
            Detail = card.rectTransform;
            Detail.anchorMin = new Vector2(1f, 0f);
            Detail.anchorMax = new Vector2(1f, 1f);
            Detail.pivot = new Vector2(1f, 0.5f);
            Detail.anchoredPosition = new Vector2(-8f, 0f);
            Detail.sizeDelta = new Vector2(DetailWidth, -16f);

            DetailTitle = UiFactory.Label(Detail, "", 24, UiFactory.Accent, TextAnchor.UpperLeft);
            Top(DetailTitle.rectTransform, 16f, 34f);
            DetailBadge = UiFactory.Label(Detail, "", 15, new Color(0.6f, 0.85f, 1f), TextAnchor.UpperLeft);
            Top(DetailBadge.rectTransform, 52f, 24f);
            var divider = UiFactory.Panel(Detail, "Çizgi", new Color(0.3f, 0.32f, 0.38f, 0.8f));
            divider.raycastTarget = false;
            Top(divider.rectTransform, 82f, 2f);

            DetailText = UiFactory.Label(Detail, "", 16, new Color(0.9f, 0.9f, 0.86f), TextAnchor.UpperLeft, FontStyle.Normal, false);
            var body = DetailText.rectTransform;
            body.anchorMin = new Vector2(0f, 0f);
            body.anchorMax = new Vector2(1f, 1f);
            body.pivot = new Vector2(0.5f, 1f);
            body.offsetMin = new Vector2(18f, 86f);
            body.offsetMax = new Vector2(-18f, -94f);

            DetailAction = UiFactory.TextButton(Detail, "", new Vector2(DetailWidth - 36f, 52f), () => detailCallback?.Invoke(), UiFactory.Good, 19);
            var actionRect = (RectTransform)DetailAction.transform;
            actionRect.anchorMin = new Vector2(0f, 0f);
            actionRect.anchorMax = new Vector2(1f, 0f);
            actionRect.pivot = new Vector2(0.5f, 0f);
            actionRect.offsetMin = new Vector2(18f, 18f);
            actionRect.offsetMax = new Vector2(-18f, 70f);
            detailActionLabel = DetailAction.GetComponentInChildren<Text>();

            ShowDetail(false);
        }

        static void Top(RectTransform rect, float y, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(18f, -y - height);
            rect.offsetMax = new Vector2(-18f, -y);
        }

        // ---------------------------------------------------------------- Görünüm

        public void Open()
        {
            Window.Show(true);
            // Harita oranı ilk karede doğru olsun (sonraki kareye kalırsa açılışta zıplar).
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(MapArea);
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.MapOpen, 0.8f, 0.02f);
        }
        public void Close() => Window.Show(false);

        /// <summary>Sol paneli açar/kapatır; harita kalan alana sığdırılır.</summary>
        public void SetSidePanel(bool visible, float width)
        {
            SidePanel.gameObject.SetActive(visible);
            SidePanel.sizeDelta = new Vector2(width, 0f);
            MapArea.offsetMin = new Vector2(visible ? width + SideGap : 0f, 0f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(MapArea);
        }

        public void ShowDetail(bool visible)
        {
            if (Detail.gameObject.activeSelf != visible) Detail.gameObject.SetActive(visible);
        }

        /// <summary>Detay kartının alt düğmesi; label null ise düğme gizlenir.</summary>
        public void SetDetailAction(string label, Action action)
        {
            detailCallback = action;
            bool visible = !string.IsNullOrEmpty(label);
            if (DetailAction.gameObject.activeSelf != visible) DetailAction.gameObject.SetActive(visible);
            if (visible) detailActionLabel.text = label;
        }

        public void SetBanner(string text)
        {
            bool visible = !string.IsNullOrEmpty(text);
            if (bannerPlate.activeSelf != visible) bannerPlate.SetActive(visible);
            if (visible) bannerText.text = text;
            bannerPlate.transform.SetAsLastSibling();
        }

        // ---------------------------------------------------------------- Düğümler

        /// <summary>
        /// Haritaya oranlı bir düğüm ekler. norm: merkez x, merkez y (üstten), genişlik, yükseklik (0–1, harita boyutuna göre).
        /// plate verilirse haritada çizimi olmayan yer için renkli plaka çizilir ve adı yazılır.
        /// </summary>
        public Button AddNode(string name, Rect norm, Color? plate, out Image highlight, out Text label)
        {
            var anchorMin = new Vector2(norm.x - norm.width * 0.5f, 1f - norm.y - norm.height * 0.5f);
            var anchorMax = new Vector2(norm.x + norm.width * 0.5f, 1f - norm.y + norm.height * 0.5f);

            if (plate.HasValue)
            {
                // Haritadaki taşlara benzesin: koyu gölge, kalın kenarlı renkli levha, büyük koyu yazı.
                var shadow = UiFactory.Image(Map, name + " Gölge", null, new Color(0f, 0f, 0f, 0.45f));
                var shadowRect = shadow.rectTransform;
                shadowRect.anchorMin = anchorMin;
                shadowRect.anchorMax = anchorMax;
                shadowRect.offsetMin = new Vector2(5f, -7f);
                shadowRect.offsetMax = new Vector2(5f, -7f);
            }

            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(Map, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            go.AddComponent<UiHoverFlag>();

            var backing = go.GetComponent<Image>();
            backing.color = new Color(1f, 1f, 1f, 0.001f); // Görsel haritada çizili; yalnızca tıklama alanı.

            var hl = new GameObject("Vurgu", typeof(RectTransform), typeof(Image));
            hl.transform.SetParent(rect, false);
            UiFactory.Stretch((RectTransform)hl.transform, -3f);
            highlight = hl.GetComponent<Image>();
            highlight.sprite = frameSprite;
            highlight.type = Image.Type.Sliced;
            highlight.raycastTarget = false;
            highlight.color = new Color(1f, 0.85f, 0.3f, 0f);

            label = UiFactory.Label(rect, "", 15, Color.white, TextAnchor.LowerCenter);
            UiFactory.Stretch(label.rectTransform, 4f);

            if (plate.HasValue)
            {
                backing.sprite = UiFactory.Plate;
                backing.type = Image.Type.Sliced;
                backing.color = plate.Value;
                label.alignment = TextAnchor.MiddleCenter;
                label.fontSize = 22;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 12;
                label.resizeTextMaxSize = 22;
                label.color = new Color(0.09f, 0.1f, 0.17f);
                foreach (var effect in label.GetComponents<Shadow>()) effect.enabled = false;
                UiFactory.Stretch(label.rectTransform, 8f);
            }

            var button = go.GetComponent<Button>();
            button.targetGraphic = backing;
            go.AddComponent<UiPress>().ScaleOnHover = false;
            return button;
        }

        /// <summary>Düğümün altına isim rozeti (ör. o yere giden kazazedeler). Boş metinde gizlenir.</summary>
        public static Text AddBadge(Button node, out GameObject plate, Color? color = null)
        {
            var image = UiFactory.Image(node.transform, "Rozet", null, color ?? new Color(0.12f, 0.26f, 0.45f, 0.95f));
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -4f);
            rect.sizeDelta = new Vector2(260f, 30f);
            var text = UiFactory.Label(rect, "", 15, UiFactory.Text, TextAnchor.MiddleCenter);
            UiFactory.Stretch(text.rectTransform, 4f);
            plate = image.gameObject;
            plate.SetActive(false);
            return text;
        }

        // ---------------------------------------------------------------- Yerleşim (orana bağlı)

        // city_map.png (1024×898) üzerindeki taşların ölçülmüş yerleri: merkez x, merkez y (üstten), genişlik, yükseklik.
        public static readonly Rect ShelterRect = new Rect(0.531f, 0.567f, 0.22f, 0.25f);
        static readonly Rect ClinicRect = new Rect(0.285f, 0.353f, 0.315f, 0.18f);
        static readonly Rect ConstructionRect = new Rect(0.805f, 0.31f, 0.255f, 0.15f);
        static readonly Rect RubbleRect = new Rect(0.648f, 0.828f, 0.315f, 0.175f);
        // Görselde çizimi olmayan yerler parşömenin boş kısımlarına levha olarak konur.
        static readonly Rect WorkshopRect = new Rect(0.215f, 0.6f, 0.24f, 0.085f);
        static readonly Rect MarketRect = new Rect(0.55f, 0.2f, 0.24f, 0.085f);
        static readonly Rect StationRect = new Rect(0.25f, 0.84f, 0.27f, 0.085f);

        static readonly Color WorkshopPlate = new Color(0.45f, 0.72f, 0.48f);
        static readonly Color MarketPlate = new Color(0.78f, 0.6f, 0.86f);
        static readonly Color StationPlate = new Color(0.9f, 0.82f, 0.4f);
        static readonly Color DefaultPlate = new Color(0.7f, 0.74f, 0.8f);

        /// <summary>Sefer lokasyonunun haritadaki yeri; plate dolu ise haritada çizimi yoktur.</summary>
        public static Rect SiteLayout(ExpeditionSite site, out Color? plate)
        {
            plate = null;
            string name = Normalize(site.name);
            if (name.Contains("klinik")) return ClinicRect;
            if (name.Contains("inşaat") || name.Contains("insaat")) return ConstructionRect;
            if (name.Contains("enkaz")) return RubbleRect;
            if (name.Contains("atölye") || name.Contains("atolye")) { plate = WorkshopPlate; return WorkshopRect; }
            if (name.Contains("market")) { plate = MarketPlate; return MarketRect; }
            if (name.Contains("istasyon") || name.Contains("benzin")) { plate = StationPlate; return StationRect; }
            plate = DefaultPlate;
            return new Rect(site.mapPosition.x, site.mapPosition.y, 0.246f, 0.112f);
        }

        /// <summary>Türkçe büyük İ/I küçültülünce bozulmasın ("İnşaat" → "inşaat"; ToLowerInvariant "i̇nşaat" verir).</summary>
        static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace('İ', 'i').Replace('I', 'ı').ToLowerInvariant().Replace("\u0307", "");
        }

        /// <summary>Gece keşfi yerlerinin haritadaki düğümü: Yıkıntılar → Enkaz Alanı, Market → Market, Hastane → Yıkık Klinik.</summary>
        public static Rect NightLayout(NightSite site, out Color? plate)
        {
            plate = null;
            switch (site)
            {
                case NightSite.Market: plate = MarketPlate; return MarketRect;
                case NightSite.Hospital: return ClinicRect;
                default: return RubbleRect;
            }
        }

        // ---------------------------------------------------------------- Görseller

        static void EnsureSprites()
        {
            // Önce derleme güvenli yol: arayüz görselleri kaydı (UiSkin). Dosyadan okuma yalnızca yedek (editör / geliştirme).
            if (mapSprite == null && GameAssets.Ui != null) mapSprite = GameAssets.Ui.cityMap;
            if (mapSprite == null)
            {
                string path = Path.Combine(Application.dataPath, "Models/UI/city_map.png");
                if (File.Exists(path))
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(File.ReadAllBytes(path), false))
                    {
                        tex.filterMode = FilterMode.Bilinear;
                        mapSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    }
                }
            }

            if (frameSprite == null)
            {
                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        bool border = x < 4 || x >= size - 4 || y < 4 || y >= size - 4;
                        tex.SetPixel(x, y, border ? Color.white : new Color(1f, 1f, 1f, 0.12f));
                    }
                tex.Apply();
                frameSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(8, 8, 8, 8));
            }
        }
    }

    /// <summary>Fare üstünde ya da gamepad ile seçili mi (harita düğümü vurgusu için).</summary>
    public class UiHoverFlag : UnityEngine.MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler,
        UnityEngine.EventSystems.ISelectHandler, UnityEngine.EventSystems.IDeselectHandler
    {
        bool pointer;
        bool selected;
        public bool Hovered => pointer || selected;
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData) => pointer = true;
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData) => pointer = false;
        public void OnSelect(UnityEngine.EventSystems.BaseEventData eventData) => selected = true;
        public void OnDeselect(UnityEngine.EventSystems.BaseEventData eventData) => selected = false;
        void OnDisable() => pointer = selected = false;
    }
}
