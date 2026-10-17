using System.Collections.Generic;
using AftershockIsle.Building;
using UnityEngine;

namespace AftershockIsle.Characters
{
    /// <summary>
    /// Karakteri bina grafiği üzerinde yürütür ve merdivenlerde tırmandırır. İki hareket biçimi:
    /// Yol (iş ve fare emirleri): MoveTo hedefe yol çıkarır; merdivenlere LadderZ çizgisinde yaklaşılır,
    /// son nokta hedef Z'sidir (nesnenin önü). Kat içi her yürüyüş mobilyaların etrafından dolaşır (FloorObstacles.Route). Doğrudan (klavye/gamepad): Steer ile kat içinde X-Z'de serbest yürür,
    /// mobilyaların etrafından kayar, yol kapatan engellerde durur.
    /// </summary>
    public class CharacterMover : MonoBehaviour
    {
        const float SteerDeadZone = 0.2f;
        const float SteerAcceleration = 30f;
        // Derinlikte hareket perspektif yüzünden ekranda hızlı görünür; biraz yavaşlatılır.
        const float DepthSpeedFactor = 0.85f;
        const float BodyRadius = 0.3f;

        [SerializeField] BuildingLayout building;
        [SerializeField] float walkSpeed = 3f;
        [SerializeField] float climbSpeed = 1.6f;
        [SerializeField] float turnSpeed = 720f;

        readonly List<Waypoint> path = new List<Waypoint>();
        readonly List<Waypoint> buffer = new List<Waypoint>();
        readonly List<Vector3> detour = new List<Vector3>();
        // Son verilen hedef: yol üstüne yeni engel düşerse (enkaz) aynı hedefe yeniden yol çıkarılır.
        int targetFloor;
        float targetX;
        float? targetZ;
        int routeVersion;
        int pathIndex;
        Vector2 steerInput;
        Vector3 steerVelocity;

        /// <summary>Taşıma yükü, tükenmişlik ve yetenekler bu çarpanı değiştirir.</summary>
        public float SpeedMultiplier { get; set; } = 1f;
        /// <summary>True iken yol korunur ama karakter yerinden kıpırdamaz (enkaz altında, ölü).</summary>
        public bool Frozen { get; set; }
        public BuildingLayout Building => building;
        public int CurrentFloor { get; private set; }
        public bool IsMoving => pathIndex < path.Count;
        public bool IsClimbing => IsMoving && path[pathIndex].IsClimb;
        public bool IsSteering => steerInput.sqrMagnitude > SteerDeadZone * SteerDeadZone;

        void Awake()
        {
            if (building == null) building = FindFirstObjectByType<BuildingLayout>();
        }

        void Start()
        {
            UpdateCurrentFloor();
        }

        public void UpdateCurrentFloor()
        {
            if (building != null) CurrentFloor = building.FloorAtHeight(transform.position.y + 0.1f);
        }

        /// <summary>
        /// Hedefe yol başlatır. z verilmezse aynı katta mevcut derinlik korunur, kat değişiyorsa merdiven çizgisinde kalınır.
        /// Engel yüzünden tam ulaşılamıyorsa en yakın noktaya gider ve false döner.
        /// </summary>
        public bool MoveTo(int floor, float x, float? z = null)
        {
            targetFloor = floor;
            targetX = x;
            targetZ = z;
            steerInput = Vector2.zero;
            steerVelocity = Vector3.zero;

            bool reachable;
            if (IsClimbing)
            {
                // Merdivenin ortasında yön değiştirmek yerine tırmanışı bitirip yeni yola geç.
                var climb = path[pathIndex];
                reachable = building.FindPath(climb.Floor, climb.Position.x, floor, x, buffer);
                path.Clear();
                path.Add(climb);
                path.AddRange(buffer);
            }
            else
            {
                reachable = building.FindPath(CurrentFloor, transform.position.x, floor, x, path);
            }

            ApplyDepth(z, floor);
            AddDetours();
            routeVersion = FloorObstacles.Version(building);
            pathIndex = 0;
            return reachable;
        }

        // Yol bulma X-Y'dedir; Z burada atanır: merdiven öncesi ve tırmanış LadderZ, son nokta hedef derinlik.
        void ApplyDepth(float? targetZ, int targetFloor)
        {
            bool changesFloor = false;
            foreach (var waypoint in path)
                if (waypoint.IsClimb) changesFloor = true;

            float finalZ = targetZ.HasValue ? building.ClampZ(targetZ.Value)
                : changesFloor ? building.LadderZ
                : building.ClampZ(transform.position.z);

            for (int i = 0; i < path.Count; i++)
            {
                var waypoint = path[i];
                bool ladder = waypoint.IsClimb || (i + 1 < path.Count && path[i + 1].IsClimb);
                waypoint.Position.z = ladder ? building.LadderZ : finalZ;
                path[i] = waypoint;
            }
        }

