using AftershockIsle.Building;
using AftershockIsle.CameraControl;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using AftershockIsle.Expedition;
using AftershockIsle.Interaction;
using AftershockIsle.UI;
using UnityEngine;

namespace AftershockIsle.Player
{
    /// <summary>
    /// Sol tık: karakter seç (boşa tıklamak seçimi kaldırır).
    /// Sağ tık: nesne üzerindeyse iş ver (Shift ile sıraya ekle), boşluktaysa oraya yürü.
    /// Duraklatmada da emir verilebilir; karakter zaman akınca harekete geçer.
    /// </summary>
    public class CommandController : MonoBehaviour
    {
        [SerializeField] Camera cam;
        [SerializeField] BuildingLayout building;
        [SerializeField] float maxRayDistance = 200f;

        readonly RaycastHit[] hits = new RaycastHit[32];
        int hitCount;

        public Survivor Selected { get; private set; }
        public Interactable Hovered { get; private set; }

        void Awake()
        {
            if (cam == null) cam = Camera.main;
            if (building == null) building = FindFirstObjectByType<BuildingLayout>();
        }

        void Update()
        {
            hitCount = Physics.RaycastNonAlloc(PointerRay(), hits, maxRayDistance);
            Hovered = Closest<Interactable>();

            // Dış sahadayken tıklamaları FieldExpedition yönetir; sığınaktaki seçim/emir askıda kalır.
            if (SideViewCamera.ViewingField)
            {
                Hovered = null;
                return;
            }

            // Ölen ya da sefere çıkan (sahnede pasif) kazazedenin seçimi kalkar.
            if (Selected != null && (Selected.IsDead || !Selected.isActiveAndEnabled)) Select(null);
            if (UIBlocker.IsPointerOver(GameInput.PointerPosition))
            {
                Hovered = null;
                return;
            }

            if (GameInput.SelectPressed)
            {
                var clicked = Closest<Survivor>();
                Select(clicked != null && !clicked.IsDead ? clicked : null);
            }

            if (GameInput.CommandPressed && Selected != null) IssueOrder();
        }

        public void Select(Survivor survivor)
        {
            if (survivor == Selected) return;

            if (Selected != null) Selected.SetSelected(false);
            Selected = survivor;
            if (Selected != null) Selected.SetSelected(true);

            EventBus.Publish(new SurvivorSelected(Selected));
        }

        void IssueOrder()
        {
            if (Selected.Condition.IsPanicking)
            {
                EventBus.Publish(new TaskNotice(Selected, "Panik halinde, emir dinlemiyor."));
                return;
            }

            if (ExpeditionSystem.Instance != null && ExpeditionSystem.Instance.IsInTransit(Selected))
            {
                EventBus.Publish(new TaskNotice(Selected, "Kapıdan geçiyor, emir veremezsin."));
                return;
            }

            if (Hovered != null)
            {
                Hovered.HandleCommand(Selected, GameInput.QueueModifierHeld);
                return;
            }

            var ray = PointerRay();
            if (Mathf.Abs(ray.direction.z) < 0.0001f) return;

            float t = (building.WalkPlaneZ - ray.origin.z) / ray.direction.z;
            if (t < 0f) return;

            Vector3 point = ray.GetPoint(t);
            int floor = building.FloorAtHeight(point.y);
            if (FireSpot.IsSmokeBlocked(floor) && floor != Selected.Mover.CurrentFloor)
            {
                EventBus.Publish(new TaskNotice(Selected, $"Duman yüzünden {building.GetFloorName(floor)} katına girilemiyor."));
                return;
            }

            Selected.Tasks.CancelAll();
            Selected.Mover.MoveTo(floor, point.x);
        }

        Ray PointerRay() => cam.ScreenPointToRay(GameInput.PointerPosition);

        // Bina geometrisi de collider taşıdığı için ilk isabet yerine aranan türün en yakınını seç.
        T Closest<T>() where T : Component
        {
            T closest = null;
            float closestDistance = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                var candidate = hits[i].collider.GetComponentInParent<T>();
                if (candidate == null || hits[i].distance >= closestDistance) continue;

                closest = candidate;
                closestDistance = hits[i].distance;
            }

            return closest;
        }
    }
}
