using System;
using System.Collections.Generic;
using Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Disappearance.Comfort
{
    internal sealed class FieldOfView : IDisposable
    {
        private readonly EnemyVisibilityFov enemy = new EnemyVisibilityFov();
        private readonly Dictionary<CinemachineVirtualCamera, float> cameras = new Dictionary<CinemachineVirtualCamera, float>();
        private Camera gameCamera;
        private CinemachineVirtualCamera follow;
        internal float Baseline { get; private set; }
        internal double Default => Ready ? FovMath.DefaultHorizontal(cameras[follow], gameCamera.aspect) : double.NaN;
        internal float Applied { get; private set; }
        internal double CurrentHorizontal => Ready ? FovMath.Horizontal(gameCamera.fieldOfView, gameCamera.aspect) : double.NaN;
        internal float Aspect { get; private set; }
        internal bool Ready => follow != null && gameCamera != null && cameras.Count > 0 && gameCamera.aspect > 0 && FovMath.Finite(gameCamera.aspect);
        internal FieldOfView() { try { enemy.Install(); } catch { enemy.Uninstall(); throw; } }
        internal void Discover(Scene scene, ComfortConfigStore store)
        {
            if (Ready) return;
            Release();
            GameObject systems = GameObject.Find("PlayerSystems");
            Camera camera = Camera.main;
            if (systems == null || systems.scene != scene || camera == null || camera.gameObject.scene != scene || camera.orthographic || camera.aspect <= 0 || !FovMath.Finite(camera.aspect)) return;
            // Use the game's serialized PlayerInput reference, then prove original hierarchy membership.
            var input = systems.GetComponentInChildren<InstantHorror.Scripts.PlayerInput>();
            var field = HarmonyLib.AccessTools.Field(typeof(InstantHorror.Scripts.PlayerInput), "virtualCamera");
            var original = input == null || field == null ? null : field.GetValue(input) as CinemachineVirtualCamera;
            if (original == null || original.gameObject.name != "PlayerFollowCamera" || original.gameObject.scene != scene || !original.transform.IsChildOf(systems.transform)) return;
            float vertical = original.m_Lens.FieldOfView;
            if (!FovMath.Finite(vertical) || vertical <= 0 || vertical >= 180) return;
            follow = original; gameCamera = camera; Baseline = (float)FovMath.DefaultHorizontal(vertical, camera.aspect);
            cameras.Add(original, vertical);
            try
            {
                store.Migrate(vertical, camera.aspect);
                enemy.UseOriginalFov(vertical, camera);
                Apply(store.Number("Camera.HorizontalFov", Baseline));
                ComfortPlugin.Log.LogInfo("FOV ready: original vertical=" + vertical + ", baseline horizontal=" + Baseline + ", applied=" + Applied);
            }
            catch { Release(); throw; }
        }
        internal void Apply(double horizontal)
        {
            if (!Ready) throw new InvalidOperationException("FOV unavailable");
            foreach (var entry in cameras)
            {
                if (entry.Key == null) continue;
                var lens = entry.Key.m_Lens;
                lens.FieldOfView = (float)FovMath.Vertical(horizontal, gameCamera.aspect);
                entry.Key.m_Lens = lens;
            }
            Applied = (float)horizontal; Aspect = gameCamera.aspect;
        }
        internal bool AspectChanged => Ready && Math.Abs(gameCamera.aspect - Aspect) > .0001f;
        internal Action Snapshot()
        {
            var values = new Dictionary<CinemachineVirtualCamera, float>();
            foreach (var entry in cameras) if (entry.Key != null) values[entry.Key] = entry.Key.m_Lens.FieldOfView;
            float h = Applied, a = Aspect;
            return () => { foreach (var entry in values) if (entry.Key != null) { var lens = entry.Key.m_Lens; lens.FieldOfView = entry.Value; entry.Key.m_Lens = lens; } Applied = h; Aspect = a; };
        }
        internal void DisableEnemy() => enemy.Disable();
        internal void Release()
        {
            enemy.Disable();
            try { foreach (var entry in cameras) if (entry.Key != null) { var lens = entry.Key.m_Lens; lens.FieldOfView = entry.Value; entry.Key.m_Lens = lens; } }
            finally { cameras.Clear(); gameCamera = null; follow = null; }
        }
        public void Dispose() { try { Release(); } finally { enemy.Uninstall(); } }
    }
}
