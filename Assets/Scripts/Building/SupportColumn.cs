using System.Collections.Generic;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Economy;
using AftershockIsle.Interaction;
using AftershockIsle.Skills;
using UnityEngine;

namespace AftershockIsle.Building
{
    public enum ColumnState { Intact, Cracked, Supported, Reinforced, Broken }

    public static class ColumnStateNames
    {
        public static string ToDisplayName(this ColumnState state) => state switch
        {
            ColumnState.Intact => "Sağlam",
            ColumnState.Cracked => "Çatlak",
            ColumnState.Supported => "Ahşap destekli",
            ColumnState.Reinforced => "Çelik güçlendirilmiş",
            _ => "Kırık",
        };
    }

    public readonly struct ColumnStateChanged
    {
        public readonly SupportColumn Column;
        public readonly ColumnState Previous;

        public ColumnStateChanged(SupportColumn column, ColumnState previous)
        {
            Column = column;
            Previous = previous;
        }
    }

    /// <summary>
    /// Katın tavanını taşıyan kolon. Çatlak/kırık kolon sarsıntı hasarını büyütür (Aşama 3);
    /// Kalas harcanarak ahşap destek dikilir. Çelik güçlendirme tezgah Kademe 3 ile gelecek.
    /// </summary>
    public class SupportColumn : Interactable
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly List<SupportColumn> all = new List<SupportColumn>();

        [SerializeField] ColumnState state = ColumnState.Intact;
        [SerializeField] ResourceAmount supportCost = new ResourceAmount(ResourceType.Scrap, 2);
        [SerializeField] ResourceAmount rebuildCost = new ResourceAmount(ResourceType.Scrap, 4);
        [SerializeField] Renderer body;

        MaterialPropertyBlock block;
        int supportHitsRemaining;

        public static IReadOnlyList<SupportColumn> All => all;

        public ColumnState State => state;
        public bool NeedsRepair => state == ColumnState.Cracked || state == ColumnState.Broken;
        ResourceAmount RepairCost => state == ColumnState.Broken ? rebuildCost : supportCost;

        public override string ActionLabel => state == ColumnState.Broken ? "Yeniden Dik" : "Destekle";
        public override WorkCategory Category => WorkCategory.Repair;
        public override string StatusText => NeedsRepair
            ? $"Durum: {state.ToDisplayName()}  — {ActionLabel.ToLowerInvariant()}: {RepairCost}"
            : state == ColumnState.Supported
                ? $"Durum: {state.ToDisplayName()} ({supportHitsRemaining} sarsıntı daha dayanır)"
                : $"Durum: {state.ToDisplayName()}";

        protected override void Awake()
        {
            base.Awake();
            if (body == null) body = GetComponent<Renderer>();
            ApplyVisual();
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        // Editörde başlangıç durumunu renkle görebilmek için.
        void OnValidate()
        {
            if (body == null) body = GetComponent<Renderer>();
            ApplyVisual();
        }

        public override bool CanStart(Survivor worker, out string reason)
        {
            if (!NeedsRepair)
            {
                reason = "Kolonun desteğe ihtiyacı yok.";
                return false;
            }

            if (!ResourceStorage.Instance.Has(RepairCost))
            {
                reason = $"Depoda yeterli malzeme yok: {RepairCost} gerekli.";
                return false;
            }

            reason = null;
            return true;
        }

        public override bool CanContinue(Survivor worker) => NeedsRepair;

        public override void Complete(Survivor worker)
        {
            // Çalışma sırasında malzeme başka işe harcanmış olabilir.
            if (!ResourceStorage.Instance.TrySpend(RepairCost)) return;
            supportHitsRemaining = SkillTree.SupportDurability;
            SetState(ColumnState.Supported);
        }

        /// <summary>
        /// Sarsıntının bu kolona etkisi: çatlak kırılır, ahşap destek bir darbe emer (dayanıklılığı bitince
        /// çatlağa döner), sağlam kolon ihtimalle çatlar, çelik etkilenmez. Bu sarsıntıda kırıldıysa true.
        /// </summary>
        public bool ApplyShock(float intactCrackChance)
        {
            switch (state)
            {
                case ColumnState.Cracked:
                    SetState(ColumnState.Broken);
                    return true;
                case ColumnState.Supported:
                    supportHitsRemaining--;
                    if (supportHitsRemaining <= 0) SetState(ColumnState.Cracked);
                    return false;
                case ColumnState.Intact:
                    if (Random.value < intactCrackChance) SetState(ColumnState.Cracked);
                    return false;
                default:
                    return false;
            }
        }

        public void SetState(ColumnState newState)
        {
            if (newState == state) return;

            var previous = state;
            state = newState;
            ApplyVisual();
            EventBus.Publish(new ColumnStateChanged(this, previous));
        }

        void ApplyVisual()
        {
            if (body == null) return;

            block ??= new MaterialPropertyBlock();
            body.GetPropertyBlock(block);
            block.SetColor(BaseColorId, state switch
            {
                ColumnState.Intact => new Color(0.62f, 0.62f, 0.6f),
                ColumnState.Cracked => new Color(0.85f, 0.55f, 0.2f),
                ColumnState.Supported => new Color(0.55f, 0.38f, 0.2f),
                ColumnState.Reinforced => new Color(0.4f, 0.55f, 0.75f),
                _ => new Color(0.5f, 0.12f, 0.1f),
            });
            body.SetPropertyBlock(block);
        }
    }
}
