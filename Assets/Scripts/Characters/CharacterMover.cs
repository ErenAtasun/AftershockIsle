using System.Collections.Generic;
using AftershockIsle.Building;
using UnityEngine;

namespace AftershockIsle.Characters
{
    /// <summary>
    /// Karakteri bina grafiği üzerinde yürütür ve merdivenlerde tırmandırır. İki hareket biçimi:
    /// Yol (iş ve fare emirleri): MoveTo hedefe yol çıkarır; basamaklı merdivenlere alt/üst ucundan girilip eğim boyunca yürünür,
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
        const float StairSpeedFactor = 0.65f;
        // Aynı kattaki iki kazazede birbirinin içinden geçmesin: bu mesafenin altına girince yumuşakça ayrılırlar.
        const float MinSeparation = 0.62f;
        const float SeparationSpeed = 6f;

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
        public bool IsMoving => pathIndex < path.Count || (manualClimb && Mathf.Abs(climbAxis) > 0.05f);
        public bool IsClimbing => manualClimb || (pathIndex < path.Count && path[pathIndex].IsClimb);
        /// <summary>Oyuncu merdivende W/S ile kendisi çıkıp iniyor (bırakınca merdivende durur).</summary>
        public bool IsManualClimbing => manualClimb;
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
            // Elle tırmanırken yol emri gelirse (fare, iş) karakter yakın uca iner ve yoldan devam eder.
            if (manualClimb) EndManualClimb(climbT >= 0.5f);
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

        /// <summary>Dünya koordinatındaki bir noktaya (katı otomatik tespit ederek) yol bulur.</summary>
        public bool WalkTo(Vector3 worldPos)
        {
            int floor = building != null ? building.FloorAtHeight(worldPos.y + 0.1f) : 0;
            return MoveTo(floor, worldPos.x, worldPos.z);
        }

