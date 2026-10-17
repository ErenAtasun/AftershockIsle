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
        [Tooltip("Doluysa kapsül yerine iskeletli karakter modeli kullanılır (sahne kurucu atar).")]
        [SerializeField] CharacterLibrary characters;
        [SerializeField, Range(0.1f, 1f)] float heavyCarrySpeed = 0.75f;
        [SerializeField] float maxHealth = 100f;

        ResourceAmount carried;
        readonly List<ResourceAmount> bag = new List<ResourceAmount>();
        readonly List<TraitId> traits = new List<TraitId>();
        MaterialPropertyBlock block;
        CharacterView view;
        float trappedRemaining;

        const float ModelHeight = 1.75f;

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
        public IReadOnlyList<TraitId> Traits => traits;
        public bool HasTrait(TraitId id) => traits.Contains(id);
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

        public string TraitsSummary()
        {
            if (traits.Count == 0) return "Özellik Yok";
            var parts = new string[traits.Count];
            for (int i = 0; i < traits.Count; i++)
            {
                var info = TraitCatalog.Get(traits[i]);
                parts[i] = info.IsPositive ? $"+{info.Name}" : $"-{info.Name}";
            }
            return string.Join(", ", parts);
        }

        public void SetTraits(IEnumerable<TraitId> newTraits)
        {
            traits.Clear();
            if (newTraits != null) traits.AddRange(newTraits);
            maxHealth = HasTrait(TraitId.Frail) ? 85f : 100f;
            Health = Mathf.Min(Health, maxHealth);
        }

        void Awake()
        {
            Mover = GetComponent<CharacterMover>();
            Tasks = GetComponent<TaskRunner>();
            Condition = GetComponent<SurvivorCondition>();
            if (traits.Count == 0)
            {
                int seed = (displayName != null ? displayName.GetHashCode() : 0) ^ (int)branch * 397;
                TraitCatalog.RollBalancedTraits(traits, seed);
            }
            maxHealth = HasTrait(TraitId.Frail) ? 85f : 100f;
            Health = maxHealth;
            SetSelected(false);
            RefreshCarry();
            AttachModel();
            RefreshBodyColor();
        }

        // Kapsül ve vizör gizlenir; oyun mantığı (collider, yön) kökte kalır, model onu izler.
        void AttachModel()
        {
            // Önce otomatik güncellenen kayıt (yeni model eklenince sahne kurulmadan gelir), yoksa sahnedeki referans.
            var library = GameAssets.Characters != null ? GameAssets.Characters : characters;
            if (library == null) return;

            view = CharacterView.Attach(library, transform, transform, ModelHeight, transform.position);
            if (view == null) return;

            if (body != null) body.enabled = false;
            var visor = transform.Find("Visor");
            if (visor != null && visor.TryGetComponent<Renderer>(out var visorRenderer)) visorRenderer.enabled = false;
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        /// <summary>Çalışma anında eklenen (kurtarılan) kazazedeyi kurar.</summary>
        public void Initialize(string newName, SkillBranch newBranch)
        {
            displayName = newName;
            branch = newBranch;
            int seed = (displayName != null ? displayName.GetHashCode() : 0) ^ (int)branch * 7919 ^ all.Count * 97;
            TraitCatalog.RollBalancedTraits(traits, seed);
            maxHealth = HasTrait(TraitId.Frail) ? 85f : 100f;
            Health = maxHealth;
            RefreshBodyColor();
        }

        void Update()
        {
            if (IsDead) return;

            // Hız çarpanları: yük × tükenmişlik × yetenek × özellikler.
            bool sprinter = HasTrait(TraitId.Sprinter);
            float heavyFactor = sprinter ? Mathf.Max(heavyCarrySpeed, 0.88f) : heavyCarrySpeed;
            float carry = IsCarrying && carried.type.IsHeavy() ? heavyFactor : 1f;
            float traitMoveBonus = sprinter ? 1.2f : 1f;
            Mover.SpeedMultiplier = carry * Condition.SpeedMultiplier * SkillTree.MoveSpeedBonus * traitMoveBonus;

            float pyrophobicFactor = 1f;
            if (HasTrait(TraitId.Pyrophobic))
            {
                var fire = AftershockIsle.Crisis.FireSpot.ForFloor(Mover.CurrentFloor);
                if (fire != null && fire.IsBurning) pyrophobicFactor = 0.8f;
            }
            Tasks.WorkSpeedMultiplier = Condition.SpeedMultiplier * pyrophobicFactor;

            if (!IsTrapped) return;
            trappedRemaining = Mathf.Max(0f, trappedRemaining - Time.deltaTime);
            if (!IsTrapped) Mover.Frozen = false;
        }

        public void TakeDamage(float amount, string cause)
        {
            if (IsDead || amount <= 0f) return;

            bool onExpedition = AftershockIsle.Expedition.ExpeditionSystem.AwayExplorer == this ||
                                AftershockIsle.Expedition.ExpeditionSystem.AwayCompanion == this;
            if (onExpedition && AftershockIsle.Crafting.Workbench.Has(AftershockIsle.Crafting.RecipeId.ArmorVest))
                amount *= 0.65f;

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

            if (HasTrait(TraitId.Miner)) duration *= 0.5f;
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

        /// <summary>Dış seferden dönerken taşınan çanta. Eldeki tek slottan ayrıdır; sandıkta tek hamlede boşalır.</summary>
        public IReadOnlyList<ResourceAmount> ExpeditionBag => bag;
        public bool HasBag => bag.Count > 0;

        public void FillBag(IEnumerable<ResourceAmount> items)
        {
            bag.Clear();
            bag.AddRange(items);
        }

        public List<ResourceAmount> EmptyBag()
        {
            var items = new List<ResourceAmount>(bag);
            bag.Clear();
            return items;
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

            var color = branch switch
            {
                SkillBranch.Construction => new Color(0.95f, 0.55f, 0.2f),
                SkillBranch.Exploration => new Color(0.35f, 0.8f, 0.35f),
                _ => new Color(0.3f, 0.6f, 0.95f),
            };
            SetColor(body, color);
            if (view != null) view.SetTint(color);
        }

        void SetColor(Renderer target, Color color)
        {
            block ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            target.SetPropertyBlock(block);
        }

        public IReadOnlyList<ResourceAmount> Bag => bag;

        public void RestoreState(float health, bool isDead, ResourceAmount carriedItem, bool isCarrying, float trapped, List<ResourceAmount> bagItems, Vector3 position)
        {
            Health = health;
            IsDead = isDead;
            carried = carriedItem;
            IsCarrying = isCarrying;
            trappedRemaining = trapped;
            bag.Clear();
            if (bagItems != null) bag.AddRange(bagItems);
            RefreshCarry();

            transform.position = position;
            if (Mover != null)
            {
                Mover.Halt();
                Mover.UpdateCurrentFloor();
            }

            if (IsDead)
            {
                trappedRemaining = 0f;
                if (Tasks != null) Tasks.CancelAll();
                if (Mover != null)
                {
                    Mover.Halt();
                    Mover.Frozen = true;
                }
                SetSelected(false);
                transform.SetPositionAndRotation(transform.position + Vector3.up * 0.3f,
                    Quaternion.Euler(0f, transform.eulerAngles.y, 90f));
            }
        }
    }
}
