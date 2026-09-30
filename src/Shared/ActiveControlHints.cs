using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.Switch;
using UnityEngine.InputSystem.XInput;

namespace Disappearance.Shared
{
    // Last meaningful input wins. Idle frames retain the currently shown device.
    internal sealed class ActiveControlHints
    {
        private Vector2 previousLeftStick;
        private Vector2 previousRightStick;
        private int observedGamepadId = Gamepad.current != null ? Gamepad.current.deviceId : 0;
        internal bool GamepadActive { get; private set; }

        internal void PreferConnectedGamepad()
        {
            GamepadActive = Gamepad.current != null;
            observedGamepadId = Gamepad.current != null ? Gamepad.current.deviceId : 0;
            previousLeftStick = Vector2.zero;
            previousRightStick = Vector2.zero;
        }

        internal bool Update(bool allowMouseMotion = true)
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            Gamepad pad = Gamepad.current;
            int currentGamepadId = pad != null ? pad.deviceId : 0;
            bool newlyConnected = currentGamepadId != 0 && currentGamepadId != observedGamepadId;
            observedGamepadId = currentGamepadId;
            bool keyboardInput = keyboard != null && keyboard.anyKey.wasPressedThisFrame;
            if (mouse != null)
                keyboardInput |= mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame ||
                    mouse.scroll.ReadValue().sqrMagnitude > 0f ||
                    allowMouseMotion && mouse.delta.ReadValue().sqrMagnitude > 16f;
            bool padInput = false;
            if (pad != null)
            {
                Vector2 leftStick = pad.leftStick.ReadValue();
                bool stickMoved = leftStick.sqrMagnitude > 0.3f &&
                    (leftStick - previousLeftStick).sqrMagnitude > 0.04f;
                previousLeftStick = leftStick;
                Vector2 rightStick = pad.rightStick.ReadValue();
                bool lookMoved = rightStick.sqrMagnitude > 0.3f &&
                    (rightStick - previousRightStick).sqrMagnitude > 0.04f;
                previousRightStick = rightStick;
                padInput = pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame ||
                    pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame ||
                    pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame ||
                    pad.buttonWest.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame ||
                    pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame ||
                    pad.leftShoulder.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame ||
                    pad.leftTrigger.wasPressedThisFrame || pad.rightTrigger.wasPressedThisFrame ||
                    pad.leftStickButton.wasPressedThisFrame || pad.rightStickButton.wasPressedThisFrame ||
                    stickMoved || lookMoved;
            }
            else { previousLeftStick = Vector2.zero; previousRightStick = Vector2.zero; }
            if (keyboardInput != padInput) GamepadActive = padInput;
            if (newlyConnected) GamepadActive = true;
            if (pad == null) GamepadActive = false;
            return GamepadActive;
        }

        internal static string SouthButton(Gamepad pad) => ControllerGlyphSettings.Face(pad, 0);
        internal static string EastButton(Gamepad pad) => ControllerGlyphSettings.Face(pad, 1);
        internal static string NorthButton(Gamepad pad) => ControllerGlyphSettings.Face(pad, 2);
        internal static string WestButton(Gamepad pad) => ControllerGlyphSettings.Face(pad, 3);
    }

    internal enum ControllerLayout { Auto, Xbox, Switch, PlayStation, Unknown }

    // PlayerPrefs is shared across the separately built hint plugins within this game.
    internal static class ControllerGlyphSettings
    {
        private const string PreferenceKey = "local.disappearance.controllerGlyphLayout";
        private static readonly string[] XboxFaces = { "A", "B", "Y", "X" };
        private static readonly string[] SwitchFaces = { "B", "A", "X", "Y" };
        private static readonly string[] PlayStationFaces = { "×", "○", "△", "□" };

        internal static ControllerLayout Mode
        {
            get
            {
                int stored = PlayerPrefs.GetInt(PreferenceKey, 0);
                return stored >= 0 && stored <= 3 ? (ControllerLayout)stored : ControllerLayout.Auto;
            }
        }

        internal static void SetMode(ControllerLayout mode)
        {
            if (mode < ControllerLayout.Auto || mode > ControllerLayout.PlayStation) return;
            PlayerPrefs.SetInt(PreferenceKey, (int)mode);
            PlayerPrefs.Save();
        }

        internal static ControllerLayout Detect(Gamepad pad)
        {
            if (pad == null) return ControllerLayout.Unknown;
            if (pad is DualShockGamepad) return ControllerLayout.PlayStation;
            if (pad is SwitchProControllerHID) return ControllerLayout.Switch;
            if (pad is XInputController) return ControllerLayout.Xbox;
            string name = ((pad.description.manufacturer ?? "") + " " +
                (pad.description.product ?? "")).ToLowerInvariant();
            if (name.Contains("sony") || name.Contains("dualshock") || name.Contains("dualsense") ||
                name.Contains("playstation")) return ControllerLayout.PlayStation;
            if (name.Contains("nintendo") || name.Contains("switch pro")) return ControllerLayout.Switch;
            if (name.Contains("xbox") || name.Contains("microsoft")) return ControllerLayout.Xbox;
            return ControllerLayout.Unknown;
        }

        internal static ControllerLayout Resolve(Gamepad pad)
        {
            ControllerLayout mode = Mode;
            if (mode != ControllerLayout.Auto) return mode;
            ControllerLayout detected = Detect(pad);
            return detected == ControllerLayout.Unknown ? ControllerLayout.Xbox : detected;
        }

        internal static string Face(Gamepad pad, int position)
        {
            switch (Resolve(pad))
            {
                case ControllerLayout.PlayStation:
                    return PlayStationFaces[position];
                case ControllerLayout.Switch:
                    return SwitchFaces[position];
                default:
                    return XboxFaces[position];
            }
        }

        internal static string FaceName(Gamepad pad, int position) => Face(pad, position) + "ボタン";

        internal static string ModeName(ControllerLayout mode)
        {
            switch (mode)
            {
                case ControllerLayout.Xbox: return "Xbox型";
                case ControllerLayout.Switch: return "Switch型";
                case ControllerLayout.PlayStation: return "PS型";
                default: return "自動";
            }
        }

        internal static string MenuButton(Gamepad pad)
        {
            switch (Resolve(pad))
            {
                case ControllerLayout.PlayStation: return "OPTIONS";
                case ControllerLayout.Switch: return "+";
                default: return "START";
            }
        }
    }
}
