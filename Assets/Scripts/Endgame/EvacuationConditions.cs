using AftershockIsle.Building;
using AftershockIsle.Crafting;
using AftershockIsle.Interaction;

namespace AftershockIsle.Endgame
{
    /// <summary>GDD 10.1 zafer şartlarının anlık durumu (HUD kontrol listesi ve telsiz için).</summary>
    public readonly struct EvacuationConditions
    {
        public readonly bool WorkbenchTier3;
        public readonly bool RadioRepaired;
        public readonly bool PowerOn;
        public readonly int RoofRubbleLeft;
        public readonly int RoofColumnsReinforced;
        public readonly int RoofColumnsTotal;

        EvacuationConditions(bool tier3, bool radio, bool power, int rubble, int reinforced, int total)
        {
            WorkbenchTier3 = tier3;
            RadioRepaired = radio;
            PowerOn = power;
            RoofRubbleLeft = rubble;
            RoofColumnsReinforced = reinforced;
            RoofColumnsTotal = total;
        }

        public bool RoofSafe => RoofRubbleLeft == 0 && RoofColumnsTotal > 0 && RoofColumnsReinforced == RoofColumnsTotal;
        public bool ReadyToSignal => WorkbenchTier3 && RadioRepaired && PowerOn && RoofSafe;

        /// <summary>Sinyal neden gönderilemiyor (hepsi tamamsa null).</summary>
        public string BlockingReason =>
            !WorkbenchTier3 ? "Tezgah Kademe 3 olmalı."
            : !RadioRepaired ? "Telsiz tamir edilmeli."
            : !PowerOn ? "Ana şalter açık olmalı."
            : RoofRubbleLeft > 0 ? $"Çatıda {RoofRubbleLeft} moloz yığını var; helikopter inemez."
            : RoofColumnsReinforced < RoofColumnsTotal ? $"Çatıyı taşıyan kolonlar çelikle güçlendirilmeli ({RoofColumnsReinforced}/{RoofColumnsTotal})."
            : null;

        public static EvacuationConditions Evaluate(BuildingLayout building, RadioStation radio)
        {
            int roofFloor = building.FloorCount - 1;

            // Çatıyı, çatının hemen altındaki katın kolonları taşır.
            int reinforced = 0;
            int total = 0;
            foreach (var column in SupportColumn.All)
            {
                if (column.Floor != roofFloor - 1) continue;
                total++;
                if (column.State == ColumnState.Reinforced) reinforced++;
            }

            return new EvacuationConditions(
                Workbench.Instance != null && Workbench.Instance.Tier >= 3,
                radio != null && radio.IsRepaired,
                PowerGrid.IsOn,
                RubblePile.CountOnFloor(roofFloor),
                reinforced,
                total);
        }
    }
}
