using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Economy;
using AftershockIsle.Interaction;
using UnityEngine;

namespace AftershockIsle.Crisis
{
    public readonly struct FireStarted
    {
        public readonly FireSpot Fire;
        public FireStarted(FireSpot fire) => Fire = fire;
    }

    public readonly struct FireExtinguished
    {
        public readonly FireSpot Fire;
        public FireExtinguished(FireSpot fire) => Fire = fire;
    }

    public readonly struct CeilingCollapsed
    {
        public readonly FireSpot Fire;
        public CeilingCollapsed(FireSpot fire) => Fire = fire;
    }

    public readonly struct SmokeEvacuation
    {
        public readonly Survivor Survivor;
        public SmokeEvacuation(Survivor survivor) => Survivor = survivor;
    }

    /// <summary>
    /// Bir kattaki yangın odağı ve o katın dumanı. Yanarken büyür, bütünlüğü saniyede eritir ve
    /// duman üretir; duman eşiği geçince kata girilemez, içeridekiler zehirlenip kaçar.
    /// Kova suyuyla söndürülür; tam güçte uzun süre yanarsa tavanı çökertir.
    /// </summary>
    public class FireSpot : Interactable
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly List<FireSpot> all = new List<FireSpot>();

        [Header("Yangın (yoğunluk 0–100)")]
        [SerializeField] float igniteIntensity = 30f;
        [SerializeField] float growthPerSecond = 1.2f;
        [SerializeField] float extinguishPerBucket = 40f;
        [Tooltip("Tam güçteki yangının bütünlük erimesi; düşük yoğunlukta orantılı azalır")]
        [SerializeField] float integrityDrainPerSecond = 1f;
        [Tooltip("Tam güçte bu kadar yanarsa tavan çöker")]
        [SerializeField] float collapseAfterFullBurn = 60f;

        [Header("Duman (0–100)")]
        [SerializeField] float smokePerSecondAtFull = 6f;
        [SerializeField] float smokeDecayPerSecond = 2f;
        [SerializeField] float ventPerSecond = 15f;
        [SerializeField] float smokeBlockThreshold = 50f;
        [SerializeField] float smokeDamagePerSecond = 5f;
        [SerializeField] float smokeStressPerSecond = 6f;

        [Header("Tavan çöküşü")]
        [SerializeField] float collapseDamage = 20f;
        [SerializeField] float collapseRadius = 3f;
        [SerializeField] float collapseSurvivorDamage = 50f;
        [SerializeField] RubblePile collapseRubblePrefab;

        [Header("Görsel")]
        [SerializeField] GameObject flame;
        [SerializeField] Light flameLight;
        [SerializeField] Renderer smokeVisual;
        [SerializeField] float flameStandOff = 1.3f;

        MaterialPropertyBlock block;
        Vector3 flameBaseScale;
        float fullBurnTimer;
        bool hasCollapsed;

        public static IReadOnlyList<FireSpot> All => all;

        public float Intensity { get; private set; }
        public float Smoke { get; private set; }
        public bool IsBurning => Intensity > 0f;
        public bool IsSmokeBlockedHere => Smoke >= smokeBlockThreshold;
        public float CollapseCountdown => collapseAfterFullBurn - fullBurnTimer;
        public bool IsAtFullBurn => Intensity >= 100f;

        public override string ActionLabel => "Söndür";
        public override WorkCategory Category => WorkCategory.Firefight;
        public override bool RepeatAfterComplete => IsBurning;
        public override string StatusText =>
            $"Yangın %{Intensity:0}  duman %{Smoke:0}. Kova suyu gerekir (her kova -%{extinguishPerBucket:0}).";

        /// <summary>Kat duman ya da gaz yüzünden girilemez durumda mı.</summary>
        public static bool IsSmokeBlocked(int floor)
        {
            foreach (var fire in all)
                if (fire.Floor == floor && fire.IsSmokeBlockedHere) return true;
            return GasLeak.IsGasBlocked(floor);
        }

        /// <summary>Yanmayan ve tavanı çökmemiş rastgele bir odağı tutuşturur.</summary>
        public static bool TryIgniteRandom()
        {
            var candidates = all.FindAll(f => !f.IsBurning && !f.hasCollapsed);
            if (candidates.Count == 0) return false;
            return candidates[Random.Range(0, candidates.Count)].Ignite();
        }

