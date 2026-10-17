using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.CameraControl;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Interaction;
using UnityEngine;

namespace AftershockIsle.Crisis
{
    public readonly struct GasLeakStarted
    {
        public readonly GasLeak Leak;
        public GasLeakStarted(GasLeak leak) => Leak = leak;
    }

    public readonly struct GasLeakStopped
    {
        public readonly GasLeak Leak;
        public GasLeakStopped(GasLeak leak) => Leak = leak;
    }

    public readonly struct GasExploded
    {
        public readonly GasLeak Leak;
        public GasExploded(GasLeak leak) => Leak = leak;
    }

    /// <summary>
    /// Doğalgaz vanası ve katın gaz yoğunluğu (GDD 8.1 / 9: gaz patlaması, zehirli gaz tahliyesi).
    /// Kaçak başlayınca yoğunluk artar; %50'de kata girilemez ve içerideki öksürerek kaçar (zehirlenir).
    /// %60'ı geçince kıvılcım (şalter açıkken) ya da o kattaki yangın gazı patlatır: bina hasar alır, kat tutuşur,
    /// içeridekiler yaralanır ve enkaz altında kalır. Çözüm: şalteri indir (kıvılcım biter), pencereyi kır (gaz tahliye olur),
    /// sonra vanayı kapat.
    /// </summary>
    public class GasLeak : Interactable
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly List<GasLeak> all = new List<GasLeak>();

        [Header("Gaz")]
        [SerializeField] float leakPerSecond = 2.5f;
        [SerializeField] float decayPerSecond = 0.6f;
        [SerializeField] float ventPerSecond = 9f;
        [SerializeField] float blockThreshold = 50f;
        [SerializeField] float explodeThreshold = 60f;
        [Tooltip("Şalter açıkken, patlama eşiğinin üstünde saniye başına kıvılcım ihtimali")]
        [SerializeField] float sparkChancePerSecond = 0.06f;
        [SerializeField] float toxicDamagePerSecond = 1.5f;
        [SerializeField] float toxicStressPerSecond = 4f;

        [Header("Patlama")]
        [SerializeField] float explosionDamage = 15f;
        [SerializeField] float explosionSurvivorDamage = 35f;
        [SerializeField] float explosionTrap = 8f;

        [Header("Görsel")]
        [SerializeField] Renderer gasVisual;
        [SerializeField] Color gasColor = new Color(0.55f, 0.75f, 0.35f, 0.5f);

        MaterialPropertyBlock block;

        public static IReadOnlyList<GasLeak> All => all;

        public bool IsLeaking { get; private set; }
        public float Concentration { get; private set; }
        public bool IsBlockingHere => Concentration >= blockThreshold;
        public bool IsExplosive => Concentration >= explodeThreshold;

        public override string ActionLabel => "Vanayı Kapat";
        public override WorkCategory Category => WorkCategory.Repair;
        public override bool BlocksMovement => false;
        public override string StatusText => IsLeaking
            ? $"GAZ KAÇIYOR — yoğunluk %{Concentration:0}. Şalteri indir, pencereyi kır, vanayı kapat."
            : Concentration > 1f ? $"Vana kapalı. Gaz dağılıyor (%{Concentration:0})." : "Doğalgaz vanası.";

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        /// <summary>Kat gaz yüzünden girilemez durumda mı.</summary>
        public static bool IsGasBlocked(int floor)
        {
            foreach (var leak in all)
                if (leak.Floor == floor && leak.IsBlockingHere) return true;
            return false;
        }

        /// <summary>Kaçmayan rastgele bir vanada kaçak başlatır.</summary>
        public static bool TryStartLeak()
        {
            var candidates = all.FindAll(l => !l.IsLeaking);
            if (candidates.Count == 0) return false;
            candidates[Random.Range(0, candidates.Count)].StartLeak();
            return true;
        }

        public void StartLeak()
        {
            if (IsLeaking) return;
            IsLeaking = true;
            EventBus.Publish(new GasLeakStarted(this));
        }

        public override bool CanStart(Survivor worker, out string reason)
        {
            reason = IsLeaking ? null : "Gaz kaçağı yok.";
            return IsLeaking;
        }

        public override void Complete(Survivor worker)
        {
            if (!IsLeaking) return;
            IsLeaking = false;
            EventBus.Publish(new GasLeakStopped(this));
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Concentration += (IsLeaking ? leakPerSecond : -decayPerSecond) * dt;
            var window = SmokeWindow.ForFloor(Floor);
            if (window != null && window.IsBroken) Concentration -= ventPerSecond * dt;
            Concentration = Mathf.Clamp(Concentration, 0f, 100f);

            if (IsBlockingHere) AffectSurvivors(dt);
            if (IsExplosive && ShouldIgnite(dt)) Explode();
            RefreshVisual();
        }

        // Şalter açıkken kıvılcım ya da o katta yanan bir yangın gazı ateşler.
        bool ShouldIgnite(float dt)
        {
            foreach (var fire in FireSpot.All)
                if (fire.Floor == Floor && fire.IsBurning) return true;
            return PowerGrid.IsOn && Random.value < sparkChancePerSecond * dt;
        }

        void AffectSurvivors(float dt)
        {
            foreach (var survivor in Survivor.All)
            {
                if (survivor.IsDead || survivor.Mover.CurrentFloor != Floor || survivor.Mover.IsClimbing) continue;

                survivor.TakeDamage(toxicDamagePerSecond * dt, "Gazdan zehirlendi.");
                survivor.Condition.AddStress(toxicStressPerSecond * dt);

                // Pencere kıran ve vanayı kapatan kalır; duran/çalışan öksürerek kaçar.
                if (!survivor.CanAct || survivor.Mover.IsMoving || survivor.Mover.IsSteering) continue;
                if (survivor.Tasks.Current is SmokeWindow || survivor.Tasks.Current == this) continue;
                if (!Building.TryGetNearestExit(Floor, survivor.transform.position.x, out int exitFloor, out float ladderX)) continue;

                survivor.Tasks.CancelAll();
                survivor.Mover.MoveTo(exitFloor, ladderX);
                EventBus.Publish(new TaskNotice(survivor, "Öksürerek gazdan kaçıyor."));
            }
        }

        void Explode()
        {
            Concentration = 0f;
            IsLeaking = false;

            StructuralIntegrity.Instance.ApplyDamage(explosionDamage, DamageSource.Gas);
            foreach (var survivor in Survivor.All)
            {
                if (survivor.IsDead || survivor.Mover.CurrentFloor != Floor) continue;
                survivor.TakeDamage(explosionSurvivorDamage, "Gaz patlamasında öldü.");
                survivor.Trap(explosionTrap);
            }

            foreach (var fire in FireSpot.All)
                if (fire.Floor == Floor) fire.Ignite();

            SideViewCamera.Kick(0.6f, 0.8f);
            GameInput.Rumble(1f, 1f, 0.6f);
            EventBus.Publish(new GasExploded(this));
        }

        void RefreshVisual()
        {
            if (gasVisual == null) return;

            bool visible = Concentration > 0.5f;
            if (gasVisual.enabled != visible) gasVisual.enabled = visible;
            if (!visible) return;

            block ??= new MaterialPropertyBlock();
            gasVisual.GetPropertyBlock(block);
            var color = gasColor;
            color.a = gasColor.a * Mathf.Clamp01(Concentration / 100f);
            block.SetColor(BaseColorId, color);
            gasVisual.SetPropertyBlock(block);
        }
    }
}
