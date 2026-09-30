using System;

namespace Disappearance.Lighting
{
    // Only the forest-side approach to the identified VillageScene tunnel opts in.
    // This is a position rule, never a persistent discovery flag.
    internal static class TunnelApproachFade
    {
        internal const float FullDistance = 10f;

        internal static float StartDistance(float keyDistance) =>
            FullDistance + (Math.Max(FullDistance + 5f, keyDistance) - FullDistance) / .8f;

        internal static float Evaluate(bool targetScene, bool tunnelEmitter, bool outside,
            float distance, float startDistance)
        {
            if (!targetScene || !tunnelEmitter || !outside || distance <= FullDistance) return 1f;
            if (distance >= startDistance) return 0f;
            float t = (startDistance - distance) / (startDistance - FullDistance);
            return t * t * (3f - 2f * t);
        }
    }
}
