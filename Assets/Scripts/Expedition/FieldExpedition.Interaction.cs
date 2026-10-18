using AftershockIsle.CameraControl;
using AftershockIsle.Core;
using AftershockIsle.Crafting;
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

            // Aranmış noktada bekleyen (çantaya sığmamış ya da yere bırakılmış) ganimet anında alınır.
            if (spot.Searched && spot.HasPending)
            {
                TakePending(spot);
                return;
            }
            if (spot.Lock == FieldSpotLock.Locked && !loadout.Crowbar)
            {
                Say(ToolKit.HasCrowbar ? "Kilitli kasa: Levye ile açılır. Sefer hazırlığında Levye'yi yanına al."
                    : "Kilitli kasa: Levye ile açılır. Tezgahta Levye üret, sefere yanında getir.");
                return;
            }
            if (spot.Lock == FieldSpotLock.Buried && !loadout.Pickaxe)
            {
                Say(ToolKit.HasPickaxe ? "Moloz altında sandık: Kazma ile kazılır. Sefer hazırlığında Kazma'yı yanına al."
                    : "Moloz altında sandık: Kazma ile kazılır. Tezgahta Kazma üret (Kademe 2), sefere yanında getir.");
                return;
            }
            if (spot.Kind == FieldSpotKind.Loot && FreeKg <= 0)
            {
                Say($"Çanta dolu! {GameInput.Prompt(InputPrompt.Inventory)} ile çantayı aç ve bir şey bırak, ya da çıkışa dön.");
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

            var found = RollLoot();
            // Kilitli kasa ve gömülü sandık bol çıkar: alet taşımanın karşılığı.
            if (spot.Lock != FieldSpotLock.None) found.amount = found.amount * 2 + 1;
            spot.Reveal(found);
            TakePending(spot);

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

        /// <summary>Noktadaki ganimetin çantaya sığan kısmını alır; sığmayan noktada bekler.</summary>
        void TakePending(FieldSpot spot)
        {
            var item = spot.Pending;
            if (item.amount <= 0) return;

            int unit = ExpeditionLoadout.ResourceWeight(item.type);
            int fit = Mathf.Min(item.amount, FreeKg / unit);
            if (fit <= 0)
            {
                Say($"Çanta dolu: {item} ({ExpeditionLoadout.Weight(item)} kg) sığmıyor. {GameInput.Prompt(InputPrompt.Inventory)} ile bir şey bırak.");
                Float($"{item} — sığmadı", spot.Position + Vector3.up * 1.4f, new Color(1f, 0.6f, 0.4f));
                return;
            }

            AddLoot(new ResourceAmount(item.type, fit));
            spot.SetPending(new ResourceAmount(item.type, item.amount - fit));
            AftershockIsle.Effects.SoundManager.Play(AftershockIsle.Effects.SoundId.Pickup, 0.8f);
            Float($"+{fit} {item.type.ToDisplayName()}", spot.Position + Vector3.up * 1.4f, item.type.ToColor());

            if (spot.HasPending) Say($"{spot.Pending} sığmadı, orada bekliyor. Çantadan bir şey bırakıp tekrar al.");
            else if (FreeKg <= 0) Say("Çanta doldu. Soldaki yeşil çıkışa dön.");
        }

        /// <summary>Düşmandan düşen ganimet: sığan çantaya, sığmayan yere bırakılır.</summary>
        void GainLoot(ResourceAmount item, Vector3 at)
        {
            int fit = Mathf.Min(item.amount, FreeKg / ExpeditionLoadout.ResourceWeight(item.type));
            if (fit > 0) AddLoot(new ResourceAmount(item.type, fit));
            if (item.amount - fit > 0) DropToGround(new ResourceAmount(item.type, item.amount - fit), at);
        }

        /// <summary>Çantadan eşya bırakır: yere aranmış bir nokta olarak konur (sonra geri alınabilir).</summary>
        public void DropFromBag(ResourceType type, int amount)
        {
            for (int i = 0; i < bag.Count; i++)
            {
                if (bag[i].type != type) continue;
                int dropped = Mathf.Min(amount, bag[i].amount);
                if (dropped <= 0) return;
                int left = bag[i].amount - dropped;
                if (left > 0) bag[i] = new ResourceAmount(type, left);
                else bag.RemoveAt(i);
                DropToGround(new ResourceAmount(type, dropped), avatarRoot.position + avatarRoot.forward * 0.7f);
                return;
            }
        }

        void DropToGround(ResourceAmount item, Vector3 at)
        {
            // Aynı yere bırakılan aynı tür birikir.
            foreach (var existing in spots)
            {
                if (existing.Kind != FieldSpotKind.Loot || !existing.Searched || !existing.HasPending || existing.Pending.type != item.type) continue;
                if (Vector2.Distance(Flat(existing.Position), Flat(at)) > 1f) continue;
                existing.SetPending(new ResourceAmount(item.type, existing.Pending.amount + item.amount));
                return;
            }

            var visual = Make(PrimitiveType.Cube, "Bırakılan Eşya", item.type.ToColor(), root.transform);
            visual.position = new Vector3(at.x, groundY + 0.15f, Mathf.Clamp(at.z, minZ + 0.3f, maxZ - 0.3f));
            visual.localScale = new Vector3(0.45f, 0.3f, 0.45f);
            var spot = new FieldSpot(visual, FieldSpotKind.Loot, 0.5f, "Bırakılan eşya");
            spot.Reveal(item);
            spots.Add(spot);
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
            if (healCooldown > 0f || explorer.Health >= explorer.MaxHealth) return;
            // Yalnızca yanına alınan kitler kullanılabilir (sefer hazırlığında seçilir).
            if (kits <= 0)
            {
                Say("Yanında Yaşam Kiti yok. Bir dahaki sefere hazırlıkta kit al.");
                return;
            }
            kits--;

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
