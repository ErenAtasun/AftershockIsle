using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Crisis;
using AftershockIsle.Endgame;
using AftershockIsle.Interaction;
using AftershockIsle.Skills;
using UnityEngine;
using UnityEngine.UI;

namespace AftershockIsle.UI
{
    /// <summary>
    /// Olay tabanlı öğretici: bir durum ilk kez yaşandığında ilgili ipucu bir kez gösterilir.
    /// İpuçları sıraya girer, kendiliğinden kapanır ya da "Tamam" ile geçilir. H kontrol rehberini açar.
    /// </summary>
    public class TutorialHints : MonoBehaviour
    {
        const float HintSeconds = 14f;

        struct Hint
        {
            public string Id;
            public string Title;
            public string Text;
        }

        readonly Queue<Hint> queue = new Queue<Hint>();
        readonly HashSet<string> shown = new HashSet<string>();
        Hint current;
        bool hasCurrent;
        float currentUntil;
        bool tutorialEnabled = true;
        bool helpOpen;
        float startDelay = 1.5f;

        // uGUI: alt ortada ipucu kartı, kontrol rehberi pencere olarak.
        GameObject hintCard;
        Text hintTitle;
        Text hintBody;
        UiWindow helpWindow;
        bool welcomed;

        // Hoş geldin ipucu ilk sırada olsun: oyun başında otomatik seçim de bir ipucu tetikler.
        void Awake()
        {
            if (GetComponent<FirstDayGuide>() == null) gameObject.AddComponent<FirstDayGuide>();
            UiFactory.EnsureEventSystem();
            BuildHintCard();
            Show("welcome", "Aftershock: Isle",
                    "Binayı ayakta tut, kazazedeleri hayatta tut, çatıdan helikopterle tahliye ol. Tab / LB-RB ile karakter seç, WASD / sol çubukla yürüt, " +
                    "F / A ile yanındaki işi yaptır (moloz kaz, kolon destekle). Space / Y zamanı durdurur. H / D-pad sol: kontrol rehberi. Fare de çalışır.");
        }

        void OnEnable()
        {
            EventBus.Subscribe<SurvivorSelected>(OnSelected);
            EventBus.Subscribe<RubbleCleared>(OnRubbleCleared);
            EventBus.Subscribe<ColumnStateChanged>(OnColumnChanged);
            EventBus.Subscribe<DayPhaseChanged>(OnPhaseChanged);
            EventBus.Subscribe<AftershockWarning>(OnQuakeWarning);
            EventBus.Subscribe<PipeBurst>(OnPipeBurst);
            EventBus.Subscribe<GasLeakStarted>(OnGasLeak);
            EventBus.Subscribe<AftershockImpact>(OnQuakeImpact);
            EventBus.Subscribe<FireStarted>(OnFireStarted);
            EventBus.Subscribe<UpkeepResolved>(OnUpkeep);
            EventBus.Subscribe<SkillPointsGained>(OnSkillPoints);
            EventBus.Subscribe<SurvivorPanicked>(OnPanic);
            EventBus.Subscribe<RecipeCrafted>(OnRecipeCrafted);
            EventBus.Subscribe<SurvivorJoined>(OnJoined);
            EventBus.Subscribe<RadioRepaired>(OnRadioRepaired);
            EventBus.Subscribe<PlaceableItemReady>(OnPlaceableReady);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<SurvivorSelected>(OnSelected);
            EventBus.Unsubscribe<RubbleCleared>(OnRubbleCleared);
            EventBus.Unsubscribe<ColumnStateChanged>(OnColumnChanged);
            EventBus.Unsubscribe<DayPhaseChanged>(OnPhaseChanged);
            EventBus.Unsubscribe<AftershockWarning>(OnQuakeWarning);
            EventBus.Unsubscribe<PipeBurst>(OnPipeBurst);
            EventBus.Unsubscribe<GasLeakStarted>(OnGasLeak);
            EventBus.Unsubscribe<AftershockImpact>(OnQuakeImpact);
            EventBus.Unsubscribe<FireStarted>(OnFireStarted);
            EventBus.Unsubscribe<UpkeepResolved>(OnUpkeep);
            EventBus.Unsubscribe<SkillPointsGained>(OnSkillPoints);
            EventBus.Unsubscribe<SurvivorPanicked>(OnPanic);
            EventBus.Unsubscribe<RecipeCrafted>(OnRecipeCrafted);
            EventBus.Unsubscribe<SurvivorJoined>(OnJoined);
            EventBus.Unsubscribe<RadioRepaired>(OnRadioRepaired);
            EventBus.Unsubscribe<PlaceableItemReady>(OnPlaceableReady);
        }

