using System.Collections.Generic;
using System.Text;
using AftershockIsle.Building;
using AftershockIsle.CameraControl;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Economy;
using AftershockIsle.Skills;
using AftershockIsle.UI;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    public enum FieldOutcome { Idle, Active, Returned, TimeUp, Died }

    /// <summary>
    /// Oynanabilir dış saha: binanın uzağında, X (sağ-sol) ve Z (haritanın içine / ekrana doğru) düzleminde
    /// serbest bir sokak alanı. Kâşif doğrudan kontrol edilir (WASD ya da sol çubuk) ve CharacterController ile
    /// gerçek collider'lara çarpar: siper nesneleri (araba, beton blok, kasa yığını), sınırlar ve girilebilir bina
    /// (FieldBuilding; içeri girince çatı ve ön duvar gizlenir). Noktalar aranır (çanta slotu kadar ganimet), çıkıştan dönülür.
    /// Silah: nişan fare ya da sağ çubukla, ateş sol tık / RT; atış ışındır, duvar ve yüksek siper keser.
    /// Siper: çömelen (Shift / LT) alçak siperin arkasında görünmez; ateş edince bir an doğrulur. Yüksek siperin
    /// kenarında nişan kenarın ötesine dönünce kâşif köşeden eğilir; düşman sadece eğilen kısmı görür.
    /// Düşmanlar (FieldEnemy) görüş hattı ve sesle fark eder. Sığınak bu sırada çalışmaya devam eder; Tab/LB-RB ile
    /// görünüm değişir. Süre dolarsa kâşif kaçarak döner.
    /// Can, kâşifin kendi canıdır (Survivor.Health): sahada alınan hasar eve de taşınır.
    /// </summary>
    public class FieldExpedition : MonoBehaviour
    {
        const float FieldOffset = 80f;
        const float FieldDepth = 9f;
        const float FrontMargin = 3f;

        const float AvatarRadius = 0.35f;
        const float AvatarHeight = 1.9f;
        const float CrouchHeight = 1.2f;
        const float StandChest = 1.25f;
        const float CrouchChest = 0.75f;
        const float CrouchSpeed = 0.5f;
        const float RiseSeconds = 0.45f;
        const float LeanOffset = 0.6f;
        const float LeanProbe = 1.0f;
        const float BuildingDepth = 6.5f;
        const float WalkSpeed = 4.2f;
        // Derinlikte hareket perspektif yüzünden ekranda hızlı görünür; biraz yavaşlatılır.
        const float DepthSpeedFactor = 0.85f;
        const float Acceleration = 30f;
        const float TurnSpeed = 900f;
        const float DodgeDuration = 0.28f;
        const float DodgeSpeed = 10f;
        const float DodgeCooldown = 0.9f;

        const float InteractRange = 1.5f;
        const float ExitRange = 1.6f;
        const float SearchDuration = 2.5f;
        const float RescueDuration = 7f;
        const float HealAmount = 35f;
        const float HealCooldown = 3f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // Sadece yerleşim için: dışarıdaki nesneler birbirine ve binaya değmesin.
        struct Placement
        {
            public Vector2 Center;
            public Vector2 Half;
        }

        struct Tracer
        {
            public Transform Line;
            public float Remaining;
        }

        struct FloatingText
        {
            public string Text;
            public Vector3 Position;
            public float Age;
            public Color Color;
        }

        readonly List<FieldSpot> spots = new List<FieldSpot>();
        readonly List<Placement> placements = new List<Placement>();
        readonly List<FieldBuilding> buildings = new List<FieldBuilding>();
        readonly List<FieldEnemy> enemies = new List<FieldEnemy>();
        readonly Dictionary<Collider, FieldEnemy> enemyByCollider = new Dictionary<Collider, FieldEnemy>();
        readonly Dictionary<FieldEnemy, Transform> enemyAimLines = new Dictionary<FieldEnemy, Transform>();
        readonly List<Tracer> tracers = new List<Tracer>();
        readonly List<Vector3> coverPoints = new List<Vector3>();
        readonly List<Vector2> navCorners = new List<Vector2>();
        NavGrid nav;
        readonly HashSet<int> claimedCover = new HashSet<int>();
        readonly List<ResourceAmount> bag = new List<ResourceAmount>();
        readonly List<FloatingText> floaters = new List<FloatingText>();
        readonly StringBuilder reportText = new StringBuilder();
        MaterialPropertyBlock block;

        Survivor explorer;
        ExpeditionSite site;
        int bagSlots;
        int usedSlots;
        float extractionDuration;
        bool rescueAvailable;
        int extraRaiders;
        FieldArtLibrary art;

        WeaponKind weapon = WeaponKind.Slingshot;
        float fireCooldown;
        Vector3 aimDirection = Vector3.right;
        bool aiming;
        Transform aimLine;

        bool crouched;
        float riseTimer;
        float posture;      // 0 ayakta, 1 çömelik (görsel geçiş)
        int leanSide;       // -1 sol, 0 yok, +1 sağ (nişan yönüne göre)
        float leanAmount;   // görsel geçiş
        Vector3 leanPoint;
        bool inCover;
        readonly Collider[] coverProbe = new Collider[8];

        GameObject root;
        // avatarRoot: yerde duran, CharacterController taşıyan kök. avatar: gövde (merkez), kamera ve etiketler buna bakar.
        Transform avatarRoot;
        Transform avatar;
        CharacterController controller;
        Renderer avatarRenderer;
        float originX;
        float groundY;
        float minZ;
        float maxZ;
        Vector3 exitPoint;

        Vector3 velocity;
        float dodgeTimer;
        float dodgeCooldown;
        Vector3 dodgeDirection;
        FieldSpot searchTarget;
        float searchProgress;
        float healCooldown;
        float flashAvatar;
        string message;
        float messageTimer;

        GUIStyle centered;
        GUIStyle small;
        GUIStyle prompt;
        Camera cam;

        public FieldOutcome Outcome { get; private set; } = FieldOutcome.Idle;
        public bool IsActive => Outcome == FieldOutcome.Active && explorer != null;
        public float Remaining { get; private set; }
        public float TimeLimit => site != null ? site.duration : 1f;
        public bool RescueSecured { get; private set; }
        public IReadOnlyList<ResourceAmount> Bag => bag;
        public string Report => reportText.Length > 0 ? reportText.ToString() : null;
        public int UsedSlots => usedSlots;
        public int BagSlots => bagSlots;
        public bool AvatarIsDead => explorer == null || explorer.IsDead;
        public bool IsDodging => dodgeTimer > 0f;

        float MaxX => originX + site.fieldLength;

        bool InsideBuilding
        {
            get
            {
                foreach (var building in buildings)
                    if (building.ShowingInterior) return true;
                return false;
            }
        }

        // ---------------------------------------------------------------- Kurulum / bitiş

        public void Begin(Survivor who, ExpeditionSite expeditionSite, BuildingLayout building, int slots, float extraction,
                          bool canRescue, int day, FieldArtLibrary artLibrary)
        {
            art = artLibrary;
            explorer = who;
            site = expeditionSite;
            bagSlots = slots;
            extractionDuration = extraction;
            rescueAvailable = canRescue;
            Outcome = FieldOutcome.Active;
            Remaining = site.duration;
            RescueSecured = false;
            usedSlots = 0;
            bag.Clear();
            reportText.Clear();
            spots.Clear();
            placements.Clear();
            buildings.Clear();
            enemies.Clear();
            enemyByCollider.Clear();
            enemyAimLines.Clear();
            tracers.Clear();
            coverPoints.Clear();
            claimedCover.Clear();
            extraRaiders = Mathf.Clamp((day - 1) / 4, 0, 2);
            weapon = FieldWeapons.Best();
            fireCooldown = 0f;
            crouched = false;
            riseTimer = 0f;
            posture = 0f;
            leanSide = 0;
            leanAmount = 0f;
            floaters.Clear();
            velocity = Vector3.zero;
            dodgeTimer = 0f;
            dodgeCooldown = 0f;
            searchTarget = null;
            searchProgress = 0f;
            healCooldown = 0f;
            message = null;
            cam = Camera.main;

            originX = building.MaxX + FieldOffset;
            groundY = building.FloorY(1);
            minZ = building.WalkPlaneZ - FrontMargin;
            maxZ = minZ + FieldDepth;

            BuildField();
            SideViewCamera.SetFieldFocus(avatar);
            Say($"{GameInput.Prompt(InputPrompt.Move)}: yürü.  {GameInput.Prompt(InputPrompt.Interact)}: ara.  " +
                "Çanta dolunca soldaki yeşil çıkışa dön.");
        }

        /// <summary>Sahayı temizler ve kamerayı sığınağa döndürür.</summary>
        public void End()
        {
            SideViewCamera.SetFieldFocus(null);
            if (root != null) Destroy(root);
            root = null;
            explorer = null;
            Outcome = FieldOutcome.Idle;
        }

        void BuildField()
        {
            root = new GameObject("Dış Saha");
            root.transform.SetParent(transform, false);

            float length = site.fieldLength;
            float midZ = (minZ + maxZ) * 0.5f;

            var ground = Make(PrimitiveType.Cube, "Zemin", new Color(0.32f, 0.3f, 0.28f), root.transform);
            ground.position = new Vector3(originX + length * 0.5f, groundY - 0.3f, midZ);
            ground.localScale = new Vector3(length + 16f, 0.6f, FieldDepth + 10f);

            // Yürünebilir alanın kenarı: önde alçak kaldırım, arkada yıkık yapılar, sağda duvar.
            var curb = Make(PrimitiveType.Cube, "Kaldırım", new Color(0.45f, 0.44f, 0.42f), root.transform);
            curb.position = new Vector3(originX + length * 0.5f, groundY + 0.08f, minZ - 0.5f);
            curb.localScale = new Vector3(length + 2f, 0.16f, 0.4f);

            for (int i = 0; i < 10; i++)
            {
                var ruin = Make(PrimitiveType.Cube, "Harabe", new Color(0.22f, 0.22f, 0.24f), root.transform);
                float height = Random.Range(2.5f, 7f);
                ruin.position = new Vector3(originX - 4f + i * (length + 8f) / 9f, groundY + height * 0.5f, maxZ + 1.8f);
                ruin.localScale = new Vector3(Random.Range(2.5f, 4.5f), height, 2f);
            }

            var wall = Make(PrimitiveType.Cube, "Sınır", new Color(0.18f, 0.18f, 0.2f), root.transform, true);
            wall.position = new Vector3(MaxX + 1f, groundY + 1.5f, midZ);
            wall.localScale = new Vector3(0.6f, 3f, FieldDepth + 2f);

            BuildBounds(length, midZ);

            exitPoint = new Vector3(originX + 0.6f, groundY, midZ);
            BuildExit(midZ);
            BuildBuilding(length);
            BuildSpots(length);
            BuildObstacles(length);
            Physics.SyncTransforms();
            BuildCoverPoints();
            BuildNavGrid();
            BuildEnemies(length);
            BuildAvatar(midZ);
        }

        // Görünmez sınırlar: ön kaldırım, arka harabeler ve çıkışın gerisi.
        void BuildBounds(float length, float midZ)
        {
            Bound(new Vector3(originX + length * 0.5f, groundY + 1f, minZ - AvatarRadius - 0.25f), new Vector3(length + 4f, 2f, 0.5f));
            Bound(new Vector3(originX + length * 0.5f, groundY + 1f, maxZ + AvatarRadius + 0.25f), new Vector3(length + 4f, 2f, 0.5f));
            Bound(new Vector3(originX - 0.8f, groundY + 1f, midZ), new Vector3(0.5f, 2f, FieldDepth + 2f));
        }

        void Bound(Vector3 center, Vector3 size)
        {
            var bound = new GameObject("Sınır (görünmez)").AddComponent<BoxCollider>();
            bound.transform.SetParent(root.transform, false);
            bound.transform.position = center;
            bound.size = size;
        }

        // Sahanın ortasında, arka kenara dayalı girilebilir yapı. Önünde kapıya giden yol boş bırakılır.
        void BuildBuilding(float length)
        {
            float width = FieldBuilding.WidthFor(site.layout);
            float depth = Mathf.Min(BuildingDepth, FieldDepth - 2.5f);
            var min = new Vector2(originX + length * 0.42f - width * 0.5f, maxZ - depth + AvatarRadius);
            var building = new FieldBuilding(
                (type, name, color, solid) => Make(type, name, color, root.transform, solid),
                Dress, min, new Vector2(width, depth - AvatarRadius), groundY, site.layout);
            buildings.Add(building);

            var footprint = building.Footprint;
            AddPlacement(footprint.center, footprint.size * 0.5f);
            AddPlacement(building.FrontDoor, new Vector2(1.2f, 1.4f));
        }

        /// <summary>Kutuya model giydirir (kütüphane yoksa ya da model eksikse kutu kalır).</summary>
        Renderer[] Dress(Transform box, ArtKey key, float yaw) => art != null ? art.Dress(box, key, yaw) : null;

        void BuildExit(float midZ)
        {
            var color = new Color(0.25f, 0.8f, 0.4f);
            for (int side = -1; side <= 1; side += 2)
            {
                var post = Make(PrimitiveType.Cube, "Çıkış Direği", color, root.transform);
                post.position = new Vector3(originX - 0.3f, groundY + 1.2f, midZ + side * 1.3f);
                post.localScale = new Vector3(0.3f, 2.4f, 0.3f);
            }

            var beam = Make(PrimitiveType.Cube, "Çıkış", color, root.transform);
            beam.position = new Vector3(originX - 0.3f, groundY + 2.4f, midZ);
            beam.localScale = new Vector3(0.3f, 0.25f, 2.9f);

            var pad = Make(PrimitiveType.Cube, "Çıkış Alanı", new Color(0.2f, 0.55f, 0.3f), root.transform);
            pad.position = new Vector3(exitPoint.x, groundY + 0.01f, midZ);
            pad.localScale = new Vector3(1.6f, 0.02f, 2.6f);
        }

        void BuildSpots(float length)
        {
            int count = Mathf.Max(1, site.lootSpots);
            var building = buildings.Count > 0 ? buildings[0] : null;
            var slots = building != null ? building.SpotSlots : new List<FieldBuilding.SpotSlot>();

            // Değerli olanın yarısı binanın içinde: içerisi dar ama ganimet orada.
            int inside = Mathf.Min(slots.Count - (site.isRescueSite ? 1 : 0), Mathf.Max(1, count / 2));
            for (int i = 0; i < inside; i++) AddLootSpot(slots[i].Position, slots[i].Size, slots[i].Art, slots[i].Yaw);

            int outside = count - inside;
            float start = originX + 5f;
            float span = length - 7f;
            var crateSize = new Vector3(0.9f, 0.8f, 0.9f);
            for (int i = 0; i < outside; i++)
            {
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    float x = start + span * (outside == 1 ? 0.5f : i / (float)(outside - 1)) + Random.Range(-2f, 2f);
                    var center = new Vector2(x, Random.Range(minZ + 0.9f, maxZ - 0.9f));
                    if (!IsFree(center, new Vector2(0.55f, 0.45f), 0.8f)) continue;

                    AddLootSpot(new Vector3(center.x, groundY, center.y), crateSize, ArtKey.Crate, Random.Range(0f, 360f));
                    break;
                }
            }

            if (!site.isRescueSite) return;

            // Mahsur kalan binanın içinde (yoksa sahanın sonunda).
            var rescueAt = building != null && slots.Count > 0
                ? slots[slots.Count - 1].Position
                : new Vector3(MaxX - 3f, groundY, Random.Range(minZ + 1.5f, maxZ - 1.5f));
            var victim = Make(PrimitiveType.Capsule, "Mahsur Kalan", new Color(0.4f, 0.85f, 0.95f), root.transform);
            victim.position = rescueAt + Vector3.up * 0.35f;
            victim.localScale = new Vector3(0.7f, 0.5f, 0.7f);
            victim.rotation = Quaternion.Euler(0f, 0f, 90f);
            spots.Add(new FieldSpot(victim, FieldSpotKind.Rescue, extractionDuration > 0f ? extractionDuration : RescueDuration, "Mahsur kalan"));
        }

        void AddLootSpot(Vector3 groundPoint, Vector3 size, ArtKey artKey, float yaw)
        {
            var visual = Make(PrimitiveType.Cube, "Aranacak Nokta", new Color(0.55f, 0.38f, 0.2f), root.transform, true);
            visual.position = groundPoint + Vector3.up * (size.y * 0.5f);
            visual.localScale = size;

            var spot = new FieldSpot(visual, FieldSpotKind.Loot, SearchDuration, "Aranacak yer");
            // Dışarıdaki kasalar rastgele dönük; kutu eksenle hizalı kalır, model içinde döner.
            spot.UseModel(Dress(visual, artKey, artKey == ArtKey.Crate ? Mathf.Round(yaw / 90f) * 90f : yaw));
            spots.Add(spot);
            AddPlacement(new Vector2(groundPoint.x, groundPoint.z), new Vector2(size.x, size.z) * 0.5f);
        }

        // Siperlik nesneler. Siper alma sonraki aşamada bunlara bağlanacak; şimdilik yolu kesip rota seçtiren engeller.
        void BuildObstacles(float length)
        {
            int target = Mathf.RoundToInt(length / 5f);
            int placed = 0;

            for (int attempt = 0; attempt < target * 12 && placed < target; attempt++)
            {
                int kind = Random.Range(0, 4);
                bool turned = Random.value < 0.35f;
                Vector2 half = kind switch
                {
                    0 => new Vector2(1.8f, 0.85f),   // araba
                    1 => new Vector2(1.1f, 0.35f),   // beton blok
                    2 => new Vector2(0.6f, 0.6f),    // kasa yığını
                    _ => new Vector2(0.8f, 0.65f),   // moloz yığını
                };
                if (turned) half = new Vector2(half.y, half.x);

                var center = new Vector2(Random.Range(originX + 5.5f, MaxX - 1.5f), Random.Range(minZ + 0.6f, maxZ - 0.6f));
                if (!IsFree(center, half, 1.3f)) continue;

                BuildObstacleVisual(kind, center, half, turned);
                placed++;
            }
        }

        bool IsFree(Vector2 center, Vector2 half, float gap)
        {
            foreach (var other in placements)
            {
                if (Mathf.Abs(center.x - other.Center.x) < half.x + other.Half.x + gap &&
                    Mathf.Abs(center.y - other.Center.y) < half.y + other.Half.y + gap) return false;
            }
            return true;
        }

        void BuildObstacleVisual(int kind, Vector2 center, Vector2 half, bool turned)
        {
            switch (kind)
            {
                case 0:
                {
                    var body = Make(PrimitiveType.Cube, "Araba", new Color(0.3f, 0.36f, 0.45f), root.transform, true);
                    body.position = new Vector3(center.x, groundY + 0.45f, center.y);
                    body.localScale = new Vector3(half.x * 2f, 0.7f, half.y * 2f);
                    var cabin = Make(PrimitiveType.Cube, "Kabin", new Color(0.25f, 0.3f, 0.38f), root.transform, true);
                    cabin.position = new Vector3(center.x, groundY + 1.05f, center.y);
                    cabin.localScale = turned
                        ? new Vector3(half.x * 1.8f, 0.55f, half.y * 1.1f)
                        : new Vector3(half.x * 1.1f, 0.55f, half.y * 1.8f);
                    break;
                }
                case 1:
                {
                    const float height = 1.0f;
                    var slab = Make(PrimitiveType.Cube, "Beton Blok", new Color(0.55f, 0.55f, 0.52f), root.transform, true);
                    slab.position = new Vector3(center.x, groundY + height * 0.5f, center.y);
                    slab.localScale = new Vector3(half.x * 2f, height, half.y * 2f);
                    break;
                }
                case 2:
                {
                    for (int level = 0; level < 2; level++)
                    {
                        var crate = Make(PrimitiveType.Cube, "Kasa", new Color(0.42f, 0.3f, 0.18f), root.transform, true);
                        crate.position = new Vector3(center.x + (level == 1 ? 0.08f : 0f), groundY + 0.42f + level * 0.85f, center.y);
                        crate.localScale = new Vector3(half.x * 2f - level * 0.15f, 0.84f, half.y * 2f - level * 0.15f);
                        Dress(crate, ArtKey.Crate, level * 90f);
                    }
                    break;
                }
                default:
                {
                    // Alçak siper: çömelince korur.
                    var rubble = Make(PrimitiveType.Cube, "Moloz Yığını", new Color(0.45f, 0.42f, 0.38f), root.transform, true);
                    rubble.position = new Vector3(center.x, groundY + 0.5f, center.y);
                    rubble.localScale = new Vector3(half.x * 2f, 1f, half.y * 2f);
                    Dress(rubble, ArtKey.Rubble, turned ? 90f : 0f);
                    break;
                }
            }

            AddPlacement(center, half);
        }

        void AddPlacement(Vector2 center, Vector2 half) =>
            placements.Add(new Placement { Center = center, Half = half });

        void BuildEnemies(float length)
        {
            var kinds = new List<FieldEnemyKind>();
            for (int i = 0; i < site.dogs; i++) kinds.Add(FieldEnemyKind.Dog);
            for (int i = 0; i < site.raiders + extraRaiders; i++) kinds.Add(FieldEnemyKind.Raider);
            for (int i = 0; i < site.brutes; i++) kinds.Add(FieldEnemyKind.Brute);
            for (int i = 0; i < site.throwers; i++) kinds.Add(FieldEnemyKind.Gunner);
            for (int i = kinds.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
            }

            var building = buildings.Count > 0 ? buildings[0] : null;
            for (int i = 0; i < kinds.Count; i++)
            {
                // Biri binanın içinde bekler (ganimetin yanında); diğerleri sokağa dağılır, çıkışa yakın yer güvenli.
                Vector3 position;
                if (i == 0 && building != null && kinds.Count >= 2)
                {
                    var interior = building.Interior;
                    position = new Vector3(interior.center.x - 1f, groundY, interior.center.y + 0.8f);
                }
                else if (!TryFindFree(originX + 9f, MaxX - 1.5f, 0.6f, out position))
                {
                    continue;
                }

                SpawnEnemy(kinds[i], position);
            }
        }

        bool TryFindFree(float fromX, float toX, float radius, out Vector3 position)
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                var center = new Vector2(Random.Range(fromX, toX), Random.Range(minZ + 0.8f, maxZ - 0.8f));
                bool insideBuilding = false;
                foreach (var building in buildings)
                    if (building.Footprint.Contains(center)) insideBuilding = true;
                if (insideBuilding || !IsFree(center, Vector2.one * radius, 0.4f)) continue;

                AddPlacement(center, Vector2.one * radius);
                position = new Vector3(center.x, groundY, center.y);
                return true;
            }
            position = default;
            return false;
        }

        void SpawnEnemy(FieldEnemyKind kind, Vector3 position)
        {
            var enemyRoot = new GameObject(kind.ToString()).transform;
            enemyRoot.SetParent(root.transform, false);
            enemyRoot.SetPositionAndRotation(position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

            bool dog = kind == FieldEnemyKind.Dog;
            bool brute = kind == FieldEnemyKind.Brute;
            float height = dog ? 0.8f : brute ? 2.2f : 1.8f;
            float radius = brute ? 0.5f : 0.35f;

            var enemyController = enemyRoot.gameObject.AddComponent<CharacterController>();
            enemyController.radius = radius;
            enemyController.height = height;
            enemyController.center = new Vector3(0f, height * 0.5f + 0.02f, 0f);
            enemyController.stepOffset = 0.1f;
            enemyController.skinWidth = 0.03f;

            var body = Make(dog ? PrimitiveType.Cube : PrimitiveType.Capsule, "Gövde", FieldEnemy.ColorFor(kind), enemyRoot);
            body.localRotation = Quaternion.identity;
            body.localScale = dog ? new Vector3(0.45f, 0.55f, 0.95f) : new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            body.localPosition = new Vector3(0f, dog ? 0.35f : height * 0.5f, 0f);

            var face = Make(PrimitiveType.Cube, "Yüz", new Color(0.15f, 0.12f, 0.1f), body);
            face.localPosition = new Vector3(0f, dog ? 0.2f : 0.3f, 0.5f);
            face.localRotation = Quaternion.identity;
            face.localScale = new Vector3(0.5f, 0.15f, 0.2f);

            var enemy = new FieldEnemy(this, kind, enemyRoot, body.GetComponent<Renderer>(), enemyController);
            enemies.Add(enemy);
            enemyByCollider[enemyController] = enemy;
        }

        void BuildAvatar(float midZ)
        {
            avatarRoot = new GameObject(explorer.DisplayName).transform;
            avatarRoot.SetParent(root.transform, false);
            avatarRoot.SetPositionAndRotation(new Vector3(originX + 2f, groundY, midZ), Quaternion.LookRotation(Vector3.right));

            controller = avatarRoot.gameObject.AddComponent<CharacterController>();
            controller.radius = AvatarRadius;
            controller.height = AvatarHeight;
            controller.center = new Vector3(0f, AvatarHeight * 0.5f + 0.02f, 0f);
            controller.stepOffset = 0.1f;
            controller.skinWidth = 0.03f;

            var body = Make(PrimitiveType.Capsule, "Gövde", BranchColor(explorer), avatarRoot);
            body.localPosition = new Vector3(0f, 0.95f, 0f);
            body.localRotation = Quaternion.identity;
            body.localScale = new Vector3(0.7f, 0.95f, 0.7f);
            avatar = body;
            avatarRenderer = body.GetComponent<Renderer>();

            var nose = Make(PrimitiveType.Cube, "Yön", Color.white, body);
            nose.localPosition = new Vector3(0f, 0.3f, 0.55f);
            nose.localRotation = Quaternion.identity;
            nose.localScale = new Vector3(0.3f, 0.2f, 0.5f);
        }

        static Color BranchColor(Survivor survivor) => survivor.Branch switch
        {
            SkillBranch.Construction => new Color(0.95f, 0.55f, 0.2f),
            SkillBranch.Exploration => new Color(0.35f, 0.8f, 0.35f),
            _ => new Color(0.3f, 0.6f, 0.95f),
        };

        /// <summary>Renkli ilkel nesne. solid: collider'ı kalır (kâşif çarpar; ileride mermi ve görüş hattını keser).</summary>
        Transform Make(PrimitiveType type, string objectName, Color color, Transform parent, bool solid = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = objectName;
            var collider = go.GetComponent<Collider>();
            if (collider != null && !solid)
            {
                // Destroy kare sonunda çalışır; aynı karede yapılan fizik sorgularına (siper, yol ızgarası) karışmasın.
                collider.enabled = false;
                Destroy(collider);
            }
            go.transform.SetParent(parent, true);
            Tint(go.GetComponent<Renderer>(), color);
            return go.transform;
        }

        public void Tint(Renderer target, Color color)
        {
            block ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            target.SetPropertyBlock(block);
        }

        // ---------------------------------------------------------------- Oyun döngüsü

        void Update()
        {
            if (!IsActive) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Remaining -= dt;
            healCooldown = Mathf.Max(0f, healCooldown - dt);
            dodgeCooldown = Mathf.Max(0f, dodgeCooldown - dt);
            messageTimer -= dt;
            if (Remaining <= 0f)
            {
                Outcome = FieldOutcome.TimeUp;
                return;
            }

            // Sığınağa bakarken kâşif yerinde bekler (karakter kontrolü sadece saha görünümünde).
            bool controlling = SideViewCamera.ViewingField && !ModalMenu.IsOpen;
            Vector2 input = controlling ? GameInput.CharacterMove : Vector2.zero;

            fireCooldown -= dt;
            UpdatePosture(controlling, dt);
            UpdateAim(controlling);
            UpdateLean(controlling, dt);

            if (controlling)
            {
                if (GameInput.DodgePressed) TryDodge(input);
                if (GameInput.HealPressed) UseMedkit();
                if (GameInput.InteractPressed) Interact();
                if (GameInput.SwitchWeaponPressed) SwitchWeapon();
                if (GameInput.FirePressed) TryFire();
            }

            UpdateMovement(input, dt);
            foreach (var enemy in enemies) enemy.Tick(dt);
            UpdateTracers(dt);
            UpdateSearch(input, dt);
            UpdateFlash(dt);
            foreach (var spot in spots) spot.Refresh(Tint);
            foreach (var building in buildings) building.UpdateCutaway(avatarRoot.position);
            UpdateFloaters(dt);

            if (explorer.IsDead) Outcome = FieldOutcome.Died;
        }

        void UpdateMovement(Vector2 input, float dt)
        {
            float speed = WalkSpeed * explorer.Condition.SpeedMultiplier * SkillTree.MoveSpeedBonus * (crouched ? CrouchSpeed : 1f);

            if (IsDodging)
            {
                dodgeTimer -= dt;
                velocity = dodgeDirection * DodgeSpeed;
                if (!IsDodging) Tint(avatarRenderer, BranchColor(explorer));
            }
            else
            {
                var desired = new Vector3(input.x, 0f, input.y * DepthSpeedFactor) * speed;
                velocity = Vector3.MoveTowards(velocity, desired, Acceleration * dt);
            }

            controller.Move(velocity * dt);

            // Nişan alırken nişan yönüne bakar (geri geri yürüyerek ateş edilebilir), almıyorsa yürüdüğü yöne.
            var flat = aiming ? aimDirection : new Vector3(velocity.x, 0f, velocity.z);
            if (flat.sqrMagnitude > 0.05f)
                avatarRoot.rotation = Quaternion.RotateTowards(avatarRoot.rotation, Quaternion.LookRotation(flat), TurnSpeed * dt);

            // Çarpışma kayması kökü hafifçe yukarı itebilir; saha düz, yükseklik sabit tutulur.
            if (Mathf.Abs(avatarRoot.position.y - groundY) > 0.01f)
            {
                controller.enabled = false;
                avatarRoot.position = new Vector3(avatarRoot.position.x, groundY, avatarRoot.position.z);
                controller.enabled = true;
            }
        }

        void TryDodge(Vector2 input)
        {
            if (IsDodging || dodgeCooldown > 0f) return;

            dodgeDirection = input.sqrMagnitude > 0.04f ? new Vector3(input.x, 0f, input.y) : avatarRoot.forward;
            dodgeDirection.y = 0f;
            dodgeDirection.Normalize();

            dodgeTimer = DodgeDuration;
            dodgeCooldown = DodgeCooldown;
            CancelSearch(false);
            Tint(avatarRenderer, new Color(0.75f, 0.9f, 1f));
        }

        // ---------------------------------------------------------------- Silah

        public Vector3 AvatarFeet => avatarRoot != null ? avatarRoot.position : Vector3.zero;
        public Vector3 AvatarChest => AvatarFeet + Vector3.up * (CrouchedLow ? CrouchChest : StandChest);

        /// <summary>Düşmanın görüp nişan aldığı nokta: köşeden eğiliyorsa kenarın ötesindeki omuz, değilse göğüs.</summary>
        public Vector3 AvatarTarget => leanSide != 0 ? leanPoint : AvatarChest;

        /// <summary>Sakin düşmanın görüş mesafesi çarpanı: çömelik kâşif daha geç fark edilir.</summary>
        public float StealthFactor => CrouchedLow ? 0.6f : 1f;

        /// <summary>Çömelik ve ateş için doğrulmamış: alçak siperin arkasında görünmez.</summary>
        bool CrouchedLow => crouched && riseTimer <= 0f;

        // Ateş hep ayakta (ya da köşeden eğilmiş) hizadan çıkar: çömelikken ateş edince zaten doğrulur.
        Vector3 Muzzle => (leanSide != 0 ? leanPoint : AvatarFeet + Vector3.up * StandChest) + aimDirection * 0.4f;
        WeaponInfo CurrentWeapon => FieldWeapons.Get(weapon);

        // Gamepad: sağ çubuk; fare: imlecin göğüs hizasındaki düzlemde gösterdiği nokta. İkisi de yoksa baktığı yön.
        void UpdateAim(bool controlling)
        {
            aiming = false;
            if (controlling)
            {
                var stick = GameInput.AimStick;
                if (stick != Vector2.zero)
                {
                    aimDirection = new Vector3(stick.x, 0f, stick.y).normalized;
                    aiming = true;
                }
                else if (!GameInput.UsingGamepad && cam != null)
                {
                    var ray = cam.ScreenPointToRay(GameInput.PointerPosition);
                    float planeY = AvatarChest.y;
                    if (Mathf.Abs(ray.direction.y) > 0.0001f)
                    {
                        float t = (planeY - ray.origin.y) / ray.direction.y;
                        var flat = ray.GetPoint(t) - AvatarChest;
                        flat.y = 0f;
                        if (t > 0f && flat.sqrMagnitude > 0.04f)
                        {
                            aimDirection = flat.normalized;
                            aiming = true;
                        }
                    }
                }
            }
            if (!aiming) aimDirection = avatarRoot.forward;

            UpdateAimLine(controlling);
        }

        void UpdateAimLine(bool show)
        {
            if (aimLine == null) aimLine = Make(PrimitiveType.Cube, "Nişan Çizgisi", new Color(1f, 0.3f, 0.25f), root.transform);
            aimLine.gameObject.SetActive(show && aiming);
            if (!aimLine.gameObject.activeSelf) return;

            float range = CurrentWeapon.Range;
            float length = Physics.Raycast(Muzzle, aimDirection, out var hit, range, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : range;
            PlaceLine(aimLine, Muzzle, aimDirection, length, 0.03f);
        }

        static void PlaceLine(Transform line, Vector3 from, Vector3 direction, float length, float thickness)
        {
            line.position = from + direction * (length * 0.5f);
            line.rotation = Quaternion.LookRotation(direction);
            line.localScale = new Vector3(thickness, thickness, length);
        }

        // ---------------------------------------------------------------- Siper

        void UpdatePosture(bool controlling, float dt)
        {
            riseTimer = Mathf.Max(0f, riseTimer - dt);
            bool wantCrouch = controlling && GameInput.CrouchHeld && !IsDodging;
            if (wantCrouch != crouched)
            {
                crouched = wantCrouch;
                if (!crouched) riseTimer = 0f;
                controller.height = crouched ? CrouchHeight : AvatarHeight;
                controller.center = new Vector3(0f, controller.height * 0.5f + 0.02f, 0f);
            }

            float targetPosture = CrouchedLow ? 1f : 0f;
            posture = Mathf.MoveTowards(posture, targetPosture, dt * 6f);

            // Yanında bir engel varsa "siperde": çömelikken alçak, ayaktayken yüksek engel yeterli.
            float probeHeight = crouched ? 0.5f : 1.2f;
            int count = Physics.OverlapSphereNonAlloc(AvatarFeet + Vector3.up * probeHeight, AvatarRadius + 0.35f,
                coverProbe, ~0, QueryTriggerInteraction.Ignore);
            inCover = false;
            for (int i = 0; i < count; i++)
                if (coverProbe[i] != controller && !enemyByCollider.ContainsKey(coverProbe[i])) inCover = true;
        }

        // Yüksek bir engele bakarak nişan alırken (önü 1 m içinde kapalı) yana 0.6 m'lik nokta açıksa oradan eğilir.
        void UpdateLean(bool controlling, float dt)
        {
            int side = 0;
            if (controlling && aiming && !IsDodging && !CrouchedLow)
            {
                var chest = AvatarFeet + Vector3.up * StandChest;
                if (Physics.Raycast(chest, aimDirection, out var hit, LeanProbe, ~0, QueryTriggerInteraction.Ignore) &&
                    hit.collider != controller && !enemyByCollider.ContainsKey(hit.collider))
                {
                    var right = Vector3.Cross(Vector3.up, aimDirection).normalized;
                    // Önce mevcut tarafı dene: köşede iki yön de açıksa titreme olmasın.
                    int first = leanSide != 0 ? leanSide : 1;
                    for (int i = 0; i < 2 && side == 0; i++)
                    {
                        int candidate = i == 0 ? first : -first;
                        var point = chest + right * (candidate * LeanOffset);
                        if (Physics.CheckSphere(point, 0.15f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        if (Physics.Raycast(point, aimDirection, LeanProbe, ~0, QueryTriggerInteraction.Ignore)) continue;
                        if (Physics.Linecast(chest, point, ~0, QueryTriggerInteraction.Ignore)) continue;
                        side = candidate;
                        leanPoint = point;
                    }
                }
            }

            leanSide = side;
            leanAmount = Mathf.MoveTowards(leanAmount, side, dt * 6f);

            // Görsel: gövde yana kayıp eğilir, çömelince alçalır.
            float height = Mathf.Lerp(0.95f, 0.62f, posture);
            avatar.localPosition = new Vector3(leanAmount * 0.45f, height, 0f);
            avatar.localRotation = Quaternion.Euler(0f, 0f, -leanAmount * 15f);
            avatar.localScale = new Vector3(0.7f, Mathf.Lerp(0.95f, 0.62f, posture), 0.7f);
        }

        void SwitchWeapon()
        {
            var next = FieldWeapons.Next(weapon);
            if (next == weapon)
            {
                Say("Başka silah yok: tezgahta (Kademe 2) tabanca ya da pompalı üret.");
                return;
            }
            weapon = next;
            Say($"Silah: {CurrentWeapon.Name}");
        }

        void TryFire()
        {
            if (fireCooldown > 0f || IsDodging || searchTarget != null) return;

            var info = CurrentWeapon;

            // Çömelikken ateş: siperin üstünden atmak için bir an doğrulur (o sürede açıkta).
            if (crouched) riseTimer = RiseSeconds;

            if (info.UsesAmmo && (Workbench.Instance == null || !Workbench.Instance.TryUseAmmo()))
            {
                Say("Mermi yok! Tezgahta Mermi Paketi üret ya da sapana geç.");
                fireCooldown = 0.3f;
                return;
            }

            fireCooldown = info.Interval;
            var from = Muzzle;

            // Her saçma ayrı ışın; aynı düşmana değenlerin hasarı toplanıp tek seferde uygulanır (tek sayı, tek geri itme).
            pelletHits.Clear();
            for (int pellet = 0; pellet < info.Pellets; pellet++)
            {
                var direction = Quaternion.Euler(0f, Random.Range(-info.Spread, info.Spread), 0f) * aimDirection;
                var enemy = FirePellet(from, direction, info.Range);
                if (enemy == null) continue;

                pelletHits.TryGetValue(enemy, out float total);
                pelletHits[enemy] = total + info.Damage;
            }

            bool killed = false;
            foreach (var pair in pelletHits)
            {
                pair.Key.TakeHit(pair.Value, AvatarFeet, info.Knockback);
                Float($"-{pair.Value:0}", pair.Key.AimPoint + Vector3.up * 0.6f, Color.white);
                killed |= pair.Key.IsDead;
            }

            // Vuruş hissi: geri tepme sarsıntısı ve titreşim; isabette kısa duraklama, öldürmede daha uzun.
            SideViewCamera.Kick(0.05f + info.Recoil * 0.15f, 0.12f);
            GameInput.Rumble(info.Recoil * 0.6f, 0.2f + info.Recoil * 0.4f, 0.08f + info.Recoil * 0.1f);
            if (pelletHits.Count > 0) TimeManager.HitStop(killed ? 0.09f : 0.035f + info.Recoil * 0.03f);

            if (info.Noise > 0f) MakeNoise(AvatarFeet, info.Noise);
        }

        readonly Dictionary<FieldEnemy, float> pelletHits = new Dictionary<FieldEnemy, float>();

        // Nişan yatayda verilir; yükseklik hedefe göre ayarlanır (köpek, çömelmiş düşman alçakta). Saçma doğrultusuna
        // en yakın düşmanın gövdesine gider; arada duvar ya da siper varsa orada kalır. İsabet ettiği düşmanı döndürür.
        FieldEnemy FirePellet(Vector3 from, Vector3 direction, float range)
        {
            var target = TargetAlong(from, direction, range);
            var aimPoint = target != null ? target.AimPoint : from + direction * range;
            var shot = (aimPoint - from).normalized;
            float length = Vector3.Distance(from, aimPoint);

            FieldEnemy struck = null;
            if (Physics.Raycast(from, shot, out var hit, length + 0.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                length = hit.distance;
                if (enemyByCollider.TryGetValue(hit.collider, out var enemy) && !enemy.IsDead) struck = enemy;
            }

            ShowTracer(from, shot, length, new Color(1f, 0.9f, 0.5f));
            return struck;
        }

        // Yatay nişan çizgisine 0.5 m'den yakın geçen, menzildeki en yakın canlı düşman.
        FieldEnemy TargetAlong(Vector3 from, Vector3 direction, float range)
        {
            var flatDirection = FlatXZ(direction).normalized;
            FieldEnemy best = null;
            float bestAlong = range;
            foreach (var enemy in enemies)
            {
                if (enemy.IsDead) continue;

                var offset = FlatXZ(enemy.Position - from);
                float along = Vector3.Dot(offset, flatDirection);
                if (along <= 0f || along >= bestAlong) continue;

                float lateral = (offset - flatDirection * along).magnitude;
                if (lateral > enemy.Radius + 0.25f) continue;

                best = enemy;
                bestAlong = along;
            }
            return best;
        }

        void ShowTracer(Vector3 from, Vector3 direction, float length, Color color)
        {
            var line = Make(PrimitiveType.Cube, "İz", color, root.transform);
            PlaceLine(line, from, direction, length, 0.05f);
            tracers.Add(new Tracer { Line = line, Remaining = 0.06f });
        }

        void UpdateTracers(float dt)
        {
            for (int i = tracers.Count - 1; i >= 0; i--)
            {
                var tracer = tracers[i];
                tracer.Remaining -= dt;
                if (tracer.Remaining > 0f)
                {
                    tracers[i] = tracer;
                    continue;
                }
                if (tracer.Line != null) Destroy(tracer.Line.gameObject);
                tracers.RemoveAt(i);
            }
        }

        /// <summary>Atış sesi: yarıçaptaki düşmanlar sesin geldiği yere bakmaya gider.</summary>
        void MakeNoise(Vector3 position, float radius)
        {
            foreach (var enemy in enemies)
                if (!enemy.IsDead && Vector3.Distance(enemy.Position, position) <= radius) enemy.Hear(position);
        }

        // ---------------------------------------------------------------- Düşmanların kullandığı

        /// <summary>
        /// from'dan to'ya engellerin (araba, kasa, bina duvarları) etrafından ve kapılardan geçen ara köşeleri yazar
        /// (from ve to hariç). Doğrudan yol açıksa liste boş kalır. Hedefe ulaşılamıyorsa en yakın noktaya kadar yazar, false döner.
        /// </summary>
        public bool FindRoute(Vector3 from, Vector3 to, List<Vector3> result)
        {
            result.Clear();
            if (nav == null) return true;

            bool reached = nav.FindPath(new Vector2(from.x, from.z), new Vector2(to.x, to.z), navCorners);
            foreach (var corner in navCorners) result.Add(new Vector3(corner.x, groundY, corner.y));
            return reached;
        }

        // Yürünebilirlik ızgarası: gövde boyunda bir kapsülün değdiği (karakterler hariç) her hücre dolu sayılır.
        void BuildNavGrid()
        {
            const float cell = 0.35f;
            const float radius = 0.38f;
            var hits = new Collider[16];

            nav = new NavGrid(new Vector2(originX - 1f, minZ), new Vector2(MaxX + 0.5f, maxZ), cell);
            nav.Fill(point =>
            {
                var bottom = new Vector3(point.x, groundY + radius + 0.05f, point.y);
                var top = new Vector3(point.x, groundY + 1.5f, point.y);
                int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, hits, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                    if (!(hits[i] is CharacterController)) return true;
                return false;
            });
        }

        FieldBuilding BuildingAt(Vector3 point)
        {
            foreach (var building in buildings)
                if (building.IsInside(point)) return building;
            return null;
        }

        static Vector3 FlatXZ(Vector3 vector) => new Vector3(vector.x, 0f, vector.z);

        // Siper noktaları: her engelin (araba, blok, kasa, duvar) dört yanında, içine girilmeyen noktalar.
        void BuildCoverPoints()
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>())
            {
                if (collider is CharacterController || collider.gameObject.name.StartsWith("Sınır")) continue;

                var bounds = collider.bounds;
                if (bounds.size.y < 0.8f) continue;

                var center = bounds.center;
                float offsetX = bounds.extents.x + 0.6f;
                float offsetZ = bounds.extents.z + 0.6f;
                AddCoverAlong(center, Vector3.right, offsetX, bounds.extents.z);
                AddCoverAlong(center, Vector3.left, offsetX, bounds.extents.z);
                AddCoverAlong(center, Vector3.forward, offsetZ, bounds.extents.x);
                AddCoverAlong(center, Vector3.back, offsetZ, bounds.extents.x);
            }
        }

        // Uzun duvarlarda yüz boyunca birkaç nokta.
        void AddCoverAlong(Vector3 center, Vector3 normal, float offset, float halfLength)
        {
            var tangent = Vector3.Cross(Vector3.up, normal);
            int count = Mathf.Clamp(Mathf.RoundToInt(halfLength / 1.5f), 0, 3);
            for (int i = -count; i <= count; i++)
            {
                var point = center + normal * offset + tangent * (i * 1.5f);
                point.y = groundY;
                if (point.z < minZ || point.z > maxZ || point.x < originX + 4f || point.x > MaxX) continue;
                if (Physics.CheckSphere(point + Vector3.up * 0.9f, AvatarRadius, ~0, QueryTriggerInteraction.Ignore)) continue;
                coverPoints.Add(point);
            }
        }

        /// <summary>
        /// Tehdide karşı siper: çömelince görünmeyen, ayağa kalkınca ateş edebilen, tehdide 4–11 m uzaklıkta, düşmana
        /// en yakın boş nokta. Bulursa sahiplenir (iki düşman aynı noktaya gitmez).
        /// </summary>
        public bool TryClaimCover(Vector3 from, Vector3 threat, ref int claimed, out Vector3 point)
        {
            ReleaseCover(ref claimed);
            point = default;

            var threatChest = threat + Vector3.up * StandChest;
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < coverPoints.Count; i++)
            {
                if (claimedCover.Contains(i)) continue;

                var candidate = coverPoints[i];
                float toThreat = FlatXZ(candidate - threat).magnitude;
                if (toThreat < 4f || toThreat > 11f) continue;

                float score = FlatXZ(candidate - from).magnitude;
                if (score >= bestScore || score > 14f) continue;
                if (BuildingAt(candidate) != BuildingAt(from)) continue;

                bool hidden = !HasLineOfSight(candidate + Vector3.up * 0.7f, threatChest);
                bool canPeek = HasLineOfSight(candidate + Vector3.up * 1.5f, threatChest);
                if (!hidden || !canPeek) continue;

                best = i;
                bestScore = score;
            }

            if (best < 0) return false;
            claimed = best;
            claimedCover.Add(best);
            point = coverPoints[best];
            return true;
        }

        public void ReleaseCover(ref int claimed)
        {
            if (claimed >= 0) claimedCover.Remove(claimed);
            claimed = -1;
        }

        /// <summary>İki nokta arasında duvar, araba, kasa yığını yoksa true (kâşifin kendisi engel sayılmaz).</summary>
        public bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            if (!Physics.Linecast(from, to, out var hit, ~0, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider == controller;
        }

        public void AlertAllies(FieldEnemy source, Vector3 position, float radius)
        {
            foreach (var enemy in enemies)
                if (enemy != source && !enemy.IsDead && Vector3.Distance(enemy.Position, position) <= radius) enemy.Hear(AvatarFeet);
        }

        public void Spotted(FieldEnemy enemy) =>
            Float("!", enemy.Position + Vector3.up * (enemy.LabelHeight + 0.3f), new Color(1f, 0.4f, 0.3f));

        public void ShowAimLine(FieldEnemy enemy, Vector3 from, Vector3 direction, float range, bool locked)
        {
            if (!enemyAimLines.TryGetValue(enemy, out var line) || line == null)
            {
                line = Make(PrimitiveType.Cube, "Düşman Nişanı", Color.red, root.transform);
                enemyAimLines[enemy] = line;
            }

            float length = Physics.Raycast(from, direction, out var hit, range, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : range;
            line.gameObject.SetActive(true);
            Tint(line.GetComponent<Renderer>(), locked ? new Color(1f, 0.1f, 0.1f) : new Color(0.8f, 0.3f, 0.3f));
            PlaceLine(line, from, direction, length, locked ? 0.06f : 0.025f);
        }

        public void HideAimLine(FieldEnemy enemy)
        {
            if (enemyAimLines.TryGetValue(enemy, out var line) && line != null) line.gameObject.SetActive(false);
        }

        public void EnemyShoot(FieldEnemy enemy, Vector3 from, Vector3 direction, float range, float damage)
        {
            float length = range;
            bool blocked = Physics.Raycast(from, direction, out var hit, range, ~0, QueryTriggerInteraction.Ignore);
            if (blocked) length = hit.distance;

            // Kurşun kâşifin gövdesine ya da köşeden taşan kısmına (ondan önce bir engele çarpmadan) değerse isabet.
            bool hitsBody = blocked && hit.collider == controller;
            var toTarget = AvatarTarget - from;
            float along = Vector3.Dot(toTarget, direction);
            bool hitsExposed = along > 0f && along <= range &&
                               Vector3.Cross(direction, toTarget).magnitude <= 0.45f &&
                               (!blocked || hit.distance >= along - 0.4f || hit.collider == controller);
            if (hitsBody || hitsExposed) DamageAvatar(damage, $"Seferde {enemy.Label} tarafından vuruldu.");

            ShowTracer(from, direction, length, new Color(1f, 0.5f, 0.3f));
            MakeNoise(enemy.Position, 10f);
        }

        public void OnEnemyKilled(FieldEnemy enemy)
        {
            GameInput.Rumble(0.4f, 0.6f, 0.15f);
            var at = enemy.Position + Vector3.up * enemy.LabelHeight;
            Float($"{enemy.Label} düştü", at, new Color(1f, 0.85f, 0.3f));

            if (enemy.Kind == FieldEnemyKind.Gunner && Workbench.Instance != null)
            {
                Workbench.Instance.AddAmmo(3);
                Float("+3 mermi", at + Vector3.up * 0.5f, new Color(1f, 0.9f, 0.5f));
            }
            else if (enemy.Kind != FieldEnemyKind.Dog && Random.value < 0.35f)
            {
                AddLoot(new ResourceAmount(ResourceType.Supplies, 1));
                Float("+1 Yaşam Kiti", at + Vector3.up * 0.5f, ResourceType.Supplies.ToColor());
            }
        }

        // ---------------------------------------------------------------- Etkileşim

        FieldSpot NearestSpot()
        {
            FieldSpot best = null;
            float bestDistance = InteractRange;
            var here = Flat(avatar.position);
            foreach (var spot in spots)
            {
                if (spot.Done) continue;
                float distance = Vector2.Distance(here, Flat(spot.Position));
                if (distance >= bestDistance) continue;
                best = spot;
                bestDistance = distance;
            }
            return best;
        }

        bool NearExit => Vector2.Distance(Flat(avatar.position), Flat(exitPoint)) <= ExitRange;

        static Vector2 Flat(Vector3 point) => new Vector2(point.x, point.z);

        void Interact()
        {
            if (searchTarget != null) return;

            if (NearExit)
            {
                Outcome = FieldOutcome.Returned;
                return;
            }

            var spot = NearestSpot();
            if (spot == null) return;

            if (spot.Kind == FieldSpotKind.Loot && usedSlots >= bagSlots)
            {
                Say("Çanta dolu! Soldaki yeşil çıkışa dön.");
                return;
            }
            if (spot.Kind == FieldSpotKind.Rescue && !rescueAvailable)
            {
                Say("Sığınakta yer yok ya da kurtarılacak kişi için dal kalmadı.");
                return;
            }

            searchTarget = spot;
            searchProgress = 0f;
            velocity = Vector3.zero;
        }

        void UpdateSearch(Vector2 input, float dt)
        {
            if (searchTarget == null) return;

            // Arama yerinde durmayı ister; yürümek ya da kaçmak aramayı keser.
            if (input.sqrMagnitude > 0.06f || IsDodging)
            {
                CancelSearch(true);
                return;
            }

            var toSpot = searchTarget.Position - avatar.position;
            toSpot.y = 0f;
            if (toSpot.sqrMagnitude > 0.01f)
                avatarRoot.rotation = Quaternion.RotateTowards(avatarRoot.rotation, Quaternion.LookRotation(toSpot), TurnSpeed * dt);

            float speed = explorer.Condition.SpeedMultiplier * (explorer.Branch == SkillBranch.Exploration ? 1.4f : 1f);
            searchProgress += dt * speed;
            if (searchProgress >= searchTarget.Duration) FinishSearch(searchTarget);
        }

        void CancelSearch(bool announce)
        {
            if (searchTarget == null) return;
            if (announce) Say("Arama yarıda kaldı.");
            searchTarget = null;
            searchProgress = 0f;
        }

        void FinishSearch(FieldSpot spot)
        {
            spot.MarkDone();
            searchTarget = null;
            searchProgress = 0f;

            if (spot.Kind == FieldSpotKind.Rescue)
            {
                RescueSecured = true;
                Say("Mahsur kalan kurtarıldı! Çıkışa dön, seninle gelecek.");
                Float("Kurtarıldı!", spot.Position + Vector3.up * 1.2f, new Color(0.4f, 0.9f, 1f));
                return;
            }

            usedSlots++;
            var found = RollLoot();
            AddLoot(found);
            Float($"+{found.amount} {found.type.ToDisplayName()}", spot.Position + Vector3.up * 1.4f, found.type.ToColor());
            if (usedSlots >= bagSlots) Say("Çanta doldu. Soldaki yeşil çıkışa dön.");

            if (site.hazard != ExpeditionHazard.None && Random.value < site.hazardChance)
            {
                string cause = site.hazard == ExpeditionHazard.Fire ? "alev aldı" : "göçük başına yıkıldı";
                reportText.Append(reportText.Length > 0 ? " " : "").Append(site.hazard == ExpeditionHazard.Fire
                    ? "Aradığı yerde yangın çıktı." : "Aradığı yerde göçük oldu.");
                explorer.Condition.AddStress(10f);
                DamageAvatar(site.hazardDamage * 0.7f, $"Seferde {cause}.");
            }
        }

        ResourceAmount RollLoot()
        {
            float total = 0f;
            foreach (var entry in site.loot) total += entry.weight;

            float roll = Random.value * total;
            foreach (var entry in site.loot)
            {
                roll -= entry.weight;
                if (roll > 0f) continue;
                return new ResourceAmount(entry.type, Random.Range(entry.min, Mathf.Max(entry.min, entry.max) + 1));
            }

            var last = site.loot[site.loot.Length - 1];
            return new ResourceAmount(last.type, last.min);
        }

        void AddLoot(ResourceAmount item)
        {
            for (int i = 0; i < bag.Count; i++)
            {
                if (bag[i].type != item.type) continue;
                bag[i] = new ResourceAmount(item.type, bag[i].amount + item.amount);
                return;
            }
            bag.Add(item);
        }

        /// <summary>Süre dolup kaçarak dönerken çantanın yarısı düşer.</summary>
        public void DropHalfBag()
        {
            for (int i = bag.Count - 1; i >= 0; i--)
            {
                int kept = bag[i].amount / 2;
                if (kept <= 0) bag.RemoveAt(i);
                else bag[i] = new ResourceAmount(bag[i].type, kept);
            }
        }

        void UseMedkit()
        {
            var storage = ResourceStorage.Instance;
            if (healCooldown > 0f || explorer.Health >= explorer.MaxHealth) return;
            if (storage == null || !storage.TrySpend(new ResourceAmount(ResourceType.Supplies, 1)))
            {
                Say("Sığınakta Yaşam Kiti kalmadı.");
                return;
            }

            float amount = explorer.Branch == SkillBranch.Engineering ? HealAmount * 1.5f : HealAmount;
            explorer.Heal(amount);
            healCooldown = HealCooldown;
            Float($"+{amount:0}", avatar.position + Vector3.up * 1.6f, new Color(0.5f, 1f, 0.5f));
        }

        // ---------------------------------------------------------------- Hasar

        /// <summary>Tuzak (ve sonraki aşamalarda düşman) hasarı. Kaçış sırasında hasar yok; arama yarıda kesilir.</summary>
        public void DamageAvatar(float amount, string cause)
        {
            if (!IsActive || explorer.IsDead || IsDodging || amount <= 0f) return;

            explorer.TakeDamage(amount, cause);
            CancelSearch(false);
            Float($"-{amount:0}", avatar.position + Vector3.up * 1.6f, new Color(1f, 0.35f, 0.3f));
            SideViewCamera.Kick(0.25f, 0.25f);
            GameInput.Rumble(0.8f, 0.5f, 0.25f);
            hurtFlash = 0.35f;
            Tint(avatarRenderer, Color.white);
            flashAvatar = 0.12f;
        }

        float hurtFlash;

        void UpdateFlash(float dt)
        {
            hurtFlash = Mathf.Max(0f, hurtFlash - Time.unscaledDeltaTime);
            if (flashAvatar <= 0f) return;
            flashAvatar -= dt;
            if (flashAvatar <= 0f && !IsDodging) Tint(avatarRenderer, BranchColor(explorer));
        }

        // ---------------------------------------------------------------- HUD

        void Say(string text)
        {
            message = text;
            messageTimer = 6f;
        }

        void Float(string text, Vector3 position, Color color) =>
            floaters.Add(new FloatingText { Text = text, Position = position, Color = color });

        void UpdateFloaters(float dt)
        {
            for (int i = floaters.Count - 1; i >= 0; i--)
            {
                var item = floaters[i];
                item.Age += dt;
                item.Position += Vector3.up * dt * 0.8f;
                if (item.Age > 1.4f) floaters.RemoveAt(i);
                else floaters[i] = item;
            }
        }

        void OnGUI()
        {
            if (!IsActive || !SideViewCamera.ViewingField || ModalMenu.IsOpen || cam == null) return;

            centered ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };
            small ??= new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            prompt ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 13, fontStyle = FontStyle.Bold };

            DrawHurtOverlay();
            DrawWorldLabels();
            DrawInteractPrompt();
            DrawPanel();
            DrawControls();
        }

        // Hasar alınca ekran kenarları kısa süre kızarır.
        void DrawHurtOverlay()
        {
            if (hurtFlash <= 0f) return;
            var previous = GUI.color;
            GUI.color = new Color(0.8f, 0f, 0f, hurtFlash * 0.6f);
            const float edge = 60f;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, edge), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, Screen.height - edge, Screen.width, edge), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, 0, edge, Screen.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(Screen.width - edge, 0, edge, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        void DrawWorldLabels()
        {
            DrawLabel(exitPoint + Vector3.up * 2.9f, "ÇIKIŞ", new Color(0.5f, 1f, 0.6f));

            foreach (var enemy in enemies)
            {
                if (enemy.IsDead) continue;
                string icon = enemy.IsAlerted ? "! " : enemy.IsInvestigating ? "? " : "";
                var top = enemy.Position + Vector3.up * enemy.LabelHeight;
                DrawLabel(top + Vector3.up * 0.25f, icon + enemy.Label, enemy.IsAlerted ? new Color(1f, 0.5f, 0.4f) : new Color(0.9f, 0.85f, 0.75f));
                DrawBar(top, enemy.Health01, Color.red, 44);
            }

            foreach (var spot in spots)
            {
                string label = spot.Done ? "(boş)" : spot.Kind == FieldSpotKind.Rescue ? "MAHSUR KALAN" : "Ara";
                DrawLabel(spot.Position + Vector3.up * 0.9f, label, spot.Done ? Color.gray : Color.white);
                if (searchTarget == spot) DrawBar(spot.Position + Vector3.up * 1.3f, searchProgress / spot.Duration, Color.white, 54);
            }

            DrawBar(avatar.position + Vector3.up * 1.3f, explorer.Health / explorer.MaxHealth, Color.green, 56);

            foreach (var item in floaters)
            {
                var color = item.Color;
                color.a = 1f - item.Age / 1.4f;
                DrawLabel(item.Position, item.Text, color);
            }
        }

        void DrawInteractPrompt()
        {
            if (searchTarget != null) return;

            string key = GameInput.Prompt(InputPrompt.Interact);
            string text;
            Vector3 anchor = avatar.position;

            if (NearExit)
            {
                text = $"[{key}] Üsse dön";
            }
            else
            {
                var spot = NearestSpot();
                if (spot == null) return;
                anchor = spot.Position;
                text = spot.Kind == FieldSpotKind.Rescue ? $"[{key}] Kurtar"
                    : usedSlots >= bagSlots ? "Çanta dolu"
                    : $"[{key}] Ara";
            }

            var screen = cam.WorldToScreenPoint(anchor + Vector3.up * 1.7f);
            if (screen.z < 0f) return;
            GUI.Box(new Rect(screen.x - 60f, Screen.height - screen.y - 13f, 120f, 26f), text, prompt);
        }

        void DrawLabel(Vector3 world, string text, Color color)
        {
            var screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f) return;

            var previous = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(new Rect(screen.x - 80f, Screen.height - screen.y - 10f, 160f, 20f), text, centered);
            GUI.contentColor = previous;
        }

        void DrawBar(Vector3 world, float fraction, Color color, float width)
        {
            var screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f) return;
            DrawBarRect(new Rect(screen.x - width * 0.5f, Screen.height - screen.y, width, 5f), fraction, color);
        }

        static void DrawBarRect(Rect rect, float fraction, Color color)
        {
            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x + 1, rect.y + 1, (rect.width - 2) * Mathf.Clamp01(fraction), rect.height - 2), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        void DrawPanel()
        {
            const float width = 440f;
            var rect = new Rect((Screen.width - width) * 0.5f, 10, width, 112);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12));

            string place = InsideBuilding ? "   [BİNA İÇİ]" : "";
            GUILayout.Label($"SAHA: {site.name} — {explorer.DisplayName}   Kalan: {Mathf.CeilToInt(Remaining)} sn{place}", centered);
            float health = explorer.Health / explorer.MaxHealth;
            DrawBarRect(GUILayoutUtility.GetRect(width - 16f, 10f), health, Color.Lerp(Color.red, Color.green, health));
            GUILayout.Label($"Can {explorer.Health:0}/{explorer.MaxHealth:0}   Çanta {usedSlots}/{bagSlots}: {BagText()}" +
                            (RescueSecured ? "   [Kazazede kurtarıldı]" : ""), small);
            var info = CurrentWeapon;
            string ammo = info.UsesAmmo ? $"   Mermi: {(Workbench.Instance != null ? Workbench.Instance.Ammo : 0)}" : "   (sessiz, sınırsız)";
            string stance = leanSide != 0 ? "Köşeden ateş" : CrouchedLow ? "Çömelik" : crouched ? "Doğruldu (açıkta)" : "Ayakta";
            GUILayout.Label($"Silah: {info.Name}{ammo}   Duruş: {stance}{(inCover ? "  [SİPERDE]" : "")}", small);
            if (messageTimer > 0f && message != null) GUILayout.Label(message, small);

            GUILayout.EndArea();
        }

        void DrawControls()
        {
            var storage = ResourceStorage.Instance;
            int kits = storage != null ? storage.Get(ResourceType.Supplies) : 0;
            string text =
                $"{GameInput.Prompt(InputPrompt.Move)}: yürü    {GameInput.Prompt(InputPrompt.Aim)}: nişan    " +
                $"{GameInput.Prompt(InputPrompt.Fire)}: ateş    {GameInput.Prompt(InputPrompt.SwitchWeapon)}: silah    " +
                $"{GameInput.Prompt(InputPrompt.Crouch)}: çömel    {GameInput.Prompt(InputPrompt.Dodge)}: kaçış    " +
                $"{GameInput.Prompt(InputPrompt.Interact)}: etkileşim    {GameInput.Prompt(InputPrompt.Heal)}: ilk yardım ({kits} kit)    " +
                $"{GameInput.Prompt(InputPrompt.SwitchCharacter)}: sığınağa dön";
            GUI.Label(new Rect(0, Screen.height - 30, Screen.width, 24), text, centered);
        }

        string BagText()
        {
            if (bag.Count == 0) return "boş";
            var text = new StringBuilder();
            for (int i = 0; i < bag.Count; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(bag[i]);
            }
            return text.ToString();
        }
    }
}
