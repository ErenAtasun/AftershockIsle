using System.Collections;
using System.Collections.Generic;
using System.Text;
using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Crisis;
using AftershockIsle.Economy;
using AftershockIsle.Interaction;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AftershockIsle.Expedition
{
    public enum NightSite { Ruins, Market, Hospital }

    /// <summary>Gece keşfine çıkan biri sabah döndü (gün kartı özeti için).</summary>
    public readonly struct NightScavengeReturned
    {
        public readonly Survivor Scout;
        public readonly string Report;

        public NightScavengeReturned(Survivor scout, string report)
        {
            Scout = scout;
            Report = report;
        }
    }

    /// <summary>
    /// Gece keşfi (This War of Mine tarzı): akşam planında seçilen kazazedeler kapıdan çıkar, gece boyunca sahnede
    /// görünmez, sabah ganimetle (ya da yaralı, eli boş) döner. Oynanabilir saha seferinden farklı olarak sonucu
    /// zarla belirlenir: risk; yer, gün, hava, özellikler, can/stres, silah ve aynı yere giden arkadaşa göre değişir.
    /// Keşifçi uyumadığı için sabah yorgun (stresli) döner.
    /// </summary>
    public class NightScavenge : MonoBehaviour
    {
        const int DoorFloor = 1;
        const float WalkSpeed = 2.5f;
        const float OutsideDistance = 3.5f;
        const float LeaveTimeout = 25f;
        public const float MinHealth = 25f;

        class Scout
        {
            public Survivor Survivor;
            public NightSite Site;
            public bool Gone;
            public bool Leaving;
            public float SentAt;
            public float NextWalk;
        }

        readonly List<Scout> scouts = new List<Scout>();
        BuildingLayout building;

        public static NightScavenge Instance { get; private set; }

        public static readonly NightSite[] Sites = { NightSite.Ruins, NightSite.Market, NightSite.Hospital };

        /// <summary>Şu an binanın dışında olan (sahnede pasif) gece keşifçisi sayısı.</summary>
        public static int AwayCount
        {
            get
            {
                if (Instance == null) return 0;
                int count = 0;
                foreach (var scout in Instance.scouts)
                    if (scout.Gone && scout.Survivor != null && !scout.Survivor.IsDead) count++;
                return count;
            }
        }

        /// <summary>Binanın dışındaki keşifçiler (Survivor.All'da görünmezler; kayıt için).</summary>
        public static List<Survivor> AwaySurvivors()
        {
            var list = new List<Survivor>();
            if (Instance == null) return list;
            foreach (var scout in Instance.scouts)
                if (scout.Gone && scout.Survivor != null) list.Add(scout.Survivor);
            return list;
        }

        /// <summary>Bu gece keşfe çıkan (yolda ya da dışarıda) biri mi.</summary>
        public static bool IsScouting(Survivor survivor)
        {
            if (Instance == null || survivor == null) return false;
            foreach (var scout in Instance.scouts)
                if (scout.Survivor == survivor) return true;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindFirstObjectByType<BuildingLayout>() == null || FindFirstObjectByType<NightScavenge>() != null) return;
            new GameObject("[Gece Keşfi]").AddComponent<NightScavenge>();
        }

        void Awake()
        {
            Instance = this;
            building = FindFirstObjectByType<BuildingLayout>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable() => EventBus.Subscribe<DayStarted>(OnDayStarted);
        void OnDisable() => EventBus.Unsubscribe<DayStarted>(OnDayStarted);

        // ---------------------------------------------------------------- Bilgi

        public static string SiteName(NightSite site) => site switch
        {
            NightSite.Market => "Market Harabesi",
            NightSite.Hospital => "Hastane",
            _ => "Yakın Yıkıntılar",
        };

        public static string SiteLoot(NightSite site) => site switch
        {
            NightSite.Market => "Yaşam Kiti, biraz hurda",
            NightSite.Hospital => "Elektronik, Yaşam Kiti",
            _ => "Hurda, Metal",
        };

        static float BaseRisk(NightSite site) => site switch
        {
            NightSite.Market => 0.25f,
            NightSite.Hospital => 0.38f,
            _ => 0.12f,
        };

        /// <summary>Keşifçinin başına bir şey gelme olasılığı (0–1). partners: aynı yere giden diğer kişi sayısı.</summary>
        public static float Risk(Survivor survivor, NightSite site, int day, int partners)
        {
            float risk = BaseRisk(site) + Mathf.Min(0.15f, 0.015f * Mathf.Max(0, day - 1));

            switch (WeatherSystem.Current)
            {
                case WeatherType.Storm: risk += 0.1f; break;
                case WeatherType.DenseFog: risk -= 0.05f; break;
                case WeatherType.AcidRain:
                case WeatherType.ColdWave: risk += 0.05f; break;
            }

            if (survivor != null)
            {
                if (survivor.HasTrait(TraitId.Sprinter)) risk -= 0.08f;
                if (survivor.HasTrait(TraitId.Clumsy)) risk += 0.06f;
                if (survivor.HasTrait(TraitId.Frail)) risk += 0.04f;
                if (survivor.Condition.Stress >= 70f) risk += 0.1f;
                if (survivor.Health < survivor.MaxHealth * 0.5f) risk += 0.08f;
            }

            if (NightDefenseSystem.HasGun) risk -= 0.06f;
            if (partners > 0) risk -= 0.07f;
            return Mathf.Clamp(risk, 0.03f, 0.8f);
        }

        public static bool CanScout(Survivor survivor, out string reason)
        {
            if (survivor == null || survivor.IsDead) reason = "yok";
            else if (!survivor.CanAct) reason = "hareket edemiyor";
            else if (survivor.Health < MinHealth) reason = "çok yaralı";
            else reason = null;
            return reason == null;
        }

        // ---------------------------------------------------------------- Gönderme

        public void Send(Survivor survivor, NightSite site)
        {
            if (survivor == null || survivor.IsDead || IsScouting(survivor)) return;

            survivor.Tasks.CancelAll();
            scouts.Add(new Scout { Survivor = survivor, Site = site, SentAt = Time.time });
            WalkToDoor(scouts[scouts.Count - 1]);

            var chatter = survivor.GetComponent<SurvivorChatter>();
            if (chatter != null) chatter.Say($"{SiteName(site)}'na gidiyorum. Sabah görüşürüz.", 3.5f, new Color(0.7f, 0.85f, 1f));
        }

        void Update()
        {
            for (int i = scouts.Count - 1; i >= 0; i--)
            {
                var scout = scouts[i];
                var survivor = scout.Survivor;
                if (survivor == null || survivor.IsDead)
                {
                    if (!scout.Gone) scouts.RemoveAt(i);
                    continue;
                }
                if (scout.Gone || scout.Leaving) continue;

                // Kapıya giderken başka işe yönelmesin.
                if (survivor.Tasks.State != TaskState.Idle || survivor.Tasks.QueuedCount > 0) survivor.Tasks.CancelAll();

                var door = DoorPoint();
                bool atDoor = survivor.Mover.CurrentFloor == DoorFloor && Mathf.Abs(survivor.transform.position.x - door.x) < 1.2f;
                if (atDoor || Time.time - scout.SentAt > LeaveTimeout)
                {
                    scout.Leaving = true;
                    StartCoroutine(Leave(scout));
                }
                else if (!survivor.Mover.IsMoving && Time.time >= scout.NextWalk && survivor.CanAct)
                {
                    WalkToDoor(scout);
                }
            }
        }

        void WalkToDoor(Scout scout)
        {
            scout.NextWalk = Time.time + 3f;
            if (scout.Survivor.CanAct) scout.Survivor.Mover.WalkTo(DoorPoint());
        }

        Vector3 DoorPoint()
        {
            var door = FindFirstObjectByType<ExitDoor>();
            if (door != null) return new Vector3(door.transform.position.x - 1f, building.FloorY(DoorFloor), door.GetWorkZ());
            return new Vector3(building.MaxX - 1f, building.FloorY(DoorFloor), building.WalkPlaneZ);
        }

        float OutsideX => building.MaxX + OutsideDistance;

        IEnumerator Leave(Scout scout)
        {
            var survivor = scout.Survivor;
            survivor.Tasks.CancelAll();

            // Takılıp kapıya ulaşamadıysa (başka kattaysa) doğrudan çıkmış sayılır.
            if (survivor.Mover.CurrentFloor == DoorFloor)
                yield return WalkThroughDoor(survivor, survivor.transform.position.x, OutsideX);

            if (survivor == null) yield break;
            survivor.gameObject.SetActive(false);
            scout.Gone = true;
            scout.Leaving = false;
        }

        IEnumerator WalkThroughDoor(Survivor survivor, float fromX, float toX)
        {
            survivor.Mover.Halt();
            survivor.Mover.Frozen = true;
            survivor.transform.rotation = Quaternion.LookRotation(toX > fromX ? Vector3.right : Vector3.left);

            float distance = Mathf.Abs(toX - fromX);
            for (float walked = 0f; walked < distance; walked += WalkSpeed * Time.deltaTime)
            {
                if (survivor == null) yield break;
                survivor.transform.position = building.Point(DoorFloor, Mathf.MoveTowards(fromX, toX, walked));
                yield return null;
            }

            survivor.transform.position = building.Point(DoorFloor, toX);
            survivor.Mover.Frozen = survivor.IsDead || survivor.IsTrapped;
        }

        // ---------------------------------------------------------------- Sabah dönüşü

        void OnDayStarted(DayStarted evt)
        {
            if (scouts.Count > 0) ResolveAll(evt.Day);
        }

        void ResolveAll(int day)
        {
            StopAllCoroutines();
            var finished = new List<Scout>(scouts);
            scouts.Clear();

            var partnersAt = new Dictionary<NightSite, int>();
            foreach (var scout in finished)
            {
                partnersAt.TryGetValue(scout.Site, out int count);
                partnersAt[scout.Site] = count + 1;
            }

            var summary = new StringBuilder();
            foreach (var scout in finished)
            {
                var survivor = scout.Survivor;
                if (survivor == null || survivor.IsDead) continue;

                BringHome(survivor, scout.Gone);
                string report = Resolve(survivor, scout.Site, day - 1, partnersAt[scout.Site] - 1);
                EventBus.Publish(new NightScavengeReturned(survivor, report));
                if (summary.Length > 0) summary.Append("  ");
                summary.Append(UiStrip(report));
            }

            if (summary.Length > 0) EventBus.Publish(new NarrativeNotice(summary.ToString()));
        }

        static string UiStrip(string text) => System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", "");

        string Resolve(Survivor survivor, NightSite site, int nightDay, int partners)
        {
            var loot = RollLoot(site);
            float risk = Risk(survivor, site, nightDay, partners);
            var text = new StringBuilder($"<b>{survivor.DisplayName}</b> {SiteName(site)}'ndan döndü");

            if (Random.value < risk)
            {
                bool emptyHanded = Random.value < 0.3f;
                if (emptyHanded) loot.Clear();
                else
                {
                    int keep = Mathf.Max(0, loot.Count / 2);
                    loot.RemoveRange(keep, loot.Count - keep);
                }

                float damage = Random.Range(12f, 26f);
                if (survivor.HasTrait(TraitId.Frail)) damage *= 1.4f;
                if (Workbench.Has(RecipeId.ArmorVest)) damage *= 0.65f;
                // Dışarıda ölmez: en kötü ağır yaralı döner.
                damage = Mathf.Min(damage, survivor.Health - 5f);
                if (damage > 0f) survivor.TakeDamage(damage, "Gece keşfinde yaralandı.");

                string what = site switch
                {
                    NightSite.Market => "başka yağmacılarla çatıştı",
                    NightSite.Hospital => "çöken bir koridorda kaldı",
                    _ => "sarsıntıda moloz altında kaldı",
                };
                text.Append($" ama {what}{(damage > 0f ? $" <color=#ff8f7a>(-{damage:0} Can)</color>" : "")}");
                if (emptyHanded) text.Append(" ve eli boş geldi.");
                else text.Append(".");
            }
            else text.Append(".");

            float stress = survivor.HasTrait(TraitId.Optimist) ? 7f : 12f;
            if (WeatherSystem.Current == WeatherType.ColdWave) stress += 6f;
            survivor.Condition.AddStress(stress);

            if (loot.Count > 0)
            {
                var storage = ResourceStorage.Instance;
                var counts = new Dictionary<ResourceType, int>();
                foreach (var type in loot)
                {
                    storage?.Add(new ResourceAmount(type, 1));
                    counts.TryGetValue(type, out int c);
                    counts[type] = c + 1;
                }
                text.Append(" Getirdiği: <color=#9be37a>");
                bool first = true;
                foreach (var pair in counts)
                {
                    if (!first) text.Append(", ");
                    text.Append($"+{pair.Value} {pair.Key.ToDisplayName()}");
                    first = false;
                }
                text.Append("</color>");
            }
            return text.ToString();
        }

        static List<ResourceType> RollLoot(NightSite site)
        {
            var loot = new List<ResourceType>();
            switch (site)
            {
                case NightSite.Market:
                    loot.Add(ResourceType.Supplies);
                    if (Random.value < 0.6f) loot.Add(ResourceType.Supplies);
                    loot.Add(Random.value < 0.5f ? ResourceType.Scrap : ResourceType.Metal);
                    break;
                case NightSite.Hospital:
                    loot.Add(ResourceType.Electronics);
                    loot.Add(Pick(ResourceType.Electronics, ResourceType.Supplies, ResourceType.Metal));
                    if (Random.value < 0.5f) loot.Add(ResourceType.Supplies);
                    break;
                default:
                    for (int i = 0; i < 3; i++)
                    {
                        float roll = Random.value;
                        loot.Add(roll < 0.5f ? ResourceType.Scrap : roll < 0.88f ? ResourceType.Metal : ResourceType.Electronics);
                    }
                    break;
            }

            // Yoğun siste el yordamıyla aranır: daha az bulunur (ama daha az görülür).
            if (WeatherSystem.Current == WeatherType.DenseFog && loot.Count > 1) loot.RemoveAt(loot.Count - 1);
            return loot;
        }

        static ResourceType Pick(params ResourceType[] options) => options[Random.Range(0, options.Length)];

        void BringHome(Survivor survivor, bool wasGone)
        {
            if (!wasGone)
            {
                // Hiç çıkamadıysa (yolda sabah olduysa) olduğu yerde kalır.
                survivor.Mover.Frozen = survivor.IsDead || survivor.IsTrapped;
                return;
            }

            survivor.transform.position = building.Point(DoorFloor, OutsideX);
            survivor.gameObject.SetActive(true);
            StartCoroutine(WalkThroughDoor(survivor, OutsideX, building.MaxX - 0.8f));
        }

        // ---------------------------------------------------------------- Kayıt

        public void Capture(List<string> names, List<int> sites)
        {
            foreach (var scout in scouts)
            {
                if (scout.Survivor == null || scout.Survivor.IsDead) continue;
                names.Add(scout.Survivor.DisplayName);
                sites.Add((int)scout.Site);
            }
        }

        /// <summary>Kayıttan: gece yüklenirse keşifçiler yine dışarıda olur; sabah yüklenirse hemen dönerler.</summary>
        public void Restore(List<string> names, List<int> sites, bool night, int day)
        {
            StopAllCoroutines();
            scouts.Clear();
            if (names == null || sites == null) return;

            int n = Mathf.Min(names.Count, sites.Count);
            for (int i = 0; i < n; i++)
            {
                Survivor match = null;
                foreach (var survivor in Survivor.All)
                    if (survivor != null && !survivor.IsDead && survivor.DisplayName == names[i]) match = survivor;
                if (match == null) continue;

                match.Tasks.CancelAll();
                match.gameObject.SetActive(false);
                scouts.Add(new Scout { Survivor = match, Site = (NightSite)sites[i], Gone = true, SentAt = Time.time });
            }

            if (!night && scouts.Count > 0) ResolveAll(day);
        }
    }
}
