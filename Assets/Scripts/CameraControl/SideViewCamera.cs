using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using UnityEngine;

namespace AftershockIsle.CameraControl
{
    /// <summary>
    /// Kesit kamerası: rotasyon kilitli, yalnızca X/Y'de yumuşak takip. Seçili karakteri izler;
    /// WASD/oklar takibi bırakıp serbest kaydırır, F tekrar odaklar, tekerlek yakınlaştırır.
    /// Duraklatmada da odaları inceleyebilmek için unscaled zaman kullanır.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class SideViewCamera : MonoBehaviour
    {
        [SerializeField] BuildingLayout building;
        [SerializeField] float fieldOfView = 28f;
        [SerializeField] float distance = 24f;
        [SerializeField] float minDistance = 12f;
        [SerializeField] float maxDistance = 36f;
        [SerializeField] float zoomStep = 2f;
        [SerializeField] float panSpeed = 12f;
        [SerializeField] float smoothTime = 0.25f;
        [SerializeField] float targetHeightOffset = 1.2f;

        Survivor selected;
        bool following;
        Vector3 focus;
        Vector3 smoothedFocus;
        Vector3 velocity;
        float targetDistance;

        void Awake()
        {
            if (building == null) building = FindFirstObjectByType<BuildingLayout>();
            GetComponent<Camera>().fieldOfView = fieldOfView;
            transform.rotation = Quaternion.identity;

            focus = new Vector3(transform.position.x, transform.position.y, building.WalkPlaneZ);
            smoothedFocus = focus;
            targetDistance = distance;
        }

        void OnEnable() => EventBus.Subscribe<SurvivorSelected>(OnSurvivorSelected);
        void OnDisable() => EventBus.Unsubscribe<SurvivorSelected>(OnSurvivorSelected);

        void OnSurvivorSelected(SurvivorSelected evt)
        {
            selected = evt.Survivor;
            following = selected != null;
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;

            Vector2 pan = GameInput.PanAxis;
            if (pan != Vector2.zero)
            {
                following = false;
                focus += (Vector3)pan * (panSpeed * dt * (distance / 24f));
            }
            else if (GameInput.FocusPressed && selected != null)
            {
                following = true;
            }

            float scroll = GameInput.ScrollDelta;
            if (scroll != 0f)
                targetDistance = Mathf.Clamp(targetDistance - Mathf.Sign(scroll) * zoomStep, minDistance, maxDistance);

            if (following && selected != null)
                focus = selected.transform.position + Vector3.up * targetHeightOffset;

            focus = ClampToBuilding(focus);
            smoothedFocus = Vector3.SmoothDamp(smoothedFocus, focus, ref velocity, smoothTime, Mathf.Infinity, dt);
            distance = Mathf.Lerp(distance, targetDistance, 1f - Mathf.Exp(-10f * dt));

            transform.position = smoothedFocus - Vector3.forward * distance;
        }

        Vector3 ClampToBuilding(Vector3 point)
        {
            float halfWidth = building.Width * 0.5f;
            float centerX = building.transform.position.x;
            point.x = Mathf.Clamp(point.x, centerX - halfWidth, centerX + halfWidth);
            point.y = Mathf.Clamp(point.y, building.FloorY(0) + 1f, building.TopY);
            point.z = building.WalkPlaneZ;
            return point;
        }
    }
}
