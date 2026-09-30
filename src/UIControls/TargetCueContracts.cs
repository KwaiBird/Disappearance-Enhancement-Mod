using System.Collections.Generic;

namespace Disappearance.UIControls
{
    internal sealed class TargetCuePosition
    {
        internal double X, Y;
    }

    // A target cue has no help-row progress, toggle, background or layout data.
    internal sealed class TargetCueState : DisplayState
    {
        internal TargetCuePosition Position;
        internal string Content;
        internal TargetCueState(DisplayState source) : base(source) { }

        internal bool SameContent(TargetCueState other) => SameDisplay(other) &&
            Content == other.Content && Position.X == other.Position.X && Position.Y == other.Position.Y;
    }

    internal static class TargetCueContractParser
    {
        internal static bool TryState(string ownerId, string actionId, string json,
            string instanceId, int generation, int epoch, ActionDefinition definition,
            out TargetCueState state, out string error)
        {
            state = null;
            object parsed;
            Dictionary<string, object> root;
            if (!Stage2Json.TryParse(json, out parsed, out error) || !Stage2Json.TryObject(parsed, out root))
            { error = error ?? "object_required"; return false; }
            if (definition.Presentation != "hint") { error = "invalid_target_presentation"; return false; }
            if (root.ContainsKey("progress") || root.ContainsKey("toggled"))
            { error = "help_fields_in_target_state"; return false; }
            DisplayState envelope;
            if (!ActionContractParser.TryEnvelope(ownerId, actionId, root, instanceId, generation,
                epoch, definition, out envelope, out error)) return false;
            string content, space, anchor;
            Dictionary<string, object> position;
            object raw;
            double x, y;
            if (!Stage2Json.TryString(root, "content", out content) ||
                (content != "control" && content != "control_label"))
            { error = "invalid_target_content"; return false; }
            if (!root.TryGetValue("position", out raw) || !Stage2Json.TryObject(raw, out position) ||
                !Stage2Json.TryString(position, "space", out space) || space != "screen01" ||
                !Stage2Json.TryNumber(position, "x", out x) || x < 0 || x > 1 ||
                !Stage2Json.TryNumber(position, "y", out y) || y < 0 || y > 1 ||
                !Stage2Json.TryString(position, "anchor", out anchor) || anchor != "center")
            { error = "invalid_target_position"; return false; }
            state = new TargetCueState(envelope) {
                Position = new TargetCuePosition { X = x, Y = y }, Content = content
            };
            return true;
        }
    }
}
