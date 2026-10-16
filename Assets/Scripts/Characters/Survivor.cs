using UnityEngine;

namespace AftershockIsle.Characters
{
    public readonly struct SurvivorSelected
    {
        /// <summary>Seçim kaldırıldıysa null.</summary>
        public readonly Survivor Survivor;
        public SurvivorSelected(Survivor survivor) => Survivor = survivor;
    }

    /// <summary>Sığınaktaki bir kazazede. İstatistikler (HP, stres, açlık) sonraki aşamada buraya eklenecek.</summary>
    [RequireComponent(typeof(CharacterMover))]
    public class Survivor : MonoBehaviour
    {
        [SerializeField] string displayName = "Kazazede";
        [SerializeField] GameObject selectionIndicator;

        public string DisplayName => displayName;
        public CharacterMover Mover { get; private set; }

        void Awake()
        {
            Mover = GetComponent<CharacterMover>();
            SetSelected(false);
        }

        public void SetSelected(bool selected)
        {
            if (selectionIndicator != null) selectionIndicator.SetActive(selected);
        }
    }
}
