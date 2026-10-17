using System;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    public enum FieldEnemyKind { Dog, Raider }

    /// <summary>
    /// Saha düşmanı (MonoBehaviour değil; FieldExpedition sürer). Menzile girince kovalar, saldırmadan önce
    /// kısa bir hazırlık (renk uyarısı) yapar; kâşif hazırlık sırasında uzaklaşırsa darbe boşa gider.
    /// Çok uzaklaşılırsa kovalamayı bırakıp başladığı yere döner.
    /// </summary>
    public class FieldEnemy
    {
        readonly Transform body;
        readonly Renderer renderer;
        readonly FieldEnemyKind kind;
        readonly float maxHealth;
        readonly float speed;
        readonly float damage;
        readonly float aggroRange;
        readonly float leashRange;
        readonly float reach;
        readonly float windupTime;
        readonly float interval;
        readonly float homeX;
        readonly Color baseColor;

        float health;
        bool aggroed;
        bool windingUp;
        float windup;
        float cooldown;
        float flash;

        public FieldEnemy(FieldEnemyKind kind, Transform body, float groundY)
        {
            this.kind = kind;
            this.body = body;
            renderer = body.GetComponent<Renderer>();
            homeX = body.position.x;
            baseColor = ColorFor(kind);

            if (kind == FieldEnemyKind.Dog)
            {
                maxHealth = 22f; speed = 3.9f; damage = 6f; aggroRange = 8f; leashRange = 14f;
                reach = 1.2f; windupTime = 0.3f; interval = 0.9f;
            }
            else
            {
                maxHealth = 45f; speed = 1.9f; damage = 11f; aggroRange = 6.5f; leashRange = 12f;
                reach = 1.5f; windupTime = 0.5f; interval = 1.4f;
            }
            health = maxHealth;
        }

        public bool IsDead => health <= 0f;
        public float X => body.position.x;
        public Vector3 Position => body.position;
        public float Health01 => health / maxHealth;
        public string Label => kind == FieldEnemyKind.Dog ? "Sokak Köpeği" : "Yağmacı";

        public static Color ColorFor(FieldEnemyKind kind) =>
            kind == FieldEnemyKind.Dog ? new Color(0.75f, 0.45f, 0.2f) : new Color(0.7f, 0.15f, 0.15f);

        public void TakeHit(float amount)
        {
            if (IsDead) return;

            health -= amount;
            aggroed = true;
            flash = 0.1f;
            windingUp = false;
            cooldown = Mathf.Max(cooldown, 0.4f);

            if (IsDead)
            {
                // Greybox: ceset yere yatar, kararır.
                body.rotation = Quaternion.Euler(0f, 0f, 90f);
                body.position = new Vector3(body.position.x, body.position.y - 0.35f, body.position.z);
            }
        }

        public void Tick(float dt, FieldExpedition field, Action<Renderer, Color> tint)
        {
            if (IsDead)
            {
                tint(renderer, baseColor * 0.35f);
                return;
            }

            if (flash > 0f)
            {
                flash -= dt;
                tint(renderer, Color.white);
                return;
            }

            float dx = field.AvatarX - X;
            float distance = Mathf.Abs(dx);
            bool targetAlive = !field.AvatarIsDead;

            if (!aggroed && distance < aggroRange && targetAlive) aggroed = true;
            if (aggroed && (!targetAlive || Mathf.Abs(X - homeX) > leashRange))
            {
                aggroed = false;
                windingUp = false;
            }

            cooldown -= dt;
            tint(renderer, windingUp ? new Color(1f, 0.85f, 0.2f) : baseColor);

            if (!aggroed)
            {
                float back = homeX - X;
                if (Mathf.Abs(back) > 0.2f) Move(Mathf.Sign(back), speed * 0.5f * dt);
                return;
            }

            if (windingUp)
            {
                windup -= dt;
                if (windup > 0f) return;

                windingUp = false;
                cooldown = interval;
                if (distance <= reach + 0.35f) field.DamageAvatar(damage, $"Seferde {Label} tarafından öldürüldü.", Label);
                return;
            }

            if (distance > reach * 0.9f)
            {
                Move(Mathf.Sign(dx), speed * dt);
                return;
            }

            if (cooldown > 0f) return;
            windingUp = true;
            windup = windupTime;
        }

        void Move(float direction, float distance)
        {
            var position = body.position;
            position.x += direction * distance;
            body.position = position;
            if (kind == FieldEnemyKind.Raider) body.rotation = Quaternion.LookRotation(direction > 0f ? Vector3.right : Vector3.left);
        }
    }
}
