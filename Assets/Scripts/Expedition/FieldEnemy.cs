using UnityEngine;

namespace AftershockIsle.Expedition
{
    public enum FieldEnemyKind { Dog, Raider, Brute, Gunner }

    enum EnemyState { Idle, Investigate, Chase, Windup, Recover, Dead }

    /// <summary>
    /// Saha düşmanı (MonoBehaviour değil; FieldExpedition sürer). CharacterController ile yürür, duvarlara çarpar.
    /// Görür: görüş mesafesinde ve arada duvar/araba/kasa yoksa (sakinken önündeki 140°'lik açıda; arkasından sızılabilir).
    /// Duyar: tabanca sesi yarıçapındaki düşmanlar sesin geldiği yere gider. Birini fark eden yakındakileri de uyarır.
    /// Saldırmadan önce sararır (uyarı): yakın dövüşçü vurur, Silahlı nişan çizgisi çizip ateş eder. Kaçış hasarı boşa çıkarır.
    /// </summary>
    public class FieldEnemy
    {
        const float IdleViewCos = 0.34f; // ~140° görüş açısı
        const float SearchSeconds = 3f;
        const float AllyAlertRadius = 7f;

        readonly FieldExpedition field;
        readonly Transform root;
        readonly Renderer body;
        readonly CharacterController controller;
        readonly FieldEnemyKind kind;
        readonly Color baseColor;
        readonly float maxHealth;
        readonly float speed;
        readonly float damage;
        readonly float sight;
        readonly float reach;
        readonly float windupTime;
        readonly float interval;
        readonly float eyeHeight;
        readonly Vector3 home;

        float health;
        EnemyState state;
        float timer;
        float cooldown;
        float flash;
        float stagger;
        Vector3 target;
        Vector3 aimDirection;
        Vector3 sidestep;
        float sidestepTimer;
        float stuckTimer;
        float wanderTimer;
        Vector3 wanderTarget;

        public FieldEnemy(FieldExpedition field, FieldEnemyKind kind, Transform root, Renderer body, CharacterController controller)
        {
            this.field = field;
            this.kind = kind;
            this.root = root;
            this.body = body;
            this.controller = controller;
            baseColor = ColorFor(kind);
            home = root.position;

            switch (kind)
            {
                case FieldEnemyKind.Dog:
                    maxHealth = 30f; speed = 4.6f; damage = 7f; sight = 9f; reach = 1.3f; windupTime = 0.35f; interval = 0.9f; eyeHeight = 0.6f;
                    break;
                case FieldEnemyKind.Brute:
                    maxHealth = 90f; speed = 2.0f; damage = 22f; sight = 7f; reach = 1.9f; windupTime = 0.8f; interval = 1.6f; eyeHeight = 1.8f;
                    break;
                case FieldEnemyKind.Gunner:
                    maxHealth = 35f; speed = 2.6f; damage = 10f; sight = 12f; reach = 14f; windupTime = 0.8f; interval = 1.8f; eyeHeight = 1.5f;
                    break;
                default:
                    maxHealth = 45f; speed = 3.0f; damage = 11f; sight = 8f; reach = 1.4f; windupTime = 0.45f; interval = 1.1f; eyeHeight = 1.5f;
                    break;
            }

            health = maxHealth;
            wanderTimer = Random.Range(1f, 3f);
            wanderTarget = home;
        }

        public FieldEnemyKind Kind => kind;
        public bool IsDead => state == EnemyState.Dead;
        public Collider Collider => controller;
        public Vector3 Position => root.position;
        public float Health01 => health / maxHealth;
        public bool IsAlerted => state == EnemyState.Chase || state == EnemyState.Windup || state == EnemyState.Recover;
        public bool IsInvestigating => state == EnemyState.Investigate;
        public float LabelHeight => kind == FieldEnemyKind.Dog ? 1.1f : kind == FieldEnemyKind.Brute ? 2.6f : 2.2f;

        public string Label => kind switch
        {
            FieldEnemyKind.Dog => "Sokak Köpeği",
            FieldEnemyKind.Brute => "İri Yağmacı",
            FieldEnemyKind.Gunner => "Silahlı Yağmacı",
            _ => "Yağmacı",
        };

        public static Color ColorFor(FieldEnemyKind kind) => kind switch
        {
            FieldEnemyKind.Dog => new Color(0.75f, 0.45f, 0.2f),
            FieldEnemyKind.Brute => new Color(0.45f, 0.15f, 0.55f),
            FieldEnemyKind.Gunner => new Color(0.85f, 0.65f, 0.15f),
            _ => new Color(0.7f, 0.15f, 0.15f),
        };

