using System.Collections.Generic;
using AftershockIsle.Core;
using UnityEngine;

namespace AftershockIsle.Crisis
{
    /// <summary>
    /// DisasterSchedule'daki afetleri gün döngüsüne göre zamanı gelince tetikler.
    /// Takvim bittikten sonra (varsa) sonsuz düzen devreye girer.
    /// </summary>
    public class DisasterDirector : MonoBehaviour
    {
        [SerializeField] DisasterSchedule schedule;
        [SerializeField] DayCycle dayCycle;

        readonly HashSet<int> fired = new HashSet<int>();
        readonly HashSet<int> firedEndless = new HashSet<int>();
        int endlessDay;
        DisasterSchedule.Entry[] endlessToday;

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
                if (!IsDue(schedule.entries[i])) continue;

                // Önceki afet sürüyorsa kaydı tüketme; bir sonraki karede tekrar dener.
                if (Fire(schedule.entries[i].type)) fired.Add(i);
            }

            UpdateEndless();
        }

        void UpdateEndless()
        {
            if (!schedule.continueAfterLastDay || dayCycle.Day <= schedule.LastScheduledDay) return;

            if (endlessDay != dayCycle.Day)
            {
                endlessDay = dayCycle.Day;
                endlessToday = DisasterSchedule.EndlessEntries(endlessDay);
                firedEndless.Clear();
            }

            for (int i = 0; i < endlessToday.Length; i++)
            {
                if (firedEndless.Contains(i) || !IsDue(endlessToday[i])) continue;
                if (Fire(endlessToday[i].type)) firedEndless.Add(i);
            }
        }

        bool IsDue(DisasterSchedule.Entry entry) =>
            entry.day == dayCycle.Day && entry.phase == dayCycle.Phase && dayCycle.PhaseElapsed >= entry.atSecond;

        public static bool Fire(DisasterType type) => type switch
        {
            DisasterType.Aftershock => AftershockSystem.Instance != null && AftershockSystem.Instance.Trigger(),
            // Boru zaten patlaksa kayıt tüketilir; yeniden denemenin anlamı yok.
            DisasterType.PipeBurst => FloodSystem.Instance == null || FloodSystem.Instance.TryBurstPipe() || FloodSystem.Instance.IsPipeBurst,
            // Yakılacak odak kalmadıysa (hepsi yanıyor/çökmüş) kayıt tüketilir.
            DisasterType.Fire => FireSpot.TryIgniteRandom() || true,
            // Kaçmayan vana kalmadıysa (hepsi zaten kaçırıyor) kayıt tüketilir.
            DisasterType.GasLeak => GasLeak.TryStartLeak() || true,
            _ => false,
        };

        public IEnumerable<int> GetFiredScheduleIndices() => fired;
        public IEnumerable<int> GetFiredEndlessIndices() => firedEndless;
        public int EndlessDay => endlessDay;

        public void RestoreState(IEnumerable<int> firedIndices, int savedEndlessDay, IEnumerable<int> firedEndlessIndices)
        {
            fired.Clear();
            if (firedIndices != null) foreach (var i in firedIndices) fired.Add(i);
            endlessDay = savedEndlessDay;
            firedEndless.Clear();
            if (firedEndlessIndices != null) foreach (var i in firedEndlessIndices) firedEndless.Add(i);
        }
    }
}
