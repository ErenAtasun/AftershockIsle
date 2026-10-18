using System.Collections.Generic;
using AftershockIsle.Core;
using UnityEngine;

namespace AftershockIsle.Building
{
    public enum DamageSource { Aftershock, Flood, Fire, Gas, Debug }

    public readonly struct IntegrityChanged
    {
        public readonly float Current;
        public readonly float Delta;
        public readonly DamageSource Source;

        public IntegrityChanged(float current, float delta, DamageSource source)
        {
            Current = current;
            Delta = delta;
            Source = source;
        }
    }

    public readonly struct BuildingCollapsed
    {
        public readonly DamageSource Source;
        public BuildingCollapsed(DamageSource source) => Source = source;
    }

    /// <summary>
    /// Binanın Yapısal Bütünlük barı. Anlık hasar (sarsıntı) ApplyDamage ile,
    /// süreğen aşınma (su, yangın) sahibine bağlı saniyelik drain olarak uygulanır.
    /// </summary>
    public class StructuralIntegrity : MonoBehaviour
    {
        [SerializeField] float maxIntegrity = 100f;

        readonly Dictionary<Object, (float perSecond, DamageSource source)> drains =
            new Dictionary<Object, (float, DamageSource)>();
        readonly float[] drainBySource = new float[System.Enum.GetValues(typeof(DamageSource)).Length];

        public static StructuralIntegrity Instance { get; private set; }

        public float Current { get; private set; }
        public float Max => maxIntegrity;
        public float Normalized => Current / maxIntegrity;
        public bool IsCollapsed { get; private set; }

        public float TotalDrainPerSecond
        {
            get
            {
                float total = 0f;
                foreach (var drain in drains.Values) total += drain.perSecond;
                return total;
            }
        }

        void Awake()
        {
            Instance = this;
            Current = maxIntegrity;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (drains.Count == 0 || IsCollapsed) return;

            // Önce kaynak başına topla: hasar olayları sırasında drains değişebilir.
            System.Array.Clear(drainBySource, 0, drainBySource.Length);
            foreach (var drain in drains.Values)
                drainBySource[(int)drain.source] += drain.perSecond;

            for (int i = 0; i < drainBySource.Length; i++)
                if (drainBySource[i] > 0f) ApplyDamage(drainBySource[i] * Time.deltaTime, (DamageSource)i);
        }

        public void ApplyDamage(float amount, DamageSource source)
        {
            if (IsCollapsed || amount <= 0f) return;

            float previous = Current;
            Current = Mathf.Max(0f, Current - amount);
            EventBus.Publish(new IntegrityChanged(Current, Current - previous, source));

            if (Current > 0f) return;

            IsCollapsed = true;
            EventBus.Publish(new BuildingCollapsed(source));
        }

        /// <summary>Onarım (ör. kırık kolonu yeniden dikmek, çelikle güçlendirmek) bütünlüğü geri kazandırır; en fazla Max.</summary>
        public void Repair(float amount, DamageSource source = DamageSource.Aftershock)
        {
            if (IsCollapsed || amount <= 0f) return;

            float previous = Current;
            Current = Mathf.Min(maxIntegrity, Current + amount);
            if (!Mathf.Approximately(previous, Current))
                EventBus.Publish(new IntegrityChanged(Current, Current - previous, source));
        }

        /// <summary>Barın değerinden bağımsız anında çöküş (ör. temelin su baskınıyla kayması).</summary>
        public void ForceCollapse(DamageSource source)
        {
            if (IsCollapsed) return;
            ApplyDamage(Current, source);
        }

        public void SetDrain(Object owner, float perSecond, DamageSource source) =>
            drains[owner] = (perSecond, source);

        public void RemoveDrain(Object owner) => drains.Remove(owner);
 
        public void RestoreState(float savedCurrent, bool savedIsCollapsed)
        {
            Current = Mathf.Clamp(savedCurrent, 0f, maxIntegrity);
            IsCollapsed = savedIsCollapsed;
            EventBus.Publish(new IntegrityChanged(Current, 0f, DamageSource.Debug));
        }
    }
}
