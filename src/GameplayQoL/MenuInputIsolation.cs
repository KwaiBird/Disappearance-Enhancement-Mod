using HarmonyLib;
using UnityEngine;

namespace Disappearance.GameplayQoL
{
    internal sealed class MenuInputIsolation : MonoBehaviour
    {
        private Harmony harmony;
        private static int blockedThrough = -1;
        internal static void Transition() => blockedThrough = Time.frameCount + 1;
        private void Awake()
        {
            harmony = new Harmony(GameplayQoLPlugin.PluginId + ".menu-isolation");
            foreach (string method in new[] { "Pause", "Up", "Down", "Left", "Right", "Decision" })
                harmony.Patch(AccessTools.Method(typeof(PauseMenuHandler), method),
                    prefix: new HarmonyMethod(typeof(MenuInputIsolation), method == "Pause" ? nameof(Allow) : nameof(AllowNavigation)) { priority = Priority.First });
        }
        private static bool Allow(PauseMenuHandler __instance)
        {
            return GameplayQoLPlugin.Instance?.Modal.AllowsMenuInput(
                __instance.CurrentPauseStatus == PauseMenuHandler.PauseStatus.Paused,
                true, Time.frameCount <= blockedThrough) == true;
        }
        private static bool AllowNavigation(PauseMenuHandler __instance) =>
            GameplayQoLPlugin.Instance?.Modal.AllowsMenuInput(
                __instance.CurrentPauseStatus == PauseMenuHandler.PauseStatus.Paused,
                false, Time.frameCount <= blockedThrough) == true;
        private void OnDestroy() => harmony?.UnpatchSelf();
    }
}
