using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.Core;
using AftershockIsle.Interaction;
using UnityEngine;

namespace AftershockIsle.Characters
{
    /// <summary>
    /// Kat içinde serbest yürürken (klavye/gamepad ile) içinden geçilmeyen nesneler: mobilya, tezgah, sandık, kolon...
    /// Etkileşilebilir nesnelerin collider'larından XZ dikdörtgenleri çıkarılır ve yarım saniyede bir yenilenir
    /// (kazılan moloz küçülür, yeni enkaz düşer). Kafa üstündeki duvar nesneleri (boru) engel sayılmaz.
    /// Aynı engeller, iş ve fare emriyle yürüyen karakterlerin kat içi yolunu da (X-Z ızgarada A*) belirler.
    /// </summary>
    public static class FloorObstacles
    {
        struct Footprint
        {
            public int Floor;
            public Vector2 Center;
            public Vector2 Half;
        }

        const float RefreshSeconds = 0.5f;
        const float OverheadHeight = 1.6f;
        const float NavCell = 0.25f;
        // Köşelerden sürtünmeden geçmek için ızgarada gövdeye eklenen pay.
        const float NavMargin = 0.05f;

        static readonly List<Footprint> footprints = new List<Footprint>();
        static readonly List<Footprint> previous = new List<Footprint>();
        static float refreshAt = float.MinValue;
        static int refreshFrame = -1;
        static int version;

        static NavGrid grid;
        static BuildingLayout gridBuilding;
        static int gridFloor = -1;
        static int gridVersion = -1;
        static float gridRadius = -1f;
        static readonly List<Vector2> corners = new List<Vector2>();

        /// <summary>
        /// Kat içinde from'dan to'ya mobilyaların etrafından dolaşan ara köşeleri result'a ekler (from ve to hariç).
        /// Doğrudan yol açıksa hiçbir şey eklemez.
        /// </summary>
        public static void Route(BuildingLayout building, int floor, Vector3 from, Vector3 to, float radius, List<Vector3> result)
        {
            Refresh(building);

            if (grid == null || gridBuilding != building)
            {
                grid = new NavGrid(new Vector2(building.MinX, building.MinZ), new Vector2(building.MaxX, building.MaxZ), NavCell);
                gridBuilding = building;
                gridFloor = -1;
            }

            if (gridFloor != floor || gridVersion != version || !Mathf.Approximately(gridRadius, radius))
            {
                gridFloor = floor;
                gridVersion = version;
                gridRadius = radius;
                grid.Fill(point => Overlaps(floor, point, radius + NavMargin));
            }

            grid.FindPath(new Vector2(from.x, from.z), new Vector2(to.x, to.z), corners);
            float y = building.FloorY(floor);
            foreach (var corner in corners) result.Add(new Vector3(corner.x, y, corner.y));
        }

        static bool Overlaps(int floor, Vector2 point, float radius)
        {
            foreach (var footprint in footprints)
            {
                if (footprint.Floor != floor) continue;

                var offset = point - footprint.Center;
                var closest = new Vector2(
                    Mathf.Clamp(offset.x, -footprint.Half.x, footprint.Half.x),
                    Mathf.Clamp(offset.y, -footprint.Half.y, footprint.Half.y));
                if ((offset - closest).sqrMagnitude < radius * radius) return true;
            }
            return false;
        }

        /// <summary>Daire (karakter) bulunduğu kattaki engellerin dışına itilir.</summary>
        public static Vector3 PushOut(BuildingLayout building, int floor, Vector3 position, float radius)
        {
            Refresh(building);

            var point = new Vector2(position.x, position.z);
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var footprint in footprints)
                {
                    if (footprint.Floor != floor) continue;

                    var offset = point - footprint.Center;
                    var closest = new Vector2(
                        Mathf.Clamp(offset.x, -footprint.Half.x, footprint.Half.x),
                        Mathf.Clamp(offset.y, -footprint.Half.y, footprint.Half.y));
                    var away = offset - closest;
                    float distance = away.magnitude;
                    if (distance >= radius) continue;

                    if (distance > 0.0001f)
                    {
                        point += away / distance * (radius - distance);
                        continue;
                    }

                    // Merkez dikdörtgenin içinde: kameraya doğru (ön tarafa) ya da en kısa eksenden çıkar.
                    float pushX = footprint.Half.x - Mathf.Abs(offset.x) + radius;
                    float pushZ = footprint.Half.y - Mathf.Abs(offset.y) + radius;
                    if (pushX < pushZ) point.x += Mathf.Sign(offset.x == 0f ? 1f : offset.x) * pushX;
                    else point.y += Mathf.Sign(offset.y == 0f ? -1f : offset.y) * pushZ;
                }
            }

            return new Vector3(point.x, position.y, point.y);
        }

        /// <summary>Sonraki sorguda listeyi hemen yenile (ör. yeni enkaz düştü).</summary>
        public static void Invalidate() => refreshAt = float.MinValue;

        /// <summary>Engel listesi her gerçekten değiştiğinde artar; yürüyen karakter bunu görünce yolunu yeniden çıkarır.</summary>
        public static int Version(BuildingLayout building)
        {
            Refresh(building);
            return version;
        }

        static void Refresh(BuildingLayout building)
        {
            if (Time.unscaledTime < refreshAt || refreshFrame == Time.frameCount) return;
            refreshAt = Time.unscaledTime + RefreshSeconds;
            refreshFrame = Time.frameCount;
            previous.Clear();
            previous.AddRange(footprints);
            footprints.Clear();

            foreach (var interactable in Object.FindObjectsByType<Interactable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!interactable.BlocksMovement || !interactable.TryGetFootprint(out var bounds)) continue;

                int floor = building.FloorAtHeight(bounds.min.y + 0.05f);
                if (bounds.min.y - building.FloorY(floor) > OverheadHeight) continue;

                footprints.Add(new Footprint
                {
                    Floor = floor,
                    Center = new Vector2(bounds.center.x, bounds.center.z),
                    Half = new Vector2(bounds.extents.x, bounds.extents.z),
                });
            }

            if (!SameAsPrevious()) version++;
        }

        static bool SameAsPrevious()
        {
            if (previous.Count != footprints.Count) return false;
            for (int i = 0; i < footprints.Count; i++)
            {
                var a = footprints[i];
                var b = previous[i];
                if (a.Floor != b.Floor || (a.Center - b.Center).sqrMagnitude > 0.0001f || (a.Half - b.Half).sqrMagnitude > 0.0001f)
                    return false;
            }
            return true;
        }
    }
}
