using UnityEngine.InputSystem;

namespace Disappearance.Shared
{
    internal static class ConfirmInput
    {
        internal static bool WasPressedThisFrame(Keyboard keyboard, Gamepad gamepad)
        {
            return keyboard != null && (keyboard.eKey.wasPressedThisFrame ||
                keyboard.enterKey.wasPressedThisFrame) ||
                gamepad != null && gamepad.buttonSouth.wasPressedThisFrame;
        }
    }
}
