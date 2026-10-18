using System.Collections.Generic;
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
        /// <summary>Hungry'nin içinden: akşam planında bilerek aç bırakılanlar.</summary>
        public readonly int Skipped;

        public UpkeepResolved(int fed, int hungry, int skipped = 0)
        {
            Fed = fed;
            Hungry = hungry;
            Skipped = skipped;
        }
    }

    /// <summary>
    /// Gün sonu tüketimi: gece fazı başlarken her yaşayan kazazede depodan 1 Yaşam Kiti yer.
    /// Yiyen iyileşir ve sakinleşir; yiyemeyen acıkır (Tükenmiş). En üst açlıkta bir gece daha aç kalan can kaybeder.
    /// Akşam planı varsa tüketim plan onaylanınca yapılır (kimin yiyeceği orada seçilir).
    /// </summary>
    public class UpkeepSystem : MonoBehaviour
    {
        [SerializeField] int kitsPerSurvivor = 1;
        [SerializeField] float healWhenFed = 20f;
        [SerializeField] float stressReliefWhenFed = 20f;
        [SerializeField] float stressWhenHungry = 30f;
        [SerializeField] float starvationDamage = 30f;

        /// <summary>True ise gece başındaki tüketimi akşam planı yapar (Resolve'u plan çağırır).</summary>
        public static bool HandledByPlan { get; set; }

        void OnEnable() => EventBus.Subscribe<DayPhaseChanged>(OnPhaseChanged);
        void OnDisable() => EventBus.Unsubscribe<DayPhaseChanged>(OnPhaseChanged);

        void OnPhaseChanged(DayPhaseChanged evt)
        {
            // Kayıt yüklenirken gelen faz olayı yeni bir gece değildir (aynı gece ikinci kez yenmesin).
            if (evt.Phase == DayPhase.Night && !HandledByPlan && !AftershockIsle.Save.SaveManager.IsApplying) Resolve();
        }

        /// <summary>skip: akşam planında bilerek aç bırakılanlar (yemezler, aç kalmış sayılırlar).</summary>
        public void Resolve(ICollection<Survivor> skip = null)
        {
            var cost = new ResourceAmount(ResourceType.Supplies, kitsPerSurvivor);
            int fed = 0;
            int hungry = 0;
            int skipped = 0;

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

                bool rationed = skip != null && skip.Contains(survivor);
                if (rationed) skipped++;
                bool chefSavedKit = hasChef && ResourceStorage.Instance.Has(cost) && Random.value < 0.35f;
                if (!rationed && (chefSavedKit || ResourceStorage.Instance.TrySpend(cost)))
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

            EventBus.Publish(new UpkeepResolved(fed, hungry, skipped));
        }
    }
}
