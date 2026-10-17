using System.Collections.Generic;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    public enum FieldEnemyKind { Dog, Raider, Brute, Gunner }

    enum EnemyState { Idle, Investigate, Chase, Windup, Recover, Dead }

    /// <summary>
    /// Saha düşmanı (MonoBehaviour değil; FieldExpedition sürer). CharacterController ile yürür, duvarlara çarpar.
    /// Görür: görüş mesafesinde ve arada duvar/araba/kasa yoksa (sakinken önündeki 140°'lik açıda; arkasından sızılabilir).
    /// Kâşifin açıkta kalan noktasına bakar: alçak siperin arkasında çömelen görünmez, köşeden eğilenin sadece kenarı görünür.
    /// Duyar: tabanca sesi yarıçapındaki düşmanlar sesin geldiği yere gider. Birini fark eden yakındakileri de uyarır.
    /// Saldırmadan önce sararır (uyarı): yakın dövüşçü vurur, Silahlı nişan çizgisi çizip ateş eder. Kaçış hasarı boşa çıkarır.
    /// Binaya kapılardan girip çıkar. Silahlı siper noktası seçer, arkasında çömelir, ateş etmek için doğrulur.
    /// Bazı Yağmacılar doğrudan değil yandan dolanarak yaklaşır.
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
        readonly List<Vector3> route = new List<Vector3>();
        int routeIndex;
        float repathTimer;
        Vector3 routeGoal;
        float wanderTimer;
        Vector3 wanderTarget;

        readonly bool flanker;
        readonly float flankSide;
        readonly float bodyHeight;
        readonly Vector3 bodyScale;
        readonly Vector3 bodyPosition;
        bool crouching;
        int coverIndex = -1;
        bool hasCover;
        Vector3 coverPoint;
        float coverTimer;
        Vector3 knock;

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
            flanker = kind == FieldEnemyKind.Raider && Random.value < 0.6f;
            flankSide = Random.value < 0.5f ? -1f : 1f;
            bodyHeight = controller.height;
            bodyScale = body.transform.localScale;
            bodyPosition = body.transform.localPosition;
            wanderTimer = Random.Range(1f, 3f);
            wanderTarget = home;
        }

        public FieldEnemyKind Kind => kind;
        public bool IsDead => state == EnemyState.Dead;
        public Collider Collider => controller;
        public Vector3 Position => root.position;
        public float Radius => controller.radius;

        /// <summary>Atışın yöneldiği gövde noktası (çömelince alçalır).</summary>
        public Vector3 AimPoint => root.position + Vector3.up * (controller.height * 0.6f);
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

        /// <summary>
        /// Kurşun/taş/saçma isabeti. Atışın geldiği yeri öğrenir ve geri savrulur. Saldırı hazırlığı bozulur;
        /// İri Yağmacı'nınki sadece güçlü darbede (pompalı) bozulur.
        /// </summary>
        public void TakeHit(float amount, Vector3 shooterPosition, float knockbackDistance = 0f)
        {
            if (IsDead) return;

            bool heavy = knockbackDistance >= 1f;
            if (knockbackDistance > 0f)
            {
                float resist = kind == FieldEnemyKind.Brute ? 0.35f : kind == FieldEnemyKind.Dog ? 1.4f : 1f;
                var away = Flat(root.position - shooterPosition).normalized;
                knock = away * Mathf.Sqrt(28f * knockbackDistance * resist);
            }

            health -= amount;
            flash = 0.08f;
            if (health <= 0f)
            {
                Die();
                return;
            }

            if ((kind != FieldEnemyKind.Brute || heavy) && state == EnemyState.Windup)
            {
                state = EnemyState.Recover;
                timer = 0.3f;
                field.HideAimLine(this);
            }
            stagger = kind == FieldEnemyKind.Brute ? (heavy ? 0.25f : 0f) : heavy ? 0.4f : 0.15f;
            coverTimer = 0f;
            if (body != null) body.GetComponentInParent<AftershockIsle.Characters.CharacterView>()?.PlayHit();
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
            field.ReleaseCover(ref coverIndex);
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
            ApplyKnockback(dt);
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

            // Hedef kâşifin açıkta kalan noktası: çömelikse alçakta, köşeden eğiliyorsa kenarın ötesinde.
            var toAvatar = field.AvatarTarget - Eye;
            float distance = toAvatar.magnitude;
            if (distance > (IsAlerted ? sight * 1.5f : sight * field.StealthFactor)) return false;

            // Sakinken sadece önüne bakar: arkasından yaklaşmak mümkün.
            if (!IsAlerted)
            {
                var flat = new Vector3(toAvatar.x, 0f, toAvatar.z).normalized;
                if (Vector3.Dot(root.forward, flat) < IdleViewCos && distance > 2f) return false;
            }

            return field.HasLineOfSight(Eye, field.AvatarTarget);
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
                SetCrouch(false);
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
                GunnerChase(dt, distance);
                return;
            }

            if (distance > reach * 0.85f)
            {
                MoveToward(ApproachPoint(distance), speed, dt, reach * 0.8f);
                return;
            }

            Face(field.AvatarFeet - root.position, dt);
            if (cooldown <= 0f) BeginWindup();
        }

        // Yandan dolanan Yağmacı uzaktayken hedefin yanındaki bir noktaya yürür, yaklaşınca doğrudan saldırır.
        Vector3 ApproachPoint(float distance)
        {
            if (!flanker || distance < 3.5f) return field.AvatarFeet;

            var toEnemy = Flat(root.position - field.AvatarFeet).normalized;
            var side = Vector3.Cross(Vector3.up, toEnemy) * flankSide;
            return field.AvatarFeet + side * 3f + toEnemy * 1.5f;
        }

        // Silahlı: siper bulursa oraya koşar, arkasında çömelir, ateş zamanı gelince doğrulup nişan alır.
        // Siper yoksa mesafe korur. Siper işe yaramaz hale gelince (kâşif yer değiştirdi, vuruldu) yenisini arar.
        void GunnerChase(float dt, float distance)
        {
            coverTimer -= dt;
            if (coverTimer <= 0f)
            {
                coverTimer = 1.5f;
                bool stillGood = hasCover &&
                                 !field.HasLineOfSight(coverPoint + Vector3.up * 0.7f, field.AvatarChest) &&
                                 field.HasLineOfSight(coverPoint + Vector3.up * 1.5f, field.AvatarTarget);
                if (!stillGood) hasCover = field.TryClaimCover(root.position, field.AvatarFeet, ref coverIndex, out coverPoint);
            }

            if (hasCover)
            {
                if (Flat(coverPoint - root.position).magnitude > 0.35f)
                {
                    SetCrouch(false);
                    MoveToward(coverPoint, speed * 1.2f, dt, 0.3f);
                    return;
                }

                Face(field.AvatarFeet - root.position, dt);
                if (cooldown > 0f)
                {
                    SetCrouch(true);
                    return;
                }
                BeginWindup();
                return;
            }

            SetCrouch(false);
            if (distance < 5f) MoveToward(root.position - Flat(field.AvatarFeet - root.position).normalized * 3f, speed, dt, 0.2f);
            else if (distance > 10f) MoveToward(field.AvatarFeet, speed, dt, 0.2f);
            else Face(field.AvatarFeet - root.position, dt);

            if (cooldown <= 0f && distance <= reach) BeginWindup();
        }

        // Çömelen düşmanın collider'ı alçalır: alçak siperin üstünden geçen atış ona değmez.
        void SetCrouch(bool value)
        {
            if (crouching == value) return;
            crouching = value;

            float height = value ? bodyHeight * 0.6f : bodyHeight;
            controller.height = height;
            controller.center = new Vector3(0f, height * 0.5f + 0.02f, 0f);

            float factor = value ? 0.6f : 1f;
            body.transform.localScale = new Vector3(bodyScale.x, bodyScale.y * factor, bodyScale.z);
            body.transform.localPosition = new Vector3(bodyPosition.x, bodyPosition.y * factor, bodyPosition.z);
        }

        void BeginWindup()
        {
            SetCrouch(false);
            state = EnemyState.Windup;
            timer = windupTime;
            aimDirection = (field.AvatarTarget - Eye).normalized;
        }

        void Windup(float dt, bool sees)
        {
            timer -= dt;

            if (kind == FieldEnemyKind.Gunner)
            {
                // İlk bölümde hedefi takip eder, son 0.3 sn'de nişan kilitlenir: yana kaçarak kurtulunur.
                if (timer > 0.3f && sees) aimDirection = (field.AvatarTarget - Eye).normalized;
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
            if (body != null) body.GetComponentInParent<AftershockIsle.Characters.CharacterView>()?.PlayFire();

            if (kind == FieldEnemyKind.Gunner)
            {
                field.HideAimLine(this);
                field.EnemyShoot(this, Eye, aimDirection, reach, damage);
                return;
            }

            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.MeleeSwing, 0.75f);
            float distance = Flat(field.AvatarFeet - root.position).magnitude;
            if (field.CompanionCloserThanAvatar(root.position, reach + 0.3f))
                field.DamageCompanion(damage, $"Seferde {Label} tarafından öldürüldü.");
            else if (distance <= reach + 0.3f && field.HasLineOfSight(Eye, field.AvatarChest))
                field.DamageAvatar(damage, $"Seferde {Label} tarafından öldürüldü.");
        }

        // ---------------------------------------------------------------- Hareket

        /// <summary>Hedefe engellerin etrafından ve kapılardan geçen yolla yürür; takılırsa kısa süre yana kayar. Varınca true.</summary>
        bool MoveToward(Vector3 destination, float moveSpeed, float dt, float stopDistance)
        {
            if (Flat(destination - root.position).magnitude <= stopDistance) return true;

            var waypoint = NextWaypoint(destination, dt);
            var toTarget = Flat(waypoint - root.position);
            if (toTarget.sqrMagnitude < 0.0001f) return false;

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

        // Yol yarım saniyede bir ya da hedef bir metreden fazla kayınca yeniden çıkarılır; köşelere vardıkça sıradakine geçilir.
        Vector3 NextWaypoint(Vector3 destination, float dt)
        {
            repathTimer -= dt;
            bool goalMoved = Flat(destination - routeGoal).sqrMagnitude > 1f;
            if (repathTimer <= 0f || (goalMoved && repathTimer < 0.35f))
            {
                repathTimer = 0.5f + Random.value * 0.2f;
                routeGoal = destination;
                routeIndex = 0;
                field.FindRoute(root.position, destination, route);
            }

            while (routeIndex < route.Count && Flat(route[routeIndex] - root.position).magnitude < 0.35f) routeIndex++;
            return routeIndex < route.Count ? route[routeIndex] : destination;
        }

        void ApplyKnockback(float dt)
        {
            if (knock.sqrMagnitude < 0.01f) return;
            controller.Move(knock * dt);
            KeepOnGround();
            knock = Vector3.MoveTowards(knock, Vector3.zero, 14f * dt);
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
