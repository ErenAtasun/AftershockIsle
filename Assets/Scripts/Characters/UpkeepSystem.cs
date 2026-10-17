using AftershockIsle.Core;
using AftershockIsle.Economy;
using AftershockIsle.Expedition;
using UnityEngine;

namespace AftershockIsle.Characters
{
    public readonly struct UpkeepResolved
    {
        public readonly int Fed;
        public readonly int Hungry;

        public UpkeepResolved(int fed, int hungry)
        {
            Fed = fed;
            Hungry = hungry;
        }
    }

    /// <summary>
    /// Gün sonu tüketimi: gece fazı başlarken her yaşayan kazazede depodan 1 Yaşam Kiti yer.
    /// Yiyen iyileşir ve sakinleşir; yiyemeyen acıkır (Tükenmiş). En üst açlıkta bir gece daha aç kalan can kaybeder.
    /// </summary>
    public class UpkeepSystem : MonoBehaviour
    {
        [SerializeField] int kitsPerSurvivor = 1;
        [SerializeField] float healWhenFed = 20f;
        [SerializeField] float stressReliefWhenFed = 20f;
        [SerializeField] float stressWhenHungry = 30f;
        [SerializeField] float starvationDamage = 30f;

        void OnEnable() => EventBus.Subscribe<DayPhaseChanged>(OnPhaseChanged);
        void OnDisable() => EventBus.Unsubscribe<DayPhaseChanged>(OnPhaseChanged);

        void OnPhaseChanged(DayPhaseChanged evt)
        {
            if (evt.Phase == DayPhase.Night) Resolve();
        }

        public void Resolve()
        {
            var cost = new ResourceAmount(ResourceType.Supplies, kitsPerSurvivor);
            int fed = 0;
            int hungry = 0;

            // Liste kopyası: açlıktan ölüm SurvivorDied olayını tetikleyip listeyi değiştirebilir.
            // Seferdeki kâşif sahnede pasiftir (Survivor.All'da yok) ama sığınağın stoğundan yemeye devam eder.
            var eaters = new System.Collections.Generic.List<Survivor>(Survivor.All);
            var away = ExpeditionSystem.AwayExplorer;
            if (away != null) eaters.Add(away);

            foreach (var survivor in eaters)
            {
                if (survivor.IsDead) continue;

                if (ResourceStorage.Instance.TrySpend(cost))
                {
                    survivor.Heal(healWhenFed);
                    survivor.Condition.Feed(stressReliefWhenFed);
                    fed++;
                    continue;
                }

                hungry++;
                if (survivor.Condition.Starve(stressWhenHungry))
                    survivor.TakeDamage(starvationDamage, "Açlıktan bitkin düştü.");
            }

            EventBus.Publish(new UpkeepResolved(fed, hungry));
        }
    }
}
