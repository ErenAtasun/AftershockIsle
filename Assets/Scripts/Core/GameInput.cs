using UnityEngine;
using UnityEngine.InputSystem;

namespace AftershockIsle.Core
{
    /// <summary>
    /// Tüm tuş/fare/gamepad okumaları tek yerde. Tuş değişiklikleri sadece burada yapılır.
    /// Input System timeScale'den bağımsız güncellenir, yani duraklatmada da çalışır.
    /// Sığınakta ve sahada aynı kontrol: seçili karakter WASD / sol çubukla yürür, F / A etkileşir.
    /// Fare (sol tık seç, sağ tık emir) sığınakta alternatif olarak çalışmaya devam eder.
    /// </summary>
    public static class GameInput
    {
        static Keyboard Keys => Keyboard.current;
        static Mouse Pointer => Mouse.current;
        static Gamepad Pad => Gamepad.current;

        static InputAction characterMove;
        static InputAction interact;
        static InputAction dodge;
        static InputAction heal;
        static InputAction retreat;

        static int deviceFrame = -1;
        static bool usingGamepad;

        static float rumbleUntil;
        static bool rumbling;

        static int nudgeFrame = -1;
        static Vector2Int nudgeValue;
        static Vector2Int nudgeHeld;
        static float nudgeRepeatAt;

        static int menuFrame = -1;
        static int menuStep;
        static int menuHeldDirection;
        static float menuRepeatAt;

        /// <summary>
        /// Dış saha görünümü açıkken true. Bu sırada Space duraklatmaz (kaçıştır), gamepad Y/X zamanı değil
        /// ilk yardım/ateşi yönetir.
        /// </summary>
        public static bool FieldControlsActive { get; set; }

        /// <summary>Son kullanılan cihaz gamepad mi (ekrandaki tuş ipuçları ve menü vurgusu buna göre değişir).</summary>
        public static bool UsingGamepad
        {
            get
            {
                if (deviceFrame != Time.frameCount)
                {
                    deviceFrame = Time.frameCount;
                    RefreshActiveDevice();
                }
                return usingGamepad;
            }
        }

        // ---------------------------------------------------------------- Zaman

        /// <summary>Space / gamepad Y (sadece sığınakta).</summary>
        public static bool PausePressed => !FieldControlsActive &&
            ((Keys != null && Keys.spaceKey.wasPressedThisFrame) || (Pad != null && Pad.buttonNorth.wasPressedThisFrame));

        public static bool NormalSpeedPressed =>
            Keys != null && (Keys.digit1Key.wasPressedThisFrame || Keys.numpad1Key.wasPressedThisFrame);

        public static bool FastSpeedPressed =>
            Keys != null && (Keys.digit2Key.wasPressedThisFrame || Keys.numpad2Key.wasPressedThisFrame);

        /// <summary>Gamepad X: normal / hızlı arasında geçiş (sadece sığınakta).</summary>
        public static bool SpeedTogglePressed => !FieldControlsActive && Pad != null && Pad.buttonWest.wasPressedThisFrame;

        // ---------------------------------------------------------------- Karakter (sığınak + saha)

        /// <summary>WASD / oklar / sol çubuk. Sığınakta x: kat içinde yürü, y: merdivene çık/in. Sahada x-z düzleminde yürü.</summary>
        public static Vector2 CharacterMove
        {
            get
            {
                EnsureActions();
                return Vector2.ClampMagnitude(characterMove.ReadValue<Vector2>(), 1f);
            }
        }

        /// <summary>F / A (Çarpı): en yakın nesneyle etkileş (sığınakta iş ver, sahada ara/çık).</summary>
        public static bool InteractPressed => WasPressed(ref interact);

        /// <summary>Sahada Space / B (Daire): kaçış atılması.</summary>
        public static bool DodgePressed => WasPressed(ref dodge);

        /// <summary>Sahada R / Y (Üçgen): Yaşam Kiti ile ilk yardım.</summary>
        public static bool HealPressed => WasPressed(ref heal);

        /// <summary>Sahada X / sol çubuğa bas (L3): çıkışa otomatik koş (hareket ya da tekrar basmak iptal eder).</summary>
        public static bool RetreatPressed => FieldControlsActive && WasPressed(ref retreat);

