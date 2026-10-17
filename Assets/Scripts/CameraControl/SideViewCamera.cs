using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using UnityEngine;

namespace AftershockIsle.CameraControl
{
    /// <summary>
    /// Kesit kamerası: rotasyon kilitli, yalnızca X/Y'de yumuşak takip. Seçili karakteri izler;
    /// WASD/oklar takibi bırakıp serbest kaydırır, F tekrar odaklar, tekerlek yakınlaştırır.
    /// Duraklatmada da odaları inceleyebilmek için unscaled zaman kullanır.
    /// Dış sefer sırasında saha görünümüne geçer: kamera kâşifi yukarıdan eğik açıyla izler,
    /// böylece haritanın içine/ekrana doğru (Z) hareket okunur. Tab/Select iki görünüm arasında geçer.
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
        [SerializeField] float shakeAmplitude = 0.35f;

        [Header("Dış saha")]
        [Tooltip("Kameranın aşağı bakış açısı (derece)")]
        [SerializeField] float fieldPitch = 32f;
        [SerializeField] float fieldDistance = 17f;
        [SerializeField] float fieldMinDistance = 11f;
        [SerializeField] float fieldMaxDistance = 26f;
        [SerializeField] float fieldHeightOffset = 0.8f;

        static Transform fieldFocus;
        static bool viewField;

        /// <summary>Dış saha görünümü açık mı (sığınağın yerine kâşif izleniyor).</summary>
        public static bool ViewingField => fieldFocus != null && viewField;

        /// <summary>Dış sefer başlayınca kâşifin avatarı verilir, bitince null. Verilince görünüm sahaya geçer.</summary>
        public static void SetFieldFocus(Transform target)
        {
            fieldFocus = target;
            viewField = target != null;
        }

        Survivor selected;
        bool wasViewingField;
        Vector3 baseFocus;
        float shakeRemaining;
        float shakeDuration = 1f;
        bool following;
        Vector3 focus;
        Vector3 smoothedFocus;
        Vector3 velocity;
        float targetDistance;
        float targetFieldDistance;
        float currentFieldDistance;

        void Awake()
        {
            if (building == null) building = FindFirstObjectByType<BuildingLayout>();
            GetComponent<Camera>().fieldOfView = fieldOfView;
            transform.rotation = Quaternion.identity;

            fieldFocus = null;
            viewField = false;
            focus = new Vector3(transform.position.x, transform.position.y, building.WalkPlaneZ);
            smoothedFocus = focus;
            targetDistance = distance;
            targetFieldDistance = fieldDistance;
            currentFieldDistance = fieldDistance;
            GameInput.FieldControlsActive = false;
        }

        void OnEnable()
        {
            EventBus.Subscribe<SurvivorSelected>(OnSurvivorSelected);
            EventBus.Subscribe<AftershockImpact>(OnAftershockImpact);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<SurvivorSelected>(OnSurvivorSelected);
            EventBus.Unsubscribe<AftershockImpact>(OnAftershockImpact);
        }

        void OnAftershockImpact(AftershockImpact evt)
        {
            shakeDuration = evt.ShakeDuration;
            shakeRemaining = evt.ShakeDuration;
        }

        void OnSurvivorSelected(SurvivorSelected evt)
        {
            selected = evt.Survivor;
            following = selected != null;
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;

            if (fieldFocus != null && GameInput.ViewSwitchPressed) viewField = !viewField;
            bool showField = ViewingField;
            GameInput.FieldControlsActive = showField;
            if (showField != wasViewingField)
            {
                wasViewingField = showField;
                velocity = Vector3.zero;
                if (showField) baseFocus = focus;
                else focus = baseFocus;
                smoothedFocus = showField ? FieldFocusPoint() : focus;
            }

            // Görünüm değişince açı yumuşakça döner (konum odakla birlikte hemen geçer).
            var targetRotation = showField ? Quaternion.Euler(fieldPitch, 0f, 0f) : Quaternion.identity;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 1f - Mathf.Exp(-8f * dt));

            if (showField)
            {
                float fieldScroll = GameInput.ScrollDelta;
                if (fieldScroll != 0f)
                    targetFieldDistance = Mathf.Clamp(targetFieldDistance - Mathf.Sign(fieldScroll) * zoomStep, fieldMinDistance, fieldMaxDistance);

                smoothedFocus = Vector3.SmoothDamp(smoothedFocus, FieldFocusPoint(), ref velocity, smoothTime * 0.6f, Mathf.Infinity, dt);
                currentFieldDistance = Mathf.Lerp(currentFieldDistance, targetFieldDistance, 1f - Exp(dt));
                transform.position = smoothedFocus - transform.forward * currentFieldDistance;
                return;
            }

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

            transform.position = smoothedFocus - transform.forward * distance + ShakeOffset();
        }

        Vector3 FieldFocusPoint()
        {
            var point = fieldFocus != null ? fieldFocus.position : focus;
            return new Vector3(point.x, point.y + fieldHeightOffset, point.z);
        }

        static float Exp(float dt) => Mathf.Exp(-10f * dt);

        // Sarsıntı oyun zamanıyla söner; duraklatınca görüntü sabitlenir.
        Vector3 ShakeOffset()
        {
            if (shakeRemaining <= 0f || Time.deltaTime <= 0f) return Vector3.zero;

            shakeRemaining -= Time.deltaTime;
            float strength = shakeAmplitude * Mathf.Clamp01(shakeRemaining / shakeDuration);
            return (Vector3)(Random.insideUnitCircle * strength);
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
