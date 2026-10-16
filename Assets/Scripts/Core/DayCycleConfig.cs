using UnityEngine;

namespace AftershockIsle.Core
{
    [CreateAssetMenu(menuName = "Aftershock/Day Cycle Config", fileName = "DayCycleConfig")]
    public class DayCycleConfig : ScriptableObject
    {
        [Tooltip("Sabah: iç bakım ve üretim (sn)")]
        [Min(1f)] public float morningDuration = 90f;

        [Tooltip("Öğle: dış sefer / keşif (sn)")]
        [Min(1f)] public float noonDuration = 60f;

        [Tooltip("Gece: afet baskısı ve tüketim (sn)")]
        [Min(1f)] public float nightDuration = 45f;

        public float GetDuration(DayPhase phase) => phase switch
        {
            DayPhase.Morning => morningDuration,
            DayPhase.Noon => noonDuration,
            _ => nightDuration,
        };
    }
}
