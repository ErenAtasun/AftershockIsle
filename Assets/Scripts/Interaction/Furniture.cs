using System.Collections.Generic;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Economy;
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
    /// </summary>
    public class Furniture : Interactable
    {
        [SerializeField] ResourceAmount salvage = new ResourceAmount(ResourceType.Scrap, 2);
        [SerializeField] bool providesShelter = true;
        [SerializeField] float shelterHalfWidth = 0.9f;

        static readonly List<Furniture> all = new List<Furniture>();

        public static IReadOnlyList<Furniture> All => all;

        public bool ProvidesShelter => providesShelter;
        public float ShelterHalfWidth => shelterHalfWidth;

        public override string ActionLabel => "Parçala";
        public override bool RequiresEmptyHands => true;
        public override string StatusText => providesShelter
            ? $"Sığınak noktası. Parçalanırsa +{salvage}, sığınak kaybolur."
            : $"Parçalanırsa +{salvage}";

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public override void Complete(Survivor worker)
        {
            worker.TryPickUp(salvage);
            EventBus.Publish(new FurnitureDismantled(this));
            Destroy(gameObject);
        }
    }
}
