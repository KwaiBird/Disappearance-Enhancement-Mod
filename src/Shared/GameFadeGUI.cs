using System;
using UnityEngine;

namespace Disappearance.Shared
{
    // Keep IMGUI dependencies out of the native fade-state reader.
    internal sealed class GameFadeGUI : IDisposable
    {
        private readonly Color previous;
        internal GameFadeGUI()
        {
            previous = GUI.color;
            Color color = previous; color.a *= GameFadeVisibility.Shared.Alpha; GUI.color = color;
        }
        public void Dispose() { GUI.color = previous; }
    }
}
