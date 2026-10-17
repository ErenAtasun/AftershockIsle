using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Crisis;
using AftershockIsle.Endgame;
using AftershockIsle.Expedition;
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
        Rect helpRect = new Rect(0, 0, 520, 420);
        GUIStyle wrapStyle;
        GUIStyle titleStyle;
        float startDelay = 1.5f;
        bool welcomed;

        void OnEnable()
        {
            EventBus.Subscribe<SurvivorSelected>(OnSelected);
            EventBus.Subscribe<RubbleCleared>(OnRubbleCleared);
            EventBus.Subscribe<ColumnStateChanged>(OnColumnChanged);
            EventBus.Subscribe<DayPhaseChanged>(OnPhaseChanged);
            EventBus.Subscribe<AftershockWarning>(OnQuakeWarning);
            EventBus.Subscribe<PipeBurst>(OnPipeBurst);
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
                "Turuncu (çatlak) kolonlar sarsıntıda kırılır. Kolona sağ tıkla: 2 Kalas ile ahşap destek dikilir. " +
                "Kalası molozlardan kazarak bulursun; karakter malzemeyi kendisi sandığa taşır.");
        }

        void OnRubbleCleared(RubbleCleared evt) => Show("rubble", "Depo",
            "Taşınan malzeme Ortak Sandık'a bırakılınca üst taraftaki sayaca eklenir. Tezgah, tamir ve kolonlar buradan harcar. " +
            "Shift + sağ tık ile birden fazla işi sıraya ekleyebilirsin.");

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
            "Space ile zamanı durdur ve karakterleri yeşil parlayan Yaşam Üçgeni alanlarına gönder (sağlam kolon dibi, mobilya önü). " +
            "Açıkta kalan hasar alır ve enkaz altında kalır. Mobilyayı parçalarsan sığınak kaybolur.");

        void OnPipeBurst(PipeBurst evt) => Show("pipe", "Su baskını",
            "Doğru sıra: 1) Giriş katından şalteri indir (su elektriklenmesin), 2) Bodrumda tulumbayı pompala (Q/E'ye dönüşümlü bas), " +
            "3) Boruyu kaynakla, 4) Şalteri kaldır. Su %100'de 45 sn kalırsa bina yıkılır.");

        void OnFireStarted(FireStarted evt) => Show("fire", "Yangın!",
            "Yangına sağ tıkla: karakter kova doldurup atar (bodrum suyu ya da Su Bidonu). Duman %50'yi geçince kata girilemez; " +
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

            if (!welcomed)
            {
                startDelay -= Time.unscaledDeltaTime;
                if (startDelay > 0f) return;

                welcomed = true;
                Show("welcome", "Aftershock: Isle",
                    "Binayı ayakta tut, kazazedeleri hayatta tut, çatıdan helikopterle tahliye ol. Önce bir karakteri sol tıkla seç, sonra sağ tıkla: " +
                    "boşluğa yürür, nesneye (moloz, kolon) iş verir. Space zamanı durdurur. H: kontrol rehberi.");
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
            wrapStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 12 };
            titleStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 13 };

            if (hasCurrent && tutorialEnabled) DrawHint();
            if (helpOpen) DrawHelp();
        }

        void DrawHint()
        {
            const float width = 520f;
            var rect = new Rect((Screen.width - width) * 0.5f, Screen.height - 190f, width, 112f);
            UIBlocker.Add(rect);

            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label($"İPUCU: {current.Title}", titleStyle);
            GUILayout.Label(current.Text, wrapStyle);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Tamam", GUILayout.Width(90)) && Event.current.button == 0) hasCurrent = false;
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
            helpRect.x = (Screen.width - helpRect.width) * 0.5f;
            helpRect.y = (Screen.height - helpRect.height) * 0.5f;
            UIBlocker.Add(helpRect);
            GUI.Window(HelpWindowId, helpRect, DrawHelpContents, "Kontrol Rehberi   (H: kapat)");
        }

        void DrawHelpContents(int id)
        {
            string[] lines =
            {
                "FARE",
                "  Sol tık: karakter seç (boşa tıklamak seçimi kaldırır)",
                "  Sağ tık: boşluğa yürü / nesneye iş ver    Shift + sağ tık: işi sıraya ekle",
                "ZAMAN",
                "  Space: duraklat (duraklatmada da emir verilir)    1: normal hız    2: hızlı",
                "KAMERA",
                "  WASD / oklar: kaydır    F: seçili karaktere odaklan    Tekerlek: yakınlaş",
                "PENCERELER",
                "  K: yetenek ağacı    H: bu rehber    Tezgaha sağ tık: üretim menüsü",
                "MİNİ OYUN",
                "  Tulumbada çalışırken Q ve E'ye dönüşümlü bas: daha hızlı pompalar",
                "HEDEF",
                "  Tezgahı Kademe 3'e çıkar, telsizi tamir et, çatı molozunu temizle, çatı kolonlarını çelikle güçlendir,",
                "  şalteri açık tut ve telsiz başında kesintisiz 60 sn sinyal gönder. Bütünlük %0 olursa ya da herkes ölürse kaybedersin.",
            };

            foreach (string line in lines)
                GUILayout.Label(line, line.StartsWith(" ") ? wrapStyle : titleStyle);
        }
    }
}