        /// <summary>Sahada sol tık / RT: ateş.</summary>
        public static bool FirePressed =>
            (Pointer != null && Pointer.leftButton.wasPressedThisFrame) || (Pad != null && Pad.rightTrigger.wasPressedThisFrame);

        /// <summary>Sahada Shift / C / LT basılı: çömel (siper).</summary>
        public static bool CrouchHeld => FieldControlsActive &&
            ((Keys != null && (Keys.leftShiftKey.isPressed || Keys.cKey.isPressed)) || (Pad != null && Pad.leftTrigger.isPressed));

        /// <summary>Sahada Q / X (Kare): silah değiştir.</summary>
        public static bool SwitchWeaponPressed => FieldControlsActive &&
            ((Keys != null && Keys.qKey.wasPressedThisFrame) || (Pad != null && Pad.buttonWest.wasPressedThisFrame));

        /// <summary>Sağ çubuk nişanı (ölü bölge altında sıfır).</summary>
        public static Vector2 AimStick
        {
            get
            {
                if (Pad == null) return Vector2.zero;
                var stick = Pad.rightStick.ReadValue();
                return stick.sqrMagnitude < 0.09f ? Vector2.zero : stick;
            }
        }

        /// <summary>Sığınakta X / B (Daire): seçili karakterin işini bırakıp durması.</summary>
        public static bool CancelOrderPressed => !FieldControlsActive &&
            ((Keys != null && Keys.xKey.wasPressedThisFrame) || (Pad != null && Pad.buttonEast.wasPressedThisFrame));

        /// <summary>Tab / RB: sonraki karakter (seferdeki kâşif de sıradadır; ona gelince saha görünümü açılır).</summary>
        public static bool NextCharacterPressed =>
            (Keys != null && Keys.tabKey.wasPressedThisFrame && !Keys.shiftKey.isPressed) ||
            (Pad != null && Pad.rightShoulder.wasPressedThisFrame);

        /// <summary>Shift+Tab / LB: önceki karakter.</summary>
        public static bool PrevCharacterPressed =>
            (Keys != null && Keys.tabKey.wasPressedThisFrame && Keys.shiftKey.isPressed) ||
            (Pad != null && Pad.leftShoulder.wasPressedThisFrame);

        /// <summary>Gamepad Select: sığınak ↔ saha görünümü.</summary>
        public static bool ViewSwitchPressed => Pad != null && Pad.selectButton.wasPressedThisFrame;

        public static bool FocusPressed => Keys != null && Keys.fKey.wasPressedThisFrame;

        /// <summary>Sahada I / D-pad ↑: envanter (çanta) penceresi.</summary>
        public static bool InventoryPressed =>
            (Keys != null && Keys.iKey.wasPressedThisFrame) || (FieldControlsActive && Pad != null && Pad.dpad.up.wasPressedThisFrame);
        /// <summary>Alt basılıyken tüm dünya işaretleri ve kat adları gösterilir (zaman duruyorsa zaten gösterilir).</summary>
        public static bool ShowAllMarkersHeld => Keys != null && (Keys.leftAltKey.isPressed || Keys.rightAltKey.isPressed);

        /// <summary>M tuşu: Şehir haritası inceleme penceresi.</summary>
        public static bool MapPressed => (Keys != null && Keys.mKey.wasPressedThisFrame);
        public static bool RestartPressed => Keys != null && Keys.rKey.wasPressedThisFrame;
        /// <summary>B / D-pad yukarı: yerleşim modunu aç-kapat (sadece sığınakta).</summary>
        public static bool BuildModeTogglePressed => BuildModeKeyPressed || BuildModePadPressed;

        public static bool BuildModeKeyPressed => !FieldControlsActive && Keys != null && Keys.bKey.wasPressedThisFrame;

        /// <summary>D-pad yukarı aynı zamanda pencerelerde menü gezinmesidir; pencere açıkken yerleşimi açmamalı.</summary>
        public static bool BuildModePadPressed => !FieldControlsActive && Pad != null && Pad.dpad.up.wasPressedThisFrame;

        /// <summary>Yerleşim modunda R / Y (Üçgen): yapıyı 90° döndür.</summary>
        public static bool RotatePlacementPressed => !FieldControlsActive &&
            ((Keys != null && Keys.rKey.wasPressedThisFrame) || (Pad != null && Pad.buttonNorth.wasPressedThisFrame));

