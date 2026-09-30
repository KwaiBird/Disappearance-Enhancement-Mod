using System.Collections.Generic;
using Disappearance.Shared;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Disappearance.UIControls
{
    internal sealed class PauseMenuHints
    {
        private readonly ActiveControlHints controls;
        private readonly ActionRegistry registry;
        private readonly GameFadeVisibility fade;
        internal string LastLabels { get; private set; }
        internal float LastAlpha { get; private set; }
        internal Rect LastRect { get; private set; }
        internal PauseMenuHints(ActiveControlHints controls, ActionRegistry registry, GameFadeVisibility fade) { this.controls = controls; this.registry = registry; this.fade = fade; }
        internal void Draw(PauseMenuHandler menu, bool modal, SettingsPresentation settings)
        {
            LastLabels = "";
            float alpha = fade.Alpha;
            LastAlpha = alpha;
            if (alpha <= 0f) return;
            bool pad = controls.GamepadActive && Gamepad.current != null;
            var rows = new List<ContextHintRenderer.VisibleRow>();
            if (modal)
            {
                foreach (var action in registry.VisibleEmbeddedActions("checkpoint"))
                {
                    var bindings = pad ? action.Definition.Gamepad : action.Definition.KeyboardMouse;
                    if (bindings.Length > 0) rows.Add(new ContextHintRenderer.VisibleRow { Action=action, Binding=bindings[0], Text=action.Definition.Label, Alpha=(float)action.State.Alpha });
                }
                if (rows.Count == 0) return; // A padlock or another feature owns its own instructions.
            }
            else
            {
                if (menu == null || menu.CurrentPauseStatus != PauseMenuHandler.PauseStatus.Paused) return;
                bool setting = menu.SelectedMode == PauseMenuHandler.Mode.Setting;
                rows.Add(Row("選択", pad, setting ? "w|s" : "a|d", "leftStick|dpad"));
                if (setting && settings != null && settings.SelectedIndex < 5)
                {
                    rows.Add(Row("調整", pad, "a|d", "leftStick|dpad"));
                    if (settings.SelectedIndex < 4) rows.Add(Row("初期値に戻す", pad, "r", "buttonNorth"));
                }
                else rows.Add(Row("決定", pad, "e|enter", "buttonSouth"));
                rows.Add(Row(menu.SelectedMode == PauseMenuHandler.Mode.PauseMenu ? "ゲームに戻る" : "戻る",
                    pad, "escape|backspace", menu.SelectedMode == PauseMenuHandler.Mode.PauseMenu ? "start|buttonEast" : "buttonEast"));
            }
            float s = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            var label = new GUIStyle(GUI.skin.label) { fontSize=Mathf.RoundToInt(22*s), alignment=TextAnchor.MiddleLeft };
            JapaneseGuiFont.Apply(label); label.normal.textColor = new Color(.87f,.93f,1);
            float width = Measure(rows, label, s);
            // Keep one line within the screen, with the same proportions for keys, text and gaps.
            if (width > Screen.width - 48*s) { s *= (Screen.width - 48*s) / width; label.fontSize=Mathf.RoundToInt(22*s); width=Measure(rows,label,s); }
            var separator = new GUIStyle(label) { alignment=TextAnchor.MiddleCenter };
            float x=24*s, y=Screen.height-64*s;
            LastRect = new Rect(x,y,width,48*s);
            var labels = new List<string>();
            foreach (var entry in rows)
            {
                var row = entry; row.Alpha *= alpha;
                float w=ContextHintRenderer.MeasureRow(row.Binding,row.Text,label,40*s,10*s,s);
                ContextHintRenderer.DrawRow(new Rect(x,y,w,48*s),row,label,separator,s);
                x+=w+12*s; labels.Add(row.Text);
            }
            LastLabels=string.Join(" / ",labels);
        }
        private static float Measure(List<ContextHintRenderer.VisibleRow> rows, GUIStyle label, float s)
        {
            float w=0; foreach(var row in rows) w+=ContextHintRenderer.MeasureRow(row.Binding,row.Text,label,40*s,10*s,s)+12*s;
            return w;
        }
        private static ContextHintRenderer.VisibleRow Row(string text, bool pad, string keys, string buttons)
        {
            string prefix=pad ? "<Gamepad>/" : "<Keyboard>/";
            string[] paths=(pad ? buttons : keys).Split('|');
            for(int i=0;i<paths.Length;i++) paths[i]=prefix+paths[i];
            return new ContextHintRenderer.VisibleRow { Action=new RegisteredAction { State=new ActionState(new DisplayState { Enabled=true }) }, Binding=new ActionBinding { Mode="alternatives",Controls=paths },Text=text,Alpha=1 };
        }
        internal static bool ShouldDraw(PauseMenuHandler menu, bool modal) => !modal && menu != null && menu.CurrentPauseStatus == PauseMenuHandler.PauseStatus.Paused && menu.SelectedMode == PauseMenuHandler.Mode.PauseMenu;
    }
}
