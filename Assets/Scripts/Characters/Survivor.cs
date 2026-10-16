using System.Collections.Generic;
using AftershockIsle.Economy;
using UnityEngine;

namespace AftershockIsle.Characters
{
    public readonly struct SurvivorSelected
    {
        /// <summary>Seçim kaldırıldıysa null.</summary>
        public readonly Survivor Survivor;
        public SurvivorSelected(Survivor survivor) => Survivor = survivor;
    }

    /// <summary>
    /// Sığınaktaki bir kazazede. Elinde tek slotta tek bir malzeme taşır; ağır yükte yavaşlar.
    /// İstatistikler (HP, stres, açlık) Aşama 4'te buraya eklenecek.
    /// </summary>
    [RequireComponent(typeof(CharacterMover))]
    public class Survivor : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly List<Survivor> all = new List<Survivor>();

        [SerializeField] string displayName = "Kazazede";
        [SerializeField] GameObject selectionIndicator;
        [SerializeField] Renderer carriedVisual;
        [SerializeField, Range(0.1f, 1f)] float heavyCarrySpeed = 0.75f;

        ResourceAmount carried;
        MaterialPropertyBlock block;

        public static IReadOnlyList<Survivor> All => all;

        public string DisplayName => displayName;
        public CharacterMover Mover { get; private set; }
        public TaskRunner Tasks { get; private set; }
        public bool IsCarrying { get; private set; }
        public ResourceAmount Carried => carried;

        void Awake()
        {
            Mover = GetComponent<CharacterMover>();
            Tasks = GetComponent<TaskRunner>();
            SetSelected(false);
            RefreshCarry();
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public void SetSelected(bool selected)
        {
            if (selectionIndicator != null) selectionIndicator.SetActive(selected);
        }

        public bool TryPickUp(ResourceAmount item)
        {
            if (IsCarrying) return false;

            carried = item;
            IsCarrying = true;
            RefreshCarry();
            return true;
        }

        public bool TryTakeCarried(out ResourceAmount item)
        {
            item = carried;
            if (!IsCarrying) return false;

            IsCarrying = false;
            RefreshCarry();
            return true;
        }

        void RefreshCarry()
        {
            Mover.SpeedMultiplier = IsCarrying && carried.type.IsHeavy() ? heavyCarrySpeed : 1f;

            if (carriedVisual == null) return;
            carriedVisual.gameObject.SetActive(IsCarrying);
            if (!IsCarrying) return;

            block ??= new MaterialPropertyBlock();
            carriedVisual.GetPropertyBlock(block);
            block.SetColor(BaseColorId, carried.type.ToColor());
            carriedVisual.SetPropertyBlock(block);
        }
    }
}
