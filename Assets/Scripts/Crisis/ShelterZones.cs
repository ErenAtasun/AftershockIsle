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
        public float MinZ;
        public float MaxZ;

        public float CenterX => (MinX + MaxX) * 0.5f;
        public float CenterZ => (MinZ + MaxZ) * 0.5f;
        public float Width => MaxX - MinX;
        public float Depth => MaxZ - MinZ;
        public bool Contains(int floor, float x, float z) =>
            floor == Floor && x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
    }

    /// <summary>
    /// Yaşam Üçgeni alanları: kırık olmayan kolonların dibi ve sağlam (parçalanmamış) mobilyaların önü.
    /// Alan, nesnenin kameraya bakan yüzünün önündeki şerittir; odanın ortasında duran açıkta sayılır.
    /// Merdivende olan karakter her zaman açıktadır.
    /// </summary>
    public static class ShelterZones
    {
        public const float ColumnHalfWidth = 0.7f;
        /// <summary>Nesnenin önünde korunan şeridin derinliği.</summary>
        public const float ShelterDepth = 1.5f;

        public static void Collect(List<ShelterZone> result)
        {
            result.Clear();

            foreach (var furniture in Furniture.All)
            {
                if (!furniture.ProvidesShelter) continue;
                float x = furniture.transform.position.x;
                Front(furniture, out float minZ, out float maxZ);
                result.Add(new ShelterZone
                {
                    Floor = furniture.Floor,
                    MinX = x - furniture.ShelterHalfWidth,
                    MaxX = x + furniture.ShelterHalfWidth,
                    MinZ = minZ,
                    MaxZ = maxZ,
                });
            }

            foreach (var column in SupportColumn.All)
            {
                if (column.State == ColumnState.Broken) continue;
                float x = column.transform.position.x;
                Front(column, out float minZ, out float maxZ);
                result.Add(new ShelterZone
                {
                    Floor = column.Floor,
                    MinX = x - ColumnHalfWidth,
                    MaxX = x + ColumnHalfWidth,
                    MinZ = minZ,
                    MaxZ = maxZ,
                });
            }
        }

        static void Front(Interactable source, out float minZ, out float maxZ)
        {
            float front = source.TryGetFootprint(out var bounds) ? bounds.min.z : source.transform.position.z;
            minZ = front - ShelterDepth;
            maxZ = front + 0.2f;
        }

        public static bool IsSheltered(Survivor survivor, List<ShelterZone> zones)
        {
            if (survivor.Mover.IsClimbing) return false;

            int floor = survivor.Mover.CurrentFloor;
            var position = survivor.transform.position;
            foreach (var zone in zones)
                if (zone.Contains(floor, position.x, position.z)) return true;

            return false;
        }
    }
}
