using AftershockIsle.Building;
using AftershockIsle.Characters;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using AftershockIsle.UI;
using UnityEngine;

namespace AftershockIsle.CameraControl
{
    /// <summary>
    /// Kesit kamerası: yalnızca X/Y'de yumuşak takip. Seçili karakteri izler; sağ çubuk (ve seçili karakter yokken
    /// WASD/oklar) takibi bırakıp serbest kaydırır, F ya da karakter hareketi tekrar odaklar, tekerlek yakınlaştırır.
    /// Duraklatmada da odaları inceleyebilmek için unscaled zaman kullanır.
    /// Sığınak ve dış saha aynı eğik açıyla (yukarıdan bakış) görülür; böylece derinlik iki yerde de okunur.
    /// Dış sefer sırasında saha görünümüne geçer ve kâşifi izler. Tab/Select iki görünüm arasında geçer.
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

        [Tooltip("Kameranın aşağı bakış açısı (derece); sığınak ve saha için ortak")]
        [SerializeField, Range(0f, 45f)] float pitch = 20f;

        [Header("Dış saha")]
        [SerializeField] float fieldDistance = 17f;
        [SerializeField] float fieldMinDistance = 11f;
        [SerializeField] float fieldMaxDistance = 26f;
        [SerializeField] float fieldHeightOffset = 0.8f;

        static Transform fieldFocus;
        static bool viewField;
        static float kickStrength;
        static float kickRemaining;
        static float kickDuration = 0.1f;

        /// <summary>Kısa kamera sarsıntısı (atış, isabet, darbe). Gerçek zamanla söner; sahada da çalışır.</summary>
        public static void Kick(float strength, float seconds)
        {
            if (strength < kickStrength * Mathf.Clamp01(kickRemaining / kickDuration)) return;
            kickStrength = strength;
            kickDuration = Mathf.Max(0.01f, seconds);
            kickRemaining = seconds;
        }

        /// <summary>Dış saha görünümü açık mı (sığınağın yerine kâşif izleniyor).</summary>
        public static bool ViewingField => fieldFocus != null && viewField;

        /// <summary>Dış sefer başlayınca kâşifin avatarı verilir, bitince null. Verilince görünüm sahaya geçer.</summary>
        public static void SetFieldFocus(Transform target)
        {
            fieldFocus = target;
            viewField = target != null;
        }

        /// <summary>Seferde bir kâşif var mı (saha görünümüne geçilebilir mi).</summary>
        public static bool HasField => fieldFocus != null;

        /// <summary>Saha ya da sığınak görünümünü açar (saha yoksa etkisiz).</summary>
        public static void ShowField(bool show)
        {
            if (fieldFocus != null) viewField = show;
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
        float pitchNoticeUntil;
        GUIStyle pitchStyle;

        // Test kısayolu (F5) ile sırayla denenen açılar.
        static readonly float[] PitchPresets = { 15f, 20f, 25f, 30f };

        void Awake()
        {
            if (building == null) building = FindFirstObjectByType<BuildingLayout>();
            var cam = GetComponent<Camera>();
            cam.fieldOfView = fieldOfView;
            cam.backgroundColor = new Color(0.553f, 0.620f, 0.667f, 1f);
            pitch = GameSettings.CameraPitch;
            transform.rotation = Quaternion.Euler(pitch, 0f, 0f);

            // Eski kameraya bağlı arka plan objesi kalmışsa temizle (arka plan artık binanın arkasında sabit durur).
            var oldCamBg = transform.Find("Genel Arka Plan");
            if (oldCamBg != null) Destroy(oldCamBg.gameObject);

            fieldFocus = null;
            viewField = false;
            kickRemaining = 0f;
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
            EventBus.Subscribe<SettingsChanged>(OnSettingsChanged);
        }

        void OnSettingsChanged(SettingsChanged evt) => pitch = GameSettings.CameraPitch;

        void OnDisable()
        {
            GameInput.StopRumble();
            EventBus.Unsubscribe<SurvivorSelected>(OnSurvivorSelected);
            EventBus.Unsubscribe<AftershockImpact>(OnAftershockImpact);
            EventBus.Unsubscribe<SettingsChanged>(OnSettingsChanged);
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
            GameInput.TickRumble();

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

            if (GameInput.DebugCameraPitchPressed) CyclePitch();

            // Açı değişince (test kısayolu) yumuşakça döner.
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(pitch, 0f, 0f), 1f - Mathf.Exp(-8f * dt));

            if (showField)
            {
                float fieldScroll = GameInput.ScrollDelta;
                if (fieldScroll != 0f)
                    targetFieldDistance = Mathf.Clamp(targetFieldDistance - Mathf.Sign(fieldScroll) * zoomStep, fieldMinDistance, fieldMaxDistance);

                smoothedFocus = Vector3.SmoothDamp(smoothedFocus, FieldFocusPoint(), ref velocity, smoothTime * 0.6f, Mathf.Infinity, dt);
                currentFieldDistance = Mathf.Lerp(currentFieldDistance, targetFieldDistance, 1f - Exp(dt));
                transform.position = smoothedFocus - transform.forward * currentFieldDistance + KickOffset(dt);
                return;
            }

            // Seçili karakter varken WASD onu yürütür; kamera sadece sağ çubukla kaydırılır.
            Vector2 pan = GameInput.CameraPanAxis;
            if (selected == null) pan = Vector2.ClampMagnitude(pan + GameInput.PanAxis, 1f);
            bool steeringSelected = selected != null && GameInput.CharacterMove.sqrMagnitude > 0.09f;
            if (pan != Vector2.zero)
            {
                following = false;
                focus += (Vector3)pan * (panSpeed * dt * (distance / 24f));
            }
            else if ((GameInput.FocusPressed || steeringSelected) && selected != null)
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

            transform.position = smoothedFocus - transform.forward * distance + ShakeOffset() + KickOffset(dt);
        }

        static Vector3 KickOffset(float dt)
        {
            if (kickRemaining <= 0f) return Vector3.zero;
            kickRemaining -= dt;
            float strength = kickStrength * Mathf.Clamp01(kickRemaining / kickDuration);
            return (Vector3)(Random.insideUnitCircle * strength);
        }

        void CyclePitch()
        {
            int next = 0;
            for (int i = 0; i < PitchPresets.Length; i++)
                if (pitch >= PitchPresets[i] - 0.5f) next = (i + 1) % PitchPresets.Length;
            GameSettings.CameraPitch = PitchPresets[next];
            pitchNoticeUntil = Time.unscaledTime + 2.5f;
        }

        void OnGUI()
        {
            UiTheme.Apply();
            if (Time.unscaledTime > pitchNoticeUntil) return;

            pitchStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.Box(new Rect((UiTheme.Width - 260f) * 0.5f, UiTheme.Height * 0.5f - 120f, 260f, 40f), $"Kamera eğimi: {pitch:0}°  (F5)", pitchStyle);
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
