using System.Collections.Generic;
using AftershockIsle.CameraControl;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    /// <summary>Silah, nişan, atış ve siper (çömelme, köşeden eğilme).</summary>
    public partial class FieldExpedition
    {
        // ---------------------------------------------------------------- Silah

        public Vector3 AvatarFeet => avatarRoot != null ? avatarRoot.position : Vector3.zero;
        public Vector3 AvatarChest => AvatarFeet + Vector3.up * (CrouchedLow ? CrouchChest : StandChest);

        /// <summary>Düşmanın görüp nişan aldığı nokta: köşeden eğiliyorsa kenarın ötesindeki omuz, değilse göğüs.</summary>
        public Vector3 AvatarTarget => leanSide != 0 ? leanPoint : AvatarChest;

        /// <summary>Sakin düşmanın görüş mesafesi çarpanı: çömelik kâşif ve yoğun sis daha geç fark edilir.</summary>
        public float StealthFactor => (CrouchedLow ? 0.6f : 1f) * AftershockIsle.Crisis.WeatherSystem.EnemyVisionMultiplier;

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
                AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.EmptyClick, 0.65f);
                Say("Başka silah yok: tezgahta tabanca, pompalı veya tüfek üret.");
                return;
            }
            weapon = next;
            RefreshAvatarWeapon();
            ViewFor(avatarRenderer)?.PlayReload();
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.Reload, 0.75f);
            Say($"Silah: {CurrentWeapon.Name}");
        }

        void TryFire()
        {
            if (fireCooldown > 0f || IsDodging || searchTarget != null) return;

            var info = CurrentWeapon;
            bool sharpshooter = explorer != null && explorer.HasTrait(AftershockIsle.Characters.TraitId.Sharpshooter);

            // Çömelikken ateş: siperin üstünden atmak için bir an doğrulur (o sürede açıkta).
            if (crouched) riseTimer = RiseSeconds;

            if (info.UsesAmmo)
            {
                bool freeShot = sharpshooter && Workbench.Instance != null && Workbench.Instance.Ammo > 0 && Random.value < 0.25f;
                if (!freeShot && (Workbench.Instance == null || !Workbench.Instance.TryUseAmmo()))
                {
                    AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.EmptyClick, 0.85f);
                    Say("Mermi yok! Tezgahta Mermi Paketi üret ya da sapana geç.");
                    fireCooldown = 0.3f;
                    return;
                }
            }

            fireCooldown = info.Interval;
            ViewFor(avatarRenderer)?.PlayFire();
            AftershockIsle.Effects.SoundManager.PlayWeaponFire(weapon, 0.9f);
            var from = Muzzle;
            float dmgMultiplier = sharpshooter ? 1.25f : 1f;

            // Her saçma ayrı ışın; aynı düşmana değenlerin hasarı toplanıp tek seferde uygulanır (tek sayı, tek geri itme).
            pelletHits.Clear();
            for (int pellet = 0; pellet < info.Pellets; pellet++)
            {
                var direction = Quaternion.Euler(0f, Random.Range(-info.Spread, info.Spread), 0f) * aimDirection;
                var enemy = FirePellet(from, direction, info.Range);
                if (enemy == null) continue;

                pelletHits.TryGetValue(enemy, out float total);
                pelletHits[enemy] = total + info.Damage * dmgMultiplier;
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
            if (pelletHits.Count > 0)
            {
                AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.BulletHit, 0.75f);
                TimeManager.HitStop(killed ? 0.09f : 0.035f + info.Recoil * 0.03f);
            }

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
            if (CastSkippingAlly(from, shot, length + 0.5f, out var hit))
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
    }
}
