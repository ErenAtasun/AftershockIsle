using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Interaction;

namespace AftershockIsle.Crisis
{
    public struct ShelterZone
    {
        public int Floor;
        public float MinX;
        public float MaxX;

        public float CenterX => (MinX + MaxX) * 0.5f;
        public float Width => MaxX - MinX;
        public bool Contains(int floor, float x) => floor == Floor && x >= MinX && x <= MaxX;
    }

    /// <summary>
    /// Yaşam Üçgeni alanları: kırık olmayan kolonların dibi ve sağlam (parçalanmamış) mobilyaların altı.
    /// Merdivende olan karakter her zaman açıktadır.
    /// </summary>
    public static class ShelterZones
    {
        public const float ColumnHalfWidth = 0.7f;

        public static void Collect(List<ShelterZone> result)
        {
            result.Clear();

            foreach (var furniture in Furniture.All)
            {
                if (!furniture.ProvidesShelter) continue;
                float x = furniture.transform.position.x;
                result.Add(new ShelterZone
                {
                    Floor = furniture.Floor,
                    MinX = x - furniture.ShelterHalfWidth,
                    MaxX = x + furniture.ShelterHalfWidth,
                });
            }

            foreach (var column in SupportColumn.All)
            {
                if (column.State == ColumnState.Broken) continue;
                float x = column.transform.position.x;
                result.Add(new ShelterZone
                {
                    Floor = column.Floor,
                    MinX = x - ColumnHalfWidth,
                    MaxX = x + ColumnHalfWidth,
                });
            }
        }

        public static bool IsSheltered(Survivor survivor, List<ShelterZone> zones)
        {
            if (survivor.Mover.IsClimbing) return false;

            int floor = survivor.Mover.CurrentFloor;
            float x = survivor.transform.position.x;
            foreach (var zone in zones)
                if (zone.Contains(floor, x)) return true;

            return false;
        }
    }
}
