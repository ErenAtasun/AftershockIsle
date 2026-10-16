using System.Collections.Generic;
using AftershockIsle.Building;
using UnityEngine;

namespace AftershockIsle.Characters
{
    /// <summary>Karakteri bina grafiği üzerinde yürütür ve merdivenlerde tırmandırır.</summary>
    public class CharacterMover : MonoBehaviour
    {
        [SerializeField] BuildingLayout building;
        [SerializeField] float walkSpeed = 3f;
        [SerializeField] float climbSpeed = 1.6f;
        [SerializeField] float turnSpeed = 720f;

        readonly List<Waypoint> path = new List<Waypoint>();
        readonly List<Waypoint> buffer = new List<Waypoint>();
        int pathIndex;

        /// <summary>Taşıma yükü, tükenmişlik ve yetenekler bu çarpanı değiştirir.</summary>
        public float SpeedMultiplier { get; set; } = 1f;
        /// <summary>True iken yol korunur ama karakter yerinden kıpırdamaz (enkaz altında, ölü).</summary>
        public bool Frozen { get; set; }
        public BuildingLayout Building => building;
        public int CurrentFloor { get; private set; }
        public bool IsMoving => pathIndex < path.Count;
        public bool IsClimbing => IsMoving && path[pathIndex].IsClimb;

        void Awake()
        {
            if (building == null) building = FindFirstObjectByType<BuildingLayout>();
        }

        void Start()
        {
            CurrentFloor = building.FloorAtHeight(transform.position.y + 0.1f);
        }

        /// <summary>Hedefe yol başlatır. Engel yüzünden tam ulaşılamıyorsa en yakın noktaya gider ve false döner.</summary>
        public bool MoveTo(int floor, float x)
        {
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

            pathIndex = 0;
            return reachable;
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
        }

        void Update()
        {
            if (!IsMoving || Frozen) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            var waypoint = path[pathIndex];
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

        void Face(Vector3 direction, float dt)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            var target = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
        }
    }
}
