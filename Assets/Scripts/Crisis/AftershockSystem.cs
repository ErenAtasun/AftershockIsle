using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Interaction;
using AftershockIsle.Skills;
using UnityEngine;

namespace AftershockIsle.Crisis
{
    public enum QuakePhase { Idle, Warning, Shaking }

    public readonly struct AftershockWarning
    {
        public readonly float Duration;
        public AftershockWarning(float duration) => Duration = duration;
    }

    public readonly struct AftershockImpact
    {
        public readonly float Damage;
        public readonly int BrokenColumns;
        public readonly int ExposedSurvivors;
        public readonly float ShakeDuration;

        public AftershockImpact(float damage, int brokenColumns, int exposedSurvivors, float shakeDuration)
        {
            Damage = damage;
            BrokenColumns = brokenColumns;
            ExposedSurvivors = exposedSurvivors;
            ShakeDuration = shakeDuration;
        }
    }

    /// <summary>
    /// Artçı sarsıntı: geri sayım sırasında Yaşam Üçgeni alanları yeşil yanar; vuruşta
    /// açıktaki kazazedeler yaralanıp enkaz altında kalır, kolonlar bir kademe kötüleşir ve
    /// binaya (çelik olmayan kolon oranı × 2^kırık kolon) ölçekli temel hasar düşer.
    /// </summary>
    public class AftershockSystem : MonoBehaviour
    {
        [Header("Zamanlama")]
        [SerializeField] float warningDuration = 6f;
        [SerializeField] float shakeDuration = 2f;

        [Header("Bina hasarı")]
        [SerializeField] float baseDamage = 10f;
        [Tooltip("Her kırık kolon sarsıntı hasarını bu kadarla çarpar")]
        [SerializeField] float brokenColumnMultiplier = 2f;
        [SerializeField] float maxDamageMultiplier = 8f;
        [SerializeField, Range(0f, 1f)] float intactCrackChance = 0.25f;
        [Tooltip("Sarsıntının sağlam şebeke borusunu patlatma ihtimali (kriz zinciri)")]
        [SerializeField, Range(0f, 1f)] float pipeBurstChance = 0.2f;
        [Tooltip("Elektrik açıkken sarsıntının kıvılcımla yangın çıkarma ihtimali")]
        [SerializeField, Range(0f, 1f)] float sparkFireChance = 0.15f;

        [Header("Kazazedeler")]
        [SerializeField] float exposedDamage = 35f;
        [SerializeField] float trappedDuration = 8f;
        [SerializeField] float exposedStress = 25f;

        [Header("Görsel")]
        [SerializeField] RubblePile debrisPrefab;
        [SerializeField] Transform debrisParent;
        [SerializeField] Material shelterMaterial;
        [SerializeField] float debrisZ = -0.2f;

        readonly List<ShelterZone> zones = new List<ShelterZone>();
        readonly List<GameObject> shelterMarkers = new List<GameObject>();
        readonly List<SupportColumn> columns = new List<SupportColumn>();
        BuildingLayout building;

        public static AftershockSystem Instance { get; private set; }

        public QuakePhase Phase { get; private set; }
        public float PhaseRemaining { get; private set; }

        void Awake()
        {
            Instance = this;
            building = FindFirstObjectByType<BuildingLayout>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Sarsıntı uyarısını başlatır. Zaten bir sarsıntı sürüyorsa false.</summary>
        public bool Trigger()
        {
            if (Phase != QuakePhase.Idle) return false;

            Phase = QuakePhase.Warning;
            PhaseRemaining = warningDuration;
            ShowShelterMarkers();
            EventBus.Publish(new AftershockWarning(warningDuration));
            return true;
        }

        void Update()
        {
            if (Phase == QuakePhase.Idle) return;

            PhaseRemaining -= Time.deltaTime;
            if (PhaseRemaining > 0f) return;

            if (Phase == QuakePhase.Warning)
            {
                Impact();
                Phase = QuakePhase.Shaking;
                PhaseRemaining = shakeDuration;
            }
            else
            {
                Phase = QuakePhase.Idle;
                PhaseRemaining = 0f;
            }
        }

        void Impact()
        {
            HideShelterMarkers();

            // Sığınak kontrolü kolonlar kırılmadan önce, vuruş anındaki duruma göre yapılır.
            ShelterZones.Collect(zones);
            int exposed = 0;
            foreach (var survivor in Survivor.All)
            {
                if (survivor.IsDead || ShelterZones.IsSheltered(survivor, zones)) continue;

                exposed++;
                survivor.Condition.AddStress(exposedStress);
                if (SkillTree.QuakeImmunity) continue;

                survivor.TakeDamage(exposedDamage, "Artçı sarsıntıda açıkta yakalandı.");
                survivor.Trap(trappedDuration);
            }

            columns.Clear();
            columns.AddRange(SupportColumn.All);
            int reinforced = 0;
            int broken = 0;
            foreach (var column in columns)
            {
                if (column.ApplyShock(intactCrackChance)) SpawnDebris(column);

                if (column.State == ColumnState.Reinforced) reinforced++;
                if (column.State == ColumnState.Broken) broken++;
            }

            float exposedShare = columns.Count > 0 ? (columns.Count - reinforced) / (float)columns.Count : 1f;
            float multiplier = Mathf.Min(Mathf.Pow(brokenColumnMultiplier, broken), maxDamageMultiplier);
            float damage = baseDamage * exposedShare * multiplier;

            StructuralIntegrity.Instance.ApplyDamage(damage, DamageSource.Aftershock);
            EventBus.Publish(new AftershockImpact(damage, broken, exposed, shakeDuration));

            if (FloodSystem.Instance != null && Random.value < pipeBurstChance)
                FloodSystem.Instance.TryBurstPipe();

            if (PowerGrid.IsOn && !Workbench.Has(RecipeId.LightningRod) && Random.value < sparkFireChance)
                FireSpot.TryIgniteRandom();
        }

        void SpawnDebris(SupportColumn column)
        {
            if (debrisPrefab == null) return;

            float height = debrisPrefab.transform.localScale.y;
            var position = new Vector3(column.transform.position.x,
                building.FloorY(column.Floor) + height * 0.5f, debrisZ);
            Instantiate(debrisPrefab, position, Quaternion.identity, debrisParent);
        }

        void ShowShelterMarkers()
        {
            HideShelterMarkers();
            ShelterZones.Collect(zones);

            foreach (var zone in zones)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = "ShelterMarker";
                Destroy(marker.GetComponent<Collider>());
                marker.transform.position = new Vector3(zone.CenterX, building.FloorY(zone.Floor) + 0.02f, zone.CenterZ);
                marker.transform.localScale = new Vector3(zone.Width, 0.04f, zone.Depth);
                if (shelterMaterial != null) marker.GetComponent<Renderer>().sharedMaterial = shelterMaterial;
                shelterMarkers.Add(marker);
            }
        }

        void HideShelterMarkers()
        {
            foreach (var marker in shelterMarkers)
                if (marker != null) Destroy(marker);
            shelterMarkers.Clear();
        }
    }
}
