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
            center = localCenter;
            size = localSize;
            passableForRoutes = passable;
            FloorObstacles.Invalidate();
        }

        void OnEnable()
        {
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
