using System;
using HarmonyLib;
using StarterAssets;
using InstantHorror.Scripts.InteractionSystems;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Disappearance.Comfort
{
    internal sealed class SpawnGrounding : IDisposable
    {
        private readonly Harmony patch = new Harmony(ComfortPlugin.PluginId + ".spawngrounding");
        private static bool shown, hidden, moved;
        internal SpawnGrounding()
        {
            Reset();
            try
            {
                patch.Patch(AccessTools.Method(typeof(TextEventUIHandler), "Show"), postfix: new HarmonyMethod(typeof(SpawnGrounding), nameof(Show)));
                patch.Patch(AccessTools.Method(typeof(TextEventUIHandler), "Hide"), postfix: new HarmonyMethod(typeof(SpawnGrounding), nameof(Hide)));
                var after = new HarmonyMethod(typeof(SpawnGrounding), nameof(Move)) { after = new[] { "local.disappearance.gameplayqol.sprint" } };
                patch.Patch(AccessTools.Method(typeof(FirstPersonController), "Move"), postfix: after);
            }
            catch { patch.UnpatchSelf(); throw; }
        }
        internal void Reset() { shown = hidden = moved = false; }
        private static void Show()
        {
            if (shown || SceneManager.GetActiveScene().name != "VillageScene") return;
            shown = true;
            var player = UnityEngine.Object.FindObjectOfType<FirstPersonController>();
            var controller = player == null ? null : player.GetComponent<CharacterController>();
            if (controller == null || !controller.enabled || controller.gameObject.scene != SceneManager.GetActiveScene()) { ComfortPlugin.Log.LogWarning("Spawn grounding skipped: current controller unavailable."); return; }
            Vector3 before = controller.transform.position;
            controller.Move(Vector3.down * .1f);
            ComfortPlugin.Log.LogInfo("Spawn grounded " + before.ToString("F4") + " -> " + controller.transform.position.ToString("F4"));
        }
        private static void Hide() { if (!shown || hidden) return; hidden = true; Log("subtitle hidden"); }
        private static void Move() { if (!hidden || moved) return; moved = true; Log("first Move after subtitle"); }
        private static void Log(string when) { var player = UnityEngine.Object.FindObjectOfType<FirstPersonController>(); ComfortPlugin.Log.LogInfo(when + " player=" + (player == null ? "missing" : player.transform.position.ToString("F4"))); }
        public void Dispose() => patch.UnpatchSelf();
    }
}