        Vector3 Eye => root.position + Vector3.up * eyeHeight;

        // ---------------------------------------------------------------- Dış olaylar

        /// <summary>Kurşun/taş isabeti. Atışın geldiği yeri öğrenir; İri Yağmacı dışında saldırı hazırlığı bozulur.</summary>
        public void TakeHit(float amount, Vector3 shooterPosition)
        {
            if (IsDead) return;

            health -= amount;
            flash = 0.08f;
            if (health <= 0f)
            {
                Die();
                return;
            }

            if (kind != FieldEnemyKind.Brute && state == EnemyState.Windup)
            {
                state = EnemyState.Recover;
                timer = 0.3f;
                field.HideAimLine(this);
            }
            stagger = kind == FieldEnemyKind.Brute ? 0f : 0.15f;
            Alert(shooterPosition);
        }

        /// <summary>Ses ya da bir arkadaşının uyarısı: oraya bakmaya gider (zaten kovalıyorsa değişmez).</summary>
        public void Hear(Vector3 position)
        {
            if (IsDead || IsAlerted) return;
            state = EnemyState.Investigate;
            target = position;
            timer = SearchSeconds + 4f;
        }

        void Alert(Vector3 position)
        {
            target = position;
            if (state == EnemyState.Idle || state == EnemyState.Investigate) state = EnemyState.Chase;
        }

        void Die()
        {
            state = EnemyState.Dead;
            field.HideAimLine(this);
            controller.enabled = false;
            root.rotation = Quaternion.Euler(0f, root.eulerAngles.y, 90f);
            root.position += Vector3.up * 0.3f;
            field.Tint(body, baseColor * 0.35f);
            field.OnEnemyKilled(this);
        }

        // ---------------------------------------------------------------- Döngü

        public void Tick(float dt)
        {
            if (IsDead) return;

            cooldown -= dt;
            UpdateColor(dt);
            if (stagger > 0f)
            {
                stagger -= dt;
                return;
            }

            bool sees = CanSeeAvatar();
            if (sees)
            {
                if (state == EnemyState.Idle || state == EnemyState.Investigate)
                {
                    field.AlertAllies(this, root.position, AllyAlertRadius);
                    field.Spotted(this);
                }
                Alert(field.AvatarFeet);
            }

            switch (state)
            {
                case EnemyState.Idle: Wander(dt); break;
                case EnemyState.Investigate: Investigate(dt); break;
                case EnemyState.Chase: Chase(dt, sees); break;
                case EnemyState.Windup: Windup(dt, sees); break;
                case EnemyState.Recover:
                    timer -= dt;
                    if (timer <= 0f) state = EnemyState.Chase;
                    break;
            }
        }

        bool CanSeeAvatar()
        {
            if (field.AvatarIsDead) return false;

            var toAvatar = field.AvatarChest - Eye;
            float distance = toAvatar.magnitude;
            if (distance > (IsAlerted ? sight * 1.5f : sight)) return false;

            // Sakinken sadece önüne bakar: arkasından yaklaşmak mümkün.
            if (!IsAlerted)
            {
                var flat = new Vector3(toAvatar.x, 0f, toAvatar.z).normalized;
                if (Vector3.Dot(root.forward, flat) < IdleViewCos && distance > 2f) return false;
            }

            return field.HasLineOfSight(Eye, field.AvatarChest);
        }

        // Sakin: başladığı yerin çevresinde ağır ağır dolaşır.
        void Wander(float dt)
        {
            wanderTimer -= dt;
            if (wanderTimer <= 0f)
            {
                wanderTimer = Random.Range(2.5f, 5f);
                var offset = Random.insideUnitCircle * 2.5f;
                wanderTarget = home + new Vector3(offset.x, 0f, offset.y);
            }
            MoveToward(wanderTarget, speed * 0.35f, dt, 0.3f);
        }

        // Ses ya da son görülen yer: oraya gider, bir süre bakınır, sonra sakinleşir.
        void Investigate(float dt)
        {
            timer -= dt;
            if (MoveToward(target, speed * 0.7f, dt, 0.4f)) Face(Quaternion.Euler(0f, 120f * dt, 0f) * root.forward, dt);
            if (timer > 0f) return;

            state = EnemyState.Idle;
            wanderTarget = root.position;
            wanderTimer = Random.Range(1f, 3f);
        }

