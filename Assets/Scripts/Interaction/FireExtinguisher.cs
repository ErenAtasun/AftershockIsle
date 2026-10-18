using AftershockIsle.Characters;
using AftershockIsle.Crisis;
using UnityEngine;

namespace AftershockIsle.Interaction
{
    /// <summary>
    /// Sahneye konan yangın tüpü: aynı kattaki yangını kovasız söndürür ve dumanı azaltır. Dolumu sınırlıdır; boşalınca
    /// süs olarak kalır (etkileşim kapanır). Tezgahta üretilen Yangın Söndürme Tüpü yapısından ayrıdır (o yayılımı da yavaşlatır).
    /// </summary>
    public class FireExtinguisher : Interactable
    {
        [Tooltip("Kaç kez sıkılabilir (her sıkış bir iş)")]
        [SerializeField, Min(1)] int charges = 3;
        [Tooltip("Her sıkışta yangından düşen miktar (yangın 0-100)")]
        [SerializeField, Range(5f, 100f)] float fireReduction = 45f;
        [Tooltip("Her sıkışta dumandan düşen miktar (duman 0-100)")]
        [SerializeField, Range(0f, 100f)] float smokeReduction = 45f;

        int remaining = -1;

        public int Remaining => remaining < 0 ? charges : remaining;

        public override string ActionLabel => "Tüple Söndür";
        public override WorkCategory Category => WorkCategory.Firefight;
        public override bool AllowedInSmoke => true;
        public override bool IsInteractive => Remaining > 0;
        public override string StatusText => Remaining > 0
            ? $"Bu kattaki yangını kovasız söndürür (her sıkış -%{fireReduction:0}). Kalan: {Remaining}/{charges}"
            : "Tüp boş.";

        protected override void Awake()
        {
            base.Awake();
            if (remaining < 0) remaining = charges;
        }

        public override bool CanStart(Survivor worker, out string reason)
        {
            if (Remaining <= 0)
            {
                reason = "Tüp boş.";
                return false;
            }

            var fire = FireSpot.ForFloor(Floor);
            if (fire == null || (!fire.IsBurning && fire.Smoke < 5f))
            {
                reason = "Bu katta söndürülecek yangın ya da duman yok.";
                return false;
            }

            reason = null;
            return true;
        }

        public override bool CanContinue(Survivor worker) => Remaining > 0;

        public override void Complete(Survivor worker)
        {
            if (Remaining <= 0) return;
            var fire = FireSpot.ForFloor(Floor);
            if (fire == null) return;

            fire.ExtinguishDirect(fireReduction, smokeReduction);
            remaining = Remaining - 1;
        }

        public void RestoreState(int remainingCharges) => remaining = Mathf.Clamp(remainingCharges, 0, charges);
    }
}
