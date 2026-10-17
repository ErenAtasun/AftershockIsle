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
            var awayCompanion = ExpeditionSystem.AwayCompanion;
            if (awayCompanion != null) eaters.Add(awayCompanion);

            float medMultiplier = AftershockIsle.Crafting.PlaceableStructure.HasAny(AftershockIsle.Crafting.RecipeId.MedStation) ? 1.5f : 1f;
            float comfortMultiplier = AftershockIsle.Crafting.PlaceableStructure.HasAny(AftershockIsle.Crafting.RecipeId.ComfortCorner) ? 1.4f : 1f;

            bool hasChef = false;
            foreach (var s in eaters)
                if (s != null && !s.IsDead && s.HasTrait(TraitId.Chef)) hasChef = true;

            foreach (var survivor in eaters)
            {
                if (survivor.IsDead) continue;

                bool chefSavedKit = hasChef && ResourceStorage.Instance.Has(cost) && Random.value < 0.35f;
                if (chefSavedKit || ResourceStorage.Instance.TrySpend(cost))
                {
                    survivor.Heal(healWhenFed * medMultiplier);
                    float extraChefRelief = hasChef ? 5f : 0f;
                    survivor.Condition.Feed(stressReliefWhenFed * comfortMultiplier + extraChefRelief);
                    fed++;
                    continue;
                }

                hungry++;
                float appetiteMult = survivor.HasTrait(TraitId.BigAppetite) ? 1.4f : 1f;
                if (survivor.Condition.Starve(stressWhenHungry * appetiteMult))
                    survivor.TakeDamage(starvationDamage * appetiteMult, "Açlıktan bitkin düştü.");
            }

            EventBus.Publish(new UpkeepResolved(fed, hungry));
        }
    }
}