        // ---------------------------------------------------------------- İpucu tetikleyicileri

        void OnSelected(SurvivorSelected evt)
        {
            if (evt.Survivor == null) return;
            Show("select", "Kolon seviyeleri",
                "Kolonların 3 seviyesi var: Lv1 Kırık, Lv2 Çatlak, Lv3 Çelik. Çatlak kolon sarsıntıda kırılır. Kırık kolonla etkileş " +
                "(F / A ya da sağ tık): 4 Kalas & Hurda ile yeniden dikilir. " +
                "Kalası molozlardan kazarak bulursun; bulunan malzeme doğrudan depoya eklenir.");
        }

        void OnRubbleCleared(RubbleCleared evt) => Show("rubble", "Depo",
            "Kazılan malzeme doğrudan depoya girer ve üst taraftaki sayaca eklenir. Tezgah, tamir ve kolonlar buradan harcar. " +
            "Farede Shift + sağ tık ile birden fazla işi sıraya ekleyebilirsin.");

        void OnColumnChanged(ColumnStateChanged evt)
        {
            if (evt.LeveledUp && evt.Column.State == ColumnState.Cracked)
                Show("support", "Kolon yeniden dikildi", "Çatlak kolon ilk sarsıntıda yine kırılır. Kalıcı çözüm: Kademe 3'te Çelik Kiriş üretip kolonu Lv3'e güçlendir; çelik kolon sarsıntıdan etkilenmez.");
        }

        void OnPhaseChanged(DayPhaseChanged evt)
        {
            if (evt.Phase == DayPhase.Noon)
                Show("noon", "Öğle: dış sefer",
                    "Öğle vakti giriş katındaki dış kapıdan sefere çıkabilirsin (günde bir kez). Önce kapı önündeki yıkıntıyı temizlemelisin. " +
                    "Yıkık Klinik'ten Yaşam Kiti, Atölye'den Elektronik getir.");
            else if (evt.Phase == DayPhase.Night && evt.Day == 1)
                Show("night1", "Gece tüketimi",
                    "Her gece kazazede başına 1 Yaşam Kiti harcanır. Kit yoksa karakter aç kalır, 'Tükenmiş' olur ve yarı hızda çalışır. " +
                    "Dolapları 'Ara' ile arayıp kit bulabilirsin.");
        }

        void OnQuakeWarning(AftershockWarning evt) => Show("quake", "Artçı sarsıntı!",
            "Zamanı durdur (Space / Y) ve karakterleri yeşil parlayan Yaşam Üçgeni alanlarına gönder (sağlam kolon dibi, mobilya önü). " +
            "Açıkta kalan hasar alır ve enkaz altında kalır. Mobilyayı parçalarsan sığınak kaybolur.");

        void OnPipeBurst(PipeBurst evt) => Show("pipe", "Su baskını",
            "Doğru sıra: 1) Giriş katından şalteri indir (su elektriklenmesin), 2) Bodrumda tulumbayı pompala (Q/E ya da LT/RT'ye dönüşümlü bas), " +
            "3) Boruyu kaynakla, 4) Şalteri kaldır. Su %100'de 45 sn kalırsa bina yıkılır.");