        // Kat içindeki her düz yürüyüşü engellerin etrafından geçen köşelerle böler.
        void AddDetours()
        {
            buffer.Clear();
            buffer.AddRange(path);
            path.Clear();

            var from = transform.position;
            int floor = CurrentFloor;
            foreach (var waypoint in buffer)
            {
                if (!waypoint.IsClimb && waypoint.Floor == floor)
                {
                    detour.Clear();
                    FloorObstacles.Route(building, floor, from, waypoint.Position, BodyRadius, detour);
                    foreach (var corner in detour) path.Add(new Waypoint(corner, false, floor));
                }

                path.Add(waypoint);
                from = waypoint.Position;
                floor = waypoint.Floor;
            }
        }

        /// <summary>
        /// Doğrudan kontrol girdisi (x: sağ-sol, y: odanın içine / dışına). Her kare verilir; sıfırsa karakter durur.
        /// Verildiği anda varsa yol iptal edilir (tırmanış bitene kadar beklenir).
        /// </summary>
        public void Steer(Vector2 input)
        {
            steerInput = Vector2.ClampMagnitude(input, 1f);
            if (IsSteering && IsMoving && !IsClimbing)
            {
                path.Clear();
                pathIndex = 0;
            }
        }

        /// <summary>Durduğu yerde verilen yöne döner (ör. çalışırken nesneye bakmak için).</summary>
        public void FaceDirection(Vector3 direction) => Face(direction, Time.deltaTime);

        /// <summary>Yolu iptal eder. Merdivendeyse en yakın kata oturtulur.</summary>
        public void Halt()
        {
            if (IsClimbing)
            {
                float y = transform.position.y + building.FloorHeight * 0.5f;
                CurrentFloor = building.FloorAtHeight(y);
                var position = transform.position;
                position.y = building.FloorY(CurrentFloor);
                transform.position = position;
            }

            path.Clear();
            pathIndex = 0;
            steerInput = Vector2.zero;
            steerVelocity = Vector3.zero;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || Frozen) return;

            if (!IsMoving)
            {
                UpdateSteering(dt);
                return;
            }

            var waypoint = path[pathIndex];
            if (!waypoint.IsClimb && FloorObstacles.Version(building) != routeVersion)
            {
                MoveTo(targetFloor, targetX, targetZ);
                return;
            }

            float speed = (waypoint.IsClimb ? climbSpeed : walkSpeed) * SpeedMultiplier;

            Vector3 facing = waypoint.IsClimb ? Vector3.forward : waypoint.Position - transform.position;
            Face(facing, dt);

            transform.position = Vector3.MoveTowards(transform.position, waypoint.Position, speed * dt);

            if ((transform.position - waypoint.Position).sqrMagnitude > 0.0001f) return;

            CurrentFloor = waypoint.Floor;
            pathIndex++;
            if (pathIndex >= path.Count)
            {
                path.Clear();
                pathIndex = 0;
            }
        }

        void UpdateSteering(float dt)
        {
            var desired = IsSteering
                ? new Vector3(steerInput.x, 0f, steerInput.y * DepthSpeedFactor) * (walkSpeed * SpeedMultiplier)
                : Vector3.zero;
            steerVelocity = Vector3.MoveTowards(steerVelocity, desired, SteerAcceleration * dt);
            if (steerVelocity.sqrMagnitude < 0.0001f) return;

            var from = transform.position;
            var next = from + steerVelocity * dt;

            // Yol kapatan engeller (moloz, çöken tavan) tüm derinliği keser; mobilyanın etrafından ise kayılır.
            next.x = building.LimitWalk(CurrentFloor, from.x, next.x);
            next.z = building.ClampZ(next.z);
            next = FloorObstacles.PushOut(building, CurrentFloor, next, BodyRadius);
            next.x = building.LimitWalk(CurrentFloor, from.x, next.x);
            next.z = building.ClampZ(next.z);
            next.y = building.FloorY(CurrentFloor);

            transform.position = next;
            Face(steerVelocity, dt);
        }

        void Face(Vector3 direction, float dt)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            var target = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
        }
    }
}
