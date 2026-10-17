using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using AftershockIsle.Interaction;
using UnityEngine;

namespace AftershockIsle.Effects
{
    /// <summary>
    /// Oyun olaylarına bağlı kısa partikül patlamaları (toz, su, kıvılcım, duman). Partikül sistemleri kodla
    /// kurulur ve kendiliğinden yok olur; gerçek varlıklar gelince sadece Burst çağrıları değiştirilir.
    /// </summary>
    public class EffectsPlayer : MonoBehaviour
    {
        [SerializeField] Material particleMaterial;

        static readonly Color Dust = new Color(0.62f, 0.57f, 0.5f, 0.7f);
        static readonly Color DarkSmoke = new Color(0.15f, 0.15f, 0.15f, 0.6f);
        static readonly Color Water = new Color(0.45f, 0.7f, 1f, 0.8f);
        static readonly Color Spark = new Color(1f, 0.9f, 0.3f, 1f);

        BuildingLayout building;

        /// <summary>Sahnedeki partikül malzemesi; sürekli efektler (yangın) de aynısını kullanır.</summary>
        public static Material ParticleMaterial { get; private set; }

        void Awake()
        {
            building = FindFirstObjectByType<BuildingLayout>();
            if (particleMaterial != null) ParticleMaterial = particleMaterial;
        }

        void OnEnable()
        {
            EventBus.Subscribe<AftershockImpact>(OnAftershock);
            EventBus.Subscribe<ColumnStateChanged>(OnColumnChanged);
            EventBus.Subscribe<CeilingCollapsed>(OnCeilingCollapsed);
            EventBus.Subscribe<RubbleCleared>(OnRubbleCleared);
            EventBus.Subscribe<FurnitureDismantled>(OnDismantled);
            EventBus.Subscribe<PipeBurst>(OnPipeBurst);
            EventBus.Subscribe<ElectrocutionStarted>(OnElectrocution);
            EventBus.Subscribe<FireStarted>(OnFireStarted);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<AftershockImpact>(OnAftershock);
            EventBus.Unsubscribe<ColumnStateChanged>(OnColumnChanged);
            EventBus.Unsubscribe<CeilingCollapsed>(OnCeilingCollapsed);
            EventBus.Unsubscribe<RubbleCleared>(OnRubbleCleared);
            EventBus.Unsubscribe<FurnitureDismantled>(OnDismantled);
            EventBus.Unsubscribe<PipeBurst>(OnPipeBurst);
            EventBus.Unsubscribe<ElectrocutionStarted>(OnElectrocution);
            EventBus.Unsubscribe<FireStarted>(OnFireStarted);
        }

        // Sarsıntıda her katın tavanından toz yağar.
        void OnAftershock(AftershockImpact evt)
        {
            for (int floor = 0; floor < building.FloorCount - 1; floor++)
            {
                for (int i = 0; i < 4; i++)
                {
                    float x = Random.Range(building.MinX, building.MaxX);
                    var position = building.Point(floor, x) + new Vector3(0f, building.FloorHeight - 0.5f, -0.3f);
                    Burst(position, Dust, 14, 1.2f, 0.28f, 1.6f, gravity: 0.5f, radius: 0.6f);
                }
            }
        }

        void OnColumnChanged(ColumnStateChanged evt)
        {
            if (evt.Column.State != ColumnState.Broken) return;
            Burst(evt.Column.transform.position, Dust, 40, 2.5f, 0.4f, 2.2f, gravity: 0.3f, radius: 0.7f);
        }

        void OnCeilingCollapsed(CeilingCollapsed evt)
        {
            var position = building.Point(evt.Fire.Floor, evt.Fire.transform.position.x) + new Vector3(0f, building.FloorHeight * 0.6f, -0.3f);
            Burst(position, Dust, 60, 3f, 0.5f, 2.6f, gravity: 0.4f, radius: 1.2f);
        }

        void OnRubbleCleared(RubbleCleared evt) =>
            Burst(evt.Pile.transform.position, Dust, 24, 1.8f, 0.35f, 1.4f, gravity: 0.2f, radius: 0.6f);

        void OnDismantled(FurnitureDismantled evt) =>
            Burst(evt.Furniture.transform.position, Dust, 22, 1.8f, 0.3f, 1.2f, gravity: 0.3f, radius: 0.5f);

        void OnPipeBurst(PipeBurst evt)
        {
            var position = evt.Pipe.transform.position + Vector3.down * 0.3f;
            Burst(position, Water, 50, 4f, 0.2f, 1.2f, gravity: 1.2f, radius: 0.2f);
        }

        void OnElectrocution(ElectrocutionStarted evt) =>
            Burst(evt.Survivor.transform.position + Vector3.up * 0.6f, Spark, 24, 4f, 0.12f, 0.5f, gravity: 0.8f, radius: 0.3f);

        void OnFireStarted(FireStarted evt) =>
            Burst(evt.Fire.transform.position + Vector3.up * 1.5f, DarkSmoke, 30, 1.2f, 0.6f, 2.5f, gravity: -0.15f, radius: 0.5f);

        /// <summary>Tek seferlik partikül patlaması. Gravity negatifse yukarı yükselir (duman).</summary>
        public void Burst(Vector3 position, Color color, int count, float speed, float size, float lifetime,
                          float gravity = 0.2f, float radius = 0.3f)
        {
            var go = new GameObject("FX");
            go.transform.position = position;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startColor = color;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count * 2;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            if (particleMaterial != null) go.GetComponent<ParticleSystemRenderer>().sharedMaterial = particleMaterial;

            ps.Play();
            Destroy(go, lifetime + 1f);
        }
    }
}
