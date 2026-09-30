using System;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.Mono;
using BepInEx.Unity.Mono.Bootstrap;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Disappearance.Comfort
{
    internal sealed class PresentationClient : IDisposable
    {
        private readonly ComfortPlugin owner;
        private BaseUnityPlugin endpoint;
        private string instance;
        private int generation, settingsGeneration, epoch, sequence;
        private float next;
        private bool warned;
        internal PresentationClient(ComfortPlugin owner) { this.owner = owner; }
        private object Call(string name, params object[] args)
        {
            MethodInfo method = endpoint.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
            if (method == null) throw new MissingMethodException(name);
            return method.Invoke(endpoint, args);
        }
        internal void Update()
        {
            if (Time.unscaledTime < next) return; next = Time.unscaledTime + .2f;
            try
            {
                if (endpoint == null || !endpoint.isActiveAndEnabled)
                {
                    endpoint = null;
                    var loader = UnityChainloader.Instance;
                    if (loader == null || !loader.Plugins.TryGetValue("local.disappearance.uicontrols", out PluginInfo info)) return;
                    endpoint = info.Instance as BaseUnityPlugin;
                    if (endpoint == null || !endpoint.isActiveAndEnabled) return;
                    if ((int)Call("GetSettingsApiVersion") != 2 || (int)Call("GetApiVersion") != 2) { endpoint = null; return; }
                    instance = null;
                }
                string id = (string)Call("GetPresentationInstanceId");
                int ag = (int)Call("GetActionGeneration"), sg = (int)Call("GetSettingsGeneration"), se = (int)Call("GetSceneEpoch");
                bool actionStreamChanged = id != instance || ag != generation || se != epoch;
                bool settingsChanged = id != instance || sg != settingsGeneration;
                if (actionStreamChanged || settingsChanged)
                {
                    instance = id; generation = ag; settingsGeneration = sg; epoch = se;
                    // The settings view can restart without resetting the action registry's states.
                    if (actionStreamChanged) sequence = 0;
                    if (settingsChanged)
                    {
                        foreach (SettingSpec spec in SettingSpec.All)
                        {
                            string definition = ComfortWire.Setting(spec, instance, sg);
                            if (!((string)Call("RegisterSetting", ComfortPlugin.PluginId, spec.Id, definition)).Contains("\"ok\"")) throw new InvalidOperationException("Setting registration rejected: " + spec.Id);
                        }
                    }
                    if (actionStreamChanged)
                    {
                        for (int i = 0; i < ComfortWire.ActionIds.Length; i++)
                        {
                            string json = ComfortWire.Action(i, instance, ag);
                            if (!(bool)Call("RegisterAction", ComfortPlugin.PluginId, ComfortWire.ActionIds[i], json)) throw new InvalidOperationException("Action registration rejected: " + ComfortWire.ActionIds[i]);
                        }
                    }
                    ComfortPlugin.Log.LogInfo("Comfort registered UIControls " +
                        (settingsChanged ? "settings v2 " : "") + (actionStreamChanged ? "actions v2 " : "") + "epoch=" + epoch);
                }
                for (int i = 0; i < ComfortWire.ActionIds.Length; i++)
                {
                    bool available = SceneManager.GetActiveScene().name == "VillageScene" &&
                        (i < 3 ? owner.Ready("horizontal_fov") : i == 5 ? owner.Vignette.Ready : owner.Lighting.Ready);
                    string json = ComfortWire.State(instance, generation, epoch, ++sequence, available);
                    if (!(bool)Call("UpdateActionState", ComfortPlugin.PluginId, ComfortWire.ActionIds[i], json)) throw new InvalidOperationException("Action state rejected");
                }
            }
            catch (Exception e)
            {
                if (!warned) { warned = true; ComfortPlugin.Log.LogWarning("Optional UI link unavailable: " + e.Message); }
                Dispose();
            }
        }
        public void Dispose()
        {
            if (endpoint != null)
            {
                try { Call("UnregisterSettings", ComfortPlugin.PluginId); } catch { }
                try { Call("UnregisterOwner", ComfortPlugin.PluginId); } catch { }
            }
            endpoint = null; instance = null;
        }
    }
}

