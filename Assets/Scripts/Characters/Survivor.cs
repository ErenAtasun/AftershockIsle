using System.Collections.Generic;
using AftershockIsle.Core;
using AftershockIsle.Economy;
using AftershockIsle.Skills;
using UnityEngine;

namespace AftershockIsle.Characters
{
    public readonly struct SurvivorSelected
    {
        /// <summary>Seçim kaldırıldıysa null.</summary>
        public readonly Survivor Survivor;
        public SurvivorSelected(Survivor survivor) => Survivor = survivor;
    }

    public readonly struct SurvivorDied
    {
        public readonly Survivor Survivor;
        public readonly string Cause;

        public SurvivorDied(Survivor survivor, string cause)
        {
            Survivor = survivor;
            Cause = cause;
        }
    }

    /// <summary>
    /// Sığınaktaki bir kazazede. Elinde tek slotta tek bir malzeme taşır; ağır yükte yavaşlar.
    /// Can sıfırlanınca kalıcı olarak ölür. Her kazazede bir yetenek dalının uzmanıdır;
    /// stres ve açlık SurvivorCondition'dadır.
    /// </summary>
    [RequireComponent(typeof(CharacterMover))]
    public class Survivor : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly List<Survivor> all = new List<Survivor>();

        [SerializeField] string displayName = "Kazazede";
        [SerializeField] SkillBranch branch = SkillBranch.Construction;
        [SerializeField] GameObject selectionIndicator;
        [SerializeField] Renderer body;
        [SerializeField] Renderer carriedVisual;
        [SerializeField, Range(0.1f, 1f)] float heavyCarrySpeed = 0.75f;
        [SerializeField] float maxHealth = 100f;

        ResourceAmount carried;
        MaterialPropertyBlock block;
        float trappedRemaining;

        /// <summary>Ölüler de dahil sahnedeki tüm kazazedeler.</summary>
        public static IReadOnlyList<Survivor> All => all;

        public static int AliveCount
        {
            get
            {
                int count = 0;
                foreach (var survivor in all)
                    if (!survivor.IsDead) count++;
                return count;
            }
        }

        public string DisplayName => displayName;
        public SkillBranch Branch => branch;
        public CharacterMover Mover { get; private set; }
        public TaskRunner Tasks { get; private set; }
        public SurvivorCondition Condition { get; private set; }
        public bool IsCarrying { get; private set; }
        public ResourceAmount Carried => carried;

        public float Health { get; private set; }
        public float MaxHealth => maxHealth;
        public bool IsDead { get; private set; }
        public bool IsTrapped => trappedRemaining > 0f;
        public float TrappedRemaining => trappedRemaining;
        public bool CanAct => !IsDead && !IsTrapped && !Condition.IsPanicking;

        void Awake()
        {
            Mover = GetComponent<CharacterMover>();
            Tasks = GetComponent<TaskRunner>();
            Condition = GetComponent<SurvivorCondition>();
            Health = maxHealth;
            SetSelected(false);
            RefreshCarry();
            RefreshBodyColor();
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        /// <summary>Çalışma anında eklenen (kurtarılan) kazazedeyi kurar.</summary>
        public void Initialize(string newName, SkillBranch newBranch)
        {
            displayName = newName;
            branch = newBranch;
            RefreshBodyColor();
        }

        void Update()
        {
            if (IsDead) return;

            // Hız çarpanları: yük × tükenmişlik × yetenek. Her kare hesaplamak durum takibinden ucuz.
            float carry = IsCarrying && carried.type.IsHeavy() ? heavyCarrySpeed : 1f;
            Mover.SpeedMultiplier = carry * Condition.SpeedMultiplier * SkillTree.MoveSpeedBonus;
            Tasks.WorkSpeedMultiplier = Condition.SpeedMultiplier;

            if (!IsTrapped) return;
            trappedRemaining = Mathf.Max(0f, trappedRemaining - Time.deltaTime);
            if (!IsTrapped) Mover.Frozen = false;
        }

        public void TakeDamage(float amount, string cause)
        {
            if (IsDead || amount <= 0f) return;

            Health = Mathf.Max(0f, Health - amount);
            if (Health <= 0f) Die(cause);
        }

        public void Heal(float amount)
        {
            if (!IsDead) Health = Mathf.Min(maxHealth, Health + amount);
        }

        /// <summary>Enkaz altında kalır: süre dolana kadar hareket edemez, işi yarıda bekler.</summary>
        public void Trap(float duration)
        {
            if (IsDead) return;

            trappedRemaining = Mathf.Max(trappedRemaining, duration);
            Mover.Halt();
            Mover.Frozen = true;
        }

        void Die(string cause)
        {
            IsDead = true;
            trappedRemaining = 0f;
            Tasks.CancelAll();
            Mover.Halt();
            Mover.Frozen = true;
            SetSelected(false);

            // Greybox: yere yatır.
            transform.SetPositionAndRotation(transform.position + Vector3.up * 0.3f,
                Quaternion.Euler(0f, transform.eulerAngles.y, 90f));

            EventBus.Publish(new SurvivorDied(this, cause));
        }

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
            if (carriedVisual == null) return;
            carriedVisual.gameObject.SetActive(IsCarrying);
            if (IsCarrying) SetColor(carriedVisual, carried.type.ToColor());
        }

        // Greybox: uzmanlık dalı gövde renginden okunur.
        void RefreshBodyColor()
        {
            if (body == null) return;
            SetColor(body, branch switch
            {
                SkillBranch.Construction => new Color(0.95f, 0.55f, 0.2f),
                SkillBranch.Exploration => new Color(0.35f, 0.8f, 0.35f),
                _ => new Color(0.3f, 0.6f, 0.95f),
            });
        }

        void SetColor(Renderer target, Color color)
        {
            block ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            target.SetPropertyBlock(block);
        }
    }
}
