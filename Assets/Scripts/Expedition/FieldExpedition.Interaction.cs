using AftershockIsle.CameraControl;
using AftershockIsle.Core;
using AftershockIsle.Economy;
using AftershockIsle.Skills;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    /// <summary>Arama, kurtarma, çıkış, ilk yardım ve kâşifin aldığı hasar.</summary>
    public partial class FieldExpedition
    {
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

            // Aranırken karıştırma sesi, yaklaşık yarım saniyede bir.
            searchSoundTimer -= dt;
            if (searchSoundTimer <= 0f)
            {
                searchSoundTimer = 0.8f;
                AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.Search, 0.6f);
            }

            if (searchProgress >= searchTarget.Duration) FinishSearch(searchTarget);
        }

        float searchSoundTimer;

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
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.Pickup, 0.8f);
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
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.CraftSuccess, 0.65f);
            Float($"+{amount:0}", avatar.position + Vector3.up * 1.6f, new Color(0.5f, 1f, 0.5f));
        }

        // ---------------------------------------------------------------- Hasar

        /// <summary>Tuzak ve düşman hasarı. Kaçış sırasında hasar yok; arama yarıda kesilir.</summary>
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
            ViewFor(avatarRenderer)?.PlayHit();
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.PlayerHurt, 0.9f);
        }

        float hurtFlash;

        void UpdateFlash(float dt)
        {
            hurtFlash = Mathf.Max(0f, hurtFlash - Time.unscaledDeltaTime);
            if (flashAvatar <= 0f) return;
            flashAvatar -= dt;
            if (flashAvatar <= 0f && !IsDodging) Tint(avatarRenderer, BranchColor(explorer));
        }
    }
}
