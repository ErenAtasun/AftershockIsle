using System.Collections.Generic;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Economy;
using AftershockIsle.Interaction;
using AftershockIsle.Skills;
using UnityEngine;

namespace AftershockIsle.Building
{
    /// <summary>
    /// Kolonun üç seviyesi: Lv1 Kırık → Lv2 Çatlak → Lv3 Çelik güçlendirilmiş.
    /// Sayısal değerler eski kayıtlar ve sahnelerle uyum için korunur (0 = eski Sağlam, 2 = eski Ahşap destekli;
    /// ikisi de yüklenirken Çatlak'a çevrilir).
    /// </summary>
    public enum ColumnState { Cracked = 1, Reinforced = 3, Broken = 4 }

    public static class ColumnStateNames
    {
        public static string ToDisplayName(this ColumnState state) => state switch
        {
            ColumnState.Cracked => "Çatlak",
            ColumnState.Reinforced => "Çelik güçlendirilmiş",
            _ => "Kırık",
        };

        /// <summary>1 = Kırık, 2 = Çatlak, 3 = Güçlendirilmiş.</summary>
        public static int ToLevel(this ColumnState state) => state switch
        {
            ColumnState.Reinforced => 3,
            ColumnState.Cracked => 2,
            _ => 1,
        };

        /// <summary>Eski 5 durumlu değerleri (Sağlam, Ahşap destekli) yeni üç seviyeye çevirir.</summary>
        public static ColumnState Normalize(ColumnState state) =>
            System.Enum.IsDefined(typeof(ColumnState), state) ? state : ColumnState.Cracked;
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

        public bool LeveledUp => Column.Level > Previous.ToLevel();
    }

    /// <summary>
    /// Katın tavanını taşıyan kolon. Onarım seviyeyi birer birer yükseltir: kırık kolon malzemeyle yeniden dikilir
    /// (Lv1→Lv2), çatlak kolon tezgahın çelik kirişiyle güçlendirilir (Lv2→Lv3). Sarsıntıda çatlak kolon kırılır,
    /// güçlendirilmiş kolon etkilenmez. Görseli ModelManager'daki seviye modelleri, yoksa renk verir.
    /// </summary>
    public class SupportColumn : Interactable
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly List<SupportColumn> all = new List<SupportColumn>();

        [SerializeField] ColumnState state = ColumnState.Cracked;
        [SerializeField] ResourceAmount rebuildCost = new ResourceAmount(ResourceType.Scrap, 4);
        [SerializeField] float reinforceDuration = 6f;
        [Tooltip("Kırık kolonu yeniden dikince bina bütünlüğüne eklenen puan")]
        [SerializeField] float rebuildIntegrity = 10f;
        [Tooltip("Çatlak kolonu çelikle güçlendirince bina bütünlüğüne eklenen puan")]
        [SerializeField] float reinforceIntegrity = 6f;
        [SerializeField] Renderer body;

        MaterialPropertyBlock block;
        int supportHitsRemaining;

        public static IReadOnlyList<SupportColumn> All => all;

        public ColumnState State => state;
        public int Level => state.ToLevel();
        /// <summary>Çatlak kolonun kırılmadan önce kaç sarsıntı daha atlatacağı (Çelik Tahkimat yeteneğiyle 2).</summary>
        public int SupportHitsRemaining => supportHitsRemaining;
        public bool NeedsRepair => state == ColumnState.Broken;

        bool WillReinforce => state == ColumnState.Cracked &&
                              Workbench.Instance != null && Workbench.Instance.SteelBeams > 0;