        /// <summary>
        /// Yerleşim modunda ok tuşları / D-pad / sol çubuk: seçili ızgara hücresini bir adım kaydırır
        /// (x: sağ-sol, y: yukarı = arka şerit, aşağı = ön şerit). Gamepad'de basılı tutunca tekrarlar.
        /// </summary>
        public static Vector2Int PlacementNudge
        {
            get
            {
                if (FieldControlsActive) return Vector2Int.zero;
                if (nudgeFrame == Time.frameCount) return nudgeValue;

                nudgeFrame = Time.frameCount;
                nudgeValue = Vector2Int.zero;

                if (Keys != null)
                {
                    int x = (Keys.rightArrowKey.wasPressedThisFrame ? 1 : 0) - (Keys.leftArrowKey.wasPressedThisFrame ? 1 : 0);
                    int z = (Keys.upArrowKey.wasPressedThisFrame ? 1 : 0) - (Keys.downArrowKey.wasPressedThisFrame ? 1 : 0);
                    nudgeValue = new Vector2Int(x, z);
                }

                if (nudgeValue == Vector2Int.zero && Pad != null)
                {
                    Vector2 v = Pad.dpad.ReadValue();
                    if (v.sqrMagnitude < 0.25f) v = Pad.leftStick.ReadValue();

                    // Baskın eksen: çapraz basış tek yöne indirgenir.
                    var direction = Vector2Int.zero;
                    if (Mathf.Abs(v.x) >= Mathf.Abs(v.y)) direction.x = v.x > 0.6f ? 1 : v.x < -0.6f ? -1 : 0;
                    else direction.y = v.y > 0.6f ? 1 : v.y < -0.6f ? -1 : 0;

                    float now = Time.unscaledTime;
                    if (direction == Vector2Int.zero)
                    {
                        nudgeHeld = Vector2Int.zero;
                    }
                    else if (direction != nudgeHeld)
                    {
                        nudgeHeld = direction;
                        nudgeRepeatAt = now + 0.35f;
                        nudgeValue = direction;
                    }
                    else if (now >= nudgeRepeatAt)
                    {
                        nudgeRepeatAt = now + 0.12f;
                        nudgeValue = direction;
                    }
                }

                return nudgeValue;
            }
        }

        /// <summary>Yerleşim modunda E / Page Up / RB: bir kat yukarı, Q / Page Down / LB: bir kat aşağı.</summary>
        public static int PlacementFloorStep
        {
            get
            {
                if (FieldControlsActive) return 0;
                int up = (Keys != null && (Keys.eKey.wasPressedThisFrame || Keys.pageUpKey.wasPressedThisFrame)) ||
                         (Pad != null && Pad.rightShoulder.wasPressedThisFrame) ? 1 : 0;
                int down = (Keys != null && (Keys.qKey.wasPressedThisFrame || Keys.pageDownKey.wasPressedThisFrame)) ||
                           (Pad != null && Pad.leftShoulder.wasPressedThisFrame) ? 1 : 0;
                return up - down;
            }
        }

        /// <summary>Yerleşim modunda Tab / RT: sıradaki kuruluma hazır yapı; Shift+Tab / LT: öncekisi.</summary>
        public static int PlacementRecipeStep
        {
            get
            {
                if (FieldControlsActive) return 0;
                bool next = (Keys != null && Keys.tabKey.wasPressedThisFrame && !Keys.shiftKey.isPressed) ||
                            (Pad != null && Pad.rightTrigger.wasPressedThisFrame);
                bool previous = (Keys != null && Keys.tabKey.wasPressedThisFrame && Keys.shiftKey.isPressed) ||
                                (Pad != null && Pad.leftTrigger.wasPressedThisFrame);
                return (next ? 1 : 0) - (previous ? 1 : 0);
            }
        }

        /// <summary>Yerleşim modunda Enter / A: seçili hücreye kur / taşı (fareyle tıklamanın karşılığı).</summary>
        public static bool PlacementConfirmPressed => !FieldControlsActive &&
            ((Keys != null && (Keys.enterKey.wasPressedThisFrame || Keys.numpadEnterKey.wasPressedThisFrame)) ||
             (Pad != null && Pad.buttonSouth.wasPressedThisFrame));

