using System.Collections.Generic;
using System.Text;
using AftershockIsle.Building;
using AftershockIsle.CameraControl;
using AftershockIsle.Characters;
using AftershockIsle.Core;
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
    /// Bu dosya durum, kurulum/bitiş ve oyun döngüsünü tutar; diğer bölümler FieldExpedition.*.cs dosyalarındadır
    /// (Build, Combat, Enemies, Interaction, Hud).
    /// </summary>
    public partial class FieldExpedition : MonoBehaviour
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
        FieldCompanion companion;
        Survivor partner;
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
        // Kapsül gövdenin yerine geçen karakter modelleri: kapsüle yapılan Tint çağrıları modele yönlenir.
        readonly Dictionary<Renderer, CharacterView> views = new Dictionary<Renderer, CharacterView>();

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

        /// <summary>Karakter modeli kütüphanesi (null ise kâşif, yoldaş ve düşmanlar kapsül olarak kalır).</summary>
        public CharacterLibrary Characters { get; set; }

        public FieldOutcome Outcome { get; private set; } = FieldOutcome.Idle;
        public bool IsActive => Outcome == FieldOutcome.Active && explorer != null;
        public float Remaining { get; private set; }
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
                          bool canRescue, int day, FieldArtLibrary artLibrary, Survivor companionSurvivor = null)
        {
            art = artLibrary;
            partner = companionSurvivor;
            companion = null;
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
            views.Clear();
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
            companion = null;
            partner = null;
            views.Clear();
            explorer = null;
            Outcome = FieldOutcome.Idle;
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
            companion?.Tick(dt);
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
    }
}
