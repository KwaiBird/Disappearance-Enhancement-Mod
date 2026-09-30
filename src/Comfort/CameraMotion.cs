using System;
using System.Collections.Generic;
using System.Reflection;
using Cinemachine;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using GameInput = InstantHorror.Scripts.PlayerInput;

namespace Disappearance.Comfort
{
    internal sealed class CameraMotion : IDisposable
    {
        private sealed class Lease
        {
            internal GameInput Input;
            internal CinemachineBasicMultiChannelPerlin Noise;
            internal MotionBlur Blur;
            internal float Amplitude;
            internal bool BlurActive, Cache, NoiseRequest, BlurRequest, HasNoiseRequest, HasBlurRequest;
        }
        private static CameraMotion current;
        private readonly Dictionary<int, Lease> leases = new Dictionary<int, Lease>();
        private readonly Harmony noisePatch = new Harmony(ComfortPlugin.PluginId + ".cameramotion.noise");
        private readonly Harmony blurPatch = new Harmony(ComfortPlugin.PluginId + ".cameramotion.blur");
        private static readonly FieldInfo VirtualCamera = AccessTools.Field(typeof(GameInput), "virtualCamera");
        private static readonly FieldInfo Volume = AccessTools.Field(typeof(GameInput), "volume");
        private static readonly FieldInfo BlurCache = AccessTools.Field(typeof(GameInput), "isMotionBlurEnabled");
        internal CameraMotion()
        {
            current = this;
            TryInstall(noisePatch, "SetVirtualCameraNoise", nameof(Noise), VirtualCamera);
            TryInstall(blurPatch, "SetMotionBlurEnabled", nameof(Blur), Volume, BlurCache);
        }
        private void TryInstall(Harmony patch, string method, string prefix, params FieldInfo[] fields)
        {
            try
            {
                if (Array.Exists(fields, f => f == null)) throw new MissingFieldException(method);
                MethodInfo target = AccessTools.Method(typeof(GameInput), method, new[] { typeof(bool) });
                if (target == null) throw new MissingMethodException(method);
                patch.Patch(target, prefix: new HarmonyMethod(typeof(CameraMotion), prefix));
            }
            catch (Exception e) { patch.UnpatchSelf(); ComfortPlugin.Log.LogError("CameraMotion " + method + " unavailable: " + e); }
        }
        private Lease For(GameInput input)
        {
            int id = input.GetInstanceID();
            if (!leases.TryGetValue(id, out Lease lease)) leases[id] = lease = new Lease { Input = input };
            return lease;
        }
        private static bool Noise(GameInput __instance, ref bool __0)
        {
            if (current == null) return true;
            try
            {
                Lease lease = current.For(__instance);
                var camera = VirtualCamera.GetValue(__instance) as CinemachineVirtualCamera;
                var noise = camera == null ? null : camera.GetCinemachineComponent<CinemachineBasicMultiChannelPerlin>();
                if (noise == null) return true;
                if (lease.Noise != noise)
                {
                    if (lease.Noise != null) lease.Noise.m_AmplitudeGain = lease.HasNoiseRequest ? (lease.NoiseRequest ? 1 : 0) : lease.Amplitude;
                    lease.Noise = noise; lease.Amplitude = noise.m_AmplitudeGain;
                }
                lease.HasNoiseRequest = true; lease.NoiseRequest = __0;
                __0 = false;
                noise.m_AmplitudeGain = 0;
            }
            catch (Exception e) { ComfortPlugin.Log.LogWarning("Noise suppression failed: " + e.Message); }
            return true;
        }
        private static bool Blur(GameInput __instance, ref bool __0)
        {
            if (current == null) return true;
            try
            {
                Lease lease = current.For(__instance);
                var volume = Volume.GetValue(__instance) as Volume;
                if (volume == null || !volume.profile.TryGet(out MotionBlur blur)) return true;
                if (lease.Blur != blur)
                {
                    if (lease.Blur != null) lease.Blur.active = lease.HasBlurRequest ? lease.BlurRequest : lease.BlurActive;
                    lease.Blur = blur; lease.BlurActive = blur.active; lease.Cache = (bool)BlurCache.GetValue(__instance);
                }
                lease.HasBlurRequest = true; lease.BlurRequest = __0;
                __0 = false;
                blur.active = false; BlurCache.SetValue(__instance, false);
            }
            catch (Exception e) { ComfortPlugin.Log.LogWarning("Blur suppression failed: " + e.Message); }
            return true;
        }
        internal void Release()
        {
            foreach (Lease lease in leases.Values)
            {
                try { if (lease.Noise != null) lease.Noise.m_AmplitudeGain = lease.HasNoiseRequest ? (lease.NoiseRequest ? 1 : 0) : lease.Amplitude; }
                catch (Exception e) { ComfortPlugin.Log.LogWarning(e); }
                try
                {
                    if (lease.Blur != null) lease.Blur.active = lease.HasBlurRequest ? lease.BlurRequest : lease.BlurActive;
                    if (lease.Input != null && lease.Blur != null) BlurCache.SetValue(lease.Input, lease.HasBlurRequest ? lease.BlurRequest : lease.Cache);
                }
                catch (Exception e) { ComfortPlugin.Log.LogWarning(e); }
            }
            leases.Clear();
        }
        public void Dispose() { current = null; try { Release(); } finally { noisePatch.UnpatchSelf(); blurPatch.UnpatchSelf(); } }
    }
}
