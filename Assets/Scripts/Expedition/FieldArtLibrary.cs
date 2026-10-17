using System;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    public enum ArtKey { Crate, Dresser, Table, Bed, Chair, Rubble, Bucket, Extinguisher }

    /// <summary>
    /// Dış sahanın çalışma anında kullandığı model listesi (FBX prefabı + malzeme). Sahne kurucu
    /// (Aftershock → Greybox Sahnesini Oluştur) Assets/Models'teki modellerden doldurur. Eksik model kutu olarak kalır.
    /// </summary>
    [CreateAssetMenu(menuName = "Aftershock/Field Art Library", fileName = "FieldArtLibrary")]
    public class FieldArtLibrary : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public ArtKey key;
            public GameObject prefab;
            public Material material;
        }

        public Entry[] entries = Array.Empty<Entry>();

        public bool TryGet(ArtKey key, out Entry entry)
        {
            foreach (var candidate in entries)
            {
                if (candidate.key != key || candidate.prefab == null) continue;
                entry = candidate;
                return true;
            }
            entry = default;
            return false;
        }

        /// <summary>
        /// Kutuya model giydirir: model kutunun boyutlarına gerilir, tabanı kutunun tabanına oturur; kutunun görseli kapanır,
        /// collider'ı kalır (çarpışma, atış ve görüş hattı değişmez). yaw: modelin kutu içindeki dönüşü (derece).
        /// Model yoksa kutu olduğu gibi kalır ve null döner. Dönen dizi modelin renderer'larıdır (renklendirme için).
        /// </summary>
        public Renderer[] Dress(Transform box, ArtKey key, float yaw = 0f)
        {
            if (box == null || !TryGet(key, out var entry)) return null;

            var model = Instantiate(entry.prefab, box.parent, false);
            model.name = $"{box.name} ({key})";
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
            model.transform.localScale = Vector3.one;

            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Destroy(model);
                return null;
            }

            foreach (var renderer in renderers)
            {
                if (entry.material == null) continue;
                var slots = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < slots.Length; i++) slots[i] = entry.material;
                renderer.sharedMaterials = slots;
            }
            foreach (var collider in model.GetComponentsInChildren<Collider>()) Destroy(collider);

            // Yaw sonrası dünya ekseninde ölç; kutunun dünya boyutuna ger. Kutular döndürülmez, eksenleri dünyayla aynı.
            var natural = Bounds(renderers);
            var target = box.lossyScale;
            model.transform.localScale = new Vector3(
                target.x / Mathf.Max(0.001f, natural.size.x),
                target.y / Mathf.Max(0.001f, natural.size.y),
                target.z / Mathf.Max(0.001f, natural.size.z));

            // Ölçek yaw'dan önce uygulandığı için dönmüş modellerde eksenler yer değiştirir; düzelt.
            if (Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) > 0.7f)
            {
                var scale = model.transform.localScale;
                model.transform.localScale = new Vector3(scale.z, scale.y, scale.x);
            }

            var fitted = Bounds(renderers);
            var bottom = box.position - Vector3.up * (target.y * 0.5f);
            model.transform.position += bottom - new Vector3(fitted.center.x, fitted.min.y, fitted.center.z);

            var boxRenderer = box.GetComponent<Renderer>();
            if (boxRenderer != null) boxRenderer.enabled = false;
            return renderers;
        }

        static Bounds Bounds(Renderer[] renderers)
        {
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
