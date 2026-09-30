using System;
using Unity.Profiling;
using UnityEngine;

namespace Disappearance.PerformanceV2
{
    // Sampling and logging continue independently of the panel's visibility.
    internal sealed class PerformanceDiagnostics : IDisposable
    {
        private readonly float[] frameMilliseconds = new float[1800];
        private readonly ProfilerRecorder batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        private readonly ProfilerRecorder setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        private readonly ProfilerRecorder mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
        private readonly ProfilerRecorder renderThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Render Thread Frame Time");
        private readonly Action<string> log;
        private int frames, batchCount, passCount, mainCount, renderCount;
        private double seconds, batchSum, passSum, mainSum, renderSum;
        private bool hasReported;
        private bool renderAvailabilityLogged;
        internal float Fps { get; private set; }
        internal float P50 { get; private set; }
        internal float P95 { get; private set; }
        internal bool HasSample => hasReported;
        internal string Counters { get; private set; } = "waiting for sample";
        internal string CounterLine1 { get; private set; } = "";
        internal string CounterLine2 { get; private set; } = "";

        internal PerformanceDiagnostics(Action<string> log) { this.log = log; }
        internal void Reset()
        {
            ResetSamples();
            hasReported = false;
            Fps = P50 = P95 = 0f;
            Counters = "waiting for sample";
            CounterLine1 = CounterLine2 = "";
        }
        private void ResetSamples()
        {
            frames = batchCount = passCount = mainCount = renderCount = 0;
            seconds = batchSum = passSum = mainSum = renderSum = 0;
        }

        internal void Tick(float interval, RenderToggles state)
        {
            float delta = Time.unscaledDeltaTime;
            if (delta <= 0f || delta > 1f) return;
            if (frames == frameMilliseconds.Length) Report(state);
            frameMilliseconds[frames++] = delta * 1000f;
            seconds += delta;
            Accumulate(batches, ref batchSum, ref batchCount, 1);
            Accumulate(setPass, ref passSum, ref passCount, 1);
            Accumulate(mainThread, ref mainSum, ref mainCount, .000001);
            if (renderThread.Valid && renderThread.Count > 0 && renderThread.LastValue > 0)
                Accumulate(renderThread, ref renderSum, ref renderCount, .000001);
            // Show the first stable sample promptly, then honor the configured log interval.
            if (seconds >= (hasReported ? Math.Max(2f, interval) : 2f)) Report(state);
        }

        private static void Accumulate(ProfilerRecorder recorder, ref double sum, ref int count, double scale)
        {
            if (!recorder.Valid || recorder.Count == 0) return;
            sum += recorder.LastValue * scale; count++;
        }

        private static string Average(double sum, int count) => count == 0 ? "n/a" : (sum / count).ToString("F1");
        private static string AverageMs(double sum, int count) => count == 0 ? "n/a" : (sum / count).ToString("F1") + "ms";

        private void Report(RenderToggles state)
        {
            if (frames == 0 || seconds <= 0) return;
            float[] sorted = new float[frames];
            Array.Copy(frameMilliseconds, sorted, frames);
            Array.Sort(sorted);
            Fps = (float)(frames / seconds);
            P50 = sorted[frames / 2];
            P95 = sorted[Math.Min(frames - 1, (int)Math.Ceiling(frames * .95) - 1)];
            CounterLine1 = "batches=" + Average(batchSum, batchCount) + ", setPass=" + Average(passSum, passCount);
            CounterLine2 = "main=" + AverageMs(mainSum, mainCount);
            if (renderCount > 0) CounterLine2 += ", render=" + AverageMs(renderSum, renderCount);
            else if (!renderAvailabilityLogged)
            {
                renderAvailabilityLogged = true;
                log("P6 render-thread timing unavailable: FrameTimingStats enabled=" +
                    FrameTimingManager.IsFeatureEnabled() + ", recorder valid=" + renderThread.Valid +
                    ", samples=" + renderThread.Count + ". The overlay omits this unsupported metric.");
            }
            Counters = CounterLine1 + ", " + CounterLine2;
            log("P6 performance: " + Screen.width + "x" + Screen.height + ", frames=" + frames +
                ", fps=" + Fps.ToString("F1") + ", p50=" + P50.ToString("F1") + "ms, p95=" +
                P95.ToString("F1") + "ms, " + Counters + ", vineShadowsOff=" + state.VineShadowsOff +
                ", vineInstancingOn=" + state.VineInstancingOn + ", pointShadowsOff=" + state.PointShadowsOff +
                ", sunShadowsOff=" + state.SunShadowsOff + ", postFxOff=" + state.PostFxOff +
                ", vSync=" + QualitySettings.vSyncCount + ", targetFps=" + Application.targetFrameRate);
            hasReported = true;
            ResetSamples();
        }

        public void Dispose() { batches.Dispose(); setPass.Dispose(); mainThread.Dispose(); renderThread.Dispose(); }
    }
}
