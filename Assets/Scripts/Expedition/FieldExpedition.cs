using System.Collections.Generic;
using System.Text;
using AftershockIsle.Building;
using AftershockIsle.CameraControl;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Economy;
using AftershockIsle.Skills;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    public enum FieldOutcome { Idle, Active, Returned, TimeUp, Died }

    /// <summary>
    /// Oynanabilir dış saha: binanın uzağında, X (sağ-sol) ve Z (haritanın içine / ekrana doğru) düzleminde
    /// serbest bir sokak alanı. Kâşif doğrudan kontrol edilir (WASD ya da sol çubuk); siper nesneleri (araba,
    /// beton blok, kasa yığını) yolu keser. Noktalar aranır (çanta slotu kadar ganimet), çıkıştan dönülür.
    /// Sığınak bu sırada çalışmaya devam eder; Tab/Select ile görünüm değişir. Süre dolarsa kâşif kaçarak döner.
    /// Can, kâşifin kendi canıdır (Survivor.Health): sahada alınan hasar eve de taşınır.
    /// </summary>
    public class FieldExpedition : MonoBehaviour
    {
        const float FieldOffset = 80f;
        const float FieldDepth = 9f;
        const float FrontMargin = 3f;

        const float AvatarRadius = 0.35f;
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

        struct Obstacle
        {
            public Vector2 Center;
            public Vector2 Half;
            public float Height;
        }

        struct FloatingText
        {
            public string Text;
            public Vector3 Position;
            public float Age;
            public Color Color;
        }

        readonly List<FieldSpot> spots = new List<FieldSpot>();
        readonly List<Obstacle> obstacles = new List<Obstacle>();
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

        GameObject root;
        Transform avatar;
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

        // ---------------------------------------------------------------- Kurulum / bitiş

        public void Begin(Survivor who, ExpeditionSite expeditionSite, BuildingLayout building, int slots, float extraction, bool canRescue, int day)
        {
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
            obstacles.Clear();
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
            Say($"{GameInput.Prompt(FieldPrompt.Move)}: yürü.  {GameInput.Prompt(FieldPrompt.Interact)}: ara.  " +
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

            var wall = Make(PrimitiveType.Cube, "Sınır", new Color(0.18f, 0.18f, 0.2f), root.transform);
            wall.position = new Vector3(MaxX + 1f, groundY + 1.5f, midZ);
            wall.localScale = new Vector3(0.6f, 3f, FieldDepth + 2f);

            exitPoint = new Vector3(originX + 0.6f, groundY, midZ);
            BuildExit(midZ);
            BuildSpots(length);
            BuildObstacles(length);
            BuildAvatar(midZ);
        }

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
            float start = originX + 6f;
            float span = length - 8f;

            for (int i = 0; i < count; i++)
            {
                float x = start + span * (count == 1 ? 0.5f : i / (float)(count - 1)) + Random.Range(-1.2f, 1.2f);
                float z = Random.Range(minZ + 1f, maxZ - 1f);
                var visual = Make(PrimitiveType.Cube, "Aranacak Nokta", new Color(0.55f, 0.38f, 0.2f), root.transform);
                visual.position = new Vector3(x, groundY + 0.45f, z);
                visual.localScale = new Vector3(1.1f, 0.9f, 0.8f);
                spots.Add(new FieldSpot(visual, FieldSpotKind.Loot, SearchDuration, "Aranacak yer"));
                AddObstacle(new Vector2(x, z), new Vector2(0.55f, 0.4f), 0.9f);
            }

            if (!site.isRescueSite) return;

            float rescueZ = Random.Range(minZ + 1.5f, maxZ - 1.5f);
            var victim = Make(PrimitiveType.Capsule, "Mahsur Kalan", new Color(0.4f, 0.85f, 0.95f), root.transform);
            victim.position = new Vector3(MaxX - 3f, groundY + 0.35f, rescueZ);
            victim.localScale = new Vector3(0.7f, 0.5f, 0.7f);
            victim.rotation = Quaternion.Euler(0f, 0f, 90f);
            spots.Add(new FieldSpot(victim, FieldSpotKind.Rescue, extractionDuration > 0f ? extractionDuration : RescueDuration, "Mahsur kalan"));
        }

        // Siperlik nesneler. Aşama 3'te siper alma bunlara bağlanacak; şimdilik yolu kesip rota seçtiren engeller.
        void BuildObstacles(float length)
        {
            int target = Mathf.RoundToInt(length / 5f);
            int placed = 0;

            for (int attempt = 0; attempt < target * 12 && placed < target; attempt++)
            {
                int kind = Random.Range(0, 3);
                bool turned = Random.value < 0.35f;
                Vector2 half = kind switch
                {
                    0 => new Vector2(1.8f, 0.85f),   // araba
                    1 => new Vector2(1.1f, 0.35f),   // beton blok
                    _ => new Vector2(0.6f, 0.6f),    // kasa yığını
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
            foreach (var other in obstacles)
            {
                if (Mathf.Abs(center.x - other.Center.x) < half.x + other.Half.x + gap &&
                    Mathf.Abs(center.y - other.Center.y) < half.y + other.Half.y + gap) return false;
            }
            return true;
        }

        void BuildObstacleVisual(int kind, Vector2 center, Vector2 half, bool turned)
        {
            float height;
            switch (kind)
            {
                case 0:
                {
                    height = 1.4f;
                    var body = Make(PrimitiveType.Cube, "Araba", new Color(0.3f, 0.36f, 0.45f), root.transform);
                    body.position = new Vector3(center.x, groundY + 0.45f, center.y);
                    body.localScale = new Vector3(half.x * 2f, 0.7f, half.y * 2f);
                    var cabin = Make(PrimitiveType.Cube, "Kabin", new Color(0.25f, 0.3f, 0.38f), root.transform);
                    cabin.position = new Vector3(center.x, groundY + 1.05f, center.y);
                    cabin.localScale = turned
                        ? new Vector3(half.x * 1.8f, 0.55f, half.y * 1.1f)
                        : new Vector3(half.x * 1.1f, 0.55f, half.y * 1.8f);
                    break;
                }
                case 1:
                {
                    height = 1.0f;
                    var slab = Make(PrimitiveType.Cube, "Beton Blok", new Color(0.55f, 0.55f, 0.52f), root.transform);
                    slab.position = new Vector3(center.x, groundY + height * 0.5f, center.y);
                    slab.localScale = new Vector3(half.x * 2f, height, half.y * 2f);
                    break;
                }
                default:
                {
                    height = 1.7f;
                    for (int level = 0; level < 2; level++)
                    {
                        var crate = Make(PrimitiveType.Cube, "Kasa", new Color(0.42f, 0.3f, 0.18f), root.transform);
                        crate.position = new Vector3(center.x + (level == 1 ? 0.08f : 0f), groundY + 0.42f + level * 0.85f, center.y);
                        crate.localScale = new Vector3(half.x * 2f - level * 0.15f, 0.84f, half.y * 2f - level * 0.15f);
                    }
                    break;
                }
            }

            AddObstacle(center, half, height);
        }

        void AddObstacle(Vector2 center, Vector2 half, float height) =>
            obstacles.Add(new Obstacle { Center = center, Half = half, Height = height });

        void BuildAvatar(float midZ)
        {
            var body = Make(PrimitiveType.Capsule, explorer.DisplayName, BranchColor(explorer), root.transform);
            body.localScale = new Vector3(0.7f, 0.95f, 0.7f);
            body.position = new Vector3(originX + 2f, groundY + 0.95f, midZ);
            body.rotation = Quaternion.LookRotation(Vector3.right);
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

        Transform Make(PrimitiveType type, string objectName, Color color, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = objectName;
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            go.transform.SetParent(parent, true);
            Tint(go.GetComponent<Renderer>(), color);
            return go.transform;
        }

        void Tint(Renderer target, Color color)
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

            GameInput.RefreshActiveDevice();

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
            bool controlling = SideViewCamera.ViewingField;
            Vector2 input = controlling ? GameInput.FieldMove : Vector2.zero;

            if (controlling)
            {
                if (GameInput.FieldDodgePressed) TryDodge(input);
                if (GameInput.FieldHealPressed) UseMedkit();
                if (GameInput.FieldInteractPressed) Interact();
            }

            UpdateMovement(input, dt);
            UpdateSearch(input, dt);
            UpdateFlash(dt);
            foreach (var spot in spots) spot.Refresh(Tint);
            UpdateFloaters(dt);

            if (explorer.IsDead) Outcome = FieldOutcome.Died;
        }

        void UpdateMovement(Vector2 input, float dt)
        {
            float speed = WalkSpeed * explorer.Condition.SpeedMultiplier * SkillTree.MoveSpeedBonus;

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

            avatar.position = ResolveCollisions(avatar.position + velocity * dt);

            var flat = new Vector3(velocity.x, 0f, velocity.z);
            if (flat.sqrMagnitude > 0.05f)
                avatar.rotation = Quaternion.RotateTowards(avatar.rotation, Quaternion.LookRotation(flat), TurnSpeed * dt);
        }

        // Kâşif bir daire; engeller eksene hizalı dikdörtgen. Daire dikdörtgenin dışına itilir, sonra alan sınırına kenetlenir.
        Vector3 ResolveCollisions(Vector3 position)
        {
            var point = new Vector2(position.x, position.z);

            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var obstacle in obstacles)
                {
                    var offset = point - obstacle.Center;
                    var closest = new Vector2(
                        Mathf.Clamp(offset.x, -obstacle.Half.x, obstacle.Half.x),
                        Mathf.Clamp(offset.y, -obstacle.Half.y, obstacle.Half.y));
                    var away = offset - closest;
                    float distance = away.magnitude;

                    if (distance >= AvatarRadius) continue;

                    if (distance > 0.0001f)
                    {
                        point += away / distance * (AvatarRadius - distance);
                        continue;
                    }

                    // Merkez dikdörtgenin içinde: en kısa eksenden dışarı çıkar.
                    float pushX = obstacle.Half.x - Mathf.Abs(offset.x) + AvatarRadius;
                    float pushZ = obstacle.Half.y - Mathf.Abs(offset.y) + AvatarRadius;
                    if (pushX < pushZ) point.x += Mathf.Sign(offset.x == 0f ? 1f : offset.x) * pushX;
                    else point.y += Mathf.Sign(offset.y == 0f ? 1f : offset.y) * pushZ;
                }
            }

            point.x = Mathf.Clamp(point.x, originX - 0.2f, MaxX - AvatarRadius);
            point.y = Mathf.Clamp(point.y, minZ, maxZ);
            return new Vector3(point.x, position.y, point.y);
        }

        void TryDodge(Vector2 input)
        {
            if (IsDodging || dodgeCooldown > 0f) return;

            dodgeDirection = input.sqrMagnitude > 0.04f ? new Vector3(input.x, 0f, input.y) : avatar.forward;
            dodgeDirection.y = 0f;
            dodgeDirection.Normalize();

            dodgeTimer = DodgeDuration;
            dodgeCooldown = DodgeCooldown;
            CancelSearch(false);
            Tint(avatarRenderer, new Color(0.75f, 0.9f, 1f));
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
                avatar.rotation = Quaternion.RotateTowards(avatar.rotation, Quaternion.LookRotation(toSpot), TurnSpeed * dt);

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
            Tint(avatarRenderer, Color.white);
            flashAvatar = 0.12f;
        }

        void UpdateFlash(float dt)
        {
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
            if (!IsActive || !SideViewCamera.ViewingField || cam == null) return;

            centered ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };
            small ??= new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            prompt ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 13, fontStyle = FontStyle.Bold };

            DrawWorldLabels();
            DrawInteractPrompt();
            DrawPanel();
            DrawControls();
        }

        void DrawWorldLabels()
        {
            DrawLabel(exitPoint + Vector3.up * 2.9f, "ÇIKIŞ", new Color(0.5f, 1f, 0.6f));

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

            string key = GameInput.Prompt(FieldPrompt.Interact);
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
            var rect = new Rect((Screen.width - width) * 0.5f, 10, width, 92);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12));

            GUILayout.Label($"SAHA: {site.name} — {explorer.DisplayName}   Kalan: {Mathf.CeilToInt(Remaining)} sn", centered);
            float health = explorer.Health / explorer.MaxHealth;
            DrawBarRect(GUILayoutUtility.GetRect(width - 16f, 10f), health, Color.Lerp(Color.red, Color.green, health));
            GUILayout.Label($"Can {explorer.Health:0}/{explorer.MaxHealth:0}   Çanta {usedSlots}/{bagSlots}: {BagText()}" +
                            (RescueSecured ? "   [Kazazede kurtarıldı]" : ""), small);
            if (messageTimer > 0f && message != null) GUILayout.Label(message, small);

            GUILayout.EndArea();
        }

        void DrawControls()
        {
            var storage = ResourceStorage.Instance;
            int kits = storage != null ? storage.Get(ResourceType.Supplies) : 0;
            string text =
                $"{GameInput.Prompt(FieldPrompt.Move)}: yürü    {GameInput.Prompt(FieldPrompt.Dodge)}: kaçış    " +
                $"{GameInput.Prompt(FieldPrompt.Interact)}: etkileşim    {GameInput.Prompt(FieldPrompt.Heal)}: ilk yardım ({kits} kit)    " +
                $"{GameInput.Prompt(FieldPrompt.ViewSwitch)}: sığınağa bak";
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
