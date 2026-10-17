using AftershockIsle.Characters;
using AftershockIsle.Expedition;

namespace AftershockIsle.Interaction
{
    /// <summary>Giriş katındaki dış çıkış kapısı. Karakter kapıya ulaşınca nodlu şehir haritası açılır.</summary>
    public class ExitDoor : Interactable
    {
        public override string ActionLabel => "Sefere Çık";

        // Elindeki yükü dışarı taşımasın; önce sandığa bırakır.
        public override bool RequiresEmptyHands => true;

        public override string StatusText
        {
            get
            {
                var expeditions = ExpeditionSystem.Instance;
                if (expeditions == null) return null;
                return expeditions.CanDepart(out string reason)
                    ? $"Dış sefer: {expeditions.BagSlots} slotlu çanta. Sadece öğle vakti."
                    : reason;
            }
        }

        // Kapı duvarın içindedir; karakter katın kenarında durur.
        public override float GetWorkX(int fromFloor, float fromX) => Building.MaxX;
        public override float GetWorkZ() => Building.WalkPlaneZ;
        public override bool BlocksMovement => false;

        public override bool CanStart(Survivor worker, out string reason)
        {
            if (ExpeditionSystem.Instance == null)
            {
                reason = "Sefer sistemi yok.";
                return false;
            }
            return ExpeditionSystem.Instance.CanDepart(out reason);
        }

        public override void Complete(Survivor worker) => ExpeditionSystem.Instance.OpenMap(worker);
    }
}
