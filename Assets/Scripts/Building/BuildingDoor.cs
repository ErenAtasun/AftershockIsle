using System.Collections.Generic;
using AftershockIsle.Characters;
using AftershockIsle.Interaction;
using UnityEngine;

namespace AftershockIsle.Building
{
    /// <summary>
    /// Blender binasındaki kapı kanadı. Pivotu menteşede; bir kazazede kapı aralığına yaklaşınca kendiliğinden açılır,
    /// kimse kalmayınca kısa süre sonra kapanır. F ya da tıklamayla etkileşimi yoktur (autoOpen kapatılırsa eski usul F ile).
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
        [Tooltip("Kazazede yaklaşınca kendiliğinden açılır, uzaklaşınca kapanır; F ile açma kapanır")]
        [SerializeField] bool autoOpen = true;
        [Tooltip("Kapı aralığına bu kadar (metre) yaklaşan kazazede kapıyı açar")]
        [SerializeField, Range(0.3f, 3f)] float openReach = 1.1f;
        [Tooltip("Aralıkta kimse kalmadıktan kaç saniye sonra kapansın (başta açık olan kapı hep açık kalır)")]
        [SerializeField, Range(0.2f, 5f)] float closeDelay = 1.2f;
        float emptyTime;

        Quaternion closedRotation;
        float openAmount;
        bool open;
        WalkBlocker doorway;

        static readonly List<BuildingDoor> all = new List<BuildingDoor>();

        public bool IsOpen => open;

        public override string ActionLabel => open ? "Kapıyı Kapat" : "Kapıyı Aç";
        public override WorkCategory Category => WorkCategory.Operate;
        public override float WorkDuration => 0.2f;
        public override bool AllowedInSmoke => true;
        // Kanadın kendisi engel değildir (açılırken savrulur); kapalıyken kapı aralığını WalkBlocker kapatır.
        public override bool BlocksMovement => false;
        // Kendiliğinden açılan kapı seçilmez: F istemi, bilgi kartı ve kontur çıkmaz.
        public override bool IsInteractive => !autoOpen;

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        /// <summary>
        /// İş/fare emriyle yürüyen karakter kapalı bir kapının aralığına yaklaşınca kapı kendiliğinden açılır.
        /// Klavye/gamepad ile yürüyen oyuncu ise F ile açar.
        /// </summary>
        public static void OpenNear(Vector3 position, float reach)
        {
            foreach (var door in all)
            {
                if (door.open || door.doorway == null) continue;

                var bounds = door.doorway.WorldBounds;
                if (position.y < bounds.min.y - 0.5f || position.y > bounds.max.y) continue;

                var closest = bounds.ClosestPoint(new Vector3(position.x, bounds.center.y, position.z));
                closest.y = position.y;
                if ((closest - position).sqrMagnitude <= reach * reach) door.SetOpen(true);
            }
        }

        public void Configure(float angle) => openAngle = angle;

        protected override void Awake()
        {
            base.Awake();
            ConfigureRuntime(name.Contains("Ext") ? "Dış Kapı" : "Kapı", WorkDuration);
            closedRotation = transform.localRotation;
            EnsureCollider();
            CreateDoorwayBlocker();
            open = startOpen;
            openAmount = open ? 1f : 0f;
            ApplyRotation();
            UpdateDoorway();
        }

