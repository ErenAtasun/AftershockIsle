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

namespace AftershockIsle.UI
{
    /// <summary>
    /// Olay tabanlı öğretici: bir durum ilk kez yaşandığında ilgili ipucu bir kez gösterilir.
    /// İpuçları sıraya girer, kendiliğinden kapanır ya da "Tamam" ile geçilir. H kontrol rehberini açar.
    /// </summary>
    public class TutorialHints : MonoBehaviour
    {
        const int HelpWindowId = 4501;
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
        Rect helpRect = new Rect(0, 0, 700, 560);
        GUIStyle wrapStyle;
        GUIStyle titleStyle;
        float startDelay = 1.5f;
        bool welcomed;

        // Hoş geldin ipucu ilk sırada olsun: oyun başında otomatik seçim de bir ipucu tetikler.
        void Awake()
        {
            if (GetComponent<FirstDayGuide>() == null) gameObject.AddComponent<FirstDayGuide>();
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
        }

        // ---------------------------------------------------------------- İpucu tetikleyicileri

        void OnSelected(SurvivorSelected evt)
        {
            if (evt.Survivor == null) return;
            Show("select", "Çatlak kolonlar",
                "Turuncu (çatlak) kolonlar sarsıntıda kırılır. Kolonla etkileş (F / A ya da sağ tık): 2 Kalas ile ahşap destek dikilir. " +
                "Kalası molozlardan kazarak bulursun; karakter malzemeyi kendisi sandığa taşır.");
        }

        void OnRubbleCleared(RubbleCleared evt) => Show("rubble", "Depo",
            "Taşınan malzeme Ortak Sandık'a bırakılınca üst taraftaki sayaca eklenir. Tezgah, tamir ve kolonlar buradan harcar. " +
            "Farede Shift + sağ tık ile birden fazla işi sıraya ekleyebilirsin.");

        void OnColumnChanged(ColumnStateChanged evt)
        {
            if (evt.Column.State == ColumnState.Supported)
                Show("support", "Destek dikildi", "Ahşap destek bir sarsıntıyı karşılar, sonra tekrar çatlar. Kalıcı çözüm için Kademe 3'te Çelik Kiriş üret.");
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
            if (GameInput.HelpTogglePressed) helpOpen = !helpOpen;
            if (hasCurrent && GameInput.DismissHintPressed) hasCurrent = false;

            if (!welcomed)
            {
                startDelay -= Time.unscaledDeltaTime;
                if (startDelay > 0f) return;

                welcomed = true;
            }

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
        }

        void OnGUI()
        {
            UiTheme.Apply();
            wrapStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 14 };
            titleStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14 };

            if (hasCurrent && tutorialEnabled) DrawHint();
            if (helpOpen) DrawHelp();
        }

        void DrawHint()
        {
            const float width = 520f;
            var rect = new Rect((UiTheme.Width - width) * 0.5f, UiTheme.Height - 190f, width, 112f);
            UIBlocker.Add(rect);

            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label($"İPUCU: {current.Title}", titleStyle);
            GUILayout.Label(current.Text, wrapStyle);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(GameInput.UsingGamepad ? "Tamam (D-pad ↓)" : "Tamam", GUILayout.Width(120)) && Event.current.button == 0) hasCurrent = false;
            if (GUILayout.Button("İpuçlarını kapat", GUILayout.Width(130)) && Event.current.button == 0)
            {
                tutorialEnabled = false;
                hasCurrent = false;
                queue.Clear();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawHelp()
        {
            helpRect.x = (UiTheme.Width - helpRect.width) * 0.5f;
            helpRect.y = (UiTheme.Height - helpRect.height) * 0.5f;
            UIBlocker.Add(helpRect);
            GUI.Window(HelpWindowId, helpRect, DrawHelpContents, $"Kontrol Rehberi   ({GameInput.Prompt(InputPrompt.Help)}: kapat)");
        }

        void DrawHelpContents(int id)
        {
            string[] lines =
            {
                "SIĞINAK                    klavye                       gamepad",
                "  Karakter yürüt           WASD / oklar                 sol çubuk",
                "  Merdiven çık / in        W / S (en yakın merdiven)    çubuk yukarı / aşağı",
                "  En yakın işi yap         F                            A",
                "  İşi bırak, dur           X                            B",
                "  Karakter değiştir        Tab / Shift+Tab              RB / LB",
                "  Duraklat / hız           Space, 1 / 2                 Y, X",
                "  Kamera kaydır / yakınlaş  (seçim yokken WASD), tekerlek  sağ çubuk",
                "  Yetenekler / rehber      K / H                        D-pad sağ / sol",
                "  Tulumba ritmi            Q / E                        LT / RT",
                "  Fare: sol tık seç, sağ tık iş ver ya da yürü, Shift + sağ tık sıraya ekle.",
                "DIŞ SAHA",
                "  Yürü / nişan / ateş      WASD / fare / sol tık        sol çubuk / sağ çubuk / RT",
                "  Silah değiştir           Q                            X",
                "  Çömel (siper)            Shift / C (basılı)           LT (basılı)",
                "  Alçak siperin arkasında çömelen görünmez; ateş edince bir an doğrulur. Duvar/araba kenarında nişanı kenarın ötesine çevir: köşeden eğilirsin.",
                "  Kaçış / ara / ilk yardım  Space / F / R                B / A / Y",
                "  Düşmanlar görünce (!) kovalar, sesi duyunca (?) bakmaya gelir. Tabanca gürültülü, sapan sessiz.",
                "  Sığınağa dön             Tab                          RB / LB ya da Select",
                "MENÜLER",
                "  Gez / seç / kapat        oklar / Enter / Backspace    D-pad / A / B     Duraklatma menüsü: Esc / Start",
                "HEDEF",
                "  Tezgahı Kademe 3'e çıkar, telsizi tamir et, çatı molozunu temizle, çatı kolonlarını çelikle güçlendir,",
                "  şalteri açık tut ve telsiz başında kesintisiz 60 sn sinyal gönder. Bütünlük %0 olursa ya da herkes ölürse kaybedersin.",
            };

            foreach (string line in lines)
                GUILayout.Label(line, line.StartsWith(" ") ? wrapStyle : titleStyle);
        }
    }
}