        /// <summary>Yerleşim modunda X / X (Kare): kurulu yapıyı depoya kaldır.</summary>
        public static bool PlacementStorePressed => !FieldControlsActive &&
            ((Keys != null && Keys.xKey.wasPressedThisFrame) || (Pad != null && Pad.buttonWest.wasPressedThisFrame));

        /// <summary>Yerleşim modunda gamepad B (Daire): modu kapat (klavyede Esc / sağ tık / B).</summary>
        public static bool PlacementBackPressed => !FieldControlsActive && Pad != null && Pad.buttonEast.wasPressedThisFrame;

        /// <summary>Basılıyken verilen emir mevcut işleri iptal etmez, sıraya eklenir.</summary>
        public static bool QueueModifierHeld => Keys != null && Keys.shiftKey.isPressed;

        /// <summary>Manuel tulumba ritim mini-oyunu: Q/E ya da LT/RT dönüşümlü basılır.</summary>
        public static bool PumpLeftPressed => !FieldControlsActive &&
            ((Keys != null && Keys.qKey.wasPressedThisFrame) || (Pad != null && Pad.leftTrigger.wasPressedThisFrame));
        public static bool PumpRightPressed => !FieldControlsActive &&
            ((Keys != null && Keys.eKey.wasPressedThisFrame) || (Pad != null && Pad.rightTrigger.wasPressedThisFrame));

        // ---------------------------------------------------------------- Pencereler

        public static bool EscapePressed =>
            (Keys != null && Keys.escapeKey.wasPressedThisFrame) || (Pad != null && Pad.startButton.wasPressedThisFrame);

        /// <summary>H / D-pad sol.</summary>
        public static bool HelpTogglePressed =>
            (Keys != null && Keys.hKey.wasPressedThisFrame) || (Pad != null && Pad.dpad.left.wasPressedThisFrame);

        /// <summary>K / D-pad sağ.</summary>
        public static bool SkillTreeTogglePressed =>
            (Keys != null && Keys.kKey.wasPressedThisFrame) || (Pad != null && Pad.dpad.right.wasPressedThisFrame);

        /// <summary>Ekrandaki ipucunu geçer: D-pad aşağı.</summary>
        public static bool DismissHintPressed => Pad != null && Pad.dpad.down.wasPressedThisFrame;

        /// <summary>Menüde odak adımı: -1 yukarı, +1 aşağı, 0 yok. D-pad / sol çubuk / oklar / W-S; basılı tutunca tekrarlar.</summary>
        public static int MenuStep
        {
            get
            {
                if (menuFrame != Time.frameCount)
                {
                    menuFrame = Time.frameCount;
                    menuStep = ReadMenuStep();
                }
                return menuStep;
            }
        }

        /// <summary>Enter / gamepad A.</summary>
        public static bool MenuConfirmPressed =>
            (Keys != null && (Keys.enterKey.wasPressedThisFrame || Keys.numpadEnterKey.wasPressedThisFrame)) ||
            (Pad != null && Pad.buttonSouth.wasPressedThisFrame);

        /// <summary>Backspace / gamepad B.</summary>
        public static bool MenuBackPressed =>
            (Keys != null && Keys.backspaceKey.wasPressedThisFrame) || (Pad != null && Pad.buttonEast.wasPressedThisFrame);

        // ---------------------------------------------------------------- Kamera ve fare

        /// <summary>Klavye kamera kaydırması (sığınakta sadece seçili karakter yokken; varken WASD karakteri yürütür).</summary>
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

        /// <summary>Sağ çubuk: kamera kaydırma (her zaman).</summary>
        public static Vector2 CameraPanAxis
        {
            get
            {
                if (Pad == null) return Vector2.zero;
                var stick = Pad.rightStick.ReadValue();
                return stick.sqrMagnitude < 0.04f ? Vector2.zero : stick;
            }
        }

        public static bool SelectPressed => Pointer != null && Pointer.leftButton.wasPressedThisFrame;
        public static bool CommandPressed => Pointer != null && Pointer.rightButton.wasPressedThisFrame;
        public static Vector2 PointerPosition => Pointer != null ? Pointer.position.ReadValue() : Vector2.zero;
        /// <summary>Fare tekerleği; gamepad'de sağ çubuğa basmak (R3) yakınlaştırır, sol çubuğa basmak (L3) uzaklaştırır.</summary>
        public static float ScrollDelta
        {
            get
            {
                float scroll = Pointer != null ? Pointer.scroll.ReadValue().y : 0f;
                if (scroll == 0f && Pad != null)
                    scroll = (Pad.rightStickButton.wasPressedThisFrame ? 1f : 0f) - (Pad.leftStickButton.wasPressedThisFrame ? 1f : 0f);
                return scroll;
            }
        }

