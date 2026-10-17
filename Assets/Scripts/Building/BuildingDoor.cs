using AftershockIsle.Characters;
using AftershockIsle.Interaction;
using UnityEngine;

namespace AftershockIsle.Building
{
    /// <summary>
    /// Blender binasındaki kapı kanadı. Pivotu menteşede; karakter yanındayken F (ya da sağ tık) ile açılır/kapanır.
    /// Building.fbx import edilirken BuildingDoorImporter tarafından eklenir; açılma açısı Blender'daki
    /// "kapi_acik_aci" özelliğinden gelir. Başlangıçta kapalıdır.
    /// </summary>
    public class BuildingDoor : Interactable
    {
        [Tooltip("Menteşe ekseni etrafında açık konum açısı. Kapı yanlış yöne açılıyorsa işaretini değiştir.")]
        [SerializeField] float openAngle = 90f;
        [Tooltip("Menteşe ekseni (Blender'ın yukarı ekseni, FBX'ten sonra objenin yerel Z'si)")]
        [SerializeField] Vector3 hingeAxis = Vector3.forward;
        [Tooltip("Açılma/kapanma süresi (saniye)")]
        [SerializeField] float swingDuration = 0.45f;
        [SerializeField] bool startOpen;

        Quaternion closedRotation;
        float openAmount;
        bool open;

        public bool IsOpen => open;

        public override string ActionLabel => open ? "Kapıyı Kapat" : "Kapıyı Aç";
        public override WorkCategory Category => WorkCategory.Operate;
        public override float WorkDuration => 0.2f;
        public override bool AllowedInSmoke => true;
        // Kapı yürüyüş engeli değildir; açıkken de kapalıyken de karakterler geçer.
        public override bool BlocksMovement => false;

        public void Configure(float angle) => openAngle = angle;

        protected override void Awake()
        {
            base.Awake();
            ConfigureRuntime(name.Contains("Ext") ? "Dış Kapı" : "Kapı", WorkDuration);
            closedRotation = transform.localRotation;
            EnsureCollider();
            open = startOpen;
            openAmount = open ? 1f : 0f;
            ApplyRotation();
        }

        /// <summary>F tuşunun kapıyı bulabilmesi için kanat boyutunda bir tıklama alanı.</summary>
        void EnsureCollider()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            var filter = GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                var bounds = filter.sharedMesh.bounds;
                box.center = bounds.center;
                box.size = bounds.size;
            }
            box.enabled = true;
        }

        public override void Complete(Survivor worker) => SetOpen(!open);

        public void SetOpen(bool value) => open = value;

        void Update()
        {
            float target = open ? 1f : 0f;
            if (Mathf.Approximately(openAmount, target)) return;

            openAmount = Mathf.MoveTowards(openAmount, target, Time.deltaTime / Mathf.Max(0.01f, swingDuration));
            ApplyRotation();
        }

        void ApplyRotation()
        {
            float angle = Mathf.SmoothStep(0f, openAngle, openAmount);
            transform.localRotation = closedRotation * Quaternion.AngleAxis(angle, hingeAxis);
        }
    }
}
