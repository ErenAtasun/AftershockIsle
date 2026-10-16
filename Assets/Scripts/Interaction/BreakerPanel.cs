using AftershockIsle.Building;
using AftershockIsle.Characters;

namespace AftershockIsle.Interaction
{
    /// <summary>Ana şalter panosu: binanın elektriğini indirir/kaldırır.</summary>
    public class BreakerPanel : Interactable
    {
        public override string ActionLabel => PowerGrid.IsOn ? "Şalteri İndir" : "Şalteri Kaldır";

        public override string StatusText => PowerGrid.IsOn
            ? "Elektrik açık. Su elektriğe değerse bodrum çarpar."
            : "Elektrik kapalı. Bina karanlık.";

        public override void Complete(Survivor worker) => PowerGrid.Instance.SetPower(!PowerGrid.IsOn);
    }
}