        /// <summary>
        /// Kapalı kanadın kapladığı aralık: kapı kanadıyla aynı boyda, duvar kalınlığında sabit bir kutu.
        /// Kanat döndükçe yerinden oynamasın diye kapının ebeveynine (bina modeline) bağlanır.
        /// </summary>
        void CreateDoorwayBlocker()
        {
            var renderer = GetComponent<Renderer>();
            if (renderer == null || transform.parent == null) return;

            var bounds = renderer.bounds;
            // İnce kanadı duvar kalınlığına genişlet ki çapraz yürürken aralıktan sızılmasın.
            var size = bounds.size;
            if (size.x < size.z) size.x = Mathf.Max(size.x, 0.25f);
            else size.z = Mathf.Max(size.z, 0.25f);
            var center = bounds.center;
            // Kutu zeminden başlar: hangi katta olduğu alt yüzünden bulunur.
            float bottom = bounds.min.y;
            size.y = Mathf.Max(0.5f, bounds.max.y - bottom);
            center.y = bottom + size.y * 0.5f;

            var go = new GameObject(name + "_Aralik");
            go.transform.SetParent(transform.parent, false);
            doorway = go.AddComponent<WalkBlocker>();
            doorway.Configure(
                go.transform.InverseTransformPoint(center),
                InverseScale(go.transform, size),
                true);
        }

        static Vector3 InverseScale(Transform t, Vector3 worldSize)
        {
            // Ebeveyn dönük olabilir: dünya kutusunu yerel eksenlere geri çevir.
            var x = t.InverseTransformVector(new Vector3(worldSize.x, 0f, 0f));
            var y = t.InverseTransformVector(new Vector3(0f, worldSize.y, 0f));
            var z = t.InverseTransformVector(new Vector3(0f, 0f, worldSize.z));
            return new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        }

        void UpdateDoorway()
        {
            if (doorway != null) doorway.gameObject.SetActive(!open);
        }

        void OnDestroy()
        {
            if (doorway != null) Destroy(doorway.gameObject);
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

        // Menteşe duvarın üstünde: karakter kapı aralığının hizasında, geldiği taraftaki odada, kanadın savrulma yayının dışında durur.
        const float WorkSideOffset = 1f;

        public override float GetWorkX(int fromFloor, float fromX)
        {
            if (doorway == null) return base.GetWorkX(fromFloor, fromX);
            float centerX = doorway.WorldBounds.center.x;
            return centerX + (fromX >= centerX ? WorkSideOffset : -WorkSideOffset);
        }

        public override float GetWorkZ()
        {
            if (doorway == null || Building == null) return base.GetWorkZ();
            return Building.ClampZ(doorway.WorldBounds.center.z);
        }

        public override void Complete(Survivor worker) => SetOpen(!open);

        public void SetOpen(bool value)
        {
            open = value;
            UpdateDoorway();
        }

        void Update()
        {
            if (autoOpen) UpdateAutoOpen(Time.deltaTime);

            float target = open ? 1f : 0f;
            if (Mathf.Approximately(openAmount, target)) return;

            openAmount = Mathf.MoveTowards(openAmount, target, Time.deltaTime / Mathf.Max(0.01f, swingDuration));
            ApplyRotation();
        }

        void UpdateAutoOpen(float dt)
        {
            if (doorway == null) return;

            // Açıkken biraz daha geniş bakılır: eşikte duran kişinin önünde kapı kapanıp açılmasın.
            if (AnyoneNear(open ? openReach + 0.4f : openReach))
            {
                emptyTime = 0f;
                if (!open) SetOpen(true);
            }
            else if (open && !startOpen)
            {
                emptyTime += dt;
                if (emptyTime >= closeDelay) SetOpen(false);
            }
        }

        bool AnyoneNear(float reach)
        {
            var bounds = doorway.WorldBounds;
            foreach (var survivor in Survivor.All)
            {
                if (survivor == null || survivor.IsDead || !survivor.isActiveAndEnabled) continue;
                var position = survivor.transform.position;
                if (position.y < bounds.min.y - 0.5f || position.y > bounds.max.y) continue;

                var closest = bounds.ClosestPoint(new Vector3(position.x, bounds.center.y, position.z));
                closest.y = position.y;
                if ((closest - position).sqrMagnitude <= reach * reach) return true;
            }
            return false;
        }

        void ApplyRotation()
        {
            float angle = Mathf.SmoothStep(0f, openAngle, openAmount);
            transform.localRotation = closedRotation * Quaternion.AngleAxis(angle, hingeAxis);
        }
    }
}
