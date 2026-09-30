using Disappearance.Shared;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Disappearance.UIControls
{
    internal static class ControlGlyphRenderer
    {
        internal static float Width(float size, string path)
        {
            string label = Label(path, path.StartsWith("<Gamepad>"));
            if (path.Contains("Stick") || path.Contains("/dpad")) return size;
            return ControlGlyphs.WidthFor(label, path.StartsWith("<Gamepad>"), size);
        }

        internal static float DrawControl(Rect rect, string path, bool controller)
        {
            string label = Label(path, controller);
            if (path == "<Gamepad>/leftStick" || path == "<Gamepad>/rightStick")
            {
                ControlHintGUI.Stick(rect, path.Contains("left") ? "L" : "R");
                return rect.width;
            }
            if (path == "<Gamepad>/leftStickPress")
            {
                ControlHintGUI.Stick(rect, "L3");
                return rect.width;
            }
            if (path == "<Gamepad>/dpad" || path.Contains("/dpad/"))
            {
                ControlHintGUI.Dpad(rect);
                return rect.width;
            }
            float width = Width(rect.width, path);
            ControlHintGUI.Button(new Rect(rect.x, rect.y, width, rect.height), label, controller);
            return width;
        }

        internal static string Label(string path, bool controller)
        {
            string label = ControlLabel(path);
            if (controller && path == "<Gamepad>/buttonSouth")
                label = ActiveControlHints.SouthButton(Gamepad.current);
            else if (controller && path == "<Gamepad>/buttonEast")
                label = ActiveControlHints.EastButton(Gamepad.current);
            else if (controller && path == "<Gamepad>/buttonNorth")
                label = ActiveControlHints.NorthButton(Gamepad.current);
            else if (controller && path == "<Gamepad>/buttonWest")
                label = ActiveControlHints.WestButton(Gamepad.current);
            else if (controller && path == "<Gamepad>/start")
                label = ControllerGlyphSettings.MenuButton(Gamepad.current);
            else if (controller && path == "<Gamepad>/leftStick") label = "L";
            else if (controller && path == "<Gamepad>/rightStick") label = "R";
            else if (controller && path == "<Gamepad>/leftStickPress") label = "L3";
            return label;
        }

        private static string ControlLabel(string path)
        {
            if (path == "<Gamepad>/dpad" || path.Contains("<Gamepad>/dpad/")) return "Dpad";
            int slash = path.LastIndexOf('/');
            string value = slash >= 0 ? path.Substring(slash + 1) : path;
            switch (value)
            {
                case "delta": return "Mouse";
                case "upArrow": return "↑";
                case "downArrow": return "↓";
                case "leftArrow": return "←";
                case "rightArrow": return "→";
                case "numpadEnter": return "NumEnter";
                case "enter": return "Enter";
                case "space": return "Space";
                case "leftShoulder": return "LB";
                case "rightShoulder": return "RB";
                case "select": return "SELECT";
                case "escape": return "Esc";
                case "backspace": return "Back";
                case "leftShift": return "Shift";
                case "ctrl":
                case "leftCtrl":
                case "rightCtrl": return "Ctrl";
                case "f2": return "F2";
                default: return value.Length == 1 ? value.ToUpperInvariant() : value;
            }
        }
    }
}
