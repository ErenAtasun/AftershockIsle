using System.Collections.Generic;
using AftershockIsle.Characters;
using UnityEngine;

namespace AftershockIsle.Building
{
    /// <summary>
    /// Etkileşimsiz, kat içinde içinden geçilmeyen kutu: bina modelinin bölme duvarı, döşeme deliği ya da kapalı kapı aralığı.
    /// Fizik collider'ı değildir (tıklamayı ve diğer fizik sorgularını etkilemez); FloorObstacles engel listesine ekler.
    /// Katı kutunun alt yüzünden bulunur, bu yüzden kutu zeminden başlamalıdır.
    /// </summary>
    public class WalkBlocker : MonoBehaviour
    {
        [Tooltip("İşaretliyse kutu her açılışta bu objenin (ve çocuklarının) görünen modeline oturur: sahneye konan mobilyaya " +
                 "eklemek yeterli. Kapalıysa aşağıdaki merkez/boyut kullanılır.")]
        [SerializeField] bool fitToModel;
        [SerializeField] Vector3 center;
        [SerializeField] Vector3 size = Vector3.one;
        [Tooltip("İş ve fare emriyle yürüyenler yol çıkarırken bunu yok sayar (kapı: geçerken kendiliğinden açılır). " +
                 "Klavye/gamepad ile yürüyen yine takılır.")]
        [SerializeField] bool passableForRoutes;

        static readonly List<WalkBlocker> all = new List<WalkBlocker>();

        public static IReadOnlyList<WalkBlocker> All => all;
        public bool PassableForRoutes => passableForRoutes;

        public Bounds WorldBounds
        {
            get
            {
                // Dönük ebeveynde (bina modeli 180° döner) eksen hizalı kutu: kenar uzunlukları mutlak değer.
                var right = transform.TransformVector(new Vector3(size.x * 0.5f, 0f, 0f));
                var up = transform.TransformVector(new Vector3(0f, size.y * 0.5f, 0f));
                var forward = transform.TransformVector(new Vector3(0f, 0f, size.z * 0.5f));
                var extents = new Vector3(
                    Mathf.Abs(right.x) + Mathf.Abs(up.x) + Mathf.Abs(forward.x),
                    Mathf.Abs(right.y) + Mathf.Abs(up.y) + Mathf.Abs(forward.y),
                    Mathf.Abs(right.z) + Mathf.Abs(up.z) + Mathf.Abs(forward.z));
                return new Bounds(transform.TransformPoint(center), extents * 2f);
            }
        }

        public void Configure(Vector3 localCenter, Vector3 localSize, bool passable)
        {
            fitToModel = false;
            center = localCenter;
            size = localSize;
            passableForRoutes = passable;
            FloorObstacles.Invalidate();
        }

        // Inspector'dan eklenince (Add Component) modele otursun.
        void Reset() => fitToModel = true;

        void FitToRenderers()
        {
            bool found = false;
            var bounds = new Bounds();
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (found) bounds.Encapsulate(r.bounds); else { bounds = r.bounds; found = true; }
            }
            if (!found) return;

            center = transform.InverseTransformPoint(bounds.center);
            var x = transform.InverseTransformVector(new Vector3(bounds.size.x, 0f, 0f));
            var y = transform.InverseTransformVector(new Vector3(0f, bounds.size.y, 0f));
            var z = transform.InverseTransformVector(new Vector3(0f, 0f, bounds.size.z));
            size = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                               Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                               Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        }

        void OnEnable()
        {
            if (fitToModel) FitToRenderers();
            all.Add(this);
            FloorObstacles.Invalidate();
        }

        void OnDisable()
        {
            all.Remove(this);
            FloorObstacles.Invalidate();
        }

        void OnDrawGizmosSelected()
        {
            var bounds = WorldBounds;
            Gizmos.color = passableForRoutes ? new Color(1f, 0.8f, 0.2f, 0.6f) : new Color(1f, 0.25f, 0.2f, 0.6f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
