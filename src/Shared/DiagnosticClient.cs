using System;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.Mono;
using BepInEx.Unity.Mono.Bootstrap;
using UnityEngine;

namespace Disappearance.Shared
{
    // Linked source: every feature publishes its own values, without a dependency on Performance.
    internal sealed class DiagnosticClient : IDisposable
    {
        internal sealed class Row
        {
            internal string Id, Label;
            internal int Order;
            internal Func<string> Value;
        }
        private readonly string owner;
        private readonly Row[] rows;
        private readonly Action<string> log;
        private BaseUnityPlugin endpoint;
        private string instance;
        private int generation, epoch, sequence;
        private float next;
        private bool warned;
        internal DiagnosticClient(string owner, Action<string> log, params Row[] rows)
        { this.owner = owner; this.log = log; this.rows = rows; }
        private object Call(string name, params object[] args) => endpoint.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance).Invoke(endpoint, args);
        private static string Quote(string text) => "\"" + (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
        internal void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + .2f;
            try
            {
                if (endpoint == null || !endpoint.isActiveAndEnabled)
                {
                    endpoint = null; instance = null;
                    var loader = UnityChainloader.Instance;
                    if (loader == null || !loader.Plugins.TryGetValue("local.disappearance.performance.v2", out PluginInfo info)) return;
                    endpoint = info.Instance as BaseUnityPlugin;
                    if (endpoint == null || !endpoint.isActiveAndEnabled || (int)Call("GetDiagnosticsApiVersion") != 1) { endpoint = null; return; }
                }
                string current = (string)Call("GetDiagnosticsInstanceId");
                int gen = (int)Call("GetDiagnosticsGeneration"), scene = (int)Call("GetDiagnosticsSceneEpoch");
                if (string.IsNullOrEmpty(current) || gen <= 0) return;
                if (current != instance || gen != generation || scene != epoch)
                {
                    instance = current; generation = gen; epoch = scene; sequence = 0;
                    foreach (Row row in rows)
                        if (!(bool)Call("RegisterDiagnostic", owner, row.Id, Header() + ",\"label\":" + Quote(row.Label) + ",\"order\":" + row.Order + "}"))
                            throw new InvalidOperationException("Diagnostic registration rejected: " + row.Id);
                }
                foreach (Row row in rows)
                {
                    string value = row.Value();
                    if (!(bool)Call("UpdateDiagnostic", owner, row.Id, Header() + ",\"sceneEpoch\":" + epoch + ",\"sequence\":" + (++sequence) + ",\"visible\":" + (value != null ? "true" : "false") + ",\"valueText\":" + Quote(value) + "}"))
                        throw new InvalidOperationException("Diagnostic update rejected: " + row.Id);
                }
                warned = false;
            }
            catch (Exception error)
            {
                if (!warned) { warned = true; log("Optional diagnostic overlay unavailable: " + error.Message); }
                Dispose();
            }
        }
        private string Header() => "{\"schema\":1,\"instanceId\":" + Quote(instance) + ",\"generation\":" + generation;
        public void Dispose()
        {
            if (endpoint != null) { try { Call("UnregisterDiagnostics", owner); } catch { } }
            endpoint = null; instance = null;
        }
    }
}
