using System.Collections.Generic;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Economy;
using AftershockIsle.Skills;
using UnityEngine;

namespace AftershockIsle.Interaction
{
    public readonly struct FurnitureDismantled
    {
        public readonly Furniture Furniture;
        public FurnitureDismantled(Furniture furniture) => Furniture = furniture;
    }

    /// <summary>
    /// "Koru mu, parçala mı?" ikilemi: sağlam mobilya sarsıntıda Yaşam Üçgeni sığınağıdır;
    /// parçalanırsa malzeme verir ama o sığınak kalıcı olarak kaybolur.
    /// İçinde eşya olan mobilya (dolap) önce bir kez aranır, sonra istenirse parçalanır.
    /// </summary>
    public class Furniture : Interactable
    {
        [SerializeField] ResourceAmount salvage = new ResourceAmount(ResourceType.Scrap, 2);
        [SerializeField] bool providesShelter = true;
        [SerializeField] float shelterHalfWidth = 0.9f;

        [Header("Aranabilir içerik")]
        [SerializeField] bool hasSearchLoot;
        [SerializeField] ResourceAmount searchLoot = new ResourceAmount(ResourceType.Supplies, 1);
        [SerializeField, Min(0.1f)] float searchDuration = 3f;

        static readonly List<Furniture> all = new List<Furniture>();

        bool searched;

        public static IReadOnlyList<Furniture> All => all;

        public bool ProvidesShelter => providesShelter;
        public float ShelterHalfWidth => shelterHalfWidth;
        bool WillSearch => hasSearchLoot && !searched;

        public override string ActionLabel => WillSearch ? "Ara" : "Parçala";
        public override WorkCategory Category => WillSearch ? WorkCategory.Search : WorkCategory.Dismantle;
        public override float WorkDuration => WillSearch ? searchDuration : base.WorkDuration;
        public override bool RequiresEmptyHands => true;

        public override string StatusText
        {
            get
            {
                if (WillSearch) return "İçinde işe yarar bir şey olabilir.";
                string loot = $"+{SalvageWithSkills()}";
                return providesShelter ? $"Sığınak noktası. Parçalanırsa {loot}, sığınak kaybolur." : $"Parçalanırsa {loot}";
            }
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public override void Complete(Survivor worker)
        {
            if (WillSearch)
            {
                searched = true;
                worker.TryPickUp(searchLoot);
                return;
            }

            worker.TryPickUp(SalvageWithSkills());
            EventBus.Publish(new FurnitureDismantled(this));
            Destroy(gameObject);
        }

        ResourceAmount SalvageWithSkills()
        {
            var result = salvage;
            if (result.type == ResourceType.Scrap) result.amount = SkillTree.ApplyRecycling(result.amount);
            return result;
        }
    }
}
