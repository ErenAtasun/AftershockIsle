using UnityEngine;
using UnityEngine.InputSystem;

namespace AftershockIsle.Core
{
    /// <summary>
    /// Tüm tuş/fare okumaları tek yerde. Tuş değişiklikleri sadece burada yapılır.
    /// Input System timeScale'den bağımsız güncellenir, yani duraklatmada da çalışır.
    /// </summary>
    public static class GameInput
    {
        static Keyboard Keys => Keyboard.current;
        static Mouse Pointer => Mouse.current;

        public static bool PausePressed => Keys != null && Keys.spaceKey.wasPressedThisFrame;

        public static bool NormalSpeedPressed =>
            Keys != null && (Keys.digit1Key.wasPressedThisFrame || Keys.numpad1Key.wasPressedThisFrame);

        public static bool FastSpeedPressed =>
            Keys != null && (Keys.digit2Key.wasPressedThisFrame || Keys.numpad2Key.wasPressedThisFrame);

        public static bool FocusPressed => Keys != null && Keys.fKey.wasPressedThisFrame;
        public static bool RestartPressed => Keys != null && Keys.rKey.wasPressedThisFrame;

        /// <summary>Basılıyken verilen emir mevcut işleri iptal etmez, sıraya eklenir.</summary>
        public static bool QueueModifierHeld => Keys != null && Keys.shiftKey.isPressed;

        /// <summary>Manuel tulumba ritim mini-oyunu: Q ve E dönüşümlü basılır.</summary>
        public static bool PumpLeftPressed => Keys != null && Keys.qKey.wasPressedThisFrame;
        public static bool PumpRightPressed => Keys != null && Keys.eKey.wasPressedThisFrame;

        public static bool DebugFloodPressed => Keys != null && Keys.f7Key.wasPressedThisFrame;
        public static bool DebugQuakePressed => Keys != null && Keys.f8Key.wasPressedThisFrame;
        public static bool DebugDamagePressed => Keys != null && Keys.f9Key.wasPressedThisFrame;
        public static bool DebugCrackPressed => Keys != null && Keys.f10Key.wasPressedThisFrame;
        public static bool DebugResourcesPressed => Keys != null && Keys.f11Key.wasPressedThisFrame;

        public static Vector2 PanAxis
        {
            get
            {
                if (Keys == null) return Vector2.zero;
                float x = Axis(Keys.dKey.isPressed || Keys.rightArrowKey.isPressed,
                               Keys.aKey.isPressed || Keys.leftArrowKey.isPressed);
                float y = Axis(Keys.wKey.isPressed || Keys.upArrowKey.isPressed,
                               Keys.sKey.isPressed || Keys.downArrowKey.isPressed);
                return new Vector2(x, y);
            }
        }

        public static bool SelectPressed => Pointer != null && Pointer.leftButton.wasPressedThisFrame;
        public static bool CommandPressed => Pointer != null && Pointer.rightButton.wasPressedThisFrame;
        public static Vector2 PointerPosition => Pointer != null ? Pointer.position.ReadValue() : Vector2.zero;
        public static float ScrollDelta => Pointer != null ? Pointer.scroll.ReadValue().y : 0f;

        static float Axis(bool positive, bool negative) => (positive ? 1f : 0f) - (negative ? 1f : 0f);
    }
}
