using UnityEngine.InputSystem;

namespace Disappearance.GameplayQoL
{
    internal static class UnlockShortcut
    {
        internal const string KeyboardBinding = "<Keyboard>/ctrl+<Keyboard>/shift+<Keyboard>/u";
        internal const string GamepadBinding = "<Gamepad>/leftShoulder+<Gamepad>/rightShoulder+<Gamepad>/buttonNorth";
        // U/Y completes the chord; modifier edges cannot also confirm a row.
        internal static bool Completed(Keyboard key, Gamepad pad) =>
            key != null && (key.leftCtrlKey.isPressed || key.rightCtrlKey.isPressed) &&
            (key.leftShiftKey.isPressed || key.rightShiftKey.isPressed) && key.uKey.wasPressedThisFrame ||
            pad != null && pad.leftShoulder.isPressed && pad.rightShoulder.isPressed && pad.buttonNorth.wasPressedThisFrame;
    }
}