        // Yol bulma X-Y'dedir; Z burada atanır. Merdiven başı ve sonu noktalarının Z'si merdivenden gelir (basamaklı
        // merdivende alt uç önde, üst uç arkada); son nokta hedef derinliktir, verilmemişse merdivenden inilen derinlik.
        void ApplyDepth(float? targetZ, int targetFloor)
        {
            float? arrivalZ = null;
            foreach (var waypoint in path)
                if (waypoint.IsClimb) arrivalZ = waypoint.Position.z;

            float finalZ = targetZ.HasValue ? building.ClampZ(targetZ.Value)
                : arrivalZ ?? building.ClampZ(transform.position.z);

            for (int i = 0; i < path.Count; i++)
            {
                var waypoint = path[i];
                if (waypoint.FixedDepth) continue;
                waypoint.Position.z = finalZ;
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
            if (manualClimb) EndManualClimb(climbT >= 0.5f);
            else if (IsClimbing)
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

            if (manualClimb)
            {
                UpdateManualClimb(dt);
                return;
            }

            if (!IsMoving)
            {
                UpdateSteering(dt);
                Separate(dt);
                return;
            }

            var waypoint = path[pathIndex];
            if (!waypoint.IsClimb && FloorObstacles.Version(building) != routeVersion)
            {
                MoveTo(targetFloor, targetX, targetZ);
                return;
            }

            // Yol, kapalı kapıların aralığından geçer; karakter yaklaşınca kapıyı açar.
            if (!waypoint.IsClimb) BuildingDoor.OpenNear(transform.position, BodyRadius + 0.5f);

            // Basamaklı merdivende eğim boyunca yürünür (normal yürüyüşten biraz yavaş), el merdiveninde dikey tırmanılır.
            float speed = (waypoint.IsStair ? walkSpeed * StairSpeedFactor : waypoint.IsClimb ? climbSpeed : walkSpeed) * SpeedMultiplier;

            // El merdiveninde duvara bakar; basamaklı merdivende ve yürürken gittiği yöne.
            Vector3 facing = waypoint.IsClimb && !waypoint.IsStair ? Vector3.forward : waypoint.Position - transform.position;
            Face(facing, dt);

            transform.position = Vector3.MoveTowards(transform.position, waypoint.Position, speed * dt);
            // Yolun sonuna (hedefe) yaklaşırken ayrılma kapalı: aynı noktaya giden iki kişi birbirini itip varışı sonsuza dek engellemesin.
            if (!waypoint.IsClimb && pathIndex < path.Count - 1 &&
                (transform.position - waypoint.Position).sqrMagnitude > 0.5f) Separate(dt);

            if ((transform.position - waypoint.Position).sqrMagnitude > 0.0001f) return;

            CurrentFloor = waypoint.Floor;
            pathIndex++;
            if (pathIndex >= path.Count)
            {
                path.Clear();
                pathIndex = 0;
            }
        }

        // ---------------------------------------------------------------- Elle tırmanma

        bool manualClimb;
        bool climbIsStair;
        int climbLowerFloor;
        Vector3 climbLower;
        Vector3 climbUpper;
        // 0 = alt uç, 1 = üst uç.
        float climbT;
        float climbStartT;
        // Merdivene binerken karakterin uçtan ne kadar uzakta durduğu: tırmanışın ilk çeyreğinde yavaşça kapanır.
        Vector3 climbEntryOffset;
        float climbAxis;
        int climbAxisFrame;

        /// <summary>
        /// Oyuncunun kendi sürdüğü tırmanış: karakter bulunduğu kattaki uçtan merdivene biner, ClimbInput ile (+1 yukarı,
        /// -1 aşağı) eğim / el merdiveni boyunca ilerler; bırakınca merdivende durur, uca ulaşınca o kata iner.
        /// </summary>
        public void BeginManualClimb(LadderDef ladder)
        {
            if (building == null) return;
            ladder = building.WithProfile(ladder);
            float x = building.transform.position.x + ladder.x;
            int upperFloor = ladder.lowerFloor + 1;

            path.Clear();
            pathIndex = 0;
            steerInput = Vector2.zero;
            steerVelocity = Vector3.zero;

            climbLowerFloor = ladder.lowerFloor;
            climbIsStair = ladder.stair;
            climbLower = new Vector3(x, building.FloorY(ladder.lowerFloor), building.LadderStandZ(ladder, ladder.lowerFloor));
            climbUpper = new Vector3(x, building.FloorY(upperFloor), building.LadderStandZ(ladder, upperFloor));
            climbT = CurrentFloor > ladder.lowerFloor ? 1f : 0f;
            climbStartT = climbT;
            climbEntryOffset = transform.position - (climbT > 0.5f ? climbUpper : climbLower);
            climbEntryOffset.y = 0f;
            climbAxis = 0f;
            manualClimb = true;
        }

        /// <summary>Elle tırmanışın girdisi (her kare): +1 yukarı, -1 aşağı, 0 dur.</summary>
        public void ClimbInput(float axis)
        {
            climbAxis = Mathf.Clamp(axis, -1f, 1f);
            climbAxisFrame = Time.frameCount;
        }

        void UpdateManualClimb(float dt)
        {
            // Girdi gelmiyorsa (karakter seçimden çıktı) merdivende bekler.
            if (Time.frameCount - climbAxisFrame > 1) climbAxis = 0f;
            if (Mathf.Abs(climbAxis) < 0.05f) return;

            float length = Mathf.Max(0.1f, Vector3.Distance(climbLower, climbUpper));
            float speed = (climbIsStair ? walkSpeed * StairSpeedFactor : climbSpeed) * SpeedMultiplier;
            climbT += climbAxis * speed * dt / length;

            if (climbT >= 1f)
            {
                EndManualClimb(true);
                return;
            }
            if (climbT <= 0f)
            {
                EndManualClimb(false);
                return;
            }

            float fade = 1f - Mathf.Clamp01(Mathf.Abs(climbT - climbStartT) / 0.25f);
            var next = Vector3.Lerp(climbLower, climbUpper, climbT) + climbEntryOffset * fade;
            var moved = next - transform.position;
            transform.position = next;

            // Basamaklı merdivende gidilen yöne, el merdiveninde duvara bakar.
            Face(climbIsStair ? moved : Vector3.forward, dt);
        }

        void EndManualClimb(bool atUpper)
        {
            manualClimb = false;
            climbAxis = 0f;
            CurrentFloor = atUpper ? climbLowerFloor + 1 : climbLowerFloor;
            var end = atUpper ? climbUpper : climbLower;
            // Bindiği uçtan geri inerse bindiği yere döner.
            if ((atUpper ? 1f : 0f) == climbStartT) end += climbEntryOffset;
            end.y = building.FloorY(CurrentFloor);
            transform.position = end;
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

        // Aynı kattaki canlı kazazedelerden uzaklaştırır; merdivende, enkaz altında ve farklı katlarda işlemez.
        void Separate(float dt)
        {
            if (building == null || IsClimbing) return;

            var position = transform.position;
            var push = Vector3.zero;
            foreach (var other in Survivor.All)
            {
                if (other == null || other.IsDead || other.Mover == this || other.Mover == null) continue;
                var otherMover = other.Mover;
                if (otherMover.CurrentFloor != CurrentFloor || otherMover.IsClimbing || !other.isActiveAndEnabled) continue;

                var away = position - other.transform.position;
                away.y = 0f;
                float distance = away.magnitude;
                if (distance >= MinSeparation) continue;

                // Tam üst üsteyseler yön tutarlı olsun diye kimliğe göre bir yana kayarlar.
                away = distance > 0.001f ? away / distance : (GetInstanceID() > other.GetInstanceID() ? Vector3.forward : Vector3.back);
                push += away * (MinSeparation - distance);
            }

            if (push.sqrMagnitude < 0.00001f) return;

            var next = position + Vector3.ClampMagnitude(push * SeparationSpeed * dt, 0.08f);
            next.x = building.LimitWalk(CurrentFloor, position.x, next.x);
            next.z = building.ClampZ(next.z);
            next = FloorObstacles.PushOut(building, CurrentFloor, next, BodyRadius);
            next.y = position.y;
            transform.position = next;
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