        void OnGasLeak(GasLeakStarted evt) => Show("gas", "Gaz kaçağı!",
            "Gaz %50'yi geçince kata girilemez ve içeridekiler zehirlenir; %60'ta bir kıvılcım (şalter açıkken) ya da yangın gazı patlatır. " +
            "Doğru sıra: 1) Şalteri indir, 2) Dış pencereyi kır (gaz tahliye olur), 3) Gaz Vanası'nı kapat, 4) Şalteri kaldır.");

        void OnQuakeImpact(AftershockImpact evt)
        {
            if (evt.BrokenColumns > 0)
                Show("debris", "Tavan enkazı",
                    "Kırılan kolon tavan enkazı düşürdü: enkaz kat içinde yolu kapatır. Kazarak aç (Kalas da çıkar); kazılana kadar öbür tarafa geçilemez.");
        }

        void OnFireStarted(FireStarted evt) => Show("fire", "Yangın!",
            "Yangınla etkileş: karakter kova doldurup atar (bodrum suyu ya da Su Bidonu). Duman %50'yi geçince kata girilemez; " +
            "arka duvardaki pencereyi kır. Yangın 60 sn tam güçte kalırsa tavan çöker.");

        void OnUpkeep(UpkeepResolved evt)
        {
            if (evt.Hungry > 0)
                Show("hungry", "Açlık", "Aç kalan karakter Tükenmiş olur (yarı hız) ve stresi artar. Stoğa Yaşam Kiti getir ya da dolap ara.");
        }

        void OnSkillPoints(SkillPointsGained evt) => Show("skills", "Yetenek puanı",
            "K tuşuyla yetenek ağacını aç. Her gün +1 puan gelir. Bir dalın yetenekleri, o dalın uzmanı bir kazazede sığınaktayken açılır.");

        void OnPanic(SurvivorPanicked evt) => Show("panic", "Panik",
            "Stres %100 olunca karakter 15 sn emir dinlemez. Stresi düşürmek için ışıkta dinlendir, gece doyur, şalteri açık tut.");

        void OnRecipeCrafted(RecipeCrafted evt)
        {
            if (evt.Id == RecipeId.UpgradeTier3)
                Show("tier3", "Kademe 3",
                    "Tahliye için: Telsiz Modülü üret ve çatıdaki telsizi tamir et; çatı kolonlarını Çelik Kirişle güçlendir; çatı molozunu temizle. " +
                    "Sağ alttaki listeyi takip et.");
        }

        void OnJoined(SurvivorJoined evt) => Show("joined", "Yeni kazazede",
            "Her kazazede farklı bir yetenek dalının uzmanı (gövde rengi: turuncu İnşaat, yeşil Keşif, mavi Mühendislik). " +
            "Bir kişi daha, gece bir Yaşam Kiti daha harcar.");

        void OnPlaceableReady(PlaceableItemReady evt) => Show("build", "Eşya kuruluma hazır",
            "Eve kurulan yapılar İnşaat Masası'nda hazırlanır (silah ve ekipman ise Çalışma Tezgahı'nda) ve önce eve kurulur. Yerleşim modu açıldı ve zaman durdu: fareyle bir hücre seç (yeşil = kurulabilir, kırmızı = olmaz), " +
            "sol tık / Enter (gamepad: A) ile kur. R / Y: döndür, ok tuşları / sol çubuk: hücre, Q-E / LB-RB: kat, Tab / LT-RT: yapı seç, X: depoya kaldır, B / sağ tık: kapat. " +
            "Daha sonra B (gamepad: D-pad ↑) ile yeniden açabilirsin.");

        void OnRadioRepaired(RadioRepaired evt) => Show("radio", "Sinyal",
            "Telsize 'Sinyal Gönder' de. Başında kesintisiz 60 sn kalınmalı. Elektrik kesilirse, deprem olursa ya da operatör ayrılırsa sinyal sıfırlanır; " +
            "sarsıntı gelmeden önce sinyali başlatma.");

