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
    /// Oynanabilir dış saha: binanın uzağında yatay bir sokak şeridi. Kâşif sağ tıkla yürür, düşmana saldırır,
    /// noktaları arar (çanta slotu kadar ganimet) ve çıkışa dönerek seferi bitirir. Sığınak bu sırada
    /// çalışmaya devam eder; Tab ile görünüm sahayla sığınak arasında değişir. Süre dolarsa kâşif kaçarak döner.
    /// Can, kâşifin kendi canıdır (Survivor.Health): sahada alınan hasar eve de taşınır.
    /// </summary>
    public class FieldExpedition : MonoBehaviour
    {
        const float FieldOffset = 80f;
        const float AvatarSpeed = 3.6f;
        const float AvatarReach = 1.7f;
        const float AvatarDamage = 14f;
        const float AvatarAttackInterval = 0.75f;
        const float SearchReach = 1.1f;
        const float SearchDuration = 2.5f;
        const float RescueDuration = 7f;
        const float HealAmount = 35f;
        const float HealCooldown = 3f;
        const float ExitReach = 0.8f;
        const float ExitHudRange = 3f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        struct FloatingText
        {
            public string Text;
            public Vector3 Position;
            public float Age;
            public Color Color;
        }

        readonly List<FieldEnemy> enemies = new List<FieldEnemy>();
        readonly List<FieldSpot> spots = new List<FieldSpot>();
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
        Transform slash;
        float originX;
        float groundY;
        float z;

        float moveGoalX;
        bool hasMoveGoal;
        bool returnOnArrival;
        FieldEnemy attackTarget;
        FieldSpot searchTarget;
        float searchProgress;
        float attackCooldown;
        float healCooldown;
        float slashTimer;
        string message;
        float messageTimer;

        GUIStyle centered;
        GUIStyle small;
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
        public float AvatarX => avatar != null ? avatar.position.x : originX;
        public float GroundY => groundY;

        // ---------------------------------------------------------------- Kurulum / bitiş

        public void Begin(Survivor who, ExpeditionSite expeditionSite, BuildingLayout building, int slots, float extraction, bool canRescue)
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
            enemies.Clear();
            spots.Clear();
            floaters.Clear();
            attackTarget = null;
            searchTarget = null;
            searchProgress = 0f;
            hasMoveGoal = false;
            returnOnArrival = false;
            attackCooldown = 0f;
            healCooldown = 0f;
            message = null;
            cam = Camera.main;

            originX = building.MaxX + FieldOffset;
            groundY = building.FloorY(1);
            z = building.WalkPlaneZ;

            BuildField();
            SideViewCamera.SetFieldFocus(avatar);
            Say("Sağ tık: yürü / saldır / ara.  Tab: sığınağa bak.  Çanta dolunca çıkışa dön.");
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
            var ground = Make(PrimitiveType.Cube, "Zemin", new Color(0.32f, 0.3f, 0.28f), root.transform);
            ground.position = new Vector3(originX + length * 0.5f, groundY - 0.3f, z);
            ground.localScale = new Vector3(length + 16f, 0.6f, 5f);

            // Arka plan: yıkık yapı silüetleri.
            for (int i = 0; i < 10; i++)
            {
                var ruin = Make(PrimitiveType.Cube, "Harabe", new Color(0.2f, 0.2f, 0.22f), root.transform);
                float height = Random.Range(2.5f, 7f);
                ruin.position = new Vector3(originX - 4f + i * (length + 8f) / 9f, groundY + height * 0.5f, z + 3.5f);
                ruin.localScale = new Vector3(Random.Range(2.5f, 4.5f), height, 1.5f);
            }

            var exit = Make(PrimitiveType.Cube, "Çıkış", new Color(0.25f, 0.8f, 0.4f), root.transform);
            exit.position = new Vector3(originX, groundY + 1.2f, z + 0.9f);
            exit.localScale = new Vector3(0.35f, 2.4f, 0.35f);

            var wall = Make(PrimitiveType.Cube, "Sınır", new Color(0.15f, 0.15f, 0.17f), root.transform);
            wall.position = new Vector3(originX + length + 1f, groundY + 2f, z);
            wall.localScale = new Vector3(0.6f, 4f, 3f);

            BuildSpots(length);
            BuildEnemies(length);
            BuildAvatar();
        }

        void BuildSpots(float length)
        {
            int count = Mathf.Max(1, site.lootSpots);
            float start = originX + 6f;
            float span = length - 8f;

            for (int i = 0; i < count; i++)
            {
                float x = start + span * (count == 1 ? 0.5f : i / (float)(count - 1)) + Random.Range(-1.2f, 1.2f);
                var visual = Make(PrimitiveType.Cube, "Aranacak Nokta", new Color(0.55f, 0.38f, 0.2f), root.transform);
                visual.position = new Vector3(x, groundY + 0.45f, z - 0.1f);
                visual.localScale = new Vector3(1.2f, 0.9f, 0.8f);
                spots.Add(new FieldSpot(visual, FieldSpotKind.Loot, SearchDuration, "Aranacak yer"));
            }

            if (!site.isRescueSite) return;

            var victim = Make(PrimitiveType.Capsule, "Mahsur Kalan", new Color(0.4f, 0.85f, 0.95f), root.transform);
            victim.position = new Vector3(originX + length - 3f, groundY + 0.5f, z - 0.1f);
            victim.localScale = new Vector3(0.7f, 0.5f, 0.7f);
            victim.rotation = Quaternion.Euler(0f, 0f, 90f);
            spots.Add(new FieldSpot(victim, FieldSpotKind.Rescue, extractionDuration > 0f ? extractionDuration : RescueDuration, "Mahsur kalan"));
        }

        void BuildEnemies(float length)
        {
            int total = site.dogs + site.raiders;
            if (total <= 0) return;

            // Çıkışa yakın yer güvenli; düşmanlar sahaya düzenli dağılır, köpek/haydut sırayla karışır.
            float start = originX + 9f;
            float span = length - 11f;
            int dogs = site.dogs;
            int raiders = site.raiders;

            for (int i = 0; i < total; i++)
            {
                bool dog = dogs > 0 && (raiders == 0 || i % 2 == 0);
                if (dog) dogs--; else raiders--;

                float x = start + span * (total == 1 ? 0.5f : i / (float)(total - 1)) + Random.Range(-1f, 1f);
                var kind = dog ? FieldEnemyKind.Dog : FieldEnemyKind.Raider;
                var body = Make(dog ? PrimitiveType.Cube : PrimitiveType.Capsule, dog ? "Köpek" : "Haydut",
                    FieldEnemy.ColorFor(kind), root.transform);
                body.localScale = dog ? new Vector3(0.9f, 0.55f, 0.45f) : new Vector3(0.75f, 0.95f, 0.75f);
                body.position = new Vector3(x, groundY + (dog ? 0.28f : 0.95f), z);
                enemies.Add(new FieldEnemy(kind, body, groundY));
            }
        }

        void BuildAvatar()
        {
            var body = Make(PrimitiveType.Capsule, explorer.DisplayName, BranchColor(explorer), root.transform);
            body.localScale = new Vector3(0.75f, 0.95f, 0.75f);
            body.position = new Vector3(originX + 1.5f, groundY + 0.95f, z);
            avatar = body;
            avatarRenderer = body.GetComponent<Renderer>();

            var nose = Make(PrimitiveType.Cube, "Yön", Color.white, body);
            nose.localPosition = new Vector3(0f, 0.3f, 0.55f);
            nose.localScale = new Vector3(0.3f, 0.2f, 0.5f);

            slash = Make(PrimitiveType.Cube, "Darbe", new Color(1f, 1f, 0.7f), root.transform);
            slash.localScale = new Vector3(1f, 0.12f, 0.12f);
            slash.gameObject.SetActive(false);
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

            UpdateInput();

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Remaining -= dt;
            healCooldown = Mathf.Max(0f, healCooldown - dt);
            messageTimer -= dt;
            if (Remaining <= 0f)
            {
                Outcome = FieldOutcome.TimeUp;
                return;
            }

            UpdateAvatar(dt);

            foreach (var enemy in enemies) enemy.Tick(dt, this, Tint);
            foreach (var spot in spots) spot.Refresh(Tint);

            UpdateSlash(dt);
            UpdateFloaters(dt);

            if (explorer.IsDead) Outcome = FieldOutcome.Died;
        }

        void UpdateInput()
        {
            if (!SideViewCamera.ViewingField || !GameInput.CommandPressed) return;
            if (UIBlocker.IsPointerOver(GameInput.PointerPosition) || cam == null) return;

            var ray = cam.ScreenPointToRay(GameInput.PointerPosition);
            if (Mathf.Abs(ray.direction.z) < 0.0001f) return;

            float t = (z - ray.origin.z) / ray.direction.z;
            if (t < 0f) return;
            Vector3 point = ray.GetPoint(t);

            var enemy = PickEnemy(point);
            if (enemy != null)
            {
                ClearOrders();
                attackTarget = enemy;
                return;
            }

            var spot = PickSpot(point);
            if (spot != null)
            {
                OrderSearch(spot);
                return;
            }

            ClearOrders();
            if (Mathf.Abs(point.x - originX) <= 1.3f) returnOnArrival = true;
            GoTo(point.x);
        }

        FieldEnemy PickEnemy(Vector3 point)
        {
            FieldEnemy best = null;
            float bestDistance = 1f;
            foreach (var enemy in enemies)
            {
                if (enemy.IsDead) continue;
                float distance = Mathf.Abs(enemy.X - point.x);
                if (distance > bestDistance || point.y < groundY - 0.3f || point.y > groundY + 2.4f) continue;
                best = enemy;
                bestDistance = distance;
            }
            return best;
        }

        FieldSpot PickSpot(Vector3 point)
        {
            foreach (var spot in spots)
            {
                if (Mathf.Abs(spot.X - point.x) <= 0.9f && point.y > groundY - 0.3f && point.y < groundY + 1.8f) return spot;
            }
            return null;
        }

        void OrderSearch(FieldSpot spot)
        {
            if (spot.Done)
            {
                Say("Burası zaten arandı.");
                return;
            }
            if (spot.Kind == FieldSpotKind.Loot && usedSlots >= bagSlots)
            {
                Say("Çanta dolu! Çıkışa dön ya da başka bir şeyi bırakma.");
                return;
            }
            if (spot.Kind == FieldSpotKind.Rescue && !rescueAvailable)
            {
                Say("Sığınakta yer yok ya da kurtarılacak kişi için dal kalmadı.");
                return;
            }

            ClearOrders();
            searchTarget = spot;
            searchProgress = 0f;
        }

        void ClearOrders()
        {
            attackTarget = null;
            searchTarget = null;
            searchProgress = 0f;
            hasMoveGoal = false;
            returnOnArrival = false;
        }

        void GoTo(float x)
        {
            hasMoveGoal = true;
            moveGoalX = Mathf.Clamp(x, originX, originX + site.fieldLength);
        }

        void UpdateAvatar(float dt)
        {
            attackCooldown -= dt;
            float condition = explorer.Condition.SpeedMultiplier;
            float speed = AvatarSpeed * condition * SkillTree.MoveSpeedBonus;
            float x = avatar.position.x;

            if (attackTarget != null)
            {
                if (attackTarget.IsDead)
                {
                    attackTarget = null;
                }
                else
                {
                    float dx = attackTarget.X - x;
                    Face(dx);
                    if (Mathf.Abs(dx) > AvatarReach) Step(Mathf.Sign(dx), speed * dt);
                    else if (attackCooldown <= 0f) Strike(condition);
                    return;
                }
            }

            if (searchTarget != null)
            {
                float dx = searchTarget.X - x;
                Face(dx);
                if (Mathf.Abs(dx) > SearchReach)
                {
                    Step(Mathf.Sign(dx), speed * dt);
                    return;
                }

                searchProgress += dt * condition;
                if (searchProgress >= searchTarget.Duration) FinishSearch(searchTarget);
                return;
            }

            if (!hasMoveGoal) return;

            float remaining = moveGoalX - x;
            Face(remaining);
            float tolerance = returnOnArrival ? ExitReach : 0.1f;
            if (Mathf.Abs(remaining) > tolerance)
            {
                Step(Mathf.Sign(remaining), Mathf.Min(speed * dt, Mathf.Abs(remaining)));
                return;
            }

            hasMoveGoal = false;
            if (returnOnArrival) Outcome = FieldOutcome.Returned;
            returnOnArrival = false;
        }

        void Step(float direction, float distance)
        {
            var position = avatar.position;
            position.x = Mathf.Clamp(position.x + direction * distance, originX, originX + site.fieldLength);
            avatar.position = position;
        }

        void Face(float dx)
        {
            if (Mathf.Abs(dx) < 0.01f) return;
            avatar.rotation = Quaternion.LookRotation(dx > 0f ? Vector3.right : Vector3.left);
        }

        void Strike(float condition)
        {
            attackCooldown = AvatarAttackInterval / Mathf.Max(0.5f, condition);
            float damage = AvatarDamage * condition;
            attackTarget.TakeHit(damage);
            Float($"-{damage:0}", attackTarget.Position + Vector3.up * 1.2f, Color.white);

            float direction = Mathf.Sign(attackTarget.X - avatar.position.x);
            slash.position = avatar.position + new Vector3(direction * 0.9f, 0.2f, 0f);
            slash.gameObject.SetActive(true);
            slashTimer = 0.1f;

            if (attackTarget.IsDead)
            {
                Float(attackTarget.Label + " düştü", attackTarget.Position + Vector3.up * 1.8f, new Color(1f, 0.85f, 0.3f));
                attackTarget = null;
            }
        }

        void UpdateSlash(float dt)
        {
            if (slashTimer <= 0f) return;
            slashTimer -= dt;
            if (slashTimer <= 0f) slash.gameObject.SetActive(false);
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

            if (site.hazard != ExpeditionHazard.None && Random.value < site.hazardChance)
            {
                string cause = site.hazard == ExpeditionHazard.Fire ? "alev aldı" : "göçük başına yıkıldı";
                float damage = site.hazardDamage * 0.7f;
                reportText.Append(reportText.Length > 0 ? " " : "").Append(site.hazard == ExpeditionHazard.Fire
                    ? "Aradığı yerde yangın çıktı." : "Aradığı yerde göçük oldu.");
                explorer.Condition.AddStress(10f);
                DamageAvatar(damage, $"Seferde {cause}.", "Tuzak");
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

        /// <summary>Düşman ya da tuzak hasarı. Arama yarıda kesilir.</summary>
        public void DamageAvatar(float amount, string cause, string source)
        {
            if (!IsActive || explorer.IsDead) return;

            explorer.TakeDamage(amount, cause);
            searchProgress = 0f;
            Float($"-{amount:0}", avatar.position + Vector3.up * 1.6f, new Color(1f, 0.35f, 0.3f));
            Tint(avatarRenderer, Color.white);
            flashAvatar = 0.12f;
        }

        float flashAvatar;

        void LateUpdate()
        {
            if (!IsActive || avatarRenderer == null || flashAvatar <= 0f) return;
            flashAvatar -= Time.unscaledDeltaTime;
            if (flashAvatar <= 0f) Tint(avatarRenderer, BranchColor(explorer));
        }

        public bool AvatarIsDead => explorer == null || explorer.IsDead;

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

            DrawWorldLabels();
            DrawPanel();
        }

        void DrawWorldLabels()
        {
            DrawLabel(new Vector3(originX, groundY + 2.8f, z), "ÇIKIŞ (sağ tık)", new Color(0.5f, 1f, 0.6f));

            foreach (var spot in spots)
            {
                string label = spot.Done ? "(boş)"
                    : spot.Kind == FieldSpotKind.Rescue ? "MAHSUR KALAN"
                    : "Ara";
                DrawLabel(spot.Position + Vector3.up * 0.9f, label, spot.Done ? Color.gray : Color.white);

                if (searchTarget == spot) DrawBar(spot.Position + Vector3.up * 1.3f, searchProgress / spot.Duration, Color.white, 54);
            }

            foreach (var enemy in enemies)
            {
                if (enemy.IsDead) continue;
                DrawLabel(enemy.Position + Vector3.up * 1.6f, enemy.Label, new Color(1f, 0.6f, 0.5f));
                DrawBar(enemy.Position + Vector3.up * 1.3f, enemy.Health01, Color.red, 44);
            }

            DrawBar(avatar.position + Vector3.up * 1.4f, explorer.Health / explorer.MaxHealth, Color.green, 56);

            foreach (var item in floaters)
            {
                var previous = GUI.color;
                GUI.color = new Color(item.Color.r, item.Color.g, item.Color.b, 1f - item.Age / 1.4f);
                DrawLabel(item.Position, item.Text, GUI.color);
                GUI.color = previous;
            }
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

            var rect = new Rect(screen.x - width * 0.5f, Screen.height - screen.y, width, 5f);
            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x + 1, rect.y + 1, (rect.width - 2) * Mathf.Clamp01(fraction), rect.height - 2), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        void DrawPanel()
        {
            const float width = 460f;
            var rect = new Rect((Screen.width - width) * 0.5f, 10, width, 124);
            UIBlocker.Add(rect);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12));

            GUILayout.Label($"SAHA: {site.name} — {explorer.DisplayName}   Kalan: {Mathf.CeilToInt(Remaining)} sn", centered);
            GUILayout.Label($"Can {explorer.Health:0}/{explorer.MaxHealth:0}   Çanta {usedSlots}/{bagSlots}: {BagText()}" +
                            (RescueSecured ? "   [Kazazede kurtarıldı]" : ""), small);
            if (messageTimer > 0f && message != null) GUILayout.Label(message, small);

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();

            var storage = ResourceStorage.Instance;
            int kits = storage != null ? storage.Get(ResourceType.Supplies) : 0;
            GUI.enabled = kits > 0 && explorer.Health < explorer.MaxHealth && healCooldown <= 0f;
            if (LeftClick($"İlk Yardım (Yaşam Kiti: {kits})")) UseMedkit(storage);
            GUI.enabled = true;

            bool nearExit = Mathf.Abs(avatar.position.x - originX) <= ExitHudRange;
            if (LeftClick(nearExit ? "Üsse Dön" : "Çıkışa Git"))
            {
                ClearOrders();
                returnOnArrival = true;
                GoTo(originX);
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void UseMedkit(ResourceStorage storage)
        {
            if (storage == null || !storage.TrySpend(new ResourceAmount(ResourceType.Supplies, 1))) return;

            explorer.Heal(HealAmount);
            healCooldown = HealCooldown;
            Float($"+{HealAmount:0}", avatar.position + Vector3.up * 1.6f, new Color(0.5f, 1f, 0.5f));
        }

        // IMGUI butonları sağ tıka da tepki verir; sadece sol tık kabul edilir.
        static bool LeftClick(string text) => GUILayout.Button(text) && Event.current.button == 0;

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