        public override string ActionLabel => state == ColumnState.Broken ? "Yeniden Dik" : "Çelikle Güçlendir";
        public override WorkCategory Category => WorkCategory.Repair;
        public override float WorkDuration => WillReinforce ? reinforceDuration : base.WorkDuration;
        public override string StatusText
        {
            get
            {
                string text = $"Durum: Lv{Level} {state.ToDisplayName()}";
                if (state == ColumnState.Cracked && supportHitsRemaining > 1)
                    text += $" ({supportHitsRemaining} sarsıntı daha dayanır)";

                if (NeedsRepair) return $"{text}  — yeniden dik: {rebuildCost}";
                if (WillReinforce) return $"{text}  — çelikle güçlendir: 1 çelik kiriş (stok {Workbench.Instance.SteelBeams})";
                return text;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            if (body == null) body = GetComponent<Renderer>();
            state = ColumnStateNames.Normalize(state);
            if (state == ColumnState.Cracked && supportHitsRemaining <= 0) supportHitsRemaining = SkillTree.SupportDurability;
            ApplyVisual();
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        // Editörde başlangıç durumunu renkle görebilmek için. Model değişimi burada yapılamaz (OnValidate'te obje yaratılmaz).
        void OnValidate()
        {
            if (body == null) body = GetComponent<Renderer>();
            ApplyTint();
        }

        public override bool CanStart(Survivor worker, out string reason)
        {
            switch (state)
            {
                case ColumnState.Reinforced:
                    reason = "Kolon zaten çelikle güçlendirilmiş.";
                    return false;
                case ColumnState.Cracked:
                    reason = WillReinforce ? null : "Güçlendirmek için çelik kiriş gerekli (Tezgah Kademe 3).";
                    return reason == null;
                default:
                    reason = ResourceStorage.Instance.Has(rebuildCost) ? null : $"Depoda yeterli malzeme yok: {rebuildCost} gerekli.";
                    return reason == null;
            }
        }

        public override bool CanContinue(Survivor worker) => NeedsRepair || WillReinforce;

        public override void Complete(Survivor worker)
        {
            if (state == ColumnState.Cracked)
            {
                if (Workbench.Instance != null && Workbench.Instance.TryUseSteelBeam())
                {
                    SetState(ColumnState.Reinforced);
                    StructuralIntegrity.Instance?.Repair(reinforceIntegrity);
                }
                return;
            }

            // Çalışma sırasında malzeme başka işe harcanmış olabilir.
            if (state != ColumnState.Broken || !ResourceStorage.Instance.TrySpend(rebuildCost)) return;
            supportHitsRemaining = SkillTree.SupportDurability;
            SetState(ColumnState.Cracked);
            // Yeniden dikilen kolon binanın yükünü tekrar taşır: bütünlük barı yükselir.
            StructuralIntegrity.Instance?.Repair(rebuildIntegrity);
        }

        /// <summary>
        /// Sarsıntının bu kolona etkisi: çatlak kolon dayanıklılığı bitince kırılır, güçlendirilmiş ve zaten kırık
        /// kolon etkilenmez. Bu sarsıntıda kırıldıysa true.
        /// </summary>
        public bool ApplyShock()
        {
            if (state != ColumnState.Cracked) return false;

            supportHitsRemaining--;
            if (supportHitsRemaining > 0) return false;

            SetState(ColumnState.Broken);
            return true;
        }

        public void SetState(ColumnState newState)
        {
            newState = ColumnStateNames.Normalize(newState);
            if (newState == state) return;

            var previous = state;
            state = newState;
            if (state == ColumnState.Cracked && supportHitsRemaining <= 0) supportHitsRemaining = SkillTree.SupportDurability;
            ApplyVisual();
            EventBus.Publish(new ColumnStateChanged(this, previous));
        }

        /// <summary>Seviye modelini (varsa) yeniden giydirir; ModelManager güncellendiğinde çağrılır.</summary>
        public void ApplyVisual()
        {
            if (ModelManager.Instance != null && ModelManager.Instance.ApplyColumnModel(this)) return;
            ApplyTint();
        }

        void ApplyTint()
        {
            if (body == null) return;

            block ??= new MaterialPropertyBlock();
            body.GetPropertyBlock(block);
            block.SetColor(BaseColorId, state switch
            {
                ColumnState.Cracked => new Color(0.85f, 0.55f, 0.2f),
                ColumnState.Reinforced => new Color(0.4f, 0.55f, 0.75f),
                _ => new Color(0.5f, 0.12f, 0.1f),
            });
            body.SetPropertyBlock(block);
        }

        public void RestoreState(ColumnState savedState, int hitsRemaining)
        {
            state = ColumnStateNames.Normalize(savedState);
            supportHitsRemaining = state == ColumnState.Cracked ? Mathf.Max(1, hitsRemaining) : 0;
            ApplyVisual();
        }
    }
}