        // ---------------------------------------------------------------- Gösterim

        void Show(string id, string title, string text)
        {
            if (!tutorialEnabled || !shown.Add(id)) return;
            queue.Enqueue(new Hint { Id = id, Title = title, Text = text });
        }

        void Update()
        {
            // D-pad sol başka pencerelerde menü gezinmesidir; rehber yalnızca başka pencere açık değilken (ya da kendisi açıkken) değişir.
            if (GameInput.HelpTogglePressed && (helpOpen || !ModalMenu.IsOpen)) helpOpen = !helpOpen;
            else if (helpOpen && GameInput.MenuBackPressed) helpOpen = false;
            if (hasCurrent && GameInput.DismissHintPressed && !ModalMenu.IsOpen) hasCurrent = false;
            RefreshHelp();

            if (!welcomed)
            {
                startDelay -= Time.unscaledDeltaTime;
                if (startDelay > 0f) return;

                welcomed = true;
            }

            RefreshHint();

            if (!hasCurrent && queue.Count > 0)
            {
                current = queue.Dequeue();
                hasCurrent = true;
                currentUntil = Time.unscaledTime + HintSeconds;
            }
            else if (hasCurrent && Time.unscaledTime >= currentUntil)
            {
                hasCurrent = false;
            }

            RefreshHint();
        }

        // ---------------------------------------------------------------- uGUI

        void BuildHintCard()
        {
            var canvas = UiFactory.Canvas("İpucu", 12, transform);
            var card = UiFactory.Panel(canvas.transform, "İpucu Kartı", UiFactory.Slate);
            hintCard = card.gameObject;
            UiFactory.Place(card.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(860f, 0f));

            var layout = hintCard.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 16, 16);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            hintCard.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            hintTitle = UiFactory.Label(hintCard.transform, "", 24, UiFactory.Accent, TextAnchor.MiddleLeft);
            hintTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

            hintBody = UiFactory.Label(hintCard.transform, "", 21, UiFactory.Text, TextAnchor.UpperLeft, FontStyle.Normal, false);
            hintBody.lineSpacing = 1.1f;

            var buttons = UiFactory.Rect("Düğmeler", hintCard.transform);
            buttons.gameObject.AddComponent<LayoutElement>().preferredHeight = 50f;
            var row = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 12f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = false;
            row.childControlHeight = false;

            UiFactory.TextButton(buttons, "Tamam", new Vector2(180f, 46f), () => hasCurrent = false, UiFactory.Good, 20);
            UiFactory.TextButton(buttons, "İpuçlarını kapat", new Vector2(250f, 46f), () =>
            {
                tutorialEnabled = false;
                hasCurrent = false;
                queue.Clear();
            }, null, 20);

            hintCard.SetActive(false);
        }

        void RefreshHint()
        {
            bool show = hasCurrent && tutorialEnabled && !PauseMenu.IsOpen;
            if (show == hintCard.activeSelf) { if (show) FillHint(); return; }

            hintCard.SetActive(show);
            if (show) FillHint();
        }

        void FillHint()
        {
            hintTitle.text = $"İPUCU: {current.Title}";
            hintBody.text = current.Text;
        }

        void RefreshHelp()
        {
            if (helpWindow == null)
            {
                if (!helpOpen) return;
                BuildHelp();
            }

            helpWindow.Show(helpOpen);
            if (helpOpen)
            {
                helpWindow.Title.text = $"Kontrol Rehberi   ({GameInput.Prompt(InputPrompt.Help)}: kapat)";
                helpWindow.Tick(null);
            }
        }

