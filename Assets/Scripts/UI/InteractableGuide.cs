using AftershockIsle.Building;
using AftershockIsle.Crafting;
using AftershockIsle.Crisis;
using AftershockIsle.Endgame;
using AftershockIsle.Interaction;
using UnityEngine;

namespace AftershockIsle.UI
{
    public enum MarkerKind
    {
        /// <summary>İşaret yok.</summary>
        None,
        /// <summary>İstasyon (tezgah, sandık, telsiz, çıkış): her zaman soluk etiket.</summary>
        Station,
        /// <summary>İsteğe bağlı iş (dolap arama, moloz): yalnızca seçili kazazedenin katında.</summary>
        Optional,
        /// <summary>Yapılması iyi olan iş (yolu kapatan moloz, kırık kolon): amber.</summary>
        Important,
        /// <summary>Acil (yangın, gaz, boru, su baskını): kırmızı, yanıp söner.</summary>
        Urgent,
    }

    public struct GuideInfo
    {
        public MarkerKind Kind;
        public string Marker;
        /// <summary>Bu nesne ne, neden önemli.</summary>
        public string Why;
        /// <summary>Yapılırsa ne kazanılır.</summary>
        public string Reward;
        /// <summary>Süre satırı gösterilsin mi (menü açan istasyonlarda anlamsız).</summary>
        public bool ShowDuration;
    }

