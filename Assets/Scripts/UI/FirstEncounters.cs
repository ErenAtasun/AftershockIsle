using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.CameraControl;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using AftershockIsle.Endgame;
using AftershockIsle.Expedition;
using AftershockIsle.Interaction;
using AftershockIsle.Save;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AftershockIsle.UI
{
    /// <summary>
    /// İlk karşılaşma açıklamaları: oyuncu bir tehlikeyle ya da mekanikle ilk kez karşılaşınca ("Artçı sarsıntı", "Yangın",
    /// "Gaz kaçağı", "Panik", "Hava olayı", "Sefer"...) zaman durur ve kısa bir pencere "bu nedir, ne yapmalısın" diye anlatır.
    /// Her konu bir kez gösterilir (PlayerPrefs); H rehberinde kalıcı özeti bulunur. Pencere, sığınak serbestken ve başka
    /// bir pencere açık değilken gelir.
    /// </summary>
    public class FirstEncounters : MonoBehaviour
    {
        const string SeenPrefix = "Aftershock.Seen.";

        struct Topic
        {
            public string Key;
            public string Title;
            public string Body;
        }

        readonly Queue<Topic> pending = new Queue<Topic>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindFirstObjectByType<BuildingLayout>() == null || FindFirstObjectByType<FirstEncounters>() != null) return;
            new GameObject("[İlk Karşılaşmalar]").AddComponent<FirstEncounters>();
        }

        /// <summary>Test / yeni başlayanlar için: tüm açıklamaları yeniden gösterilebilir yapar.</summary>
        public static void ResetAll()
        {
            foreach (var key in new[] { "quake", "fire", "gas", "pipe", "panic", "weather", "expedition", "radio", "hunger" })
            {
                try { PlayerPrefs.DeleteKey(SeenPrefix + key); }
                catch { }
            }
        }

        void OnEnable()
        {
            EventBus.Subscribe<AftershockWarning>(OnQuake);
            EventBus.Subscribe<FireStarted>(OnFire);
            EventBus.Subscribe<GasLeakStarted>(OnGas);
            EventBus.Subscribe<PipeBurst>(OnPipe);
            EventBus.Subscribe<SurvivorPanicked>(OnPanic);
            EventBus.Subscribe<WeatherChanged>(OnWeather);
            EventBus.Subscribe<ExpeditionStarted>(OnExpedition);
            EventBus.Subscribe<RadioRepaired>(OnRadio);
            EventBus.Subscribe<UpkeepResolved>(OnUpkeep);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<AftershockWarning>(OnQuake);
            EventBus.Unsubscribe<FireStarted>(OnFire);
            EventBus.Unsubscribe<GasLeakStarted>(OnGas);
            EventBus.Unsubscribe<PipeBurst>(OnPipe);
            EventBus.Unsubscribe<SurvivorPanicked>(OnPanic);
            EventBus.Unsubscribe<WeatherChanged>(OnWeather);
            EventBus.Unsubscribe<ExpeditionStarted>(OnExpedition);
            EventBus.Unsubscribe<RadioRepaired>(OnRadio);
            EventBus.Unsubscribe<UpkeepResolved>(OnUpkeep);
        }

        // ---------------------------------------------------------------- Konular

        void OnQuake(AftershockWarning evt) => Queue("quake", "Artçı Sarsıntı",
            "Sarsıntı gelmeden önce geri sayım var. Zaman şimdi durdu: karakterleri <b>yeşil Yaşam Üçgenlerine</b> (sağlam mobilya yanları) gönder; " +
            "açıkta kalan hasar alır, sıkışabilir.\n\nÇatlak kolonlar kırılabilir; kırılan kolon bütünlüğü düşürür ve yolu enkazla kapatır. " +
            "Kalıcı çözüm: kolonları <b>Çelik Kiriş</b> ile güçlendirmek (tezgah, Kademe 3).");

        void OnFire(FireStarted evt) => Queue("fire", "Yangın",
            "Yangın zamanla büyür ve <b>duman</b> katı kapatır; %100'de tavan çöker.\n\n" +
            "Bir <b>su kaynağından kova doldur</b> (bidon ya da bodrum suyu), dolu kovayı yangına götür. Kovalar yangını %20'şer düşürür. " +
            "<b>Yangın Söndürme Tüpü</b> kurarsan kovasız söndürürsün; <b>Gaz Maskesi</b> dumanda çalışmayı sağlar.");

        void OnGas(GasLeakStarted evt) => Queue("gas", "Gaz Kaçağı",
            "Gaz biriktikçe kat girilemez olur ve <b>elektrik kıvılcımıyla patlar</b>.\n\n" +
            "Sırayla: 1) <b>Ana şalteri indir</b>, 2) <b>pencereyi kır</b> (gaz tahliye olur), 3) vanayı kapat.");

        void OnPipe(PipeBurst evt) => Queue("pipe", "Patlak Boru",
            "Bodruma su dolmaya başladı. Su <b>%100</b> olursa temel kayar (yenilgi). Su yüksekken elektrik çarpar: şalteri indir.\n\n" +
            "<b>Tulumbayla</b> ya da elektrikli pompayla boşalt, boruyu <b>Metal</b> ile kaynakla. Kalıcı çözüm: Bypass Vanası.");

        void OnPanic(SurvivorPanicked evt) => Queue("panic", "Panik",
            $"{evt.Survivor.DisplayName} stresi %100'e ulaştı ve 15 sn emir dinlemeyecek.\n\n" +
            "Stres karanlıkta çalışmakla, felaketlerle ve açlıkla artar. Azaltmak için: ışıkta boş bekle, <b>sandalyede otur</b>, yemek ye, Dinlenme Köşesi kur.");

        void OnWeather(WeatherChanged evt)
        {
            if (evt.Weather == WeatherType.Clear || SaveManager.IsApplying) return;
            Queue("weather", $"Hava Olayı: {WeatherSystem.ToDisplayName(evt.Weather)}",
                $"{WeatherSystem.ToEffectSummary(evt.Weather)}.\n\nHava her sabah değişir; bugünün havası gün başı kartında ve üst panelde yazar.");
        }

        void OnExpedition(ExpeditionStarted evt) => Queue("expedition", "Dış Sefer",
            "Kâşif şimdi dış sahada. <b>WASD</b> ile yürü, <b>F</b> ile ara. Düşmanlar seni görünce kovalar, sesi duyunca bakmaya gelir.\n\n" +
            $"Çantanın kapasitesi kilo ile ölçülür; <b>{GameInput.Prompt(InputPrompt.Inventory)}</b> ile çantayı aç, eşya bırak. " +
            "Süre dolarsa karanlık basar ve çantanın yarısı düşer: soldaki <b>yeşil çıkışa</b> zamanında dön.");

        void OnRadio(RadioRepaired evt) => Queue("radio", "Telsiz Tamir Edildi",
            "Telsiz çalışıyor. Tahliye için: Tezgah Kademe 3, çatı molozu temiz, çatı kolonları çelikle güçlü, şalter açık olmalı. " +
            "Sonra bir kazazedeyi telsiz başına gönder: <b>60 sn kesintisiz</b> sinyal gerekir; sarsıntı sinyali keser.");

        void OnUpkeep(UpkeepResolved evt)
        {
            if (evt.Hungry <= 0) return;
            Queue("hunger", "Açlık",
                $"{evt.Hungry} kişi aç kaldı. Aç karakter <b>yarı hızda</b> yürür ve çalışır; son kademede bir gece daha aç kalan can kaybeder.\n\n" +
                "Yaşam Kiti bulmak için dolap ara, enkaz kaz, dış sefere ya da gece keşfine çık. Akşam Planı'nda kit yetmezse kimin yiyeceğini sen seçersin.");
        }

        // ---------------------------------------------------------------- Kuyruk

        void Queue(string key, string title, string body)
        {
            if (SaveManager.IsApplying || SaveManager.PendingLoad || Seen(key)) return;
            foreach (var topic in pending)
                if (topic.Key == key) return;
            pending.Enqueue(new Topic { Key = key, Title = title, Body = body });
        }

        static bool Seen(string key)
        {
            try { return PlayerPrefs.GetInt(SeenPrefix + key, 0) == 1; }
            catch { return false; }
        }

        static void MarkSeen(string key)
        {
            try
            {
                PlayerPrefs.SetInt(SeenPrefix + key, 1);
                PlayerPrefs.Save();
            }
            catch { }
        }

        void Update()
        {
            if (pending.Count == 0) return;
            if (TimeManager.IsHeld || NarrativeEventSystem.IsModalOpen || SideViewCamera.ViewingField || PlacementController.IsPlacementActive) return;

            var narrative = NarrativeEventSystem.Instance;
            if (narrative == null) return;

            var topic = pending.Dequeue();
            if (Seen(topic.Key)) return;
            MarkSeen(topic.Key);
            narrative.ShowExternal(topic.Title, topic.Body + "\n\n<size=14><color=#a8a8a0>Bu açıklama bir kez gösterilir; H rehberinde özeti var.</color></size>",
                new[] { new NarrativeEventSystem.ExternalChoice("Anladım", null) });
        }
    }
}
