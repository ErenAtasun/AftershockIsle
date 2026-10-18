using System.Collections.Generic;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    /// <summary>
    /// Dış saha manzarası (Blender'da tasarlanan OutdoorKit parçalarından): asfalt zemin, kaldırım, arka plandaki bina sıraları,
    /// sokak mobilyaları ve sağ uçtaki kapanış. Hiçbirinin collider'ı yoktur; oyun mantığı (yürüme, siper, görüş) kutu
    /// collider'larında kalır. Kit yoksa false döner ve saha eski kutu görünümüyle kurulur.
    ///
    /// Kamera varsayılan olarak 20° aşağıdan baktığı için arkadaki binaların yalnızca alt birkaç metresi görünür:
    /// bu yüzden zemin, kaldırım, bina tabanları ve ön plandaki nesneler en çok özen gören kısımlardır.
    /// </summary>
    public partial class FieldExpedition
    {
        const float RowOneFront = 3.6f;    // maxZ'den itibaren, yakın bina sırasının ön cephesi
        const float RowTwoFront = 16f;
        const float SkylineDepth = 32f;
        static readonly Color HazeMid = new Color(0.86f, 0.89f, 0.93f);
        static readonly Color HazeFar = new Color(0.7f, 0.76f, 0.84f);

        OutdoorKit kit;
        MaterialPropertyBlock hazeBlock;

        bool BuildScenery(float length, float midZ)
        {
            kit = OutdoorKit.Instance;
            if (kit == null || !kit.HasContent || !kit.Has(OutdoorCategory.Ground)) return false;

            var holder = new GameObject("Manzara").transform;
            holder.SetParent(root.transform, false);

            BuildGroundTiles(holder);
            BuildBackdropRows(holder);
            BuildFrontSidewalkProps(holder);
            BuildBackEdgeProps(holder);
            BuildRightEnd(holder, midZ);
            return true;
        }

        Transform PlaceModel(OutdoorKit.Entry entry, Transform parent, Vector3 position, float yaw, Color? tint = null)
        {
            var go = Instantiate(entry.prefab, parent);
            go.name = entry.name;
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            if (tint.HasValue)
            {
                hazeBlock ??= new MaterialPropertyBlock();
                hazeBlock.Clear();
                hazeBlock.SetColor(BaseColorId, tint.Value);
                foreach (var renderer in go.GetComponentsInChildren<Renderer>()) renderer.SetPropertyBlock(hazeBlock);
            }
            return go.transform;
        }

        // ---------------------------------------------------------------- Zemin

        void BuildGroundTiles(Transform holder)
        {
            float start = originX - 14f;
            float end = MaxX + 14f;

            for (float x = start; x < end; x += 6f)
            {
                if (kit.TryPick(OutdoorCategory.Ground, out var tile))
                    PlaceModel(tile, holder, new Vector3(x + 3f, groundY + 0.012f, minZ + 4.5f), Random.value < 0.5f ? 0f : 180f);
                if (kit.TryPick(OutdoorCategory.Curb, out var curb))
                    PlaceModel(curb, holder, new Vector3(x + 3f, groundY, minZ - 0.5f), 0f);
            }

            // Yürüme alanının gerisi: yıkıntı sahası (arka bina sıralarının zemini).
            for (int strip = 0; strip < 3; strip++)
            {
                float z = maxZ + 1.5f + 7f + 14f * strip;
                for (float x = start - 6f; x < end + 6f; x += 6f)
                    if (kit.TryPick(OutdoorCategory.GroundBack, out var back))
                        PlaceModel(back, holder, new Vector3(x + 3f, groundY - 0.01f, z), (strip + Mathf.FloorToInt(x)) % 2 == 0 ? 0f : 180f);
            }
        }

        // ---------------------------------------------------------------- Arka plan

        void BuildBackdropRows(Transform holder)
        {
            float left = originX - 16f;
            float right = MaxX + 16f;

            FillRow(holder, left, right, maxZ + RowOneFront, null, 0.2f, 1.6f, false);
            FillRow(holder, left - 4f, right + 4f, maxZ + RowTwoFront, HazeMid, 0.5f, 4f, true);

            for (float x = left - 12f; x < right + 12f; x += 34f)
                if (kit.TryPick(OutdoorCategory.Skyline, out var sky))
                    PlaceModel(sky, holder, new Vector3(x + 18f, groundY - 0.02f, maxZ + SkylineDepth), 0f, HazeFar);
        }

        void FillRow(Transform holder, float left, float right, float frontZ, Color? tint, float gapMin, float gapMax, bool preferTall)
        {
            float cursor = left;
            string last = null;
            while (cursor < right)
            {
                if (!kit.TryPick(OutdoorCategory.Building, out var building)) return;
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    bool repeat = building.name == last;
                    bool tooLow = preferTall && building.size.y < 12f;
                    if (!repeat && !tooLow) break;
                    kit.TryPick(OutdoorCategory.Building, out building);
                }

                float width = Mathf.Max(6f, building.size.x);
                var position = new Vector3(cursor + width * 0.5f, groundY - 0.02f, frontZ + building.facade);
                PlaceModel(building, holder, position, 0f, tint);
                cursor += width + Random.Range(gapMin, gapMax);
                last = building.name;
            }
        }

        // ---------------------------------------------------------------- Sokak mobilyaları

        // Ön kaldırım: kamera tarafında, ekranın altında görünür.
        void BuildFrontSidewalkProps(Transform holder)
        {
            float x = originX - 12f;
            float surface = groundY + 0.18f;
            while (x < MaxX + 12f)
            {
                x += Random.Range(5f, 10f);
                float roll = Random.value;
                OutdoorKit.Entry entry;
                bool found;
                if (roll < 0.3f) found = kit.TryPick(OutdoorCategory.Prop, out entry, "Prop_Lamp");
                else if (roll < 0.46f) found = kit.TryPick(OutdoorCategory.Prop, out entry, "Prop_Tree");
                else if (roll < 0.56f) found = kit.TryPick(OutdoorCategory.Prop, out entry, "Prop_Hydrant");
                else if (roll < 0.66f) found = kit.TryPick(OutdoorCategory.Prop, out entry, "Prop_Bench");
                else if (roll < 0.72f) found = kit.TryPick(OutdoorCategory.Prop, out entry, "Prop_BusShelter");
                else if (roll < 0.84f) found = kit.TryPick(OutdoorCategory.Prop, out entry, "Prop_Sign");
                else if (roll < 0.92f) found = kit.TryGet("Prop_TrashBags", out entry);
                else found = kit.TryPick(OutdoorCategory.Prop, out entry, "Prop_TrafficLight");
                if (!found) continue;

                float z = minZ - 0.5f - Random.Range(1.2f, 2.4f);
                var placed = PlaceModel(entry, holder, new Vector3(x, surface, z), Random.value < 0.5f ? 0f : 180f);
                // Ağaçlar ön plandaki kaldırımda ekranı kapatmasın.
                if (entry.name.StartsWith("Prop_Tree")) placed.localScale = Vector3.one * Random.Range(0.7f, 0.85f);
                x += entry.size.x * 0.5f;
            }
        }

        // Yürüme alanının hemen gerisi: çit, moloz, direk, konteyner, çöp. Bina sırasının önünü doldurur.
        void BuildBackEdgeProps(Transform holder)
        {
            float x = originX - 12f;
            while (x < MaxX + 12f)
            {
                x += Random.Range(2f, 6f);
                float roll = Random.value;
                float z = maxZ + Random.Range(1.0f, 2.0f);

                if (roll < 0.3f)
                {
                    int segments = Random.Range(2, 5);
                    for (int i = 0; i < segments; i++)
                        if (kit.TryPick(OutdoorCategory.Prop, out var fence, "Prop_Fence"))
                            PlaceModel(fence, holder, new Vector3(x + i * 3f, groundY, maxZ + 1.1f), 0f);
                    x += segments * 3f;
                }
                else if (roll < 0.52f)
                {
                    if (kit.TryPick(OutdoorCategory.Debris, out var heap, "Prop_Rubble"))
                    {
                        PlaceModel(heap, holder, new Vector3(x, groundY, z + 0.4f), Random.Range(0f, 360f));
                        x += heap.size.x * 0.6f;
                    }
                }
                else if (roll < 0.6f)
                {
                    if (kit.TryGet("Prop_PowerPole", out var pole)) PlaceModel(pole, holder, new Vector3(x, groundY, maxZ + 1.4f), 0f);
                }
                else if (roll < 0.72f)
                {
                    if (kit.TryPick(OutdoorCategory.Debris, out var debris)) PlaceModel(debris, holder, new Vector3(x, groundY, z), Random.Range(0f, 360f));
                }
                else if (roll < 0.8f)
                {
                    if (kit.TryPick(OutdoorCategory.Prop, out var container, "Prop_Container"))
                    {
                        PlaceModel(container, holder, new Vector3(x + 3f, groundY, maxZ + 2.1f), Random.value < 0.5f ? 0f : 180f);
                        x += 6f;
                    }
                }
                else if (roll < 0.88f)
                {
                    if (kit.TryPick(OutdoorCategory.Dumpster, out var bin)) PlaceModel(bin, holder, new Vector3(x, groundY, maxZ + 1.5f), Random.value < 0.5f ? 0f : 180f);
                }
                else if (roll < 0.95f)
                {
                    if (kit.TryPick(OutdoorCategory.Barrier, out var barrier)) PlaceModel(barrier, holder, new Vector3(x, groundY, z), Random.Range(-20f, 20f));
                }
                else if (kit.TryGet("Prop_Lamp_Broken", out var lamp))
                {
                    PlaceModel(lamp, holder, new Vector3(x, groundY, maxZ + 1.2f), 0f);
                }
            }
        }

        // Çıkış: yeşil kapı yok; iki bariyer, koniler ve yön tabelası geçidi belli eder ("ÇIKIŞ" yazısı ekranda).
        bool BuildExitScenery(float midZ)
        {
            if (!kit.TryGet("Barrier_Jersey", out var barrier)) return false;

            float x = originX - 0.35f;
            PlaceModel(barrier, root.transform, new Vector3(x, groundY, midZ - 2.1f), 90f);
            PlaceModel(barrier, root.transform, new Vector3(x, groundY, midZ + 2.1f), 90f);
            if (kit.TryGet("Cones", out var cones))
            {
                PlaceModel(cones, root.transform, new Vector3(originX + 0.7f, groundY, midZ - 1.5f), 90f);
                PlaceModel(cones, root.transform, new Vector3(originX + 0.7f, groundY, midZ + 1.5f), 90f);
            }
            if (kit.TryGet("Prop_Sign_Direction", out var sign))
                PlaceModel(sign, root.transform, new Vector3(originX + 1.2f, groundY, midZ + 2.8f), 0f);
            return true;
        }

        // Sağ uç: görünmez sınır duvarının önüne konteynerler yığılır.
        void BuildRightEnd(Transform holder, float midZ)
        {
            if (!kit.TryGet("Prop_Container", out var blue)) return;
            kit.TryGet("Prop_Container_Rust", out var rust);
            if (rust.prefab == null) rust = blue;

            float x = MaxX + 1.9f;
            PlaceModel(blue, holder, new Vector3(x, groundY, midZ - 3.1f), 90f);
            PlaceModel(rust, holder, new Vector3(x + 0.1f, groundY, midZ + 3.1f), 90f);
            if (Random.value < 0.6f)
                PlaceModel(rust, holder, new Vector3(x + 0.05f, groundY + 2.6f, midZ + Random.Range(-2.5f, 2.5f)), 90f);
        }

        // ---------------------------------------------------------------- Siper nesneleri (modelli)

        /// <summary>
        /// Gizlenen kutunun (collider kalır) yerine model yerleştirir. size: dünya eksenlerinde hedef boyut; yaw 90/270 ise modelin
        /// eksenleri yer değiştirir. uniform: orantılı ölçek (yalnızca zemindeki en küçük oran), yükseklik doğal kalır.
        /// </summary>
        Transform PlaceFitted(OutdoorKit.Entry entry, Vector3 bottomCenter, Vector3 target, float yaw, bool uniform = false, bool keepHeight = true)
        {
            var model = PlaceModel(entry, root.transform, bottomCenter, yaw);
            bool swapped = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) > 0.7f;
            float natX = swapped ? entry.size.z : entry.size.x;
            float natZ = swapped ? entry.size.x : entry.size.z;
            float sx = target.x / Mathf.Max(0.01f, natX);
            float sz = target.z / Mathf.Max(0.01f, natZ);
            float sy = keepHeight ? 1f : target.y / Mathf.Max(0.01f, entry.size.y);

            if (uniform)
            {
                float s = Mathf.Min(sx, sz);
                sx = sz = s;
                sy = keepHeight ? s : sy;
            }

            // Ölçek yaw'dan önce uygulandığı için döndürülmüş modelde yerel eksenler yer değiştirir.
            model.localScale = swapped ? new Vector3(sz, sy, sx) : new Vector3(sx, sy, sz);
            return model;
        }

        static void Hide(Transform box)
        {
            var renderer = box != null ? box.GetComponent<Renderer>() : null;
            if (renderer != null) renderer.enabled = false;
        }
    }
}