        // ---------------------------------------------------------------- Test

        public static bool DebugGasPressed => Keys != null && Keys.f3Key.wasPressedThisFrame;
        public static bool DebugSpawnSurvivorPressed => Keys != null && Keys.f12Key.wasPressedThisFrame;
        public static bool DebugFirePressed => Keys != null && Keys.f6Key.wasPressedThisFrame;
        public static bool DebugFloodPressed => Keys != null && Keys.f7Key.wasPressedThisFrame;
        public static bool DebugQuakePressed => Keys != null && Keys.f8Key.wasPressedThisFrame;
        public static bool DebugDamagePressed => Keys != null && Keys.f9Key.wasPressedThisFrame;
        public static bool DebugCrackPressed => Keys != null && Keys.f10Key.wasPressedThisFrame;
        /// <summary>Test: kamera eğimini 15/20/25/30 derece arasında değiştirir.</summary>
        public static bool DebugCameraPitchPressed => Keys != null && Keys.f5Key.wasPressedThisFrame;
        public static bool DebugResourcesPressed => Keys != null && Keys.f11Key.wasPressedThisFrame;

        // ---------------------------------------------------------------- Titreşim

        /// <summary>Gamepad titreşimi (low: ağır motor, high: hafif motor, 0–1). Gerçek zamanla durur.</summary>
        public static void Rumble(float low, float high, float seconds)
        {
            var pad = Pad;
            if (pad == null || !UsingGamepad) return;
            pad.SetMotorSpeeds(Mathf.Clamp01(low), Mathf.Clamp01(high));
            rumbleUntil = Mathf.Max(rumbleUntil, Time.unscaledTime + seconds);
            rumbling = true;
        }

        /// <summary>Süresi dolan titreşimi durdurur. Kamera her kare çağırır.</summary>
        public static void TickRumble()
        {
            if (!rumbling || Time.unscaledTime < rumbleUntil) return;
            StopRumble();
        }

        public static void StopRumble()
        {
            rumbling = false;
            Pad?.ResetHaptics();
        }

        // ---------------------------------------------------------------- Tuş ipuçları

        /// <summary>Ekran ipucu: son kullanılan cihaza göre aynı eylemin tuş adı.</summary>
        public static string Prompt(InputPrompt prompt)
        {
            bool pad = UsingGamepad;
            return prompt switch
            {
                InputPrompt.Move => pad ? "Sol çubuk" : "WASD",
                InputPrompt.Dodge => pad ? "B" : "Space",
                InputPrompt.Interact => pad ? "A" : "F",
                InputPrompt.Heal => pad ? "Y" : "R",
                InputPrompt.Cancel => pad ? "B" : "X",
                InputPrompt.Pause => pad ? "Y" : "Space",
                InputPrompt.Speed => pad ? "X" : "1 / 2",
                InputPrompt.SwitchCharacter => pad ? "LB / RB" : "Tab",
                InputPrompt.SkillTree => pad ? "D-pad →" : "K",
                InputPrompt.Help => pad ? "D-pad ←" : "H",
                InputPrompt.Menu => pad ? "Start" : "Esc",
                InputPrompt.Confirm => pad ? "A" : "Enter",
                InputPrompt.Back => pad ? "B" : "Backspace",
                InputPrompt.Pump => pad ? "LT / RT" : "Q / E",
                InputPrompt.Aim => pad ? "Sağ çubuk" : "Fare",
                InputPrompt.Fire => pad ? "RT" : "Sol tık",
                InputPrompt.SwitchWeapon => pad ? "X" : "Q",
                InputPrompt.Crouch => pad ? "LT" : "Shift",
                InputPrompt.Build => pad ? "D-pad ↑" : "B",
                InputPrompt.PlaceCell => pad ? "Sol çubuk / D-pad" : "Fare / ok tuşları",
                InputPrompt.PlaceFloor => pad ? "LB / RB" : "Q / E",
                InputPrompt.PlaceRecipe => pad ? "LT / RT" : "Tab",
                InputPrompt.PlaceConfirm => pad ? "A" : "Sol tık / Enter",
                InputPrompt.PlaceRotate => pad ? "Y" : "R",
                InputPrompt.PlaceStore => "X",
                InputPrompt.PlaceClose => pad ? "B" : "B / Sağ tık",
                InputPrompt.Zoom => pad ? "L3 / R3" : "Tekerlek",
                InputPrompt.Inventory => pad ? "D-pad ↑" : "I",
                InputPrompt.Retreat => pad ? "L3" : "X",
                _ => "?",
            };
        }

