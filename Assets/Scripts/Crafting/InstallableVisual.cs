using AftershockIsle.Core;
using UnityEngine;

namespace AftershockIsle.Crafting
{
    /// <summary>Tarif üretilene kadar gizli kalan yerleşik nesne (pompa, vana, paratoner). Collider'ı da gizler.</summary>
    public class InstallableVisual : MonoBehaviour
    {
        [SerializeField] RecipeId recipe;

        public bool IsInstalled { get; private set; }

        void Start() => Apply(Workbench.Has(recipe));

        void OnEnable() => EventBus.Subscribe<RecipeCrafted>(OnCrafted);
        void OnDisable() => EventBus.Unsubscribe<RecipeCrafted>(OnCrafted);

        void OnCrafted(RecipeCrafted evt)
        {
            if (evt.Id == recipe) Apply(true);
        }

        void Apply(bool installed)
        {
            IsInstalled = installed;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled = installed;
            foreach (var collider in GetComponentsInChildren<Collider>(true)) collider.enabled = installed;
        }
    }
}
