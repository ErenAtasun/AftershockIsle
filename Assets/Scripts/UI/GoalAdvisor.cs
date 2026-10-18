using System.Text;
using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Crisis;
using AftershockIsle.Economy;
using AftershockIsle.Endgame;
using AftershockIsle.Expedition;
using AftershockIsle.Interaction;
using UnityEngine;

namespace AftershockIsle.UI
{
    /// <summary>
    /// "Sıradaki hedef": oyunun o anki durumuna bakıp oyuncuya ne yapması gerektiğini ve eksik malzemenin nereden bulunacağını söyler.
    /// Önce acil durumlar (yangın, gaz, su baskını, sarsıntı, yıkık kolon), sonra akşam yemeği, sonra tahliye / hayatta kalma adımları.
    /// </summary>
    public static class GoalAdvisor
    {
        public struct Goal
        {
            public string Title;
            public string Detail;
            public bool Urgent;
        }

        const float RefreshSeconds = 0.5f;

        static float refreshAt;
        static Goal cached;
        static readonly StringBuilder text = new StringBuilder();

        public static Goal Current(BuildingLayout building)
        {
            if (Time.unscaledTime < refreshAt) return cached;
            refreshAt = Time.unscaledTime + RefreshSeconds;
            cached = Compute(building);
            return cached;
        }

        static Goal Compute(BuildingLayout building)
        {
            // ---- Acil durumlar
            var quake = AftershockSystem.Instance;
            if (quake != null && quake.Phase == QuakePhase.Warning)
                return Urgent("SARSINTI GELİYOR", "Karakterleri yeşil Yaşam Üçgenlerine (sağlam mobilya yanları) gönder.");

            foreach (var leak in GasLeak.All)
                if (leak != null && leak.IsLeaking)
                    return Urgent($"GAZ KAÇAĞI ({building.GetFloorName(leak.Floor)})", "Şalteri indir (patlamasın), pencereyi kır, sonra vanayı kapat.");

            foreach (var fire in FireSpot.All)
                if (fire != null && fire.IsBurning)
                    return Urgent($"YANGIN ({building.GetFloorName(fire.Floor)}) %{fire.Intensity:0}", "Su kaynağından kova doldur ve yangına götür; duman girişi kapatır.");

            var flood = FloodSystem.Instance;
            if (flood != null)
            {
                if (flood.IsFull) return Urgent("TEMEL KAYIYOR", "Bodrum suyunu hemen pompala!");
                if (flood.IsPipeBurst) return Urgent("BORU PATLAK", "Boruyu Metal ile kaynakla; kalıcı çözüm için Bypass Vanası üret.");
                if (flood.Level >= 40f) return Urgent($"BODRUM SUYU %{flood.Level:0}", "Tulumbayla ya da elektrikli pompayla suyu boşalt.");
            }

            foreach (var column in SupportColumn.All)
                if (column != null && column.State == ColumnState.Broken)
                    return Urgent($"KIRIK KOLON ({building.GetFloorName(column.Floor)})", $"Önce enkazı kaz, sonra yeniden dik ({AmountText(ResourceType.Scrap, 4)}).");

            // ---- Akşam yemeği
            var cycle = Object.FindFirstObjectByType<DayCycle>();
            var storage = ResourceStorage.Instance;
            if (storage != null && cycle != null && cycle.Phase != DayPhase.Night)
            {
                int need = Survivor.AliveCount + ExpeditionSystem.AwayCount;
                int kits = storage.Get(ResourceType.Supplies);
                if (kits < need)
                    return Normal($"Akşam için Yaşam Kiti: {kits}/{need}",
                        $"{need - kits} kişi aç kalacak. Kit: {SourceHint(ResourceType.Supplies)}.");
            }

            // ---- Hedef adımları
            var radio = RadioStation.Instance;
            var conditions = radio != null ? EvacuationConditions.Evaluate(building, radio) : default;
            bool endless = GameMode.IsEndless;
            string prefix = endless ? "Yardım" : "Tahliye";

            var bench = Workbench.Instance;
            int tier = bench != null ? bench.Tier : 1;
            if (bench != null && tier < 3)
            {
                var recipe = RecipeCatalog.Get(tier == 1 ? RecipeId.UpgradeTier2 : RecipeId.UpgradeTier3);
                return Normal($"{prefix}: Tezgahı Kademe {tier + 1}'e yükselt", MissingText(recipe, "Tezgahı aç, yükseltmeyi seç."));
            }

            if (radio != null && !radio.IsRepaired)
            {
                var module = RecipeCatalog.Get(RecipeId.RadioModule);
                if (!Workbench.IsCrafted(RecipeId.RadioModule))
                    return Normal($"{prefix}: Telsiz Modülü üret", MissingText(module, "Tezgahta üret, sonra çatıdaki telsizi tamir et."));
                return Normal($"{prefix}: Çatıdaki telsizi tamir et", "Telsiz Modülü hazır; bir kazazedeyi çatıya, telsize gönder.");
            }

            if (radio != null && conditions.RoofRubbleLeft > 0)
                return Normal($"{prefix}: Çatı molozunu temizle", $"{conditions.RoofRubbleLeft} moloz yığını kaldı; helikopter inecek yer açılmalı.");

            if (radio != null && conditions.RoofColumnsReinforced < conditions.RoofColumnsTotal)
            {
                int missing = conditions.RoofColumnsTotal - conditions.RoofColumnsReinforced;
                int beams = bench != null ? bench.SteelBeams : 0;
                var beam = RecipeCatalog.Get(RecipeId.SteelBeam);
                string detail = beams >= missing
                    ? $"Elinde {beams} Çelik Kiriş var; çatıyı taşıyan kolonlara uygula."
                    : MissingText(beam, $"Her kolon için 1 Çelik Kiriş gerekir (stok {beams}, eksik {missing - beams}).");
                return Normal($"{prefix}: Çatı kolonlarını çelikle güçlendir ({conditions.RoofColumnsReinforced}/{conditions.RoofColumnsTotal})", detail);
            }

            if (radio != null && !conditions.PowerOn)
                return Normal($"{prefix}: Ana şalteri aç", "Telsiz için elektrik gerekir.");

            if (radio != null && !radio.SignalComplete)
                return Normal(endless ? "Telsizden yardım çağır" : "Telsizden tahliye sinyali gönder",
                    $"Operatör telsiz başında kalmalı: kesintisiz 60 sn (şu an %{radio.Signal01 * 100f:0}). Sarsıntı sinyali keser.");

            if (endless)
            {
                int day = cycle != null ? cycle.Day : 1;
                string best = GameMode.EndlessBest > 0 ? $" Rekorun: {GameMode.EndlessBest}. gün." : "";
                return Normal($"Hayatta kal: Gün {day}", $"Telsiz Gün {EvacuationSystem.NextDropDay}'de yeniden yardım çağırabilir.{best}");
            }

            return Normal("Helikopter yolda", "Çatıya çıkıp tahliyeyi bekle; kalan kazazedeleri sağ tut.");
        }