        // ---------------------------------------------------------------- İç işler

        static void RefreshActiveDevice()
        {
            var pad = Pad;
            if (pad != null && PadActive(pad))
            {
                usingGamepad = true;
                return;
            }

            bool keyboard = Keys != null && Keys.anyKey.wasPressedThisFrame;
            bool mouse = Pointer != null && (Pointer.delta.ReadValue().sqrMagnitude > 4f ||
                                             Pointer.leftButton.wasPressedThisFrame || Pointer.rightButton.wasPressedThisFrame);
            if (keyboard || mouse) usingGamepad = false;
        }

        static bool PadActive(Gamepad pad) =>
            pad.leftStick.ReadValue().sqrMagnitude > 0.1f || pad.rightStick.ReadValue().sqrMagnitude > 0.1f ||
            pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame ||
            pad.buttonNorth.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame ||
            pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame ||
            pad.leftShoulder.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame ||
            pad.leftTrigger.wasPressedThisFrame || pad.rightTrigger.wasPressedThisFrame ||
            pad.dpad.ReadValue().sqrMagnitude > 0.1f;

        static int ReadMenuStep()
        {
            int direction = 0;
            var pad = Pad;
            if (pad != null)
            {
                float y = pad.dpad.ReadValue().y;
                if (Mathf.Abs(y) < 0.5f) y = pad.leftStick.ReadValue().y;
                if (y > 0.6f) direction = -1;
                else if (y < -0.6f) direction = 1;
            }
            if (direction == 0 && Keys != null)
            {
                if (Keys.upArrowKey.isPressed || Keys.wKey.isPressed) direction = -1;
                else if (Keys.downArrowKey.isPressed || Keys.sKey.isPressed) direction = 1;
            }

            float now = Time.unscaledTime;
            if (direction == 0)
            {
                menuHeldDirection = 0;
                return 0;
            }
            if (direction != menuHeldDirection)
            {
                menuHeldDirection = direction;
                menuRepeatAt = now + 0.4f;
                return direction;
            }
            if (now < menuRepeatAt) return 0;

            menuRepeatAt = now + 0.15f;
            return direction;
        }

        // ref: eylem alanı EnsureActions çalıştıktan sonra okunur (değer olarak verilseydi ilk çağrıda null olurdu).
        static bool WasPressed(ref InputAction action)
        {
            EnsureActions();
            return action.WasPressedThisFrame();
        }

        // Eylemler koddan kurulur: ayrı bir .inputactions dosyası gerekmez, klavye ve gamepad aynı eyleme bağlanır.
        static void EnsureActions()
        {
            if (characterMove != null) return;

            characterMove = new InputAction("CharacterMove", InputActionType.Value, expectedControlType: "Vector2");
            characterMove.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            characterMove.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            characterMove.AddBinding("<Gamepad>/leftStick");

            interact = Button("Interact", "<Keyboard>/f", "<Gamepad>/buttonSouth");
            dodge = Button("Dodge", "<Keyboard>/space", "<Gamepad>/buttonEast");
            heal = Button("Heal", "<Keyboard>/r", "<Gamepad>/buttonNorth");
            retreat = Button("Retreat", "<Keyboard>/x", "<Gamepad>/leftStickPress");

            characterMove.Enable();
            interact.Enable();
            dodge.Enable();
            heal.Enable();
            retreat.Enable();
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

    public enum InputPrompt
    {
        Move, Dodge, Interact, Heal, Cancel, Pause, Speed, SwitchCharacter,
        SkillTree, Help, Menu, Confirm, Back, Pump, Aim, Fire, SwitchWeapon, Crouch,
        Build, PlaceCell, PlaceFloor, PlaceRecipe, PlaceConfirm, PlaceRotate, PlaceStore, PlaceClose, Zoom, Inventory, Retreat,
    }
}
