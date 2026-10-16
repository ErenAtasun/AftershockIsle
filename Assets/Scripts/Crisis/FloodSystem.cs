using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Interaction;
using UnityEngine;

namespace AftershockIsle.Crisis
{
    public readonly struct ElectrocutionStarted
    {
        public readonly Survivor Survivor;
        public ElectrocutionStarted(Survivor survivor) => Survivor = survivor;
    }

    /// <summary>
    /// Bodrum su baskını. Patlak borudan su dolar; %50'yi geçince bina bütünlüğü erir,
    /// %100'de belirli süre tahliye edilmezse temel kayar. Elektrik açıkken su eşiği geçerse
    /// suda duran herkes çarpılır. Doğru sıra: şalteri indir → pompala → boruyu kaynakla → şalteri kaldır.
    /// </summary>
    public class FloodSystem : MonoBehaviour
    {
        const int FloodFloor = 0;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] BurstPipe pipe;

        [Header("Su seviyesi (yüzde puanı)")]
        [SerializeField] float fillPerSecond = 1.2f;
        [SerializeField] float integrityDrainThreshold = 50f;
        [SerializeField] float integrityDrainPerSecond = 0.5f;
        [Tooltip("Su %100'deyken temelin kaymasına kalan süre")]
        [SerializeField] float fullCollapseDelay = 45f;

        [Header("Elektrik kaçağı")]
        [SerializeField] float shockThreshold = 25f;
        [Tooltip("Bu seviyenin altında karakterin ayağı suya değmez")]
        [SerializeField] float wadingThreshold = 3f;
        [SerializeField] float shockDamagePerSecond = 15f;

        [Header("Görsel")]
        [SerializeField] Transform waterVisual;
        [SerializeField] Color waterColor = new Color(0.2f, 0.45f, 0.85f, 0.55f);
        [SerializeField] Color electrifiedColor = new Color(0.75f, 0.9f, 1f, 0.7f);

        readonly HashSet<Survivor> shocked = new HashSet<Survivor>();
        BuildingLayout building;
        Renderer waterRenderer;
        MaterialPropertyBlock block;

        public static FloodSystem Instance { get; private set; }

        /// <summary>0–100 arası bodrum su seviyesi.</summary>
        public float Level { get; private set; }
        public bool IsPipeBurst => pipe != null && pipe.IsBurst;
        public bool IsElectrified => PowerGrid.IsOn && Level >= shockThreshold;
        public bool IsFull => Level >= 100f;
        public float CollapseCountdown => fullCollapseDelay - fullTimer;

        float fullTimer;

        void Awake()
        {
            Instance = this;
            building = FindFirstObjectByType<BuildingLayout>();
            if (waterVisual != null) waterRenderer = waterVisual.GetComponent<Renderer>();
            RefreshVisual();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Boruyu patlatır (afet takvimi veya artçı sarsıntı zinciri).</summary>
        public bool TryBurstPipe() => pipe != null && pipe.Burst();

        public void Drain(float amount) => Level = Mathf.Max(0f, Level - amount);

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (IsPipeBurst) Level = Mathf.Min(100f, Level + fillPerSecond * dt);

            UpdateIntegrityDrain();
            UpdateFoundation(dt);
            UpdateElectrocution(dt);
            RefreshVisual();
        }

        void UpdateIntegrityDrain()
        {
            if (Level > integrityDrainThreshold)
                StructuralIntegrity.Instance.SetDrain(this, integrityDrainPerSecond, DamageSource.Flood);
            else
                StructuralIntegrity.Instance.RemoveDrain(this);
        }

        void UpdateFoundation(float dt)
        {
            if (!IsFull)
            {
                fullTimer = 0f;
                return;
            }

            fullTimer += dt;
            if (fullTimer >= fullCollapseDelay)
                StructuralIntegrity.Instance.ForceCollapse(DamageSource.Flood);
        }

        void UpdateElectrocution(float dt)
        {
            bool live = IsElectrified && Level >= wadingThreshold;

            foreach (var survivor in Survivor.All)
            {
                bool inWater = live && !survivor.IsDead &&
                               survivor.Mover.CurrentFloor == FloodFloor && !survivor.Mover.IsClimbing;
                if (!inWater)
                {
                    shocked.Remove(survivor);
                    continue;
                }

                if (shocked.Add(survivor)) EventBus.Publish(new ElectrocutionStarted(survivor));
                survivor.TakeDamage(shockDamagePerSecond * dt, "Elektrikli suya girdi.");
            }
        }

        void RefreshVisual()
        {
            if (waterVisual == null) return;

            bool visible = Level > 0.01f;
            waterVisual.gameObject.SetActive(visible);
            if (!visible) return;

            float maxHeight = building.FloorHeight - 0.3f;
            float height = maxHeight * Level / 100f;
            var scale = waterVisual.localScale;
            scale.y = height;
            waterVisual.localScale = scale;

            var position = waterVisual.position;
            position.y = building.FloorY(FloodFloor) + height * 0.5f;
            waterVisual.position = position;

            if (waterRenderer == null) return;

            // Elektriklenen su titreşir.
            var color = IsElectrified && Mathf.Repeat(Time.unscaledTime * 8f, 1f) > 0.5f ? electrifiedColor : waterColor;
            block ??= new MaterialPropertyBlock();
            waterRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            waterRenderer.SetPropertyBlock(block);
        }
    }
}
