using System;
using System.Reflection;
using HarmonyLib;
using InstantHorror.Scripts;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using GamePlayerInput = InstantHorror.Scripts.PlayerInput;

namespace Disappearance.GameplayQoL
{
    internal sealed class SprintModule : MonoBehaviour
    {
        private const string VillageScene = "VillageScene";
        private static readonly PropertyInfo GameStateProperty = typeof(GamePlayerInput).GetProperty(
            "GameStateHandler", BindingFlags.Instance | BindingFlags.NonPublic);

        private static SprintModule instance;
        private static PlayerSpawnHandler spawnHandler;
        private static GamePlayerInput playerInput;
        private bool sprintToggled;
        internal bool Toggled => sprintToggled;
        private int lastToggleFrame = -1;
        private Harmony harmony;

        private struct SprintState
        {
            internal StarterAssetsInputs Input;
            internal bool OriginalSprint;
        }

        private void Awake()
        {
            instance = this;
            SceneManager.sceneLoaded += OnSceneLoaded;
            harmony = new Harmony(GameplayQoLPlugin.PluginId + ".sprint");
            try
            {
                harmony.Patch(AccessTools.Method(typeof(FirstPersonController), "Move"),
                    prefix: new HarmonyMethod(typeof(SprintModule), nameof(BeforeMove)),
                    postfix: new HarmonyMethod(typeof(SprintModule), nameof(AfterMove)),
                    finalizer: new HarmonyMethod(typeof(SprintModule), nameof(FinalizeMove)));
                GameplayQoLPlugin.Log.LogInfo("Sprint module ready: LB hold, V/Y toggle; starts OFF.");
            }
            catch (Exception exception)
            {
                harmony.UnpatchSelf();
                harmony = null;
                GameplayQoLPlugin.Log.LogError("Could not patch player movement: " + exception);
            }
        }

        private static void BeforeMove(FirstPersonController __instance, out SprintState __state)
        {
            __state = default;
            SprintModule module = instance;
            if (module == null || module.harmony == null ||
                SceneManager.GetActiveScene().name != VillageScene ||
                GameplayQoLPlugin.Instance == null || GameplayQoLPlugin.Instance.Modal.IsOccupied)
                return;

            if (spawnHandler == null) spawnHandler = FindObjectOfType<PlayerSpawnHandler>();
            if (spawnHandler == null || !spawnHandler.CanMove) return;
            if (playerInput == null) playerInput = FindObjectOfType<GamePlayerInput>();
            GameStateHandler gameState = playerInput == null || GameStateProperty == null ? null :
                GameStateProperty.GetValue(playerInput) as GameStateHandler;
            if (gameState == null || gameState.CurrentState.Value != GameState.PlayGame) return;

            StarterAssetsInputs input = __instance.GetComponent<StarterAssetsInputs>();
            if (input == null) return;
            __state.Input = input;
            __state.OriginalSprint = input.sprint;

            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;
            bool togglePressed = keyboard != null && keyboard.vKey.wasPressedThisFrame ||
                gamepad != null && gamepad.buttonNorth.wasPressedThisFrame;
            if (togglePressed && module.lastToggleFrame != Time.frameCount)
            {
                module.sprintToggled = !module.sprintToggled;
                module.lastToggleFrame = Time.frameCount;
            }
            input.sprint = __state.OriginalSprint || module.sprintToggled ||
                gamepad != null && gamepad.leftShoulder.isPressed;
        }

        private static void AfterMove(ref SprintState __state)
        {
            Restore(ref __state);
        }

        private static Exception FinalizeMove(Exception __exception, ref SprintState __state)
        {
            Restore(ref __state);
            return __exception;
        }

        private static void Restore(ref SprintState state)
        {
            StarterAssetsInputs input = state.Input;
            state.Input = null;
            if (input != null) input.sprint = state.OriginalSprint;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            lastToggleFrame = -1;
            spawnHandler = null;
            playerInput = null;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            harmony?.UnpatchSelf();
            harmony = null;
            if (instance == this) instance = null;
            spawnHandler = null;
            playerInput = null;
        }
    }
}