    /// <summary>
    /// Etkileşilebilir nesneler için okunur açıklamalar: dünya işaretleri (WorldMarkers) ve bilgi kartı (InfoCard) buradan beslenir.
    /// Durum okuyan her şey genel API'lerden gelir; nesne sınıfları değişmez.
    /// </summary>
    public static class InteractableGuide
    {
        public static GuideInfo Of(Interactable thing)
        {
            var info = new GuideInfo { Kind = MarkerKind.None, ShowDuration = true };
            if (thing == null) return info;

            switch (thing)
            {
                case FireSpot fire:
                    info.Why = "Yangın büyür, duman katı kapatır; %100'de tavan çöker. Kova suyu ya da Yangın Söndürme Tüpü gerekir.";
                    info.Reward = "Yangın ve duman azalır, bina bütünlüğü korunur.";
                    if (fire.IsBurning) Mark(ref info, MarkerKind.Urgent, "SÖNDÜR");
                    break;

                case GasLeak gas:
                    info.Why = "Gaz birikir; yoğun gaz katı kapatır ve kıvılcımda patlar. Önce şalteri indir, pencereyi kır, sonra vanayı kapat.";
                    info.Reward = "Sızıntı durur, patlama riski biter.";
                    if (gas.IsLeaking) Mark(ref info, MarkerKind.Urgent, "VANAYI KAPAT");
                    break;

                case BurstPipe pipe:
                    info.Why = "Patlak boru bodruma su doldurur; su %100 olursa temel kayar (yenilgi).";
                    info.Reward = "Su girişi durur.";
                    if (pipe.IsBurst) Mark(ref info, MarkerKind.Urgent, "BORUYU KAYNAKLA");
                    break;

                case SupportColumn column:
                    info.Why = "Taşıyıcı kolonlar sarsıntıda kırılabilir: kırılan kolon bütünlüğü düşürür ve yolu enkazla kapatır. Çelikle güçlendirilen kolon sarsıntıdan etkilenmez.";
                    if (column.State == ColumnState.Broken)
                    {
                        info.Reward = "Kolon yeniden dikilir.";
                        Mark(ref info, MarkerKind.Urgent, "KOLONU DİK");
                    }
                    else if (column.State == ColumnState.Cracked)
                    {
                        info.Reward = "Kolon kalıcı olarak sarsıntıya dayanıklı olur (Çelik Kiriş harcanır).";
                        if (Workbench.Instance != null && Workbench.Instance.SteelBeams > 0) Mark(ref info, MarkerKind.Important, "GÜÇLENDİR");
                    }
                    else info.Reward = "Zaten çelikle güçlendirilmiş.";
                    break;

                case RubblePile rubble:
                    info.Why = rubble.BlocksWholeFloor
                        ? "Moloz yolu kapatıyor; kazılana kadar geçilemez."
                        : "Kazdıkça Hurda (bazen Metal) verir. Hurda ve Metal tezgah işleri için gerekli.";
                    info.Reward = "Her kazıda depoya malzeme girer.";
                    Mark(ref info, rubble.BlocksWholeFloor ? MarkerKind.Important : MarkerKind.Optional, "KAZ");
                    break;

                case Furniture furniture:
                    if (furniture.ActionLabel == "Ara")
                    {
                        info.Why = "Dolap ve kutuların içinde Yaşam Kiti, Elektronik gibi malzeme olabilir.";
                        info.Reward = "İçindeki eşya depoya girer.";
                        Mark(ref info, MarkerKind.Optional, "ARA");
                    }
                    else
                    {
                        info.Why = furniture.ProvidesShelter
                            ? "Sağlam mobilya sarsıntıda saklanma noktasıdır. Parçalarsan malzeme alırsın ama bu sığınak kaybolur."
                            : "Parçalarsan malzeme verir.";
                        info.Reward = "Parçalayınca malzeme verir.";
                    }
                    break;

                case ManualPump pump:
                    info.Why = "Bodrum suyunu elle boşaltır; Q / E (LT / RT) tuşlarına dönüşümlü basarsan hızlanır.";
                    info.Reward = "Su seviyesi düşer.";
                    var flood = FloodSystem.Instance;
                    if (flood != null && flood.Level > 1f) Mark(ref info, flood.Level >= 60f ? MarkerKind.Urgent : MarkerKind.Important, "POMPALA");
                    break;

                case AftershockIsle.Crafting.ElectricPump electric:
                    info.Why = "Elektrik açıkken bodrumu kendiliğinden boşaltır; çalıştıkça yıpranır.";
                    info.Reward = "Pompa yeniden çalışır.";
                    if (electric.IsBroken) Mark(ref info, MarkerKind.Important, "POMPAYI ONAR");
                    break;

                case BreakerPanel _:
                    info.Why = "Ana şalter: gaz kaçağında patlamayı önlemek için indir. İnikken ışıklar ve cihazlar çalışmaz (tahliye için açık olmalı).";
                    info.Reward = PowerGrid.IsOn ? "Elektrik kesilir (kıvılcım riski biter)." : "Elektrik gelir.";
                    bool gasDanger = AnyGasDanger();
                    if (PowerGrid.IsOn && gasDanger) Mark(ref info, MarkerKind.Urgent, "ŞALTERİ İNDİR");
                    else if (!PowerGrid.IsOn && !gasDanger) Mark(ref info, MarkerKind.Important, "ŞALTERİ AÇ");
                    break;

                case SmokeWindow window:
                    info.Why = "Pencere kırılırsa duman ve gaz dışarı tahliye olur.";
                    info.Reward = "Kat daha çabuk havalanır.";
                    if (!window.IsBroken && FireSpot.IsSmokeBlocked(window.Floor)) Mark(ref info, MarkerKind.Important, "PENCEREYİ KIR");
                    break;

                case WaterSource source:
                    info.Why = "Kova doldurur. Dolu kovayı yangına, tulumbaya ya da depoya taşırsın (elde su taşırken başka iş yapılamaz).";
                    info.Reward = "Dolu kova (yangın söndürme ve su).";
                    if (source.HasWater && AnyBurning()) Mark(ref info, MarkerKind.Important, "KOVA DOLDUR");
                    break;

                case Workbench _:
                    info.ShowDuration = false;
                    info.Why = "Alet, silah, mermi, ekipman ve yükseltmeler burada üretilir. Kademe yükseldikçe yeni tarifler açılır.";
                    Mark(ref info, MarkerKind.Station, "TEZGAH");
                    break;

                case BuildTable _:
                    info.ShowDuration = false;
                    info.Why = "Soba, pompa, revir gibi eve kurulan yapılar burada hazırlanır; sonra yerleşim modunda (B) eve kurulur.";
                    Mark(ref info, MarkerKind.Station, "İNŞAAT");
                    break;

                case StorageChest _:
                    info.ShowDuration = false;
                    info.Why = "Ortak depo. Malzemeler kendiliğinden depoya girer; elde taşıdığın eşyayı ya da sefer çantasını buraya bırakırsın.";
                    Mark(ref info, MarkerKind.Station, "SANDIK");
                    break;

                case RadioStation radio:
                    info.Why = "Tahliye için çatıdaki telsizi tamir et, sonra operatör telsiz başında kalarak 60 sn kesintisiz sinyal gönder.";
                    bool ready = radio.IsRepaired && !radio.SignalComplete && EvacuationConditions.Evaluate(Layout(), radio).ReadyToSignal;
                    Mark(ref info, ready ? MarkerKind.Important : MarkerKind.Station, ready ? "SİNYAL GÖNDER" : "TELSİZ");
                    break;

                case ExitDoor _:
                    info.ShowDuration = false;
                    info.Why = "Öğle vakti dış sefere çıkış kapısı: malzeme ve kazazede aramak için dışarı çıkarsın.";
                    Mark(ref info, MarkerKind.Station, "SEFER");
                    break;

                case Seat _:
                    info.Why = "Oturup dinlen: stres azalır, can dolar.";
                    info.Reward = "-18 stres, +5 can.";
                    break;
            }

            return info;
        }

        static BuildingLayout layout;

        static BuildingLayout Layout()
        {
            if (layout == null) layout = Object.FindFirstObjectByType<BuildingLayout>();
            return layout;
        }

        static void Mark(ref GuideInfo info, MarkerKind kind, string label)
        {
            info.Kind = kind;
            info.Marker = label;
        }

        static bool AnyGasDanger()
        {
            foreach (var leak in GasLeak.All)
                if (leak != null && (leak.IsLeaking || leak.IsExplosive)) return true;
            return false;
        }

        static bool AnyBurning()
        {
            foreach (var fire in FireSpot.All)
                if (fire != null && fire.IsBurning) return true;
            return false;
        }
    }
}
