using System.Collections.Generic;
using System.Text;
using AftershockIsle.Building;
using AftershockIsle.CameraControl;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Economy;
using AftershockIsle.Effects;
using AftershockIsle.Expedition;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AftershockIsle.Crisis
{
    public readonly struct NightRaidResolved
    {
        public readonly bool Happened;
        public readonly bool Repelled;
        public NightRaidResolved(bool happened, bool repelled)
        {
            Happened = happened;
            Repelled = repelled;
        }
    }

    /// <summary>
    /// This War of Mine tarzı gece savunması. Gece başlarken bir kazazede nöbetçi seçilir (ya da kimse).
    /// Gecenin ortasında yağmacı baskını zarı atılır: savunma gücü (nöbetçi + silah/mermi + Çelik Kapı Barikatı)
    /// baskın gücüyle (gün, hava, sığınağa çekilen dikkat) karşılaştırılır ve sonuç bir raporla gösterilir.
    /// Nöbetçi gece uyumadığı için sabah yorgun (stresli) uyanır.
    /// </summary>
    public class NightDefenseSystem : MonoBehaviour
    {
        const int FirstRaidDay = 3;
        const int MaxGuardOptions = 5;

        public static NightDefenseSystem Instance { get; private set; }

        /// <summary>Bu gecenin nöbetçisi (yoksa null).</summary>
        public static Survivor Guard => Instance != null ? Instance.guard : null;

        /// <summary>Sığınağa çekilen yağmacı dikkati (0–100). Kapıdan kovulan yabancılar artırır, baskın sıfırlar.</summary>
        public int Attention => attention;

        /// <summary>True ise nöbetçi akşam planında seçilir; bu sistem kendi nöbet penceresini açmaz.</summary>
        public static bool GuardChosenByPlan { get; set; }

        Survivor guard;
        float nextGuardWalkTime;
        int attention;
        bool promptPending;
        bool raidResolved = true;
        float raidAtNormalized;
        DayCycle dayCycle;

        // Oyun sahnesi ana menüden yüklendiği için yalnızca açılış sahnesine bakmak yetmez: her sahne yüklemesinde kontrol edilir.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Instance != null) return;
            var existing = FindFirstObjectByType<NightDefenseSystem>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }
            var systems = GameObject.Find("[Systems]");
            if (systems != null) systems.AddComponent<NightDefenseSystem>();
        }

        public static void RaiseAttention(int amount)
        {
            if (Instance == null) return;
            Instance.attention = Mathf.Clamp(Instance.attention + amount, 0, 100);
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable() => EventBus.Subscribe<DayPhaseChanged>(OnPhaseChanged);
        void OnDisable() => EventBus.Unsubscribe<DayPhaseChanged>(OnPhaseChanged);

        void OnPhaseChanged(DayPhaseChanged evt)
        {
            if (evt.Phase == DayPhase.Night)
            {
                guard = null;
                promptPending = evt.Day >= 2 && !GuardChosenByPlan;
                raidResolved = false;
                raidAtNormalized = Random.Range(0.35f, 0.75f);
            }
            else if (evt.Phase == DayPhase.Morning)
            {
                EndNightShift();
            }
        }

        void Update()
        {
            if (dayCycle == null) dayCycle = FindFirstObjectByType<DayCycle>();
            if (dayCycle == null || dayCycle.Phase != DayPhase.Night) return;

            bool blocked = NarrativeEventSystem.IsModalOpen || SideViewCamera.ViewingField || PlacementController.IsPlacementActive;

            if (promptPending)
            {
                if (!blocked) ShowGuardPrompt();
                return;
            }

            if (dayCycle.Phase == DayPhase.Night && guard != null && !guard.IsDead)
            {
                // Nöbetçi başka işe gitmesin, kapı başında nöbette dursun
                if (guard.Tasks.State != TaskState.Idle && guard.Tasks.Current != null)
                    guard.Tasks.CancelAll();

                var post = GetDoorPostPosition();
                float dist = Vector3.Distance(guard.transform.position, post);
                // Kapı önü enkazla kapalıysa nöbetçi en yakın noktaya gider; her karede yeniden yol aramasın.
                if (!guard.Mover.IsMoving && dist > 1.8f && Time.time >= nextGuardWalkTime)
                {
                    nextGuardWalkTime = Time.time + 3f;
                    guard.Mover.WalkTo(post);
                }
                else if (dist <= 1.8f && !guard.Mover.IsMoving)
                {
                    guard.Mover.FaceDirection(Vector3.right);
                }
            }

            if (!raidResolved && dayCycle.PhaseDuration > 0f &&
                dayCycle.PhaseElapsed / dayCycle.PhaseDuration >= raidAtNormalized && !SideViewCamera.ViewingField)
            {
                raidResolved = true;
                ResolveNight(dayCycle.Day);
            }
        }

        // ---------------------------------------------------------------- Nöbetçi seçimi

        void ShowGuardPrompt()
        {
            promptPending = false;
            var narrative = NarrativeEventSystem.Instance;
            var candidates = GuardCandidates();
            if (narrative == null || candidates.Count == 0) return;

            int day = dayCycle != null ? dayCycle.Day : 1;
            var body = new StringBuilder();
            body.Append("Gece çöktü. Kapıda nöbet tutacak birini seçin; nöbetçi uyumaz ve sabah yorgun uyanır (+10 Stres).\n\n");
            body.Append($"Silah: {WeaponSummary()}   •   Barikat: {(HasBarricade ? "Çelik Kapı var" : "yok")}\n");
            int breaches = AftershockIsle.Interaction.PlankPatch.OpenWallBreachCount;
            if (breaches > 0)
                body.Append($"Duvarda kalasla kapatılmamış {breaches} gedik var: savunma -{AftershockIsle.Interaction.PlankPatch.OpenBreachDefensePenalty:0}\n");
            body.Append($"Tehdit: {ThreatLabel(RaidChance(day))}");
            var storage = AftershockIsle.Economy.ResourceStorage.Instance;
            int kits = storage != null ? storage.Get(AftershockIsle.Economy.ResourceType.Supplies) : 0;
            int alive = Survivor.AliveCount;
            body.Append($"\nYiyecek: {kits} Yaşam Kiti, {alive} kazazede (sabaha kadar herkes bir kit yer).");
            if (attention > 0) body.Append($"  (yağmacı dikkati %{attention})");

            var choices = new List<NarrativeEventSystem.ExternalChoice>();
            for (int i = 0; i < candidates.Count && i < MaxGuardOptions; i++)
            {
                var survivor = candidates[i];
                string tags = survivor.HasTrait(TraitId.Sharpshooter) ? "  [Keskin Nişancı]" : "";
                choices.Add(new NarrativeEventSystem.ExternalChoice(
                    $"{survivor.DisplayName} nöbet tutsun  — savunma {Mathf.RoundToInt(DefenseScore(survivor))}{tags}   (Can {survivor.Health:0}, Stres {survivor.Condition.Stress:0})",
                    () => SetGuard(survivor)));
            }
            choices.Add(new NarrativeEventSystem.ExternalChoice(
                $"Kimse nöbet tutmasın — savunma {Mathf.RoundToInt(DefenseScore(null))}",
                () => EventBus.Publish(new NarrativeNotice("Bu gece herkes uyuyor. Kapı korumasız..."))));

            narrative.ShowExternal("🌙 Gece Nöbeti", body.ToString(), choices);
        }

        static List<Survivor> GuardCandidates()
        {
            var list = new List<Survivor>();
            foreach (var s in Survivor.All)
                if (s != null && s.gameObject.activeInHierarchy && s.CanAct) list.Add(s);
            list.Sort((a, b) => DefenseScore(b).CompareTo(DefenseScore(a)));
            return list;
        }

        /// <summary>Bu gecenin nöbetçisini atar (null: kimse nöbet tutmaz).</summary>
        public void SetGuard(Survivor survivor)
        {
            if (survivor == null || survivor.IsDead)
            {
                guard = null;
                EventBus.Publish(new NarrativeNotice("Bu gece herkes uyuyor. Kapı korumasız..."));
                return;
            }
            guard = survivor;
            guard.Tasks.CancelAll();
            var chatter = guard.GetComponent<SurvivorChatter>();
            if (chatter != null) chatter.Say("Bu gece kapı bende. Kuş uçurtmam.", 4f, new Color(1f, 0.9f, 0.45f));
            SendGuardToDoor();
            EventBus.Publish(new NarrativeNotice($"{survivor.DisplayName} bu gece kapıda nöbet tutuyor."));
        }

        Vector3 GetDoorPostPosition()
        {
            var door = FindFirstObjectByType<AftershockIsle.Interaction.ExitDoor>();
            if (door != null)
                return new Vector3(door.transform.position.x - 1.4f, door.transform.position.y - 0.7f, door.GetWorkZ());

            var layout = FindFirstObjectByType<BuildingLayout>();
            if (layout != null)
                return new Vector3(layout.MaxX - 1.4f, layout.FloorY(1), layout.WalkPlaneZ);

            return guard != null ? guard.transform.position : Vector3.zero;
        }

        void SendGuardToDoor()
        {
            if (guard == null || guard.IsDead) return;
            var post = GetDoorPostPosition();
            guard.Mover.WalkTo(post);
        }

        void EndNightShift()
        {
            if (guard != null && !guard.IsDead)
            {
                guard.Condition.AddStress(10f);
                var chatter = guard.GetComponent<SurvivorChatter>();
                if (chatter != null) chatter.Say("Nöbet bitti... Şükür ki sabahı gördük.", 3.5f);
            }
            guard = null;
            promptPending = false;
            raidResolved = true;
        }

        // ---------------------------------------------------------------- Güç hesapları

        public static bool HasBarricade => Workbench.Has(RecipeId.ReinforcedDoor);

        public static bool HasGun
        {
            get
            {
                var best = FieldWeapons.Best();
                return best != WeaponKind.Slingshot && Workbench.Instance != null && Workbench.Instance.Ammo > 0;
            }
        }

        public static string WeaponSummary()
        {
            var best = FieldWeapons.Best();
            if (best == WeaponKind.Slingshot) return "sadece sapan";
            int ammo = Workbench.Instance != null ? Workbench.Instance.Ammo : 0;
            return ammo > 0 ? $"{FieldWeapons.Get(best).Name} ({ammo} mermi)" : $"{FieldWeapons.Get(best).Name} (mermi yok!)";
        }

        static float WeaponPower(WeaponKind kind) => kind switch
        {
            WeaponKind.Pistol => 20f,
            WeaponKind.Smg => 25f,
            WeaponKind.SniperRifle => 25f,
            WeaponKind.Shotgun => 30f,
            WeaponKind.AssaultRifle => 32f,
            _ => 5f,
        };

        /// <summary>Verilen nöbetçiyle (null: nöbetçi yok) toplam savunma gücü.</summary>
        public static float DefenseScore(Survivor candidate)
        {
            float score = 0f;
            if (candidate != null)
            {
                float personal = 20f;
                if (candidate.HasTrait(TraitId.Sharpshooter)) personal += 15f;
                if (candidate.Condition.Stress >= 70f) personal -= 8f;
                personal *= Mathf.Lerp(0.5f, 1f, candidate.Health / Mathf.Max(1f, candidate.MaxHealth));
                score += personal;
                score += HasGun ? WeaponPower(FieldWeapons.Best()) : 5f;
            }
            if (HasBarricade) score += 40f;
            // Kalasla kapatılmamış duvar gedikleri yağmacılara ikinci bir giriş verir.
            score -= AftershockIsle.Interaction.PlankPatch.OpenBreachDefensePenalty;
            return Mathf.Max(0f, score);
        }

        public float RaidChance(int day)
        {
            if (day < FirstRaidDay) return attention > 0 ? attention / 200f : 0f;

            float chance = 0.2f + 0.04f * (day - FirstRaidDay) + attention / 100f;
            switch (WeatherSystem.Current)
            {
                case WeatherType.Storm:
                case WeatherType.DenseFog: chance += 0.1f; break;
                case WeatherType.ColdWave: chance -= 0.1f; break;
            }
            return Mathf.Clamp(chance, 0f, 0.85f);
        }

        public static string ThreatLabel(float chance) =>
            chance <= 0.01f ? "<color=#9be37a>yok</color>"
            : chance < 0.3f ? "<color=#cfe37a>düşük</color>"
            : chance < 0.55f ? "<color=#ffd75a>orta</color>"
            : "<color=#ff7a5a>yüksek</color>";

        // ---------------------------------------------------------------- Baskın

        void ResolveNight(int day)
        {
            if (Random.value >= RaidChance(day))
            {
                attention = Mathf.Max(0, attention - 10);
                if (guard != null && !guard.IsDead)
                    EventBus.Publish(new NarrativeNotice($"Gece sakin geçti. {guard.DisplayName} sabaha kadar kapıyı bekledi."));
                EventBus.Publish(new NightRaidResolved(false, true));
                return;
            }

            bool guardOk = guard != null && !guard.IsDead && guard.gameObject.activeInHierarchy;
            var activeGuard = guardOk ? guard : null;

            float defense = DefenseScore(activeGuard);
            float strength = 30f + day * 6f + attention * 0.3f + Random.Range(-10f, 15f);
            float margin = defense - strength;
            attention = 0;

            var storage = ResourceStorage.Instance;
            var report = new StringBuilder();
            report.Append("Gecenin karanlığında yağmacılar sığınağın kapısına dayandı!\n\n");

            SoundManager.Play(SoundId.DoorKnock, 1f);

            bool usedGun = activeGuard != null && HasGun;
            if (usedGun)
            {
                int shots = margin >= 15f ? 2 : 3;
                for (int i = 0; i < shots; i++) Workbench.Instance.TryUseAmmo();
                SoundManager.PlayWeaponFire(FieldWeapons.Best(), 0.9f);
            }

            bool repelled;
            if (margin >= 15f)
            {
                repelled = true;
                report.Append(activeGuard != null
                    ? $"<b>{activeGuard.DisplayName}</b> onları erkenden fark etti{(usedGun ? " ve birkaç el ateşle" : "")} kaçırdı."
                    : "Çelik Kapı Barikatı tüm zorlamalara dayandı; yağmacılar eli boş döndü.");

                if (activeGuard != null && Random.value < 0.4f && storage != null)
                {
                    storage.Add(new ResourceAmount(ResourceType.Metal, 1));
                    storage.Add(new ResourceAmount(ResourceType.Scrap, 1));
                    report.Append("\nKaçarken düşürdükleri çantadan <color=#9be37a>+1 Metal, +1 Hurda</color> çıktı.");
                }
                if (activeGuard != null) activeGuard.Condition.RelieveStress(5f);
            }
            else if (margin >= -10f)
            {
                repelled = true;
                report.Append("Kapıda kısa ama sert bir boğuşma yaşandı; yağmacılar sonunda geri çekildi.");
                if (activeGuard != null)
                {
                    float damage = Random.Range(15f, 25f);
                    activeGuard.TakeDamage(damage, "Gece baskınında yaralandı.");
                    SoundManager.Play(SoundId.PlayerHurt, 0.9f);
                    report.Append($"\n<b>{activeGuard.DisplayName}</b> yaralandı <color=#ff7a5a>(-{damage:0} Can)</color>.");
                }
                else
                {
                    StructuralIntegrity.Instance?.ApplyDamage(4f, DamageSource.Debug);
                    report.Append("\nKapı zorlanırken bina hasar gördü <color=#ff7a5a>(-4 Bütünlük)</color>.");
                }
                string stolen = Steal(storage, 1);
                if (stolen.Length > 0) report.Append($"\nKargaşada çalınan: <color=#ff7a5a>{stolen}</color>");
            }
            else
            {
                repelled = false;
                report.Append("<color=#ff7a5a>Yağmacılar içeri girdi!</color> Depoyu talan edip karanlığa karıştılar.");
                string stolen = Steal(storage, Random.Range(2, 5));
                if (stolen.Length > 0) report.Append($"\nÇalınan: <color=#ff7a5a>{stolen}</color>");

                if (activeGuard != null)
                {
                    float damage = Random.Range(25f, 40f);
                    activeGuard.TakeDamage(damage, "Gece baskınında ağır yaralandı.");
                    report.Append($"\n<b>{activeGuard.DisplayName}</b> direnirken ağır yaralandı <color=#ff7a5a>(-{damage:0} Can)</color>.");
                }
                else
                {
                    var victim = RandomSleeper();
                    if (victim != null && Random.value < 0.5f)
                    {
                        float damage = Random.Range(10f, 20f);
                        victim.TakeDamage(damage, "Gece baskınında uykusunda saldırıya uğradı.");
                        report.Append($"\n<b>{victim.DisplayName}</b> uykusunda saldırıya uğradı <color=#ff7a5a>(-{damage:0} Can)</color>.");
                    }
                }
                SoundManager.Play(SoundId.PlayerHurt, 0.8f);
                StructuralIntegrity.Instance?.ApplyDamage(4f, DamageSource.Debug);
                foreach (var s in Survivor.All)
                    if (s != null && !s.IsDead) s.Condition.AddStress(15f);
                report.Append("\nHerkes sarsıldı <color=#ff7a5a>(+15 Stres)</color>.");
            }

            if (!HasBarricade && !repelled)
                report.Append("\n\n<i>İpucu: Tezgahta Çelik Kapı Barikatı üretmek baskınlara karşı en güçlü savunmadır.</i>");

            report.Append($"\n\n<size=12>Savunma {defense:0}  vs  Baskın {strength:0}</size>");

            EventBus.Publish(new NightRaidResolved(true, repelled));
            var narrative = NarrativeEventSystem.Instance;
            if (narrative != null)
            {
                narrative.ShowExternal(repelled ? "🛡 Gece Baskını Püskürtüldü" : "⚠ Gece Baskını!", report.ToString(),
                    new[] { new NarrativeEventSystem.ExternalChoice("Tamam", null) });
            }
        }

        // Önce Yaşam Kiti, sonra elektronik/metal/hurda: yağmacılar en değerliyi alır.
        static string Steal(ResourceStorage storage, int units)
        {
            if (storage == null || units <= 0) return "";

            // Her Ek Depo Rafı çalınan miktarı 1 azaltır.
            units -= AftershockIsle.Crafting.PlaceableStructure.CountOf(AftershockIsle.Crafting.RecipeId.StorageRack);
            if (units <= 0) return "";
            ResourceType[] priority = { ResourceType.Supplies, ResourceType.Electronics, ResourceType.Metal, ResourceType.Scrap };
            var taken = new Dictionary<ResourceType, int>();
            for (int i = 0; i < units; i++)
            {
                foreach (var type in priority)
                {
                    if (!storage.TrySpend(new ResourceAmount(type, 1))) continue;
                    taken.TryGetValue(type, out int count);
                    taken[type] = count + 1;
                    break;
                }
            }

            var text = new StringBuilder();
            foreach (var pair in taken)
            {
                if (text.Length > 0) text.Append(", ");
                text.Append($"{pair.Value} {pair.Key.ToDisplayName()}");
            }
            return text.ToString();
        }

        static Survivor RandomSleeper()
        {
            var list = new List<Survivor>();
            foreach (var s in Survivor.All)
                if (s != null && !s.IsDead && s.gameObject.activeInHierarchy) list.Add(s);
            return list.Count > 0 ? list[Random.Range(0, list.Count)] : null;
        }

        // ---------------------------------------------------------------- Kayıt

        public void RestoreState(int savedAttention)
        {
            attention = Mathf.Clamp(savedAttention, 0, 100);
        }

        /// <summary>Kayıt için: bu gece baskın zarı henüz atılmadı mı, ne zaman atılacak, nöbetçi kim.</summary>
        public void CaptureNight(out bool raidPending, out float raidAt, out string guardName)
        {
            raidPending = !raidResolved;
            raidAt = raidAtNormalized;
            guardName = guard != null && !guard.IsDead ? guard.DisplayName : null;
        }

        /// <summary>Kayıttan: gece ortasında yüklenirse baskın ve nöbetçi kaldığı yerden sürer.</summary>
        public void RestoreNight(bool raidPending, float raidAt, string guardName)
        {
            raidResolved = !raidPending;
            raidAtNormalized = raidAt > 0f ? raidAt : raidAtNormalized;
            promptPending = false;
            guard = null;

            if (string.IsNullOrEmpty(guardName)) return;
            foreach (var survivor in Survivor.All)
            {
                if (survivor == null || survivor.IsDead || survivor.DisplayName != guardName) continue;
                guard = survivor;
                break;
            }
        }
    }
}