        // ---------------------------------------------------------------- Metin yardımcıları

        static Goal Urgent(string title, string detail) => new Goal { Title = title, Detail = detail, Urgent = true };
        static Goal Normal(string title, string detail) => new Goal { Title = title, Detail = detail, Urgent = false };

        static string AmountText(ResourceType type, int amount)
        {
            var storage = ResourceStorage.Instance;
            int have = storage != null ? storage.Get(type) : 0;
            string color = have >= amount ? "#9be37a" : "#ff8f7a";
            return $"<color={color}>{type.ToDisplayName()} {have}/{amount}</color>";
        }

        /// <summary>Tarifin maliyeti (yeşil: yeter, kırmızı: eksik) ve eksik malzemenin nereden bulunacağı.</summary>
        public static string MissingText(RecipeInfo recipe, string fallback)
        {
            var storage = ResourceStorage.Instance;
            if (storage == null) return fallback;

            text.Clear();
            string sources = null;
            foreach (var cost in recipe.Costs)
            {
                if (text.Length > 0) text.Append(", ");
                text.Append(AmountText(cost.type, cost.amount));
                if (storage.Get(cost.type) < cost.amount && sources == null) sources = $"{cost.type.ToDisplayName()}: {SourceHint(cost.type)}";
            }
            return sources != null ? $"{text}. {sources}." : $"{text}. {fallback}";
        }

        /// <summary>Malzemenin nereden bulunacağı (kısa).</summary>
        public static string SourceHint(ResourceType type) => type switch
        {
            ResourceType.Scrap => "moloz kaz, mobilya parçala",
            ResourceType.Metal => "radyatör ve buzdolabı parçala, kilitli kasa, dış sefer",
            ResourceType.Electronics => "bilgisayar ve kitaplıkta ara, Atölye / Klinik seferi",
            ResourceType.Supplies => "dolap ara, Klinik / Market seferi, gece keşfi",
            ResourceType.Water => "su bidonu ve kova",
            _ => "",
        };

        /// <summary>Kaynağın ne işe yaradığı (kaynak ipucu kartı için).</summary>
        public static string UseHint(ResourceType type) => type switch
        {
            ResourceType.Scrap => "Hemen her tarifin temel malzemesi; kolon dikmek, kürek, soba, raf.",
            ResourceType.Metal => "Tezgah yükseltmeleri, silahlar, çelik kiriş, boru tamiri.",
            ResourceType.Electronics => "Kademe 3, telsiz modülü, pompa, jeneratör, gelişmiş silahlar.",
            ResourceType.Supplies => "Gece kişi başı 1 kit (yemek). Ayrıca sahada ilk yardım ve revir masası.",
            _ => "",
        };
    }
}