        void Chase(float dt, bool sees)
        {
            if (!sees)
            {
                // Son görülen yere gider, bulamazsa arar.
                if (MoveToward(target, speed, dt, 0.5f))
                {
                    state = EnemyState.Investigate;
                    timer = SearchSeconds;
                    target = root.position + root.forward * 2f;
                }
                return;
            }

            float distance = Flat(field.AvatarFeet - root.position).magnitude;

            if (kind == FieldEnemyKind.Gunner)
            {
                // Mesafeyi korur: yaklaşılırsa geri çekilir, uzaktaysa yaklaşır, arada ateş eder.
                if (distance < 5f) MoveToward(root.position - Flat(field.AvatarFeet - root.position).normalized * 3f, speed, dt, 0.2f);
                else if (distance > 10f) MoveToward(field.AvatarFeet, speed, dt, 0.2f);
                else Face(field.AvatarFeet - root.position, dt);

                if (cooldown <= 0f && distance <= reach) BeginWindup();
                return;
            }

            if (distance > reach * 0.85f)
            {
                MoveToward(field.AvatarFeet, speed, dt, reach * 0.8f);
                return;
            }

            Face(field.AvatarFeet - root.position, dt);
            if (cooldown <= 0f) BeginWindup();
        }

        void BeginWindup()
        {
            state = EnemyState.Windup;
            timer = windupTime;
            aimDirection = (field.AvatarChest - Eye).normalized;
        }

        void Windup(float dt, bool sees)
        {
            timer -= dt;

            if (kind == FieldEnemyKind.Gunner)
            {
                // İlk bölümde hedefi takip eder, son 0.3 sn'de nişan kilitlenir: yana kaçarak kurtulunur.
                if (timer > 0.3f && sees) aimDirection = (field.AvatarChest - Eye).normalized;
                Face(aimDirection, dt);
                field.ShowAimLine(this, Eye, aimDirection, reach, timer <= 0.3f);
            }
            else
            {
                Face(field.AvatarFeet - root.position, dt);
            }

            if (timer > 0f) return;

            cooldown = interval;
            state = EnemyState.Recover;
            timer = kind == FieldEnemyKind.Brute ? 0.8f : 0.35f;

            if (kind == FieldEnemyKind.Gunner)
            {
                field.HideAimLine(this);
                field.EnemyShoot(this, Eye, aimDirection, reach, damage);
                return;
            }

            float distance = Flat(field.AvatarFeet - root.position).magnitude;
            if (distance <= reach + 0.3f && field.HasLineOfSight(Eye, field.AvatarChest))
                field.DamageAvatar(damage, $"Seferde {Label} tarafından öldürüldü.");
        }

        // ---------------------------------------------------------------- Hareket

        /// <summary>Hedefe doğru yürür; takılırsa kısa süre yana kayar. Varınca true.</summary>
        bool MoveToward(Vector3 destination, float moveSpeed, float dt, float stopDistance)
        {
            var toTarget = Flat(destination - root.position);
            if (toTarget.magnitude <= stopDistance) return true;

            var direction = toTarget.normalized;
            if (sidestepTimer > 0f)
            {
                sidestepTimer -= dt;
                direction = (direction * 0.3f + sidestep).normalized;
            }

            var before = root.position;
            controller.Move(direction * (moveSpeed * dt));
            KeepOnGround();
            Face(direction, dt);

            // İlerleyemiyorsa (duvara dayandı) rastgele bir yana kayar.
            float moved = Flat(root.position - before).magnitude;
            if (moved < moveSpeed * dt * 0.3f)
            {
                stuckTimer += dt;
                if (stuckTimer > 0.35f)
                {
                    stuckTimer = 0f;
                    sidestep = Vector3.Cross(Vector3.up, direction) * (Random.value < 0.5f ? -1f : 1f);
                    sidestepTimer = 0.7f;
                }
            }
            else
            {
                stuckTimer = 0f;
            }
            return false;
        }

        void KeepOnGround()
        {
            if (Mathf.Abs(root.position.y - home.y) < 0.01f) return;
            controller.enabled = false;
            root.position = new Vector3(root.position.x, home.y, root.position.z);
            controller.enabled = true;
        }

        void Face(Vector3 direction, float dt)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            root.rotation = Quaternion.RotateTowards(root.rotation, Quaternion.LookRotation(direction), 540f * dt);
        }

        void UpdateColor(float dt)
        {
            if (flash > 0f)
            {
                flash -= dt;
                field.Tint(body, Color.white);
                return;
            }
            field.Tint(body, state == EnemyState.Windup ? new Color(1f, 0.9f, 0.2f)
                : IsAlerted ? baseColor
                : baseColor * 0.8f);
        }

        static Vector3 Flat(Vector3 vector) => new Vector3(vector.x, 0f, vector.z);
    }
}
