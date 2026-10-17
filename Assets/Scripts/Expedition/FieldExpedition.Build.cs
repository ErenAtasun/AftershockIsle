using System.Collections.Generic;
using AftershockIsle.Characters;
using AftershockIsle.Skills;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    /// <summary>Saha kurulumu: zemin, sınırlar, bina, aranacak noktalar, siperlik engeller, düşmanlar, kâşif ve yoldaş.</summary>
    public partial class FieldExpedition
    {
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

            var wall = Make(PrimitiveType.Cube, "Sınır", new Color(0.18f, 0.18f, 0.2f), root.transform, true);
            wall.position = new Vector3(MaxX + 1f, groundY + 1.5f, midZ);
            wall.localScale = new Vector3(0.6f, 3f, FieldDepth + 2f);

            BuildBounds(length, midZ);

            exitPoint = new Vector3(originX + 0.6f, groundY, midZ);
            BuildExit(midZ);
            BuildBuilding(length);
            BuildSpots(length);
            BuildObstacles(length);
            Physics.SyncTransforms();
            BuildCoverPoints();
            BuildNavGrid();
            BuildEnemies(length);
            BuildAvatar(midZ);
            BuildCompanion();
        }

        // Görünmez sınırlar: ön kaldırım, arka harabeler ve çıkışın gerisi.
        void BuildBounds(float length, float midZ)
        {
            Bound(new Vector3(originX + length * 0.5f, groundY + 1f, minZ - AvatarRadius - 0.25f), new Vector3(length + 4f, 2f, 0.5f));
            Bound(new Vector3(originX + length * 0.5f, groundY + 1f, maxZ + AvatarRadius + 0.25f), new Vector3(length + 4f, 2f, 0.5f));
            Bound(new Vector3(originX - 0.8f, groundY + 1f, midZ), new Vector3(0.5f, 2f, FieldDepth + 2f));
        }

        void Bound(Vector3 center, Vector3 size)
        {
            var bound = new GameObject("Sınır (görünmez)").AddComponent<BoxCollider>();
            bound.transform.SetParent(root.transform, false);
            bound.transform.position = center;
            bound.size = size;
        }

        // Sahanın ortasında, arka kenara dayalı girilebilir yapı. Önünde kapıya giden yol boş bırakılır.
        void BuildBuilding(float length)
        {
            float width = FieldBuilding.WidthFor(site.layout);
            float depth = Mathf.Min(BuildingDepth, FieldDepth - 2.5f);
            var min = new Vector2(originX + length * 0.42f - width * 0.5f, maxZ - depth + AvatarRadius);
            var building = new FieldBuilding(
                (type, name, color, solid) => Make(type, name, color, root.transform, solid),
                Dress, min, new Vector2(width, depth - AvatarRadius), groundY, site.layout);
            buildings.Add(building);

            var footprint = building.Footprint;
            AddPlacement(footprint.center, footprint.size * 0.5f);
            AddPlacement(building.FrontDoor, new Vector2(1.2f, 1.4f));
        }

        /// <summary>Kutuya model giydirir (kütüphane yoksa ya da model eksikse kutu kalır).</summary>
        Renderer[] Dress(Transform box, ArtKey key, float yaw) => art != null ? art.Dress(box, key, yaw) : null;

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
            var building = buildings.Count > 0 ? buildings[0] : null;
            var slots = building != null ? building.SpotSlots : new List<FieldBuilding.SpotSlot>();

            // Değerli olanın yarısı binanın içinde: içerisi dar ama ganimet orada.
            int inside = Mathf.Min(slots.Count - (site.isRescueSite ? 1 : 0), Mathf.Max(1, count / 2));
            for (int i = 0; i < inside; i++) AddLootSpot(slots[i].Position, slots[i].Size, slots[i].Art, slots[i].Yaw);

            int outside = count - inside;
            float start = originX + 5f;
            float span = length - 7f;
            var crateSize = new Vector3(0.9f, 0.8f, 0.9f);
            for (int i = 0; i < outside; i++)
            {
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    float x = start + span * (outside == 1 ? 0.5f : i / (float)(outside - 1)) + Random.Range(-2f, 2f);
                    var center = new Vector2(x, Random.Range(minZ + 0.9f, maxZ - 0.9f));
                    if (!IsFree(center, new Vector2(0.55f, 0.45f), 0.8f)) continue;

                    AddLootSpot(new Vector3(center.x, groundY, center.y), crateSize, ArtKey.Crate, Random.Range(0f, 360f));
                    break;
                }
            }

            if (!site.isRescueSite) return;

            // Mahsur kalan binanın içinde (yoksa sahanın sonunda).
            var rescueAt = building != null && slots.Count > 0
                ? slots[slots.Count - 1].Position
                : new Vector3(MaxX - 3f, groundY, Random.Range(minZ + 1.5f, maxZ - 1.5f));
            var victim = Make(PrimitiveType.Capsule, "Mahsur Kalan", new Color(0.4f, 0.85f, 0.95f), root.transform);
            victim.position = rescueAt + Vector3.up * 0.35f;
            victim.localScale = new Vector3(0.7f, 0.5f, 0.7f);
            victim.rotation = Quaternion.Euler(0f, 0f, 90f);
            spots.Add(new FieldSpot(victim, FieldSpotKind.Rescue, extractionDuration > 0f ? extractionDuration : RescueDuration, "Mahsur kalan"));
        }

        void AddLootSpot(Vector3 groundPoint, Vector3 size, ArtKey artKey, float yaw)
        {
            var visual = Make(PrimitiveType.Cube, "Aranacak Nokta", new Color(0.55f, 0.38f, 0.2f), root.transform, true);
            visual.position = groundPoint + Vector3.up * (size.y * 0.5f);
            visual.localScale = size;

            var spot = new FieldSpot(visual, FieldSpotKind.Loot, SearchDuration, "Aranacak yer");
            // Dışarıdaki kasalar rastgele dönük; kutu eksenle hizalı kalır, model içinde döner.
            spot.UseModel(Dress(visual, artKey, artKey == ArtKey.Crate ? Mathf.Round(yaw / 90f) * 90f : yaw));
            spots.Add(spot);
            AddPlacement(new Vector2(groundPoint.x, groundPoint.z), new Vector2(size.x, size.z) * 0.5f);
        }

        // Siperlik nesneler: yolu keser, mermiyi ve görüşü engeller; çevrelerinde siper noktaları oluşur.
        void BuildObstacles(float length)
        {
            int target = Mathf.RoundToInt(length / 5f);
            int placed = 0;

            for (int attempt = 0; attempt < target * 12 && placed < target; attempt++)
            {
                int kind = Random.Range(0, 4);
                bool turned = Random.value < 0.35f;
                Vector2 half = kind switch
                {
                    0 => new Vector2(1.8f, 0.85f),   // araba
                    1 => new Vector2(1.1f, 0.35f),   // beton blok
                    2 => new Vector2(0.6f, 0.6f),    // kasa yığını
                    _ => new Vector2(0.8f, 0.65f),   // moloz yığını
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
            foreach (var other in placements)
            {
                if (Mathf.Abs(center.x - other.Center.x) < half.x + other.Half.x + gap &&
                    Mathf.Abs(center.y - other.Center.y) < half.y + other.Half.y + gap) return false;
            }
            return true;
        }

        void BuildObstacleVisual(int kind, Vector2 center, Vector2 half, bool turned)
        {
            switch (kind)
            {
                case 0:
                {
                    var body = Make(PrimitiveType.Cube, "Araba", new Color(0.3f, 0.36f, 0.45f), root.transform, true);
                    body.position = new Vector3(center.x, groundY + 0.45f, center.y);
                    body.localScale = new Vector3(half.x * 2f, 0.7f, half.y * 2f);
                    var cabin = Make(PrimitiveType.Cube, "Kabin", new Color(0.25f, 0.3f, 0.38f), root.transform, true);
                    cabin.position = new Vector3(center.x, groundY + 1.05f, center.y);
                    cabin.localScale = turned
                        ? new Vector3(half.x * 1.8f, 0.55f, half.y * 1.1f)
                        : new Vector3(half.x * 1.1f, 0.55f, half.y * 1.8f);
                    break;
                }
                case 1:
                {
                    const float height = 1.0f;
                    var slab = Make(PrimitiveType.Cube, "Beton Blok", new Color(0.55f, 0.55f, 0.52f), root.transform, true);
                    slab.position = new Vector3(center.x, groundY + height * 0.5f, center.y);
                    slab.localScale = new Vector3(half.x * 2f, height, half.y * 2f);
                    break;
                }
                case 2:
                {
                    for (int level = 0; level < 2; level++)
                    {
                        var crate = Make(PrimitiveType.Cube, "Kasa", new Color(0.42f, 0.3f, 0.18f), root.transform, true);
                        crate.position = new Vector3(center.x + (level == 1 ? 0.08f : 0f), groundY + 0.42f + level * 0.85f, center.y);
                        crate.localScale = new Vector3(half.x * 2f - level * 0.15f, 0.84f, half.y * 2f - level * 0.15f);
                        Dress(crate, ArtKey.Crate, level * 90f);
                    }
                    break;
                }
                default:
                {
                    // Alçak siper: çömelince korur.
                    var rubble = Make(PrimitiveType.Cube, "Moloz Yığını", new Color(0.45f, 0.42f, 0.38f), root.transform, true);
                    rubble.position = new Vector3(center.x, groundY + 0.5f, center.y);
                    rubble.localScale = new Vector3(half.x * 2f, 1f, half.y * 2f);
                    Dress(rubble, ArtKey.Rubble, turned ? 90f : 0f);
                    break;
                }
            }

            AddPlacement(center, half);
        }

        void AddPlacement(Vector2 center, Vector2 half) =>
            placements.Add(new Placement { Center = center, Half = half });

        void BuildEnemies(float length)
        {
            var kinds = new List<FieldEnemyKind>();
            for (int i = 0; i < site.dogs; i++) kinds.Add(FieldEnemyKind.Dog);
            for (int i = 0; i < site.raiders + extraRaiders; i++) kinds.Add(FieldEnemyKind.Raider);
            for (int i = 0; i < site.brutes; i++) kinds.Add(FieldEnemyKind.Brute);
            for (int i = 0; i < site.throwers; i++) kinds.Add(FieldEnemyKind.Gunner);
            for (int i = kinds.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
            }

            var building = buildings.Count > 0 ? buildings[0] : null;
            for (int i = 0; i < kinds.Count; i++)
            {
                // Biri binanın içinde bekler (ganimetin yanında); diğerleri sokağa dağılır, çıkışa yakın yer güvenli.
                Vector3 position;
                if (i == 0 && building != null && kinds.Count >= 2)
                {
                    var interior = building.Interior;
                    position = new Vector3(interior.center.x - 1f, groundY, interior.center.y + 0.8f);
                }
                else if (!TryFindFree(originX + 9f, MaxX - 1.5f, 0.6f, out position))
                {
                    continue;
                }

                SpawnEnemy(kinds[i], position);
            }
        }

        bool TryFindFree(float fromX, float toX, float radius, out Vector3 position)
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                var center = new Vector2(Random.Range(fromX, toX), Random.Range(minZ + 0.8f, maxZ - 0.8f));
                bool insideBuilding = false;
                foreach (var building in buildings)
                    if (building.Footprint.Contains(center)) insideBuilding = true;
                if (insideBuilding || !IsFree(center, Vector2.one * radius, 0.4f)) continue;

                AddPlacement(center, Vector2.one * radius);
                position = new Vector3(center.x, groundY, center.y);
                return true;
            }
            position = default;
            return false;
        }

        void SpawnEnemy(FieldEnemyKind kind, Vector3 position)
        {
            var enemyRoot = new GameObject(kind.ToString()).transform;
            enemyRoot.SetParent(root.transform, false);
            enemyRoot.SetPositionAndRotation(position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

            bool dog = kind == FieldEnemyKind.Dog;
            bool brute = kind == FieldEnemyKind.Brute;
            float height = dog ? 0.8f : brute ? 2.2f : 1.8f;
            float radius = brute ? 0.5f : 0.35f;

            var enemyController = enemyRoot.gameObject.AddComponent<CharacterController>();
            enemyController.radius = radius;
            enemyController.height = height;
            enemyController.center = new Vector3(0f, height * 0.5f + 0.02f, 0f);
            enemyController.stepOffset = 0.1f;
            enemyController.skinWidth = 0.03f;

            var body = Make(dog ? PrimitiveType.Cube : PrimitiveType.Capsule, "Gövde", FieldEnemy.ColorFor(kind), enemyRoot);
            body.localRotation = Quaternion.identity;
            body.localScale = dog ? new Vector3(0.45f, 0.55f, 0.95f) : new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            body.localPosition = new Vector3(0f, dog ? 0.35f : height * 0.5f, 0f);

            var face = Make(PrimitiveType.Cube, "Yüz", new Color(0.15f, 0.12f, 0.1f), body);
            face.localPosition = new Vector3(0f, dog ? 0.2f : 0.3f, 0.5f);
            face.localRotation = Quaternion.identity;
            face.localScale = new Vector3(0.5f, 0.15f, 0.2f);

            if (!dog)
            {
                var enemyView = DressCharacter(body, enemyRoot, height);
                if (enemyView != null)
                {
                    var enemyGun = kind switch
                    {
                        FieldEnemyKind.Gunner => GunModelKind.AssaultRifle,
                        FieldEnemyKind.Brute => GunModelKind.Shotgun,
                        _ => GunModelKind.Pistol,
                    };
                    enemyView.SetGun(enemyGun, 1);
                }
            }
            var enemy = new FieldEnemy(this, kind, enemyRoot, body.GetComponent<Renderer>(), enemyController);
            enemies.Add(enemy);
            enemyByCollider[enemyController] = enemy;
        }

        void BuildAvatar(float midZ)
        {
            avatarRoot = new GameObject(explorer.DisplayName).transform;
            avatarRoot.SetParent(root.transform, false);
            avatarRoot.SetPositionAndRotation(new Vector3(originX + 2f, groundY, midZ), Quaternion.LookRotation(Vector3.right));

            controller = avatarRoot.gameObject.AddComponent<CharacterController>();
            controller.radius = AvatarRadius;
            controller.height = AvatarHeight;
            controller.center = new Vector3(0f, AvatarHeight * 0.5f + 0.02f, 0f);
            controller.stepOffset = 0.1f;
            controller.skinWidth = 0.03f;

            var body = Make(PrimitiveType.Capsule, "Gövde", BranchColor(explorer), avatarRoot);
            body.localPosition = new Vector3(0f, 0.95f, 0f);
            body.localRotation = Quaternion.identity;
            body.localScale = new Vector3(0.7f, 0.95f, 0.7f);
            avatar = body;
            avatarRenderer = body.GetComponent<Renderer>();

            var nose = Make(PrimitiveType.Cube, "Yön", Color.white, body);
            nose.localPosition = new Vector3(0f, 0.3f, 0.55f);
            nose.localRotation = Quaternion.identity;
            nose.localScale = new Vector3(0.3f, 0.2f, 0.5f);
            DressCharacter(body, avatarRoot, 1.8f);
            RefreshAvatarWeapon();
        }

        void RefreshAvatarWeapon()
        {
            var view = ViewFor(avatarRenderer);
            if (view == null) return;

            var (gunKind, variant) = weapon switch
            {
                WeaponKind.Pistol => (GunModelKind.Pistol, 0),
                WeaponKind.Smg => (GunModelKind.Smg, 0),
                WeaponKind.Shotgun => (GunModelKind.Shotgun, 0),
                WeaponKind.AssaultRifle => (GunModelKind.AssaultRifle, 0),
                WeaponKind.SniperRifle => (GunModelKind.SniperRifle, 0),
                _ => (GunModelKind.Pistol, 1),
            };
            view.SetGun(gunKind, variant);
        }

        public CharacterView ViewFor(Renderer target) =>
            target != null && views.TryGetValue(target, out var view) ? view : null;

        /// <summary>
        /// Kapsül gövdenin yerine karakter modeli koyar (kütüphane yoksa kapsül kalır). Kapsül ve üstündeki yön/yüz
        /// parçaları gizlenir ama yerinde durur: duruş (çömelme), eğilme ve renk değişimi kapsül üzerinden modele geçer.
        /// </summary>
        CharacterView DressCharacter(Transform capsule, Transform root, float height)
        {
            if (Characters == null) return null;

            var renderer = capsule.GetComponent<Renderer>();
            var view = CharacterView.Attach(Characters, capsule, root, height, root.position);
            if (view == null) return null;

            foreach (var part in capsule.GetComponentsInChildren<Renderer>())
                if (!view.Owns(part)) part.enabled = false;
            views[renderer] = view;
            return view;
        }

        // Yoldaş kâşifin hemen arkasında doğar.
        void BuildCompanion()
        {
            if (partner == null || partner.IsDead) return;

            var color = BranchColor(partner);
            var companionRoot = new GameObject("Yoldaş").transform;
            companionRoot.SetParent(root.transform, false);
            companionRoot.SetPositionAndRotation(AvatarFeet + new Vector3(-1.2f, 0f, 0.8f), Quaternion.Euler(0f, 90f, 0f));

            var companionController = companionRoot.gameObject.AddComponent<CharacterController>();
            companionController.radius = AvatarRadius;
            companionController.height = 1.8f;
            companionController.center = new Vector3(0f, 0.92f, 0f);
            companionController.stepOffset = 0.1f;
            companionController.skinWidth = 0.03f;

            var body = Make(PrimitiveType.Capsule, "Gövde", color, companionRoot);
            body.localPosition = new Vector3(0f, 0.95f, 0f);
            body.localRotation = Quaternion.identity;
            body.localScale = new Vector3(0.7f, 0.95f, 0.7f);

            var nose = Make(PrimitiveType.Cube, "Yön", Color.white, body);
            nose.localPosition = new Vector3(0f, 0.3f, 0.55f);
            nose.localRotation = Quaternion.identity;
            nose.localScale = new Vector3(0.3f, 0.2f, 0.5f);

            var companionView = DressCharacter(body, companionRoot, 1.8f);
            companionView?.SetGun(GunModelKind.Smg, 1);
            companion = new FieldCompanion(this, partner, companionRoot, body.GetComponent<Renderer>(), companionController, color, groundY);
        }

        static Color BranchColor(Survivor survivor) => survivor.Branch switch
        {
            SkillBranch.Construction => new Color(0.95f, 0.55f, 0.2f),
            SkillBranch.Exploration => new Color(0.35f, 0.8f, 0.35f),
            _ => new Color(0.3f, 0.6f, 0.95f),
        };

        /// <summary>Renkli ilkel nesne. solid: collider'ı kalır (kâşif çarpar, mermi ve görüş hattını keser).</summary>
        Transform Make(PrimitiveType type, string objectName, Color color, Transform parent, bool solid = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = objectName;
            var collider = go.GetComponent<Collider>();
            if (collider != null && !solid)
            {
                // Destroy kare sonunda çalışır; aynı karede yapılan fizik sorgularına (siper, yol ızgarası) karışmasın.
                collider.enabled = false;
                Destroy(collider);
            }
            go.transform.SetParent(parent, true);
            Tint(go.GetComponent<Renderer>(), color);
            return go.transform;
        }

        public void Tint(Renderer target, Color color)
        {
            if (views.TryGetValue(target, out var view) && view != null)
            {
                view.SetTint(color);
                return;
            }

            block ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            target.SetPropertyBlock(block);
        }
    }
}