        void BuildHelp()
        {
            helpWindow = new UiWindow("Kontrol Rehberi", new Vector2(1240f, 880f), () => helpOpen = false);
            helpWindow.Footer.gameObject.SetActive(false);

            Section("SIĞINAK", "klavye", "gamepad");
            Row("Karakter yürüt", "WASD / oklar", "sol çubuk");
            Row("Merdiven çık / in", "W / S (en yakın merdiven)", "çubuk yukarı / aşağı");
            Row("En yakın işi yap", "F", "A");
            Row("İşi bırak, dur", "X", "B");
            Row("Karakter değiştir", "Tab / Shift+Tab", "RB / LB");
            Row("Duraklat / hız", "Space, 1 / 2", "Y, X");
            Row("Kamera kaydır / yakınlaş", "seçim yokken WASD, tekerlek", "sağ çubuk");
            Row("Yetenekler / rehber", "K / H", "D-pad sağ / sol");
            Row("Tulumba ritmi", "Q / E", "LT / RT");
            Row("Yerleşim modu aç / kapat", "B", "D-pad ↑");
            Row("Kamera yakınlaş / uzaklaş", "fare tekerleği", "R3 / L3");
            Note("Fare: sol tık seç, sağ tık iş ver ya da yürü, Shift + sağ tık sıraya ekle.");

            Section("YERLEŞİM (sığınağa eşya kurma, zaman durur)", "klavye / fare", "gamepad");
            Row("Hücre seç", "fare / ok tuşları", "sol çubuk / D-pad");
            Row("Kat değiştir", "Q / E", "LB / RB");
            Row("Yapı seç", "Tab / Shift+Tab", "LT / RT");
            Row("Kur / taşı", "sol tık / Enter", "A");
            Row("Döndür", "R", "Y");
            Row("Depoya kaldır", "X", "X");
            Row("Kapat", "B / sağ tık / Esc", "B / Start");

            Section("DIŞ SAHA", "klavye", "gamepad");
            Row("Yürü / nişan / ateş", "WASD / fare / sol tık", "sol çubuk / sağ çubuk / RT");
            Row("Silah değiştir", "Q", "X");
            Row("Çömel (siper)", "Shift / C (basılı)", "LT (basılı)");
            Row("Kaçış / ara / ilk yardım", "Space / F / R", "B / A / Y");
            Row("Sığınağa dön", "Tab", "RB / LB ya da Select");
            Note("Alçak siperin arkasında çömelen görünmez; ateş edince bir an doğrulur. Duvar ya da araba kenarında nişanı kenarın ötesine çevir: köşeden eğilirsin.");
            Note("Düşmanlar görünce (!) kovalar, sesi duyunca (?) bakmaya gelir. Tabanca gürültülü, sapan sessiz.");

            Section("MENÜLER", "klavye", "gamepad");
            Row("Gez / seç / kapat", "oklar / Enter / Backspace", "D-pad / A / B");
            Row("Duraklatma menüsü", "Esc", "Start");

            Section("EKRANDAKİ İŞARETLER", "", "");
            Note("Nesne üstündeki etiketler: KIRMIZI yanıp sönen = acil (yangın, gaz, patlak boru, yıkık kolon, su baskını). AMBER = önerilen iş " +
                 "(yolu kapatan moloz, güçlendirilecek kolon). GRİ = isteğe bağlı iş (seçili karakterin katında). SOLUK LACİVERT = istasyon (tezgah, sandık, telsiz, çıkış).");
            Note("Bir nesnenin üstüne gelince (ya da yanına gidince) bilgi kartı açılır: ne işe yaradığı, süre (alet etkisiyle), kazancı ve şu an yapılamıyorsa nedeni.");
            Note("Karakterin başındaki etiketler: AÇ, GERGİN, YARALI, PANİK ve yaptığı iş. Binanın solundaki kat adlarının yanında o kattaki tehlike yazar.");
            Note("Sağ alttaki HEDEF kutusu sıradaki adımı ve eksik malzemenin nereden bulunacağını söyler. Üst sağdaki kaynak rozetlerinin, açlık ve stres " +
                 "göstergelerinin ve bütünlük çubuğunun üstüne gelince ayrıntı çıkar.");

            Section("KAYNAKLAR", "", "");
            Note("Kalas & Hurda: moloz kazarak ve mobilya parçalayarak; hemen her tarifin temeli. Metal: radyatör, buzdolabı, kilitli kasa, seferler; yükseltme, silah ve çelik kiriş için. " +
                 "Elektronik: bilgisayar, kitaplık, Atölye ve Klinik; Kademe 3, telsiz ve pompa için. Yaşam Kiti: dolap ara, Klinik ve Market, gece keşfi; her gece kişi başı 1 kit yenir.");

            Section("GÜN VE GECE", "", "");
            Note("Sabah üretim ve onarım, öğle dış sefer, akşam Akşam Planı: her kazazede için sığınakta kal (dinlenir), kapıda nöbet tut (baskına karşı) ya da gece keşfine çık " +
                 "(sabah ganimetle ya da yaralı döner) seç; kimin yiyeceğini de sen belirlersin. Sabah gün kartı geçen günün özetini verir.");

            Section("ALETLER VE SEFER", "", "");
            Note("Tezgahta üretilen aletler herkese yarar: Kürek ve Kazma kazıyı, Levye aramayı ve parçalamayı, Alet Takımı tamiri, Yangın Battaniyesi söndürmeyi hızlandırır; Kafa Lambası karanlık stresini azaltır.");
            Note("Seferde çanta kilo ile ölçülür. Sefer hazırlığında silah, mermi, kit ve alet seçersin; ağır ekipman ganimete yer bırakmaz. Kilitli kasa Levye, gömülü sandık Kazma ister. " +
                 "Sahada I tuşuyla çantayı açıp eşya bırakabilirsin.");

            Section("HEDEF", "", "");
            Note("Tezgahı Kademe 3'e çıkar, telsizi tamir et, çatı molozunu temizle, çatı kolonlarını çelikle güçlendir, " +
                 "şalteri açık tut ve telsiz başında kesintisiz 60 sn sinyal gönder. Bütünlük %0 olursa ya da herkes ölürse kaybedersin.");
        }

