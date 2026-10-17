using UnityEngine;
using UnityEngine.InputSystem;

namespace AftershockIsle.Core
{
    /// <summary>
    /// Tüm tuş/fare/gamepad okumaları tek yerde. Tuş değişiklikleri sadece burada yapılır.
    /// Input System timeScale'den bağımsız güncellenir, yani duraklatmada da çalışır.
    /// Sığınak fare ile yönetilir; dış saha doğrudan kontroldür (klavye ya da gamepad, aynı eylemler).
    /// </summary>
    public static class GameInput
    {
        static Keyboard Keys => Keyboard.current;
        static Mouse Pointer => Mouse.current;
        static Gamepad Pad => Gamepad.current;

        static InputAction fieldMove;
        static InputAction fieldDodge;
        static InputAction fieldInteract;
        static InputAction fieldHeal;

        /// <summary>
        /// Dış saha görünümü açıkken true. Bu sırada Space duraklatmaz (kaçıştır) ve WASD kamerayı değil karakteri yürütür.
        /// </summary>
        public static bool FieldControlsActive { get; set; }

        /// <summary>Son kullanılan cihaz gamepad mi (ekrandaki tuş ipuçları buna göre değişir).</summary>
        public static bool UsingGamepad { get; private set; }

        public static bool PausePressed => !FieldControlsActive && Keys != null && Keys.spaceKey.wasPressedThisFrame;

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

        public static bool EscapePressed =>
            (Keys != null && Keys.escapeKey.wasPressedThisFrame) || (Pad != null && Pad.startButton.wasPressedThisFrame);

        /// <summary>Dış sefer sırasında görünümü saha ile sığınak arasında değiştirir.</summary>
        public static bool ViewSwitchPressed =>
            (Keys != null && Keys.tabKey.wasPressedThisFrame) || (Pad != null && Pad.selectButton.wasPressedThisFrame);

        public static bool HelpTogglePressed => Keys != null && Keys.hKey.wasPressedThisFrame;
        public static bool SkillTreeTogglePressed => Keys != null && Keys.kKey.wasPressedThisFrame;

        public static bool DebugSpawnSurvivorPressed => Keys != null && Keys.f12Key.wasPressedThisFrame;
        public static bool DebugFirePressed => Keys != null && Keys.f6Key.wasPressedThisFrame;
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

        // ---------------------------------------------------------------- Dış saha (doğrudan kontrol)

        /// <summary>WASD / oklar / sol çubuk. x: sağ-sol, y: haritanın içine (+) / ekrana doğru (-). Uzunluğu en fazla 1.</summary>
        public static Vector2 FieldMove
        {
            get
            {
                EnsureFieldActions();
                return Vector2.ClampMagnitude(fieldMove.ReadValue<Vector2>(), 1f);
            }
        }

        /// <summary>Space / B (Daire).</summary>
        public static bool FieldDodgePressed => WasPressed(ref fieldDodge);

        /// <summary>F / A (Çarpı): ara, kurtar, çıkıştan dön.</summary>
        public static bool FieldInteractPressed => WasPressed(ref fieldInteract);

        /// <summary>R / Y (Üçgen): Yaşam Kiti ile ilk yardım.</summary>
        public static bool FieldHealPressed => WasPressed(ref fieldHeal);

        /// <summary>Ekran ipucu: klavyede ya da gamepad'de aynı eylemin tuş adı.</summary>
        public static string Prompt(FieldPrompt prompt) => prompt switch
        {
            FieldPrompt.Move => UsingGamepad ? "Sol çubuk" : "WASD",
            FieldPrompt.Dodge => UsingGamepad ? "B" : "Space",
            FieldPrompt.Interact => UsingGamepad ? "A" : "F",
            FieldPrompt.Heal => UsingGamepad ? "Y" : "R",
            FieldPrompt.ViewSwitch => UsingGamepad ? "Select" : "Tab",
            _ => "?",
        };

        /// <summary>Son kullanılan cihazı belirler. Saha her kare çağırır.</summary>
        public static void RefreshActiveDevice()
        {
            var pad = Pad;
            if (pad != null && PadActive(pad))
            {
                UsingGamepad = true;
                return;
            }

            bool keyboard = Keys != null && Keys.anyKey.wasPressedThisFrame;
            bool mouse = Pointer != null && (Pointer.delta.ReadValue().sqrMagnitude > 4f || Pointer.leftButton.wasPressedThisFrame);
            if (keyboard || mouse) UsingGamepad = false;
        }

        static bool PadActive(Gamepad pad) =>
            pad.leftStick.ReadValue().sqrMagnitude > 0.1f || pad.rightStick.ReadValue().sqrMagnitude > 0.1f ||
            pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame ||
            pad.buttonNorth.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame ||
            pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame ||
            pad.leftTrigger.wasPressedThisFrame || pad.rightTrigger.wasPressedThisFrame;

        static bool WasPressed(ref InputAction action)
        {
            EnsureFieldActions();
            return action.WasPressedThisFrame();
        }

        // Eylemler koddan kurulur: ayrı bir .inputactions dosyası gerekmez, klavye ve gamepad aynı eyleme bağlanır.
        static void EnsureFieldActions()
        {
            if (fieldMove != null) return;

            fieldMove = new InputAction("FieldMove", InputActionType.Value, expectedControlType: "Vector2");
            fieldMove.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            fieldMove.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            fieldMove.AddBinding("<Gamepad>/leftStick");

            fieldDodge = Button("FieldDodge", "<Keyboard>/space", "<Gamepad>/buttonEast");
            fieldInteract = Button("FieldInteract", "<Keyboard>/f", "<Gamepad>/buttonSouth");
            fieldHeal = Button("FieldHeal", "<Keyboard>/r", "<Gamepad>/buttonNorth");

            fieldMove.Enable();
            fieldDodge.Enable();
            fieldInteract.Enable();
            fieldHeal.Enable();
        }

        static InputAction Button(string name, string keyboard, string gamepad)
        {
            var action = new InputAction(name, InputActionType.Button);
            action.AddBinding(keyboard);
            action.AddBinding(gamepad);
            return action;
        }

        static float Axis(bool positive, bool negative) => (positive ? 1f : 0f) - (negative ? 1f : 0f);
    }

    public enum FieldPrompt { Move, Dodge, Interact, Heal, ViewSwitch }
}
