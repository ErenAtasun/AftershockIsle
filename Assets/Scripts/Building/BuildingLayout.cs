using System;
using System.Collections.Generic;
using UnityEngine;

namespace AftershockIsle.Building
{
    [Serializable]
    public struct LadderDef
    {
        [Tooltip("Merdivenin bağladığı alt kat (üst kat = lowerFloor + 1)")]
        public int lowerFloor;
        public float x;

        public LadderDef(int lowerFloor, float x)
        {
            this.lowerFloor = lowerFloor;
            this.x = x;
        }
    }

    public struct Waypoint
    {
        public Vector3 Position;
        public bool IsClimb;
        /// <summary>Karakterin bu noktaya ulaştığında bulunduğu kat.</summary>
        public int Floor;

        public Waypoint(Vector3 position, bool isClimb, int floor)
        {
            Position = position;
            IsClimb = isClimb;
            Floor = floor;
        }
    }

    /// <summary>
    /// Binanın kesit ölçüleri ve kat/merdiven grafiği. Hareket X (kat içi) ve Y (merdiven)
    /// ekseninde kilitlidir; Z sabit yürüme çizgisidir. Kat 0 bodrumdur.
    /// </summary>
    public class BuildingLayout : MonoBehaviour
    {
        [SerializeField] float floorHeight = 3.5f;
        [SerializeField] float width = 18f;
        [SerializeField] float depth = 4f;
        [SerializeField] float wallThickness = 0.3f;
        [Tooltip("Karakterlerin duvara girmemesi için kat kenarından bırakılan pay")]
        [SerializeField] float edgePadding = 0.6f;

        [SerializeField] string[] floorNames = { "Bodrum", "Giriş Katı", "Yaşam / Tezgah", "Çatı" };

        [SerializeField] LadderDef[] ladders =
        {
            new LadderDef(0, -6f),
            new LadderDef(1, 6f),
            new LadderDef(2, -6f),
        };

        public float FloorHeight => floorHeight;
        public float Width => width;
        public float Depth => depth;
        public float WallThickness => wallThickness;
        public int FloorCount => floorNames.Length;
        public IReadOnlyList<LadderDef> Ladders => ladders;
        public float WalkPlaneZ => transform.position.z;
        public float MinX => transform.position.x - width * 0.5f + edgePadding;
        public float MaxX => transform.position.x + width * 0.5f - edgePadding;
        public float TopY => FloorY(FloorCount - 1) + floorHeight;

        public string GetFloorName(int floor) => floorNames[ClampFloor(floor)];

        public float FloorY(int floor) => transform.position.y + floor * floorHeight;

        public Vector3 Point(int floor, float x) => new Vector3(x, FloorY(floor), WalkPlaneZ);

        public int FloorAtHeight(float y) =>
            ClampFloor(Mathf.FloorToInt((y - transform.position.y) / floorHeight));

        public int ClampFloor(int floor) => Mathf.Clamp(floor, 0, FloorCount - 1);

        public float ClampX(float x) => Mathf.Clamp(x, MinX, MaxX);

        /// <summary>
        /// Bir noktadan diğerine yürüme/tırmanma noktalarını üretir. Katlar dikey yığın olduğu için
        /// yol, her kat geçişinde mevcut konuma en yakın merdivenin seçilmesiyle bulunur.
        /// </summary>
        public void FindPath(int fromFloor, float fromX, int toFloor, float toX, List<Waypoint> result)
        {
            result.Clear();
            toFloor = ClampFloor(toFloor);
            toX = ClampX(toX);

            int floor = ClampFloor(fromFloor);
            float x = fromX;

            while (floor != toFloor)
            {
                int step = toFloor > floor ? 1 : -1;
                int lowerFloor = step > 0 ? floor : floor - 1;

                if (!TryFindNearestLadder(lowerFloor, x, out float ladderX))
                {
                    Debug.LogWarning($"{GetFloorName(lowerFloor)} ile üst katı arasında merdiven yok.", this);
                    break;
                }

                result.Add(new Waypoint(Point(floor, ladderX), false, floor));
                result.Add(new Waypoint(Point(floor + step, ladderX), true, floor + step));
                floor += step;
                x = ladderX;
            }

            result.Add(new Waypoint(Point(floor, toX), false, floor));
        }

        bool TryFindNearestLadder(int lowerFloor, float nearX, out float ladderX)
        {
            ladderX = 0f;
            float bestDistance = float.MaxValue;

            foreach (var ladder in ladders)
            {
                if (ladder.lowerFloor != lowerFloor) continue;

                float worldX = transform.position.x + ladder.x;
                float distance = Mathf.Abs(worldX - nearX);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                ladderX = worldX;
            }

            return bestDistance < float.MaxValue;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < FloorCount; i++)
                Gizmos.DrawLine(Point(i, MinX), Point(i, MaxX));

            Gizmos.color = Color.yellow;
            foreach (var ladder in ladders)
            {
                float x = transform.position.x + ladder.x;
                Gizmos.DrawLine(Point(ladder.lowerFloor, x), Point(ladder.lowerFloor + 1, x));
            }
        }
    }
}
