using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace Disappearance.UIControls
{
    internal sealed class RegisteredAction
    {
        internal ActionDefinition Definition;
        internal ActionState State;
    }

    internal sealed class RegisteredTargetCue
    {
        internal ActionDefinition Definition;
        internal TargetCueState State;
    }

    internal sealed class ActionRegistry
    {
        private readonly ManualLogSource log;
        private readonly Func<string, bool> ownerAlive;
        private readonly int mainThreadId;
        private readonly Dictionary<string, ActionDefinition> definitions =
            new Dictionary<string, ActionDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, ActionState> states =
            new Dictionary<string, ActionState>(StringComparer.Ordinal);
        private readonly Dictionary<string, TargetCueState> targetCues =
            new Dictionary<string, TargetCueState>(StringComparer.Ordinal);
        private readonly HashSet<string> loggedErrors = new HashSet<string>(StringComparer.Ordinal);
        private float nextOwnerCheck;

        internal ActionRegistry(ManualLogSource log, Func<string, bool> ownerAlive, string instanceId)
        {
            this.log = log;
            this.ownerAlive = ownerAlive;
            InstanceId = instanceId;
            mainThreadId = Environment.CurrentManagedThreadId;
        }

        internal string InstanceId { get; private set; }
        internal int Generation { get; private set; } = 1;
        internal int SceneEpoch { get; private set; }
        internal int TableRevision { get; private set; }

        internal bool RegisterAction(string ownerId, string actionId, string json)
        {
            if (!OnMainThread(ownerId) || !ownerAlive(ownerId)) return Reject(ownerId, "owner_not_active");
            ActionDefinition definition;
            string error;
            if (!ActionContractParser.TryDefinition(ownerId, actionId, json, InstanceId, Generation,
                out definition, out error)) return Reject(ownerId, error);
            definitions[Key(ownerId, actionId)] = definition;
            TableRevision++;
            return true;
        }

        internal bool UpdateActionState(string ownerId, string actionId, string json, float receivedAt)
        {
            if (!OnMainThread(ownerId) || !ownerAlive(ownerId)) return Reject(ownerId, "owner_not_active");
            string key = Key(ownerId, actionId);
            ActionDefinition definition;
            if (!definitions.TryGetValue(key, out definition)) return Reject(ownerId, "unregistered_action");
            ActionState candidate;
            string error;
            if (!ActionContractParser.TryState(ownerId, actionId, json, InstanceId, Generation, SceneEpoch,
                definition, out candidate, out error)) return Reject(ownerId, error);
            string stateKey = StateKey(key, candidate.ContextId);
            ActionState existing;
            if (states.TryGetValue(stateKey, out existing))
            {
                if (candidate.Sequence < existing.Sequence) return Reject(ownerId, "stale_sequence");
                if (candidate.Sequence == existing.Sequence && !candidate.SameContent(existing))
                    return Reject(ownerId, "sequence_content_conflict");
            }
            candidate.ReceivedAt = receivedAt;
            states[stateKey] = candidate;
            return true;
        }

        internal void ClearContext(string ownerId, string contextId)
        {
            if (!OnMainThread(ownerId)) return;
            RemoveWhere(states, pair => pair.Value.OwnerId == ownerId && pair.Value.ContextId == contextId);
            RemoveWhere(targetCues, pair => pair.Value.OwnerId == ownerId && pair.Value.ContextId == contextId);
        }

        internal void UnregisterOwner(string ownerId)
        {
            if (!OnMainThread(ownerId)) return;
            RemoveWhere(definitions, pair => pair.Value.OwnerId == ownerId);
            RemoveWhere(states, pair => pair.Value.OwnerId == ownerId);
            RemoveWhere(targetCues, pair => pair.Value.OwnerId == ownerId);
            TableRevision++;
        }

        internal List<ActionDefinition> TableActions(string contextId)
        {
            var result = new List<ActionDefinition>();
            ActionDefinition sprintMerge = null;
            foreach (ActionDefinition definition in definitions.Values)
            {
                if (!ownerAlive(definition.OwnerId)) continue;
                if (definition.TableMerge == "standard.sprint") sprintMerge = definition;
                if (Array.IndexOf(definition.TableContexts, contextId) >= 0)
                    result.Add(definition);
            }
            if (sprintMerge != null)
                for (int i = 0; i < result.Count; i++)
                    if (result[i].OwnerId == UIControlsPlugin.PluginId &&
                        result[i].ActionId == "standard.sprint")
                    {
                        ActionDefinition standard = result[i];
                        var gamepad = new ActionBinding[standard.Gamepad.Length + sprintMerge.Gamepad.Length];
                        Array.Copy(standard.Gamepad, gamepad, standard.Gamepad.Length);
                        Array.Copy(sprintMerge.Gamepad, 0, gamepad, standard.Gamepad.Length,
                            sprintMerge.Gamepad.Length);
                        result[i] = new ActionDefinition {
                            OwnerId = standard.OwnerId, ActionId = standard.ActionId,
                            Label = standard.Label, Order = standard.Order,
                            KeyboardMouse = standard.KeyboardMouse, Gamepad = gamepad,
                            HoldSeconds = standard.HoldSeconds, Contexts = standard.Contexts,
                            TableContexts = standard.TableContexts, Presentation = standard.Presentation
                        };
                        break;
                    }
            result.Sort((left, right) => {
                int order = left.Order.CompareTo(right.Order);
                if (order != 0) return order;
                int owner = string.CompareOrdinal(left.OwnerId, right.OwnerId);
                return owner != 0 ? owner : string.CompareOrdinal(left.ActionId, right.ActionId);
            });
            return result;
        }

        internal void BeginScene()
        {
            if (SceneEpoch == int.MaxValue) SceneEpoch = 0;
            else SceneEpoch++;
            states.Clear();
            targetCues.Clear();
        }

        internal void Update(float now)
        {
            RemoveWhere(states, pair => now - pair.Value.ReceivedAt > 1.5f);
            RemoveWhere(targetCues, pair => now - pair.Value.ReceivedAt > 1.5f);
            if (now < nextOwnerCheck) return;
            nextOwnerCheck = now + 0.5f;
            var lost = new HashSet<string>(StringComparer.Ordinal);
            foreach (ActionDefinition definition in definitions.Values)
                if (!ownerAlive(definition.OwnerId)) lost.Add(definition.OwnerId);
            foreach (string ownerId in lost)
            {
                UnregisterOwner(ownerId);
                log.LogInfo("Removed action registrations for inactive owner " + ownerId + ".");
            }
        }

        internal List<RegisteredAction> VisibleActions(string contextId)
        {
            return VisibleActions(contextId, "hint");
        }

        internal List<RegisteredAction> VisibleCompactActions(string contextId)
        {
            return VisibleActions(contextId, "compact_hint");
        }

        internal List<RegisteredAction> VisibleEmbeddedActions(string contextId)
        {
            return VisibleActions(contextId, "embedded");
        }

        internal List<RegisteredAction> EmbeddedActions(string contextId) => VisibleActions(contextId, "embedded", false);

        private List<RegisteredAction> VisibleActions(string contextId, string presentation, bool visibleOnly = true)
        {
            var result = new List<RegisteredAction>();
            foreach (ActionState state in states.Values)
            {
                if (visibleOnly && !state.Visible || state.ContextId != contextId) continue;
                ActionDefinition definition;
                if (!definitions.TryGetValue(Key(state.OwnerId, state.ActionId), out definition) ||
                    definition.Presentation != presentation) continue;
                result.Add(new RegisteredAction { Definition = definition, State = state });
            }
            result.Sort((left, right) => {
                int order = left.Definition.Order.CompareTo(right.Definition.Order);
                if (order != 0) return order;
                int owner = string.CompareOrdinal(left.Definition.OwnerId, right.Definition.OwnerId);
                return owner != 0 ? owner : string.CompareOrdinal(left.Definition.ActionId, right.Definition.ActionId);
            });
            return result;
        }

        internal bool UpdateTargetCueState(string ownerId, string actionId, string json, float receivedAt)
        {
            if (!OnMainThread(ownerId) || !ownerAlive(ownerId)) return Reject(ownerId, "owner_not_active");
            string key = Key(ownerId, actionId);
            ActionDefinition definition;
            if (!definitions.TryGetValue(key, out definition)) return Reject(ownerId, "unregistered_action");
            TargetCueState candidate;
            string error;
            if (!TargetCueContractParser.TryState(ownerId, actionId, json, InstanceId, Generation,
                SceneEpoch, definition, out candidate, out error)) return Reject(ownerId, error);
            string stateKey = StateKey(key, candidate.ContextId);
            TargetCueState existing;
            if (targetCues.TryGetValue(stateKey, out existing))
            {
                if (candidate.Sequence < existing.Sequence) return Reject(ownerId, "stale_target_sequence");
                if (candidate.Sequence == existing.Sequence && !candidate.SameContent(existing))
                    return Reject(ownerId, "target_sequence_content_conflict");
            }
            candidate.ReceivedAt = receivedAt;
            targetCues[stateKey] = candidate;
            return true;
        }

        internal List<RegisteredTargetCue> VisibleTargetCues(string contextId)
        {
            var result = new List<RegisteredTargetCue>();
            foreach (TargetCueState state in targetCues.Values)
            {
                if (!state.Visible || state.ContextId != contextId) continue;
                ActionDefinition definition;
                if (definitions.TryGetValue(Key(state.OwnerId, state.ActionId), out definition) &&
                    definition.Presentation == "hint")
                    result.Add(new RegisteredTargetCue { Definition = definition, State = state });
            }
            return result;
        }

        internal bool HasVisibleContext(string contextId)
        {
            foreach (ActionState state in states.Values)
                if (state.Visible && state.ContextId == contextId &&
                    definitions.ContainsKey(Key(state.OwnerId, state.ActionId))) return true;
            return VisibleTargetCues(contextId).Count > 0;
        }

        private bool OnMainThread(string ownerId)
        {
            return Environment.CurrentManagedThreadId == mainThreadId || Reject(ownerId, "non_main_thread");
        }

        private bool Reject(string ownerId, string reason)
        {
            string key = (ownerId ?? "<null>") + "\n" + reason;
            if (loggedErrors.Add(key)) log.LogWarning("Rejected UIControls action message from " +
                (ownerId ?? "<null>") + ": " + reason + ".");
            return false;
        }

        private static string Key(string ownerId, string actionId) => ownerId + "\n" + actionId;
        private static string StateKey(string actionKey, string contextId) => actionKey + "\n" + contextId;

        private static void RemoveWhere<T>(Dictionary<string, T> source,
            Func<KeyValuePair<string, T>, bool> predicate)
        {
            var keys = new List<string>();
            foreach (KeyValuePair<string, T> pair in source) if (predicate(pair)) keys.Add(pair.Key);
            foreach (string key in keys) source.Remove(key);
        }
    }
}
