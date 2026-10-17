using AftershockIsle.Characters;
using UnityEngine;

namespace AftershockIsle.Building
{
    /// <summary>
    /// Blender binasındaki kapı kanadı. Pivotu menteşede; yanına kazazede gelince açılır, uzaklaşınca kapanır.
    /// Building.fbx import edilirken BuildingDoorImporter tarafından eklenir ve açılma açısı Blender'daki
    /// "kapi_acik_aci" özelliğinden gelir.
    /// </summary>
    public class BuildingDoor : MonoBehaviour
    {
        [Tooltip("Menteşe ekseni etrafında açık konum açısı. Kapı yanlış yöne açılıyorsa işaretini değiştir.")]
        [SerializeField] float openAngle = 90f;
        [Tooltip("Menteşe ekseni (Blender'ın yukarı ekseni, FBX'ten sonra objenin yerel Z'si)")]
        [SerializeField] Vector3 hingeAxis = Vector3.forward;
        [SerializeField] float openRadius = 1.5f;
        [SerializeField] float sameFloorHeight = 2f;
        [Tooltip("Kapanıp açılma süresi (saniye)")]
        [SerializeField] float swingDuration = 0.45f;

        Quaternion closedRotation;
        float openAmount;

        /// <summary>true iken kimse yokken de açık durur (ör. sefer çıkışı, sahne olayları).</summary>
        public bool HoldOpen { get; set; }
        public bool IsOpen => openAmount > 0.99f;

        public void Configure(float angle) => openAngle = angle;

        void Awake() => closedRotation = transform.localRotation;

        void Update()
        {
            float target = HoldOpen || SurvivorNearby() ? 1f : 0f;
            if (Mathf.Approximately(openAmount, target)) return;

            openAmount = Mathf.MoveTowards(openAmount, target, Time.deltaTime / Mathf.Max(0.01f, swingDuration));
            float angle = Mathf.SmoothStep(0f, openAngle, openAmount);
            transform.localRotation = closedRotation * Quaternion.AngleAxis(angle, hingeAxis);
        }

        bool SurvivorNearby()
        {
            var hinge = transform.position;
            foreach (var survivor in Survivor.All)
            {
                if (survivor == null || survivor.IsDead) continue;
                var offset = survivor.transform.position - hinge;
                if (Mathf.Abs(offset.y) > sameFloorHeight) continue;
                offset.y = 0f;
                if (offset.sqrMagnitude <= openRadius * openRadius) return true;
            }
            return false;
        }
    }
}