        protected override void Awake()
        {
            base.Awake();
            if (flame != null) flameBaseScale = flame.transform.localScale;
            RefreshVisuals();
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public bool Ignite()
        {
            if (IsBurning || hasCollapsed) return false;

            Intensity = igniteIntensity;
            fullBurnTimer = 0f;
            RefreshVisuals();
            EventBus.Publish(new FireStarted(this));
            return true;
        }

        public override Interactable RequiredBefore(Survivor worker)
        {
            if (worker.IsCarrying && worker.Carried.type == ResourceType.Water) return null;
            return WaterSource.FindNearest(worker);
        }

        public override bool CanStart(Survivor worker, out string reason)
        {
            if (!IsBurning)
            {
                reason = "Yangın sönmüş.";
                return false;
            }

            if (!worker.IsCarrying || worker.Carried.type != ResourceType.Water)
            {
                reason = "Söndürmek için kova suyu lazım ama dolduracak su kaynağı yok.";
                return false;
            }

            reason = null;
            return true;
        }

        public override bool CanContinue(Survivor worker) => IsBurning;

        // Alevin içine girilmez; karakter geldiği taraftan biraz uzakta durur.
        public override float GetWorkX(int fromFloor, float fromX)
        {
            float x = transform.position.x;
            float arrival = Building.ArrivalX(fromFloor, fromX, Floor);
            return x + (arrival < x ? -flameStandOff : flameStandOff);
        }

        public override float GetWorkZ() => Building.ClampZ(transform.position.z);

        // Alevin içinden yürünebilir (duman zaten hasar verir); serbest yürüyüşte engel sayılmaz.
        public override bool BlocksMovement => false;

        public override void Complete(Survivor worker)
        {
            if (!worker.TryTakeCarried(out _)) return;
            Extinguish(extinguishPerBucket);
        }

        void Extinguish(float amount)
        {
            if (!IsBurning) return;

            Intensity = Mathf.Max(0f, Intensity - amount);
            if (IsBurning) return;

            fullBurnTimer = 0f;
            StructuralIntegrity.Instance.RemoveDrain(this);
            EventBus.Publish(new FireExtinguished(this));
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (IsBurning)
            {
                Intensity = Mathf.Min(100f, Intensity + growthPerSecond * dt);
                Smoke += smokePerSecondAtFull * (Intensity / 100f) * dt;
                StructuralIntegrity.Instance.SetDrain(this, integrityDrainPerSecond * Intensity / 100f, DamageSource.Fire);

                fullBurnTimer = IsAtFullBurn ? fullBurnTimer + dt : 0f;
                if (fullBurnTimer >= collapseAfterFullBurn) CollapseCeiling();
            }
            else
            {
                Smoke -= smokeDecayPerSecond * dt;
            }

            var window = SmokeWindow.ForFloor(Floor);
            if (window != null && window.IsBroken) Smoke -= ventPerSecond * dt;
            Smoke = Mathf.Clamp(Smoke, 0f, 100f);

            if (IsSmokeBlockedHere) AffectSurvivorsInSmoke(dt);
            RefreshVisuals();
        }

        void AffectSurvivorsInSmoke(float dt)
        {
            foreach (var survivor in Survivor.All)
            {
                if (survivor.IsDead || survivor.Mover.CurrentFloor != Floor || survivor.Mover.IsClimbing) continue;

                survivor.TakeDamage(smokeDamagePerSecond * dt, "Dumandan zehirlendi.");
                survivor.Condition.AddStress(smokeStressPerSecond * dt);

                // Pencere kıran kalır; geçen (yürüyen) dokunulmaz; duran/çalışan öksürerek kaçar.
                if (!survivor.CanAct || survivor.Mover.IsMoving || survivor.Tasks.Current is SmokeWindow) continue;
                if (!Building.TryGetNearestExit(Floor, survivor.transform.position.x, out int exitFloor, out float ladderX)) continue;

                survivor.Tasks.CancelAll();
                survivor.Mover.MoveTo(exitFloor, ladderX);
                EventBus.Publish(new SmokeEvacuation(survivor));
            }
        }

        void CollapseCeiling()
        {
            hasCollapsed = true;
            Intensity = 0f;
            fullBurnTimer = 0f;
            StructuralIntegrity.Instance.RemoveDrain(this);
            StructuralIntegrity.Instance.ApplyDamage(collapseDamage, DamageSource.Fire);

            float x = transform.position.x;
            foreach (var survivor in Survivor.All)
            {
                int floor = survivor.Mover.CurrentFloor;
                bool nearby = (floor == Floor || floor == Floor + 1) &&
                              Mathf.Abs(survivor.transform.position.x - x) <= collapseRadius;
                if (survivor.IsDead || !nearby) continue;

                survivor.TakeDamage(collapseSurvivorDamage, "Çöken tavanın altında kaldı.");
                survivor.Trap(10f);
            }

            if (collapseRubblePrefab != null)
            {
                float height = collapseRubblePrefab.transform.localScale.y;
                var position = new Vector3(x, Building.FloorY(Floor) + height * 0.5f, Building.WalkPlaneZ);
                Instantiate(collapseRubblePrefab, position, Quaternion.identity, transform.parent);
            }

            EventBus.Publish(new CeilingCollapsed(this));
        }

        void RefreshVisuals()
        {
            if (flame != null)
            {
                flame.SetActive(IsBurning);
                if (IsBurning)
                {
                    float t = Mathf.Lerp(0.4f, 1f, Intensity / 100f);
                    float flicker = 1f + Mathf.Sin(Time.time * 12f) * 0.06f;
                    flame.transform.localScale = new Vector3(flameBaseScale.x * t, flameBaseScale.y * t * flicker, flameBaseScale.z);
                    var position = flame.transform.localPosition;
                    position.y = flameBaseScale.y * t * 0.5f;
                    flame.transform.localPosition = position;
                }
            }

            if (flameLight != null)
            {
                flameLight.enabled = IsBurning;
                flameLight.intensity = 2f + Intensity / 100f * 4f + Mathf.Sin(Time.time * 17f) * 0.5f;
            }

            if (smokeVisual != null)
            {
                bool visible = Smoke > 1f;
                smokeVisual.gameObject.SetActive(visible);
                if (!visible) return;

                block ??= new MaterialPropertyBlock();
                smokeVisual.GetPropertyBlock(block);
                block.SetColor(BaseColorId, new Color(0.12f, 0.12f, 0.12f, Smoke / 100f * 0.85f));
                smokeVisual.SetPropertyBlock(block);
            }
        }
    }
}
