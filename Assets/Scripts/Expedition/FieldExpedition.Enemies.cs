using System.Collections.Generic;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using AftershockIsle.Economy;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    /// <summary>Düşmanların ve yoldaşın kullandığı servisler: yol, siper noktaları, görüş hattı, atışlar.</summary>
    public partial class FieldExpedition
    {
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
            if (!CastSkippingAlly(from, to - from, (to - from).magnitude, out var hit)) return true;
            return hit.collider == controller;
        }

        /// <summary>Yoldaşın ateş edebileceği açıklık: araya duvar/siper girmiyorsa true (kâşif ve yoldaş mermiyi kesmez).</summary>
        public bool HasClearShot(Vector3 from, Vector3 to)
        {
            if (!CastSkippingAlly(from, to - from, (to - from).magnitude, out var hit)) return true;
            return hit.collider == controller || (hit.collider != null && enemyByCollider.ContainsKey(hit.collider));
        }

        // Işın yoldaşın gövdesinden geçip devam eder (arkadaşın arkasındaki düşman da görülür ve vurulur).
        bool CastSkippingAlly(Vector3 from, Vector3 direction, float length, out RaycastHit hit)
        {
            hit = default;
            if (length <= 0.0001f) return false;

            direction.Normalize();
            float travelled = 0f;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (!Physics.Raycast(from, direction, out hit, length - travelled, ~0, QueryTriggerInteraction.Ignore)) return false;
                if (companion == null || hit.collider != companion.Collider) return true;

                travelled += hit.distance + 0.05f;
                from = hit.point + direction * 0.05f;
            }
            return false;
        }

        public IReadOnlyList<FieldEnemy> Enemies => enemies;

        /// <summary>Yoldaşın atışı: izi çizer, ses çıkarır; isabet ederse düşmana hasar verir.</summary>
        public void CompanionShoot(FieldCompanion shooter, FieldEnemy target, float damage, bool hits, float noise)
        {
            var from = shooter.Chest;
            var aim = hits ? target.AimPoint : target.AimPoint + new Vector3(Random.Range(-0.8f, 0.8f), 0.2f, Random.Range(-0.8f, 0.8f));
            var direction = (aim - from).normalized;
            ShowTracer(from, direction, Vector3.Distance(from, aim), new Color(0.6f, 0.9f, 1f));
            AftershockIsle.Effects.SoundManager.PlayWeaponFire(WeaponKind.Smg, 0.55f);
            MakeNoise(from, noise);

            if (!hits) return;
            target.TakeHit(damage, shooter.Position);
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.BulletHit, 0.5f);
            Float($"-{damage:0}", target.AimPoint + Vector3.up * 0.6f, new Color(0.6f, 0.9f, 1f));
        }

        /// <summary>Düşman darbesi yoldaşa mı gidiyor: menzildeyse ve kâşiften yakınsa.</summary>
        public bool CompanionCloserThanAvatar(Vector3 from, float reach)
        {
            if (companion == null || companion.IsDead) return false;

            float toCompanion = FlatXZ(companion.Position - from).magnitude;
            if (toCompanion > reach) return false;
            return AvatarIsDead || toCompanion < FlatXZ(AvatarFeet - from).magnitude;
        }

        public void DamageCompanion(float amount, string cause)
        {
            if (companion == null || companion.IsDead || amount <= 0f) return;

            companion.Survivor.TakeDamage(amount, cause);
            companion.Hurt();
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.PlayerHurt, 0.7f);
            Float($"-{amount:0}", companion.LabelPoint, new Color(1f, 0.35f, 0.3f));
            Say(companion.IsDead ? $"{companion.Survivor.DisplayName} öldü!" : $"{companion.Survivor.DisplayName} yaralandı.");
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
            AftershockIsle.Effects.SoundManager.PlayWeaponFire(WeaponKind.AssaultRifle, 0.7f);
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
            else if (blocked && companion != null && hit.collider == companion.Collider)
                DamageCompanion(damage, $"Seferde {enemy.Label} tarafından vuruldu.");

            ShowTracer(from, direction, length, new Color(1f, 0.5f, 0.3f));
            MakeNoise(enemy.Position, 10f);
        }

        public void OnEnemyKilled(FieldEnemy enemy)
        {
            GameInput.Rumble(0.4f, 0.6f, 0.15f);
            var at = enemy.Position + Vector3.up * enemy.LabelHeight;
            Float($"{enemy.Label} düştü", at, new Color(1f, 0.85f, 0.3f));

            if (enemy.Kind == FieldEnemyKind.Gunner)
            {
                ammo += 3;
                Float("+3 mermi", at + Vector3.up * 0.5f, new Color(1f, 0.9f, 0.5f));
            }
            else if (enemy.Kind != FieldEnemyKind.Dog && Random.value < 0.35f)
            {
                GainLoot(new ResourceAmount(ResourceType.Supplies, 1), enemy.Position);
                Float("+1 Yaşam Kiti", at + Vector3.up * 0.5f, ResourceType.Supplies.ToColor());
            }
        }
    }
}
