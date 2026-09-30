using System;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.Mono;
using BepInEx.Unity.Mono.Bootstrap;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Disappearance.GameplayQoL
{
    // Registers action lifecycle and presentation ownership. Padlock and checkpoint
    // publish embedded actions so UIControls can suppress shared hints while their
    // feature-owned help is visible without drawing those actions itself.
    internal sealed class PresentationClient : MonoBehaviour
    {
        private const string EndpointId = "local.disappearance.uicontrols";
        private sealed class ActionSpec
        {
            internal string Id, Label, Keyboard, Gamepad, Contexts, Extra, Presentation, TableContexts;
            internal int Order;
        }
        private static readonly ActionSpec[] Actions = {
            Spec("sprint.hold", "走る", 30, "<Keyboard>/leftShift", "<Gamepad>/leftShoulder", "village", "\"tableMerge\":\"standard.sprint\","),
            Spec("sprint.toggle", "走行切替", 31, "<Keyboard>/v", "<Gamepad>/buttonNorth", "village", tableContexts: "pause,loading"),
            Spec("checkpoint.open", "復帰地点", 60, "<Keyboard>/ctrl+<Keyboard>/f2", "<Gamepad>/leftShoulder+<Gamepad>/rightShoulder+<Gamepad>/select", "village", presentation: "none", tableContexts: "pause"),
            Spec("signs.glow_toggle", "標識の発光切替", 110, "<Keyboard>/g", "", "village", presentation: "overlay"),
            Spec("dialogue.skip", "一括スキップ", 70, "<Keyboard>/space", "<Gamepad>/buttonEast", "novel_opening,novel_ending,conversation,text_event", presentation: "compact_hint", tableContexts: "pause,loading"),
            Spec("title.back", "戻る", 10, "<Keyboard>/escape|<Keyboard>/backspace", "<Gamepad>/buttonEast", "title", presentation: "embedded"),
            Spec("title.mode", "モード切替（左右）", 20, "<Keyboard>/a|<Keyboard>/d", "<Gamepad>/dpad", "title", presentation: "embedded"),
            Spec("title.unlock", "全再開地点を解放", 30, UnlockShortcut.KeyboardBinding, UnlockShortcut.GamepadBinding, "title", presentation: "embedded"),
            Spec("title.select", "選択", 40, "<Keyboard>/w|<Keyboard>/s", "<Gamepad>/leftStick|<Gamepad>/dpad", "title", presentation: "embedded"),
            Spec("title.confirm", "決定", 50, "<Keyboard>/e|<Keyboard>/enter", "<Gamepad>/buttonSouth", "title", presentation: "embedded"),
            Spec("checkpoint.select", "選択", 10, "<Keyboard>/w|<Keyboard>/s", "<Gamepad>/leftStick|<Gamepad>/dpad", "checkpoint", presentation: "embedded"),
            Spec("checkpoint.confirm", "決定", 20, "<Keyboard>/e|<Keyboard>/enter", "<Gamepad>/buttonSouth", "checkpoint", presentation: "embedded"),
            Spec("checkpoint.mode", "モード切替", 30, "<Keyboard>/a|<Keyboard>/d", "<Gamepad>/dpad", "checkpoint", presentation: "embedded"),
            Spec("checkpoint.unlock", "全再開地点を解放", 40, UnlockShortcut.KeyboardBinding, UnlockShortcut.GamepadBinding, "checkpoint", presentation: "embedded"),
            Spec("checkpoint.back", "戻る", 50, "<Keyboard>/escape|<Keyboard>/backspace", "<Gamepad>/buttonEast", "checkpoint", presentation: "embedded"),
            Spec("padlock.select", "行選択", 10, "<Keyboard>/w|<Keyboard>/s", "<Gamepad>/leftStick", "padlock", presentation: "embedded"),
            Spec("padlock.confirm", "次の数字", 20, "<Keyboard>/e", "<Gamepad>/buttonSouth", "padlock", presentation: "embedded"),
            Spec("padlock.back", "戻る", 30, "<Keyboard>/escape", "<Gamepad>/buttonEast", "padlock", presentation: "embedded")
        };

        private object endpoint;
        private Type endpointType;
        private string instanceId;
        private int generation, epoch, sequence;
        private float nextConnect, nextState;
        private string activeContext;
        private bool loggedFailure;
        internal bool Ready { get; private set; }

        private static ActionSpec Spec(string id, string label, int order, string keyboard,
            string gamepad, string contexts, string extra = "", string presentation = "hint",
            string tableContexts = "")
        {
            return new ActionSpec { Id = id, Label = label, Order = order, Keyboard = keyboard,
                Gamepad = gamepad, Contexts = contexts, Extra = extra, Presentation = presentation,
                TableContexts = tableContexts };
        }

        private void Update()
        {
            if (Time.unscaledTime < nextState) return;
            nextState = Time.unscaledTime + 0.05f;
            try
            {
                if (!Ready)
                {
                    if (Time.unscaledTime >= nextConnect)
                    {
                        nextConnect = Time.unscaledTime + 0.5f;
                        Connect();
                    }
                    if (!Ready) return;
                }
                string newInstance = (string)Invoke("GetPresentationInstanceId");
                int newGeneration = (int)Invoke("GetActionGeneration");
                int newEpoch = (int)Invoke("GetSceneEpoch");
                if (newInstance != instanceId || newGeneration != generation || newEpoch != epoch)
                {
                    instanceId = newInstance;
                    generation = newGeneration;
                    epoch = newEpoch;
                    sequence = 0;
                    activeContext = null;
                    if (!RegisterAll()) { Disconnect(); return; }
                }
                Publish();
            }
            catch (Exception ex)
            {
                if (!loggedFailure)
                {
                    loggedFailure = true;
                    GameplayQoLPlugin.Log.LogWarning("UIControls optional action link failed: " + ex.Message);
                }
                Disconnect();
            }
        }

        private void Connect()
        {
            UnityChainloader loader = UnityChainloader.Instance;
            PluginInfo info;
            if (loader == null || !loader.Plugins.TryGetValue(EndpointId, out info) || info == null) return;
            BaseUnityPlugin plugin = info.Instance as BaseUnityPlugin;
            if (plugin == null || !plugin.isActiveAndEnabled) return;
            endpoint = plugin;
            endpointType = plugin.GetType();
            if ((int)Invoke("GetApiVersion") != 2) { Disconnect(); return; }
            instanceId = (string)Invoke("GetPresentationInstanceId");
            generation = (int)Invoke("GetActionGeneration");
            epoch = (int)Invoke("GetSceneEpoch");
            sequence = 0;
            activeContext = null;
            Ready = RegisterAll();
            if (Ready) GameplayQoLPlugin.Log.LogInfo("GameplayQoL registered optional UIControls v2 actions.");
            else Disconnect();
        }

        private bool RegisterAll()
        {
            foreach (ActionSpec spec in Actions)
            {
                string contexts = "\"" + spec.Contexts.Replace(",", "\",\"") + "\"";
                string definition = "{\"schema\":2,\"instanceId\":\"" + instanceId +
                    "\",\"generation\":" + generation + ",\"label\":\"" + spec.Label +
                    "\",\"order\":" + spec.Order + ",\"keyboardMouse\":" + Binding(spec.Keyboard) +
                    ",\"gamepad\":" + Binding(spec.Gamepad) +
                    ",\"holdSeconds\":" + (spec.Id == "dialogue.skip" ? "1" : "0") +
                    ",\"contexts\":[" + contexts + "],\"tableContexts\":" +
                    (spec.TableContexts.Length == 0 ? "[]" : "[\"" +
                        spec.TableContexts.Replace(",", "\",\"") + "\"]") +
                    ",\"presentation\":\"" + spec.Presentation + "\"," +
                    spec.Extra + "\"source\":\"gameplay\"}";
                if (!(bool)Invoke("RegisterAction", GameplayQoLPlugin.PluginId, spec.Id, definition))
                    return false;
            }
            return true;
        }

        private static string Binding(string path)
        {
            if (string.IsNullOrEmpty(path)) return "[]";
            char separator = path.Contains("+") ? '+' : path.Contains("|") ? '|' : '\0';
            string[] controls = separator == '\0' ? new[] { path } : path.Split(separator);
            string mode = separator == '+' ? "chord" : separator == '|' ? "directional" : "single";
            return "[{\"mode\":\"" + mode + "\",\"controls\":[\"" +
                string.Join("\",\"", controls) + "\"]}]";
        }

        private void Publish()
        {
            GameplayQoLPlugin plugin = GameplayQoLPlugin.Instance;
            if (plugin == null) return;
            var padlock = GetComponent<PadlockModule>();
            var checkpoint = GetComponent<CheckpointModule>();
            var dialogue = GetComponent<DialogueModule>();
            var sprint = GetComponent<SprintModule>();
            var signs = GetComponent<ForestSignToggleModule>();
            var title = GetComponent<TitleCheckpointMenu>();
            string context = title != null && title.Active ? "title" :
                padlock != null && padlock.IsOpen ? "padlock" :
                checkpoint != null && checkpoint.IsOpen ? "checkpoint" :
                dialogue != null && dialogue.ContextId != null ? dialogue.ContextId :
                SceneManager.GetActiveScene().name == "VillageScene" ? "village" : null;
            if (activeContext != null && activeContext != context)
                Invoke("ClearContext", GameplayQoLPlugin.PluginId, activeContext);
            activeContext = context;
            if (context == null) return;
            if (context == "title")
            {
                State("title.back", context, false, null, visible: title.BackHint);
                State("title.mode", context, false, null, visible: title.DestinationHints && title.HasDestinations);
                State("title.unlock", context, false, null, visible: title.DestinationHints);
                State("title.select", context, false, null, visible: title.SelectionHints);
                State("title.confirm", context, false, null, visible: title.SelectionHints);
            }
            else if (context == "village")
            {
                State("sprint.hold", context, false, null);
                State("sprint.toggle", context, sprint != null && sprint.Toggled, null);
                State("checkpoint.open", context, false, null);
                State("signs.glow_toggle", context, false, null, signs != null && signs.Available);
            }
            else if (context == "checkpoint")
            {
                State("checkpoint.select", context, false, null);
                State("checkpoint.confirm", context, false, null);
                State("checkpoint.mode", context, false, null, visible: checkpoint.DestinationHints);
                State("checkpoint.unlock", context, false, null, visible: checkpoint.DestinationHints);
                State("checkpoint.back", context, false, null);
            }
            else if (context == "padlock")
            {
                State("padlock.select", context, false, null);
                State("padlock.confirm", context, false, null);
                State("padlock.back", context, false, null);
            }
            else
                State("dialogue.skip", context, false, dialogue == null ? 0f : dialogue.Progress);
        }

        private void State(string actionId, string context, bool toggled, float? progress, bool available = true, bool visible = true)
        {
            string json = "{\"schema\":2,\"instanceId\":\"" + instanceId +
                "\",\"generation\":" + generation + ",\"sceneEpoch\":" + epoch +
                ",\"sequence\":" + sequence++ + ",\"contextId\":\"" + context +
                "\",\"visible\":" + (available && visible && actionId != "signs.glow_toggle" ? "true" : "false") +
                ",\"enabled\":" + (available ? "true" : "false") + ",\"alpha\":1" +
                (actionId == "sprint.toggle" ? ",\"toggled\":" + (toggled ? "true" : "false") : "") +
                (progress.HasValue ? ",\"progress\":" + progress.Value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) : "") + "}";
            if (!(bool)Invoke("UpdateActionState", GameplayQoLPlugin.PluginId, actionId, json))
                throw new InvalidOperationException("UIControls rejected state " + actionId);
        }

        private object Invoke(string name, params object[] args)
        {
            if (endpoint == null || endpointType == null) throw new InvalidOperationException("UIControls unavailable");
            MethodInfo method = endpointType.GetMethod(name, BindingFlags.Instance | BindingFlags.Public);
            if (method == null) throw new MissingMethodException(endpointType.FullName, name);
            return method.Invoke(endpoint, args);
        }

        private void Disconnect()
        {
            Ready = false;
            endpoint = null;
            endpointType = null;
            activeContext = null;
            nextConnect = Time.unscaledTime + 0.5f;
        }

        private void OnDestroy()
        {
            if (endpoint != null)
                try { Invoke("UnregisterOwner", GameplayQoLPlugin.PluginId); }
                catch (Exception ex) { GameplayQoLPlugin.Log?.LogWarning("UIControls unregister failed: " + ex.Message); }
            Disconnect();
        }
    }
}
