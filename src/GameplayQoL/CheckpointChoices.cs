using System.Collections.Generic;

namespace Disappearance.GameplayQoL
{
    // Keep the stage ID as selection state while hiding undiscovered destinations.
    internal static class CheckpointChoices
    {
        internal static int[] Visible(CheckpointProgress progress)
        {
            var stages = new List<int>();
            foreach (int stage in CheckpointStages.Order)
                if (progress.IsUnlocked(stage)) stages.Add(stage);
            return stages.ToArray();
        }

        internal static int Move(CheckpointProgress progress, int selectedStage, int direction)
        {
            int[] stages = Visible(progress);
            if (stages.Length == 0) return -1;
            int index = System.Array.IndexOf(stages, selectedStage);
            if (index < 0) return stages[0];
            return stages[(index + direction + stages.Length) % stages.Length];
        }
    }
}
