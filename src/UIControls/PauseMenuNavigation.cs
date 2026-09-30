using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Disappearance.UIControls
{
    // Return through the native transition, including Close/Open and selection restoration.
    internal sealed class PauseMenuNavigation : IDisposable
    {
        private static PauseMenuNavigation current;
        private readonly Harmony patch = new Harmony(UIControlsPlugin.PluginId + ".pause-return");
        private int returnedFrame = -1;
        internal PauseMenuNavigation()
        {
            current = this;
            patch.Patch(AccessTools.Method(typeof(PauseMenuHandler), "Resume"),
                prefix: new HarmonyMethod(typeof(PauseMenuNavigation), nameof(BeforeResume)));
            patch.Patch(AccessTools.Method(typeof(PauseMenuUIHandler), "Open"),
                postfix: new HarmonyMethod(typeof(PauseMenuNavigation), nameof(Opened)));
        }
        private static void Opened() { if (current != null) current.returnedFrame = Time.frameCount; }
        private static bool BeforeResume(PauseMenuHandler __instance)
        {
            if (current == null) return true;
            if (current.returnedFrame == Time.frameCount) return false;
            return !current.Return(__instance);
        }
        private bool Return(PauseMenuHandler menu)
        {
            if (menu == null || menu.CurrentPauseStatus != PauseMenuHandler.PauseStatus.Paused ||
                menu.SelectedMode == PauseMenuHandler.Mode.PauseMenu) return false;
            if (menu.SelectedMode == PauseMenuHandler.Mode.Setting)
            {
                var ui = (SettingMenuUIHandler)AccessTools.Field(typeof(PauseMenuHandler), "settingMenuUIHandler").GetValue(menu);
                AccessTools.Method(typeof(SettingMenuUIHandler), "SelectMenu").Invoke(ui, new object[] { SettingMenuUIHandler.SettingMenu.Back });
                AccessTools.Method(typeof(PauseMenuHandler), "DecisionOnSettingMenu").Invoke(menu, null);
            }
            else
            {
                var ui = (BackToTitleMenuUIHandler)AccessTools.Field(typeof(PauseMenuHandler), "backToTitleMenuUIHandler").GetValue(menu);
                AccessTools.Method(typeof(BackToTitleMenuUIHandler), "SelectMenu").Invoke(ui, new object[] { BackToTitleMenuUIHandler.YesNo.No });
                AccessTools.Method(typeof(PauseMenuHandler), "DecisionOnBackToTitleMenu").Invoke(menu, null);
            }
            returnedFrame = Time.frameCount;
            return true;
        }
        internal void Update(PauseMenuHandler menu, bool modal)
        {
            if (modal || returnedFrame == Time.frameCount) return;
            var k = Keyboard.current; var p = Gamepad.current;
            if (k != null && (k.escapeKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame) ||
                p != null && p.buttonEast.wasPressedThisFrame)
            {
                if (menu != null && menu.CurrentPauseStatus == PauseMenuHandler.PauseStatus.Paused && menu.SelectedMode == PauseMenuHandler.Mode.PauseMenu)
                {
                    // Esc/Start already belong to the native Pause action; do not toggle twice.
                    if (k != null && k.backspaceKey.wasPressedThisFrame || p != null && p.buttonEast.wasPressedThisFrame) menu.Resume();
                }
                else Return(menu);
            }
        }
        public void Dispose() { if (current == this) current = null; patch.UnpatchSelf(); }
    }
}
