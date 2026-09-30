using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Disappearance.UIControls
{
    internal sealed class MenuBindingsModule
    {
        private sealed class AddedBinding
        {
            internal InputAction Action;
            internal Guid Id;
        }

        private readonly ManualLogSource logger;
        private readonly Dictionary<InputActionAsset, List<AddedBinding>> configured =
            new Dictionary<InputActionAsset, List<AddedBinding>>();
        private float nextScanTime;

        internal MenuBindingsModule(ManualLogSource logger)
        {
            this.logger = logger;
        }

        internal void Update()
        {
            if (Time.unscaledTime < nextScanTime) return;
            nextScanTime = Time.unscaledTime + 0.25f;
            foreach (PlayerInput playerInput in Resources.FindObjectsOfTypeAll<PlayerInput>())
            {
                if (playerInput == null || !playerInput.gameObject.scene.isLoaded) continue;
                InputActionAsset asset = playerInput.actions;
                if (asset == null || configured.ContainsKey(asset)) continue;
                var added = new List<AddedBinding>();
                try
                {
                    configured.Add(asset, added);
                    AddDpad(asset.FindActionMap("TitleScene", false),
                        new[] { "Up", "Down" }, new[] { "up", "down" }, added);
                    AddKeyboardArrows(asset.FindActionMap("TitleScene", false),
                        new[] { "Up", "Down" }, new[] { "upArrow", "downArrow" }, added);
                    AddDpad(asset.FindActionMap("Pause", false),
                        new[] { "PauseMenuUp", "PauseMenuDown", "PauseMenuLeft", "PauseMenuRight" },
                        new[] { "up", "down", "left", "right" }, added);
                    AddKeyboardArrows(asset.FindActionMap("Pause", false),
                        new[] { "PauseMenuUp", "PauseMenuDown", "PauseMenuLeft", "PauseMenuRight" },
                        new[] { "upArrow", "downArrow", "leftArrow", "rightArrow" }, added);
                    AddConfirm(asset, "Player", "Interact", added);
                    AddConfirm(asset, "Pause", "PauseMenuDecision", added);
                    AddConfirm(asset, "TitleScene", "Interact", added);
                    AddConfirm(asset, "TextScene", "Interact", added);
                    if (added.Count > 0)
                        logger.LogInfo("Added " + added.Count + " menu bindings to " + playerInput.name + ".");
                }
                catch (Exception error)
                {
                    RemoveAdded(asset, added);
                    configured.Remove(asset);
                    nextScanTime = Time.unscaledTime + 2f;
                    logger.LogError("Menu bindings could not be added: " + error);
                }
            }
        }

        private static void AddConfirm(InputActionAsset asset, string mapName, string actionName,
            List<AddedBinding> added)
        {
            InputAction action = asset.FindActionMap(mapName, false)?.FindAction(actionName, false);
            if (action == null) return;
            Add(action, "<Keyboard>/enter", "KeyboardMouse", added);
            Add(action, "<Keyboard>/numpadEnter", "KeyboardMouse", added);
        }

        private static void AddDpad(InputActionMap map, string[] actionNames, string[] directions,
            List<AddedBinding> added)
        {
            if (map == null) return;
            for (int i = 0; i < actionNames.Length; i++)
            {
                InputAction action = map.FindAction(actionNames[i], false);
                if (action != null)
                    Add(action, "<Gamepad>/dpad/" + directions[i], "Gamepad", added);
            }
        }

        private static void AddKeyboardArrows(InputActionMap map, string[] actionNames, string[] keys,
            List<AddedBinding> added)
        {
            if (map == null) return;
            for (int i = 0; i < actionNames.Length; i++)
            {
                InputAction action = map.FindAction(actionNames[i], false);
                if (action != null) Add(action, "<Keyboard>/" + keys[i], "KeyboardMouse", added);
            }
        }

        private static void Add(InputAction action, string path, string group, List<AddedBinding> added)
        {
            foreach (InputBinding binding in action.bindings)
                if (string.Equals(binding.path, path, StringComparison.OrdinalIgnoreCase)) return;

            bool wasEnabled = action.enabled;
            int originalCount = action.bindings.Count;
            try
            {
                if (wasEnabled) action.Disable();
                action.AddBinding(path).WithGroup(group);
                int last = action.bindings.Count - 1;
                Guid id = action.bindings[last].id;
                if (id == Guid.Empty)
                    throw new InvalidOperationException("Added binding has no ID: " + path);
                added.Add(new AddedBinding { Action = action, Id = id });
            }
            catch
            {
                if (action.bindings.Count > originalCount)
                    action.ChangeBinding(originalCount).Erase();
                throw;
            }
            finally
            {
                if (wasEnabled) action.Enable();
            }
        }

        internal void OnSceneChanged()
        {
            Stop();
            nextScanTime = 0f;
        }

        internal void Stop()
        {
            foreach (KeyValuePair<InputActionAsset, List<AddedBinding>> pair in configured)
                RemoveAdded(pair.Key, pair.Value);
            configured.Clear();
        }

        private void RemoveAdded(InputActionAsset asset, List<AddedBinding> added)
        {
            if (asset == null) return;
            for (int i = added.Count - 1; i >= 0; i--)
            {
                InputAction action = added[i].Action;
                if (action == null) continue;
                try
                {
                    for (int index = action.bindings.Count - 1; index >= 0; index--)
                    {
                        if (action.bindings[index].id != added[i].Id) continue;
                        bool wasEnabled = action.enabled;
                        try
                        {
                            if (wasEnabled) action.Disable();
                            action.ChangeBinding(index).Erase();
                        }
                        finally
                        {
                            if (wasEnabled) action.Enable();
                        }
                        break;
                    }
                }
                catch (Exception error)
                {
                    logger.LogError("Could not remove owned menu binding " + added[i].Id + ": " + error);
                }
            }
        }
    }
}
