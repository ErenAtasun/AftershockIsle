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
        [Tooltip("Basamaklı (eğimli) merdiven: alt ucundan çıkılıp eğim boyunca üst ucuna yürünür. Kapalıysa dikey el merdiveni.")]
        public bool stair;
        [Tooltip("Alt katta merdivene binilen nokta (bina merkezine göre Z)")]
        public float bottomZ;
        [Tooltip("Üst katta merdivenden inilen nokta (bina merkezine göre Z)")]
        public float topZ;

        public LadderDef(int lowerFloor, float x, bool stair, float bottomZ, float topZ)
        {
            this.lowerFloor = lowerFloor;
            this.x = x;
            this.stair = stair;
            this.bottomZ = bottomZ;
            this.topZ = topZ;
        }

        /// <summary>Eski sahnelerde eğim bilgisi yoksa (hepsi 0) tanımlı değil sayılır.</summary>
        public bool HasProfile => stair || bottomZ != 0f || topZ != 0f;
    }

    public struct Waypoint
    {
        public Vector3 Position;
        public bool IsClimb;
        /// <summary>Karakterin bu noktaya ulaştığında bulunduğu kat.</summary>
        public int Floor;
        /// <summary>Z'si merdivene göre sabitlenmiş nokta (merdiven başı / sonu): yol derinliği onu değiştirmez.</summary>
        public bool FixedDepth;
        /// <summary>Basamaklı merdivende eğim boyunca yürüyüş (dikey tırmanış değil).</summary>
        public bool IsStair;

        public Waypoint(Vector3 position, bool isClimb, int floor)
        {
            Position = position;
            IsClimb = isClimb;
            Floor = floor;
            FixedDepth = false;
            IsStair = false;
        }
    }

    /// <summary>
    /// Binanın kesit ölçüleri ve kat/merdiven grafiği. Yol bulma X (kat içi) ve Y (merdiven) üzerindedir;
    /// karakterler kat içinde Z'de (odanın içine / dışına) da serbestçe yürüyebilir. Basamaklı merdivenlerde alt uç önde, üst uç arkadadır; karakter eğim boyunca yürür.
    /// Kat 0 bodrumdur.
    /// Yol kapatan moloz gibi engeller kat çizgisinin bir aralığını geçilmez yapar.
    /// </summary>
    public class BuildingLayout : MonoBehaviour
    {
        struct NavBlocker
        {
            public UnityEngine.Object Owner;
            public int Floor;
            public float MinX;
            public float MaxX;
        }

        /// <summary>Engelin önünde durulacak mesafe; çalışma noktası da burasıdır.</summary>
        public const float BlockerStandOff = 0.4f;

        // Blender binasının birebir ölçüleri (Building.fbx): kat 3.5 m, iç genişlik 18 m, derinlik 4 m, duvar 0.35 m.
        // ModelManager modeli bu ölçülere göre ölçeklediği için farklı değer binayı esnetir.
        [SerializeField] float floorHeight = 3.5f;
        [SerializeField] float width = 18f;
        [SerializeField] float depth = 4f;
        [SerializeField] float wallThickness = 0.35f;
        [Tooltip("Karakterlerin duvara girmemesi için kat kenarından bırakılan pay")]
        [SerializeField] float edgePadding = 0.6f;
        [Tooltip("Merdivenlerin bina merkezine göre Z konumu")]
        [SerializeField] float ladderZ = 0.45f;
        [Tooltip("Karakterin arka duvara ve ön kenara bırakacağı pay")]
        [SerializeField] float depthPadding = 0.45f;

        // Blender binasıyla aynı: bodrum + 3 kat + çatı.
        [SerializeField] string[] floorNames = { "Bodrum", "Giriş Katı", "Yaşam / Tezgah", "3. Kat", "Çatı" };

        // Blender'daki merdivenler -6, +6, -6: basamaklı, odanın önünden (Z -0.35) arkasına (Z 1.26) yükselir.
        // 3. kattan çatıya arka duvara dayalı dikey el merdiveni +5'te.
        [SerializeField] LadderDef[] ladders =
        {
            new LadderDef(0, -6f, true, StairBottomZ, StairTopZ),
            new LadderDef(1, 6f, true, StairBottomZ, StairTopZ),
            new LadderDef(2, -6f, true, StairBottomZ, StairTopZ),
            new LadderDef(3, 5f, false, WallLadderZ, WallLadderZ),
        };

        const float StairBottomZ = -0.5f;
        const float StairTopZ = 1.3f;
        const float WallLadderZ = 1.5f;

        readonly List<NavBlocker> blockers = new List<NavBlocker>();
        readonly List<Waypoint> scratchPath = new List<Waypoint>();

        public float FloorHeight => floorHeight;
        public float Width => width;
        public float Depth => depth;
        public float WallThickness => wallThickness;
        public int FloorCount => floorNames.Length;
        public int RoofFloor => FloorCount - 1;
        public IReadOnlyList<LadderDef> Ladders => ladders;
        public float WalkPlaneZ => transform.position.z;
        public float MinX => transform.position.x - width * 0.5f + edgePadding;
        public float MaxX => transform.position.x + width * 0.5f - edgePadding;
        public float TopY => FloorY(FloorCount - 1) + floorHeight;
        public float LadderZOffset => ladderZ;
        public float LadderZ => transform.position.z + ladderZ;
        public float MinZ => transform.position.z - depth * 0.5f + depthPadding;
        public float MaxZ => transform.position.z + depth * 0.5f - depthPadding;

        public float ClampZ(float z) => Mathf.Clamp(z, MinZ, MaxZ);

        /// <summary>Kat içinde fromX'ten toX'e doğrudan yürürken (yol kapatan) engellere takılınca durulacak X.</summary>
        public float LimitWalk(int floor, float fromX, float toX)
        {
            toX = ClampX(toX);
            return TryFindObstacle(ClampFloor(floor), fromX, toX, out float stopX) ? stopX : toX;
        }

        public string GetFloorName(int floor) => floorNames[ClampFloor(floor)];

        public float FloorY(int floor) => transform.position.y + floor * floorHeight;

        public Vector3 Point(int floor, float x) => new Vector3(x, FloorY(floor), WalkPlaneZ);

        public int FloorAtHeight(float y) =>
            ClampFloor(Mathf.FloorToInt((y - transform.position.y) / floorHeight));

        public int ClampFloor(int floor) => Mathf.Clamp(floor, 0, FloorCount - 1);

        public float ClampX(float x) => Mathf.Clamp(x, MinX, MaxX);

        /// <summary>
        /// Bir noktadan diğerine yürüme/tırmanma noktalarını üretir. Katlar dikey yığın olduğu için
        /// yol, her kat geçişinde mevcut konumdan ulaşılabilen en yakın merdivenin seçilmesiyle bulunur.
        /// Hedefe engel yüzünden ulaşılamıyorsa gidilebilen en yakın noktaya kadar yol üretir ve false döner.
        /// </summary>
        public bool FindPath(int fromFloor, float fromX, int toFloor, float toX, List<Waypoint> result)
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

                if (!TryFindNearestLadder(lowerFloor, floor, x, out float ladderX, out var ladder)) break;

                // Merdiven başına yürü, sonra eğim boyunca (basamaklıysa çaprazlama, el merdiveniyse dikey) diğer kata geç.
                var start = new Waypoint(Point(floor, ladderX), false, floor) { FixedDepth = true };
                start.Position.z = LadderStandZ(ladder, floor);
                var climb = new Waypoint(Point(floor + step, ladderX), true, floor + step) { FixedDepth = true, IsStair = ladder.stair };
                climb.Position.z = LadderStandZ(ladder, floor + step);
                result.Add(start);
                result.Add(climb);
                floor += step;
                x = ladderX;
            }

            bool reachesTarget = floor == toFloor;
            float finalX = toX;
            if (TryFindObstacle(floor, x, toX, out float stopX))
            {
                finalX = stopX;
                reachesTarget = false;
            }

            result.Add(new Waypoint(Point(floor, finalX), false, floor));
            return reachesTarget;
        }

        /// <summary>Karakterin hedef kata hangi X noktasından gireceği (aynı kattaysa kendi X'i).</summary>
        public float ArrivalX(int fromFloor, float fromX, int toFloor)
        {
            FindPath(fromFloor, fromX, toFloor, fromX, scratchPath);
            foreach (var waypoint in scratchPath)
                if (waypoint.Floor == toFloor) return waypoint.Position.x;
            return fromX;
        }

        /// <summary>Bulunulan kattan ulaşılabilen en yakın merdiveni ve onun götürdüğü katı verir (kaçış için).</summary>
        public bool TryGetNearestExit(int floor, float x, out int otherFloor, out float ladderX)
        {
            otherFloor = floor;
            ladderX = x;
            float bestDistance = float.MaxValue;

            foreach (var ladder in ladders)
            {
                int other;
                if (ladder.lowerFloor == floor) other = floor + 1;
                else if (ladder.lowerFloor == floor - 1) other = floor - 1;
                else continue;

                float worldX = transform.position.x + ladder.x;
                float distance = Mathf.Abs(worldX - x);
                if (distance >= bestDistance || TryFindObstacle(floor, x, worldX, out _)) continue;

                bestDistance = distance;
                otherFloor = other;
                ladderX = worldX;
            }

            return bestDistance < float.MaxValue;
        }

        public void AddBlocker(UnityEngine.Object owner, int floor, float minX, float maxX)
        {
            blockers.Add(new NavBlocker { Owner = owner, Floor = floor, MinX = minX, MaxX = maxX });
        }

        public void RemoveBlocker(UnityEngine.Object owner) => blockers.RemoveAll(b => b.Owner == owner);

        /// <summary>fromX'ten toX'e yürürken ilk engel varsa önünde durulacak X'i verir.</summary>
        bool TryFindObstacle(int floor, float fromX, float toX, out float stopX)
        {
            stopX = toX;
            bool found = false;
            float bestDistance = float.MaxValue;
            const float epsilon = 0.01f;

            foreach (var blocker in blockers)
            {
                if (blocker.Floor != floor) continue;

                if (toX > fromX && blocker.MinX >= fromX - epsilon && blocker.MinX < toX)
                {
                    float distance = blocker.MinX - fromX;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    stopX = Mathf.Max(fromX, blocker.MinX - BlockerStandOff);
                    found = true;
                }
                else if (toX < fromX && blocker.MaxX <= fromX + epsilon && blocker.MaxX > toX)
                {
                    float distance = fromX - blocker.MaxX;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    stopX = Mathf.Min(fromX, blocker.MaxX + BlockerStandOff);
                    found = true;
                }
            }

            return found;
        }

        bool TryFindNearestLadder(int lowerFloor, int currentFloor, float nearX, out float ladderX, out LadderDef found)
        {
            ladderX = 0f;
            found = default;
            float bestDistance = float.MaxValue;

            foreach (var ladder in ladders)
            {
                if (ladder.lowerFloor != lowerFloor) continue;

                float worldX = transform.position.x + ladder.x;
                float distance = Mathf.Abs(worldX - nearX);
                if (distance >= bestDistance || TryFindObstacle(currentFloor, nearX, worldX, out _)) continue;

                bestDistance = distance;
                ladderX = worldX;
                found = ladder;
            }

            return bestDistance < float.MaxValue;
        }

        /// <summary>
        /// Merdivenin verilen kattaki kullanım noktasının dünya Z'si: alt katta merdivenin alt ucu, üst katta üst ucu.
        /// El merdiveninde ikisi aynıdır (dikey tırmanış).
        /// </summary>
        public float LadderStandZ(LadderDef ladder, int floor)
        {
            ladder = WithProfile(ladder);
            float local = floor <= ladder.lowerFloor ? ladder.bottomZ : ladder.topZ;
            return ClampZ(transform.position.z + local);
        }

        /// <summary>Eğim bilgisi kaydedilmemiş (eski sahne) merdivene Blender binasındaki ölçüleri verir.</summary>
        public LadderDef WithProfile(LadderDef ladder)
        {
            if (ladder.HasProfile) return ladder;
            bool wallLadder = ladder.lowerFloor >= FloorCount - 2;
            ladder.stair = !wallLadder;
            ladder.bottomZ = wallLadder ? WallLadderZ : StairBottomZ;
            ladder.topZ = wallLadder ? WallLadderZ : StairTopZ;
            return ladder;
        }

        void OnValidate()
        {
            // Eski sahnede kayıtlı merdivenler eğim bilgisini alsın (Inspector'da görünür ve düzenlenebilir).
            if (ladders == null) return;
            for (int i = 0; i < ladders.Length; i++) ladders[i] = WithProfile(ladders[i]);
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
