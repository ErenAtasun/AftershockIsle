using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.Interaction;
using UnityEngine;

namespace AftershockIsle.Characters
{
    /// <summary>
    /// Kat içinde serbest yürürken (klavye/gamepad ile) içinden geçilmeyen nesneler: mobilya, tezgah, sandık, kolon...
    /// Etkileşilebilir nesnelerin collider'larından XZ dikdörtgenleri çıkarılır ve yarım saniyede bir yenilenir
    /// (kazılan moloz küçülür, yeni enkaz düşer). Kafa üstündeki duvar nesneleri (boru) engel sayılmaz.
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

        static readonly List<Footprint> footprints = new List<Footprint>();
        static float refreshAt = float.MinValue;
        static int refreshFrame = -1;

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

        static void Refresh(BuildingLayout building)
        {
            if (Time.unscaledTime < refreshAt || refreshFrame == Time.frameCount) return;
            refreshAt = Time.unscaledTime + RefreshSeconds;
            refreshFrame = Time.frameCount;
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
        }
    }
}