        void Section(string title, string second, string third)
        {
            var row = TableRow(34f);
            AddCell(row, title, UiFactory.Accent, 1.3f, FontStyle.Bold);
            AddCell(row, second, UiFactory.Accent, 1.25f, FontStyle.Bold);
            AddCell(row, third, UiFactory.Accent, 1f, FontStyle.Bold);
        }

        void Row(string action, string keyboard, string gamepad)
        {
            var row = TableRow(32f);
            AddCell(row, action, UiFactory.Text, 1.3f, FontStyle.Normal);
            AddCell(row, keyboard, new Color(0.95f, 0.88f, 0.6f), 1.25f, FontStyle.Normal);
            AddCell(row, gamepad, new Color(0.7f, 0.85f, 0.95f), 1f, FontStyle.Normal);
        }

        void Note(string text)
        {
            var label = UiFactory.Label(helpWindow.Content, text, 19, new Color(0.84f, 0.84f, 0.8f, 1f), TextAnchor.UpperLeft, FontStyle.Italic, false);
            label.gameObject.AddComponent<LayoutElement>().minHeight = 30f;
        }

        RectTransform TableRow(float height)
        {
            var row = UiFactory.Rect("Satır", helpWindow.Content);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.padding = new RectOffset(6, 6, 0, 0);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return row;
        }

        static void AddCell(RectTransform row, string text, Color color, float weight, FontStyle style)
        {
            var label = UiFactory.Label(row, text, 20, color, TextAnchor.MiddleLeft, style, false);
            label.gameObject.AddComponent<LayoutElement>().flexibleWidth = weight;
        }
    }
}
