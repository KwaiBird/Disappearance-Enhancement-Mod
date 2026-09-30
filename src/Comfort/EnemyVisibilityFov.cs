using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Disappearance.Comfort
{
    internal sealed class EnemyVisibilityFov
    {
        private static readonly FieldInfo PlayerCameraField =
            AccessTools.Field(typeof(EnemyController), "playerCamera");
        private readonly Harmony harmony = new Harmony("local.disappearance.comfort.enemyvisibility");
        private static float originalVerticalFov;
        private static bool active; private static Camera baselineCamera;
        private static bool reportedCorrection, reportedDifference, reportedCameraMismatch;
        private static int comparisons;

        internal void Install()
        {
            if (PlayerCameraField == null)
                throw new System.MissingFieldException(typeof(EnemyController).FullName, "playerCamera");
            MethodInfo target = AccessTools.Method(typeof(EnemyController), "IsPlayerVisible");
            if (target == null)
                throw new System.MissingMethodException(typeof(EnemyController).FullName, "IsPlayerVisible");
            harmony.Patch(target, prefix: new HarmonyMethod(
                AccessTools.Method(typeof(EnemyVisibilityFov), nameof(BeforeIsPlayerVisible))));
        }

        internal void UseOriginalFov(float verticalFov, Camera camera)
        {
            originalVerticalFov = verticalFov; baselineCamera = camera;
            active = verticalFov > 0f && verticalFov < 180f;
            reportedCorrection = reportedDifference = reportedCameraMismatch = false;
            comparisons = 0;
        }

        internal void Disable()
        {
            active = false;
        }

        internal void Uninstall()
        {
            active = false;
            harmony.UnpatchSelf();
        }

        private static bool BeforeIsPlayerVisible(EnemyController __instance, ref bool __result)
        {
            if (!active || __instance.enemyType != EnemyController.EnemyType.Forest)
                return true;
            Camera camera = PlayerCameraField.GetValue(__instance) as Camera;
            Collider collider = __instance.GetComponent<Collider>();
            if (camera != null && camera != baselineCamera && !reportedCameraMismatch)
            {
                reportedCameraMismatch = true;
                ComfortPlugin.Log.LogWarning("Forest enemy uses a camera different from the FOV baseline; original visibility path retained.");
            }
            if (camera == null || camera != baselineCamera || collider == null || camera.orthographic ||
                Mathf.Approximately(camera.fieldOfView, originalVerticalFov))
                return true;

            Matrix4x4 originalProjection = Matrix4x4.Perspective(originalVerticalFov,
                camera.aspect, camera.nearClipPlane, camera.farClipPlane);
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(
                originalProjection * camera.worldToCameraMatrix);
            __result = GeometryUtility.TestPlanesAABB(planes, collider.bounds);
            if (!reportedCorrection)
            {
                reportedCorrection = true;
                ComfortPlugin.Log.LogInfo("Forest enemy visibility uses original vertical FOV=" + originalVerticalFov + " instead of rendered FOV=" + camera.fieldOfView + ".");
            }
            if (!reportedDifference && comparisons < 600)
            {
                comparisons++;
                bool renderedResult = GeometryUtility.TestPlanesAABB(
                    GeometryUtility.CalculateFrustumPlanes(camera), collider.bounds);
                if (renderedResult != __result)
                {
                    reportedDifference = true;
                    ComfortPlugin.Log.LogInfo("Forest enemy FOV comparison diverged: original=" + __result + ", rendered=" + renderedResult + ".");
                }
                else if (comparisons == 600)
                    ComfortPlugin.Log.LogInfo("Forest enemy FOV comparison found no divergence in 600 visibility calls.");
            }
            return false;
        }
    }
}

