using UnityEngine.InputSystem;

namespace Disappearance.GameplayQoL
{
    internal static class CheckpointShortcut
    {
        internal static bool PadChordHeld(Gamepad pad)
        {
            return pad != null && pad.selectButton.isPressed &&
                pad.leftShoulder.isPressed && pad.rightShoulder.isPressed;
        }

        internal static bool PadChordCompletedThisFrame(Gamepad pad)
        {
            return PadChordHeld(pad) &&
                (pad.selectButton.wasPressedThisFrame || pad.leftShoulder.wasPressedThisFrame ||
                 pad.rightShoulder.wasPressedThisFrame || pad.startButton.wasPressedThisFrame);
        }

        internal static bool KeyboardChordCompletedThisFrame(Keyboard keyboard)
        {
            return keyboard != null && keyboard.f2Key.wasPressedThisFrame &&
                (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
        }
    }
}
