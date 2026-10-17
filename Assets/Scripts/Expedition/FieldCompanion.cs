using System.Collections.Generic;
using AftershockIsle.Characters;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    /// <summary>
    /// Sefere kâşifle birlikte çıkan ikinci kazazede (MonoBehaviour değil; FieldExpedition sürer). Kâşifi arkadan takip eder,
    /// alarmdaki ya da yakındaki düşmanlara görüş hattı varsa ateş eder (sesi düşmanları çeker), darbe alırsa canı kazazedenin
    /// kendi canından düşer ve ölürse sahada kalır. Çanta taşımaz ama çantaya bir slot ekler (sefer sistemi verir).
    /// </summary>
    public class FieldCompanion
    {
        const float FollowSpeed = 4.3f;
        const float CatchUpSpeed = 6.2f;
        const float SightRange = 13f;
        const float ShotInterval = 1.15f;
        const float ShotDamage = 11f;
        const float Accuracy = 0.75f;
        const float ShotNoise = 12f;
        const float ChestHeight = 1.25f;
        const float AlwaysEngageRange = 4.5f;

        readonly FieldExpedition field;
        readonly Survivor survivor;
        readonly Transform root;
        readonly Renderer body;
        readonly CharacterController controller;
        readonly Color baseColor;
        readonly float groundY;
        readonly float side;

        readonly List<Vector3> route = new List<Vector3>();
        int routeIndex;
        float repathTimer;
        float shotTimer = 0.6f;
        float flash;
        bool downed;

        public FieldCompanion(FieldExpedition field, Survivor survivor, Transform root, Renderer body,
                              CharacterController controller, Color baseColor, float groundY)
        {
            this.field = field;
            this.survivor = survivor;
            this.root = root;
            this.body = body;
            this.controller = controller;
            this.baseColor = baseColor;
            this.groundY = groundY;
            side = Random.value < 0.5f ? -1f : 1f;
        }

        public Survivor Survivor => survivor;
        public bool IsDead => survivor.IsDead;
        public Collider Collider => controller;
        public Vector3 Position => root.position;
        public Vector3 AimPoint => root.position + Vector3.up * 1.0f;
        public Vector3 Chest => root.position + Vector3.up * ChestHeight;
        public Vector3 LabelPoint => root.position + Vector3.up * 2.1f;

        public void Hurt()
        {
            flash = 0.12f;
            if (body != null) body.GetComponentInParent<CharacterView>()?.PlayHit();
        }

        public void Tick(float dt)
        {
            if (survivor.IsDead)
            {
                Down();
                return;
            }

            UpdateColor(dt);
            Follow(dt);
            Fight(dt);
        }

        // Yere yığılır, bir daha kıpırdamaz.
        void Down()
        {
            if (downed) return;
            downed = true;
            controller.enabled = false;
            root.rotation = Quaternion.Euler(0f, root.eulerAngles.y, 90f);
            root.position += Vector3.up * 0.3f;
            field.Tint(body, baseColor * 0.35f);
        }

        // Kâşifin biraz gerisinde (çıkışa doğru) ve yan şeritte durur; kâşif uzaklaşırsa koşarak yetişir.
        void Follow(float dt)
        {
            var anchor = field.AvatarFeet;
            var slot = anchor + new Vector3(-1.9f, 0f, side * 1.3f);
            float distance = Flat(slot - root.position).magnitude;
            if (distance < 0.5f) return;

            repathTimer -= dt;
            if (repathTimer <= 0f)
            {
                repathTimer = 0.45f + Random.value * 0.2f;
                routeIndex = 0;
                field.FindRoute(root.position, slot, route);
            }

            while (routeIndex < route.Count && Flat(route[routeIndex] - root.position).magnitude < 0.35f) routeIndex++;
            var waypoint = routeIndex < route.Count ? route[routeIndex] : slot;
            var direction = Flat(waypoint - root.position);
            if (direction.sqrMagnitude < 0.0001f) return;

            float speed = distance > 6f ? CatchUpSpeed : FollowSpeed;
            direction.Normalize();
            controller.Move(direction * (speed * dt));
            KeepOnGround();
            Face(direction, dt);
        }

        void Fight(float dt)
        {
            shotTimer -= dt;
            var target = PickTarget();
            if (target == null) return;

            Face(target.Position - root.position, dt);
            if (shotTimer > 0f) return;

            shotTimer = ShotInterval * Random.Range(0.85f, 1.2f);
            if (body != null) body.GetComponentInParent<CharacterView>()?.PlayFire();
            field.CompanionShoot(this, target, ShotDamage, Random.value <= Accuracy, ShotNoise);
        }

        // Alarmdaki ya da çok yakın, görüş hattındaki en yakın düşman. Sakin, uzaktaki düşmanlara dokunmaz (sessiz ilerleme bozulmasın).
        FieldEnemy PickTarget()
        {
            FieldEnemy best = null;
            float bestDistance = SightRange;
            foreach (var enemy in field.Enemies)
            {
                if (enemy.IsDead) continue;

                float distance = Flat(enemy.Position - root.position).magnitude;
                if (distance >= bestDistance) continue;
                if (!enemy.IsAlerted && distance > AlwaysEngageRange) continue;
                if (!field.HasClearShot(Chest, enemy.AimPoint)) continue;

                best = enemy;
                bestDistance = distance;
            }
            return best;
        }

        void KeepOnGround()
        {
            if (Mathf.Abs(root.position.y - groundY) < 0.01f) return;
            controller.enabled = false;
            root.position = new Vector3(root.position.x, groundY, root.position.z);
            controller.enabled = true;
        }

        void Face(Vector3 direction, float dt)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            root.rotation = Quaternion.RotateTowards(root.rotation, Quaternion.LookRotation(direction), 600f * dt);
        }

        void UpdateColor(float dt)
        {
            if (flash > 0f)
            {
                flash -= dt;
                field.Tint(body, Color.white);
                return;
            }
            field.Tint(body, baseColor);
        }

        static Vector3 Flat(Vector3 vector) => new Vector3(vector.x, 0f, vector.z);
    }
}
