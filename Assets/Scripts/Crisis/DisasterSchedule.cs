using System;
using AftershockIsle.Core;
using UnityEngine;

namespace AftershockIsle.Crisis
{
    public enum DisasterType { Aftershock, PipeBurst, Fire }

    /// <summary>Hangi gün, hangi fazın kaçıncı saniyesinde hangi afetin geleceği. Zorluk eğrisi buradan ayarlanır.</summary>
    [CreateAssetMenu(menuName = "Aftershock/Disaster Schedule", fileName = "DisasterSchedule")]
    public class DisasterSchedule : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Min(1)] public int day;
            public DayPhase phase;
            [Tooltip("Faz başladıktan kaç saniye sonra")]
            [Min(0f)] public float atSecond;
            public DisasterType type;

            public Entry(int day, DayPhase phase, float atSecond, DisasterType type)
            {
                this.day = day;
                this.phase = phase;
                this.atSecond = atSecond;
                this.type = type;
            }
        }

        [Tooltip("Takvimin son gününden sonra afetler tekrar eden bir düzenle devam eder")]
        public bool continueAfterLastDay = true;

        public int LastScheduledDay
        {
            get
            {
                int last = 0;
                foreach (var entry in entries) last = Mathf.Max(last, entry.day);
                return last;
            }
        }

        /// <summary>
        /// Takvim bittikten sonraki günler için sonsuz düzen: her gün sabah ve gece birer sarsıntı;
        /// çift günlerde boru patlaması, tek günlerde yangın.
        /// </summary>
        public static Entry[] EndlessEntries(int day) => new[]
        {
            new Entry(day, DayPhase.Morning, 35f, DisasterType.Aftershock),
            new Entry(day, DayPhase.Morning, day % 2 == 0 ? 10f : 15f, day % 2 == 0 ? DisasterType.PipeBurst : DisasterType.Fire),
            new Entry(day, DayPhase.Night, 10f, DisasterType.Aftershock),
        };

        // Varsayılan takvim: 1. gün öğretici (sadece çatlak kolon ve moloz), ilk deprem 2. gece,
        // ilk boru patlaması 3. sabah, ilk yangın 5. sabah, 6. gün su ve deprem birlikte.
        public Entry[] entries =
        {
            new Entry(2, DayPhase.Night, 10f, DisasterType.Aftershock),
            new Entry(3, DayPhase.Morning, 20f, DisasterType.PipeBurst),
            new Entry(4, DayPhase.Morning, 45f, DisasterType.Aftershock),
            new Entry(4, DayPhase.Night, 15f, DisasterType.Aftershock),
            new Entry(5, DayPhase.Morning, 30f, DisasterType.Fire),
            new Entry(5, DayPhase.Night, 20f, DisasterType.Aftershock),
            new Entry(6, DayPhase.Morning, 10f, DisasterType.PipeBurst),
            new Entry(6, DayPhase.Morning, 30f, DisasterType.Aftershock),
            new Entry(6, DayPhase.Night, 10f, DisasterType.Aftershock),
            new Entry(7, DayPhase.Morning, 40f, DisasterType.Aftershock),
            new Entry(7, DayPhase.Night, 10f, DisasterType.Aftershock),
        };
    }
}
