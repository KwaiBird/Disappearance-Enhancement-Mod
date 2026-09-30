using System;

namespace Disappearance.GameplayQoL
{
    internal static class CheckpointStages
    {
        // Disk IDs 0..3 must remain compatible with existing saves.
        internal const int CrewDeparted = 4;
        internal const int AllMask = 0x1f;
        internal static readonly string[] Names = { "松沢村への道", "ガスの噴き出す穴", "森", "トンネル", "松沢村・奥部入口" };
        internal static readonly int[] Order = { 0, CrewDeparted, 1, 2, 3 };
        internal static bool Valid(int stage) => stage >= 0 && stage < Names.Length;
        internal static int Rank(int stage) => Array.IndexOf(Order, stage);
    }
}
