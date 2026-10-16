using System.Collections.Generic;
using AftershockIsle.Core;
using UnityEngine;

namespace AftershockIsle.Crisis
{
    /// <summary>DisasterSchedule'daki afetleri gün döngüsüne göre zamanı gelince tetikler.</summary>
    public class DisasterDirector : MonoBehaviour
    {
        [SerializeField] DisasterSchedule schedule;
        [SerializeField] DayCycle dayCycle;

        readonly HashSet<int> fired = new HashSet<int>();

        void Awake()
        {
            if (dayCycle == null) dayCycle = FindFirstObjectByType<DayCycle>();
            if (schedule == null) schedule = ScriptableObject.CreateInstance<DisasterSchedule>();
        }

        void Update()
        {
            for (int i = 0; i < schedule.entries.Length; i++)
            {
                if (fired.Contains(i)) continue;

                var entry = schedule.entries[i];
                if (entry.day != dayCycle.Day || entry.phase != dayCycle.Phase || dayCycle.PhaseElapsed < entry.atSecond)
                    continue;

                // Önceki afet sürüyorsa kaydı tüketme; bir sonraki karede tekrar dener.
                if (Fire(entry.type)) fired.Add(i);
            }
        }

        public static bool Fire(DisasterType type) => type switch
        {
            DisasterType.Aftershock => AftershockSystem.Instance != null && AftershockSystem.Instance.Trigger(),
            _ => false,
        };
    }
}
