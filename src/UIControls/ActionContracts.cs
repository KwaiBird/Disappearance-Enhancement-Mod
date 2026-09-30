using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Disappearance.UIControls
{
    internal sealed class ActionBinding
    {
        internal string Mode;
        internal string[] Controls;
    }

    internal sealed class ActionDefinition
    {
        internal string OwnerId;
        internal string ActionId;
        internal string Label;
        internal int Order;
        internal ActionBinding[] KeyboardMouse;
        internal ActionBinding[] Gamepad;
        internal double HoldSeconds;
        internal string[] Contexts;
        internal string[] TableContexts;
        internal string Presentation;
        internal string TableMerge;
    }

    internal class DisplayState
    {
        internal string OwnerId;
        internal string ActionId;
        internal int SceneEpoch;
        internal int Sequence;
        internal string ContextId;
        internal bool Visible;
        internal bool Enabled;
        internal double Alpha;
        internal float ReceivedAt;

        internal DisplayState() { }
        protected DisplayState(DisplayState source)
        {
            OwnerId = source.OwnerId; ActionId = source.ActionId; SceneEpoch = source.SceneEpoch;
            Sequence = source.Sequence; ContextId = source.ContextId; Visible = source.Visible;
            Enabled = source.Enabled; Alpha = source.Alpha;
        }

        protected bool SameDisplay(DisplayState other) => other != null &&
            SceneEpoch == other.SceneEpoch && ContextId == other.ContextId &&
            Visible == other.Visible && Enabled == other.Enabled && Alpha == other.Alpha;
    }

    // A help row cannot carry an object/selection position.
    internal sealed class ActionState : DisplayState
    {
        internal bool HasProgress;
        internal double Progress;
        internal bool HasToggled;
        internal bool Toggled;

        internal ActionState(DisplayState source) : base(source) { }
        internal bool SameContent(ActionState other)
        {
            if (!SameDisplay(other) ||
                HasProgress != other.HasProgress || HasProgress && Progress != other.Progress ||
                HasToggled != other.HasToggled || HasToggled && Toggled != other.Toggled) return false;
            return true;
        }
    }

    internal static class ActionContractParser
    {
        private static readonly Regex Identifier = new Regex("^[A-Za-z0-9._-]+$",
            RegexOptions.CultureInvariant);
        private static readonly HashSet<string> Contexts = Set("title", "village", "novel_opening",
            "novel_ending", "conversation", "text_event", "padlock", "checkpoint");
        private static readonly HashSet<string> TableContexts = Set("pause", "loading");
        private static readonly HashSet<string> Controls = Set(
            "<Keyboard>/w", "<Keyboard>/a", "<Keyboard>/s", "<Keyboard>/d",
            "<Keyboard>/upArrow", "<Keyboard>/downArrow", "<Keyboard>/leftArrow",
            "<Keyboard>/rightArrow", "<Keyboard>/e", "<Keyboard>/enter",
            "<Keyboard>/numpadEnter", "<Keyboard>/escape", "<Keyboard>/backspace",
            "<Keyboard>/space", "<Keyboard>/v", "<Keyboard>/g", "<Keyboard>/ctrl",
            "<Keyboard>/h", "<Keyboard>/u",
            "<Keyboard>/shift", "<Keyboard>/leftShift", "<Keyboard>/f2", "<Keyboard>/f3",
            "<Keyboard>/f4", "<Keyboard>/f5", "<Keyboard>/home", "<Keyboard>/end",
            "<Keyboard>/delete", "<Mouse>/delta", "<Gamepad>/leftStick",
            "<Gamepad>/rightStick", "<Gamepad>/leftStickPress", "<Gamepad>/dpad",
            "<Gamepad>/dpad/up", "<Gamepad>/dpad/down", "<Gamepad>/dpad/left",
            "<Gamepad>/dpad/right", "<Gamepad>/buttonSouth", "<Gamepad>/buttonEast",
            "<Gamepad>/buttonNorth", "<Gamepad>/buttonWest", "<Gamepad>/leftShoulder",
            "<Gamepad>/rightShoulder", "<Gamepad>/start", "<Gamepad>/select");

        internal static bool ValidIdentifier(string value)
        {
            return !string.IsNullOrEmpty(value) && value.Length <= 128 && Identifier.IsMatch(value);
        }

        internal static bool TryDefinition(string ownerId, string actionId, string json,
            string instanceId, int generation, out ActionDefinition definition, out string error)
        {
            definition = null;
            error = null;
            object parsed;
            Dictionary<string, object> root;
            if (!ValidIdentifier(ownerId) || !ValidIdentifier(actionId)) { error = "invalid_identifier"; return false; }
            if (!Stage2Json.TryParse(json, out parsed, out error) || !Stage2Json.TryObject(parsed, out root))
            { error = error ?? "object_required"; return false; }

            int schema, messageGeneration, order;
            string messageInstance, label, presentation;
            double holdSeconds;
            List<object> keyboard, gamepad, contexts, tableContexts;
            if (!Stage2Json.TryInteger(root, "schema", out schema) || schema != 2 ||
                !Stage2Json.TryString(root, "instanceId", out messageInstance) || messageInstance != instanceId ||
                !Stage2Json.TryInteger(root, "generation", out messageGeneration) || messageGeneration != generation ||
                !Stage2Json.TryString(root, "label", out label) || label.Length < 1 || label.Length > 256 ||
                !Stage2Json.TryInteger(root, "order", out order) ||
                !Stage2Json.TryArray(root, "keyboardMouse", out keyboard) ||
                !Stage2Json.TryArray(root, "gamepad", out gamepad) ||
                !Stage2Json.TryNumber(root, "holdSeconds", out holdSeconds) || holdSeconds < 0 || holdSeconds > 30 ||
                !Stage2Json.TryArray(root, "contexts", out contexts) ||
                !Stage2Json.TryArray(root, "tableContexts", out tableContexts) ||
                !Stage2Json.TryString(root, "presentation", out presentation))
            { error = "invalid_definition_fields"; return false; }
            if (keyboard.Count > 16 || gamepad.Count > 16 || keyboard.Count + gamepad.Count == 0 ||
                (presentation != "none" && presentation != "hint" && presentation != "compact_hint" &&
                 presentation != "native_next" && presentation != "embedded" && presentation != "overlay"))
            { error = "invalid_definition_values"; return false; }

            ActionBinding[] keyboardBindings, gamepadBindings;
            string[] contextValues, tableContextValues;
            if (!TryBindings(keyboard, out keyboardBindings) || !TryBindings(gamepad, out gamepadBindings) ||
                !TryStringSet(contexts, Contexts, true, out contextValues) ||
                !TryStringSet(tableContexts, TableContexts, false, out tableContextValues))
            { error = "invalid_definition_collection"; return false; }

            string tableMerge = null;
            if (root.ContainsKey("tableMerge"))
            {
                if (!Stage2Json.TryString(root, "tableMerge", out tableMerge) ||
                    tableMerge != "standard.sprint" || ownerId != "local.disappearance.gameplayqol" ||
                    actionId != "sprint.hold")
                { error = "invalid_table_merge"; return false; }
            }
            definition = new ActionDefinition {
                OwnerId = ownerId, ActionId = actionId, Label = label, Order = order,
                KeyboardMouse = keyboardBindings, Gamepad = gamepadBindings, HoldSeconds = holdSeconds,
                Contexts = contextValues, TableContexts = tableContextValues,
                Presentation = presentation, TableMerge = tableMerge
            };
            return true;
        }

        internal static bool TryState(string ownerId, string actionId, string json,
            string instanceId, int generation, int currentEpoch, ActionDefinition definition,
            out ActionState state, out string error)
        {
            state = null;
            error = null;
            object parsed;
            Dictionary<string, object> root;
            if (!Stage2Json.TryParse(json, out parsed, out error) || !Stage2Json.TryObject(parsed, out root))
            { error = error ?? "object_required"; return false; }
            if (root.ContainsKey("position") || root.ContainsKey("content"))
            { error = "target_fields_in_action_state"; return false; }
            DisplayState envelope;
            if (!TryEnvelope(ownerId, actionId, root, instanceId, generation, currentEpoch,
                definition, out envelope, out error)) return false;
            var result = new ActionState(envelope);
            if (root.ContainsKey("progress"))
            {
                double progress;
                if (!Stage2Json.TryNumber(root, "progress", out progress) || progress < 0 || progress > 1)
                { error = "invalid_progress"; return false; }
                result.HasProgress = true; result.Progress = progress;
            }
            if (root.ContainsKey("toggled"))
            {
                bool toggled;
                if (!Stage2Json.TryBool(root, "toggled", out toggled)) { error = "invalid_toggled"; return false; }
                result.HasToggled = true; result.Toggled = toggled;
            }
            state = result;
            return true;
        }

        internal static bool TryEnvelope(string ownerId, string actionId, Dictionary<string, object> root,
            string instanceId, int generation, int currentEpoch, ActionDefinition definition,
            out DisplayState state, out string error)
        {
            state = null;
            error = null;
            int schema, messageGeneration, sceneEpoch, sequence;
            string messageInstance, contextId;
            bool visible, enabled;
            double alpha;
            if (!Stage2Json.TryInteger(root, "schema", out schema) || schema != 2 ||
                !Stage2Json.TryString(root, "instanceId", out messageInstance) || messageInstance != instanceId ||
                !Stage2Json.TryInteger(root, "generation", out messageGeneration) || messageGeneration != generation ||
                !Stage2Json.TryInteger(root, "sceneEpoch", out sceneEpoch) || sceneEpoch != currentEpoch ||
                !Stage2Json.TryInteger(root, "sequence", out sequence) || sequence < 0 ||
                !Stage2Json.TryString(root, "contextId", out contextId) || !Contains(definition.Contexts, contextId) ||
                !Stage2Json.TryBool(root, "visible", out visible) ||
                !Stage2Json.TryBool(root, "enabled", out enabled) ||
                !Stage2Json.TryNumber(root, "alpha", out alpha) || alpha < 0 || alpha > 1)
            { error = "invalid_state_fields"; return false; }

            state = new DisplayState {
                OwnerId = ownerId, ActionId = actionId, SceneEpoch = sceneEpoch, Sequence = sequence,
                ContextId = contextId, Visible = visible, Enabled = enabled, Alpha = alpha
            };
            return true;
        }

        private static bool TryBindings(List<object> values, out ActionBinding[] bindings)
        {
            bindings = new ActionBinding[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                Dictionary<string, object> item;
                string mode;
                List<object> controls;
                if (!Stage2Json.TryObject(values[i], out item) || !Stage2Json.TryString(item, "mode", out mode) ||
                    !Stage2Json.TryArray(item, "controls", out controls) || controls.Count < 1 || controls.Count > 8 ||
                    mode != "single" && mode != "chord" && mode != "directional" ||
                    mode == "single" && controls.Count != 1 || mode != "single" && controls.Count < 2)
                    return false;
                var controlValues = new string[controls.Count];
                var unique = new HashSet<string>(StringComparer.Ordinal);
                for (int j = 0; j < controls.Count; j++)
                {
                    string control = controls[j] as string;
                    if (control == null || !Controls.Contains(control) || !unique.Add(control)) return false;
                    controlValues[j] = control;
                }
                bindings[i] = new ActionBinding { Mode = mode, Controls = controlValues };
            }
            return true;
        }

        private static bool TryStringSet(List<object> values, HashSet<string> allowed, bool requireOne,
            out string[] result)
        {
            result = null;
            if (requireOne && values.Count == 0) return false;
            var unique = new HashSet<string>(StringComparer.Ordinal);
            result = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                string value = values[i] as string;
                if (value == null || !allowed.Contains(value) || !unique.Add(value)) return false;
                result[i] = value;
            }
            return true;
        }

        private static bool Contains(string[] values, string value)
        {
            foreach (string item in values) if (item == value) return true;
            return false;
        }

        private static HashSet<string> Set(params string[] values)
        {
            return new HashSet<string>(values, StringComparer.Ordinal);
        }
    }
}
