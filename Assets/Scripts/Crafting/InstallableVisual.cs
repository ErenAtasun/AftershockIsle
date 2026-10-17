using AftershockIsle.Core;
using UnityEngine;

namespace AftershockIsle.Crafting
{
    /// <summary>Tarif üretilene kadar gizli kalan yerleşik nesne (ör. boru üzerindeki Bypass Vanası). Collider'ı da gizler.</summary>
    public class InstallableVisual : MonoBehaviour
    {
        [SerializeField] RecipeId recipe;

        public bool IsInstalled { get; private set; }

        void Start() => Refresh();

        void OnEnable() => EventBus.Subscribe<RecipeCrafted>(OnCrafted);
        void OnDisable() => EventBus.Unsubscribe<RecipeCrafted>(OnCrafted);

        void OnCrafted(RecipeCrafted evt)
        {
            if (evt.Id == recipe) Refresh();
        }

        public void Refresh()
        {
            // Grid ile yerleştirilen eşyalar (Pompa, Paratoner vb.) artık ShelterGrid üzerinden oyuncunun seçtiği hücreye kurulur.
            var info = RecipeCatalog.Get(recipe);
            if (info.IsPlaceable)
            {
                Apply(false);
                return;
            }
            Apply(Workbench.Has(recipe));
        }

        public static void RefreshAll()
        {
            foreach (var visual in FindObjectsByType<InstallableVisual>(FindObjectsSortMode.None))
                visual.Refresh();
        }

        void Apply(bool installed)
        {
            IsInstalled = installed;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled = installed;
            foreach (var collider in GetComponentsInChildren<Collider>(true)) collider.enabled = installed;
        }
    }
}
