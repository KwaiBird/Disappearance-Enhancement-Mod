#if P5_HALO_DIAGNOSTICS
using System.Collections;
using System.IO;
using BepInEx;
using UnityEngine;

namespace Disappearance.Lighting
{
    public sealed partial class LightingPlugin
    {
        private float nextComparisonPoll;
        private bool comparisonRunning;

        private void PollHaloComparison()
        {
            if (!captured || atmosphereFailed || comparisonRunning || Time.unscaledTime < nextComparisonPoll) return;
            nextComparisonPoll = Time.unscaledTime + .5f;
            string request = Path.Combine(Paths.ConfigPath, "P5-halo-comparison.request");
            if (!File.Exists(request)) return;
            try
            {
                string directory = File.ReadAllText(request).Trim();
                File.Delete(request);
                if (!Path.IsPathRooted(directory) || !Directory.Exists(directory))
                    throw new IOException("Comparison output must be an existing absolute directory.");
                comparisonRunning = true;
                StartCoroutine(CaptureHaloComparison(directory));
            }
            catch (System.Exception error) { comparisonRunning = false; Logger.LogError("Halo comparison request: " + error); }
        }

        private IEnumerator CaptureHaloComparison(string directory)
        {
            Camera camera = Camera.main;
            if (camera == null) { comparisonRunning = false; yield break; }
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            int handle = scene.handle;
            try
            {
                // User holds the view still. Only generated halo renderers change;
                // original geometry, lights, beams, Bloom and Comfort stay identical.
                atmosphere.SetHaloRenderersVisible(false);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(directory, "halos-off.png"));
                yield return new WaitForSecondsRealtime(.3f);
                if (scene.handle != handle || camera == null) yield break;
                atmosphere.SetHaloRenderersVisible(true);
                yield return new WaitForEndOfFrame();
                atmosphere.WriteTunnelHaloStates(camera, Path.Combine(directory, "tunnel-halo-states.csv"));
                ScreenCapture.CaptureScreenshot(Path.Combine(directory, "halos-on.png"));
                yield return new WaitForSecondsRealtime(.3f);
                if (scene.handle != handle || camera == null) yield break;
                string result = "scene=" + scene.name + ", positionDelta=" +
                    Vector3.Distance(position, camera.transform.position).ToString("F6") +
                    ", rotationDelta=" + Quaternion.Angle(rotation, camera.transform.rotation).ToString("F6") +
                    ", fovDelta=" + Mathf.Abs(fov - camera.fieldOfView).ToString("F6");
                File.WriteAllText(Path.Combine(directory, "comparison.txt"), result);
                Logger.LogInfo("Halo comparison saved: " + directory + "; " + result);
            }
            finally
            {
                atmosphere.SetHaloRenderersVisible(true);
                comparisonRunning = false;
            }
        }
    }
}
#endif
