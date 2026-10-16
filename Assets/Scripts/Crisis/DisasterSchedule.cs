using System;
using AftershockIsle.Core;
using UnityEngine;

namespace AftershockIsle.Crisis
{
    /// <summary>Su baskını ve yangın sonraki adımlarda eklenecek.</summary>
    public enum DisasterType { Aftershock }

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

        // Varsayılan takvim: 1. gün öğretici (sadece çatlak kolon ve moloz), ilk deprem 2. gece.
        public Entry[] entries =
        {
            new Entry(2, DayPhase.Night, 10f, DisasterType.Aftershock),
            new Entry(4, DayPhase.Morning, 45f, DisasterType.Aftershock),
            new Entry(4, DayPhase.Night, 15f, DisasterType.Aftershock),
            new Entry(5, DayPhase.Night, 20f, DisasterType.Aftershock),
            new Entry(6, DayPhase.Morning, 30f, DisasterType.Aftershock),
            new Entry(6, DayPhase.Night, 10f, DisasterType.Aftershock),
            new Entry(7, DayPhase.Morning, 40f, DisasterType.Aftershock),
            new Entry(7, DayPhase.Night, 10f, DisasterType.Aftershock),
        };
    }
}
