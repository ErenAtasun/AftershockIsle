using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using UnityEngine;

namespace AftershockIsle.Player
{
    /// <summary>
    /// Sol tık: karakter seç (boşa tıklamak seçimi kaldırır). Sağ tık: seçili karakteri oraya gönder.
    /// Duraklatmada da emir verilebilir; karakter zaman akınca yürümeye başlar.
    /// </summary>
    public class CommandController : MonoBehaviour
    {
        [SerializeField] Camera cam;
        [SerializeField] BuildingLayout building;
        [SerializeField] float maxRayDistance = 200f;

        readonly RaycastHit[] hits = new RaycastHit[16];

        public Survivor Selected { get; private set; }

        void Awake()
        {
            if (cam == null) cam = Camera.main;
            if (building == null) building = FindFirstObjectByType<BuildingLayout>();
        }

        void Update()
        {
            if (GameInput.SelectPressed) Select(PickSurvivor());
            if (GameInput.CommandPressed && Selected != null) IssueMoveOrder();
        }

        public void Select(Survivor survivor)
        {
            if (survivor == Selected) return;

            if (Selected != null) Selected.SetSelected(false);
            Selected = survivor;
            if (Selected != null) Selected.SetSelected(true);

            EventBus.Publish(new SurvivorSelected(Selected));
        }

        Survivor PickSurvivor()
        {
            var ray = cam.ScreenPointToRay(GameInput.PointerPosition);
            int count = Physics.RaycastNonAlloc(ray, hits, maxRayDistance);

            // Bina geometrisi de collider taşıdığı için ilk isabet yerine en yakın kazazedeyi ara.
            Survivor closest = null;
            float closestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var survivor = hits[i].collider.GetComponentInParent<Survivor>();
                if (survivor == null || hits[i].distance >= closestDistance) continue;

                closest = survivor;
                closestDistance = hits[i].distance;
            }

            return closest;
        }

        void IssueMoveOrder()
        {
            var ray = cam.ScreenPointToRay(GameInput.PointerPosition);
            if (Mathf.Abs(ray.direction.z) < 0.0001f) return;

            float t = (building.WalkPlaneZ - ray.origin.z) / ray.direction.z;
            if (t < 0f) return;

            Vector3 point = ray.GetPoint(t);
            Selected.Mover.MoveTo(building.FloorAtHeight(point.y), point.x);
        }
    }
}
