using System;
using System.Collections;
using System.Reflection;
using Disappearance.Shared;
using HarmonyLib;
using InstantHorror.Scripts.Characters;
using InstantHorror.Scripts.InteractionSystems;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using GamePlayerInput = InstantHorror.Scripts.PlayerInput;

namespace Disappearance.GameplayQoL
{
    internal sealed class DialogueModule : MonoBehaviour
    {
        private const string OpeningScene = "OpeningTextScene";
        private const string EndingScene = "EndingTextScene";
        private const float HoldSeconds = 1f;
        private enum SkipContext { None, Opening, Ending, Conversation, TextEvent }
        private enum HoldSource { None, Space, GamepadEast }
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static DialogueModule instance;
        private static FieldInfo bottomBarField, finishedField, completedField, barTextField, barStateField;
        private static FieldInfo textEndField, fadeImageField, currentInteractableField;
        private static FieldInfo conversationTextField, conversationSentencesField, conversationIndexField;
        private static FieldInfo eventTextField, eventSentencesField, eventIndexField;
        private static PropertyInfo textProperty;
        private static MethodInfo showTextEnd, finishOpening, finishEnding;
        private static object openingBar, endingBar;
        private static MonoBehaviour openingController, endingController;
        private static GamePlayerInput villageInput;
        private static CharacterConversationUIHandler conversationUi;
        private static TextEventUIHandler eventUi;
        private Harmony harmony;
        private SkipContext context;
        private HoldSource source;
        private float holdTime;
        private bool triggered;
        private bool completionErrorLogged, fadeErrorLogged, skipErrorLogged;

        internal string ContextId => context == SkipContext.Opening ? "novel_opening" :
            context == SkipContext.Ending ? "novel_ending" :
            context == SkipContext.Conversation ? "conversation" :
            context == SkipContext.TextEvent ? "text_event" : null;
        internal float Progress => Mathf.Clamp01(holdTime / HoldSeconds);

        private void Awake()
        {
            instance = this;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Type openingType = AccessTools.TypeByName("InstantHorror.Scripts.OpeningTextSceneHandler");
            Type endingType = AccessTools.TypeByName("InstantHorror.Scripts.EndingTextSceneHandler");
            Type novelType = AccessTools.TypeByName("InstantHorror.Scripts.Novel.NovelControllerBase");
            Type barType = AccessTools.TypeByName("InstantHorror.Scripts.Novel.BottomBarController");
            Type promptType = AccessTools.TypeByName("TextEndImageHandler");
            Type fadeType = AccessTools.TypeByName("InstantHorror.Scripts.GameEventSystems.FadeInOut.FadeInOutUIHandler");
            if (openingType == null || endingType == null || novelType == null || barType == null ||
                promptType == null || fadeType == null)
            {
                GameplayQoLPlugin.Log.LogError("Dialogue game types were not found.");
                return;
            }
            bottomBarField = novelType.GetField("bottomBar", Flags);
            finishedField = openingType.GetField("isFinished", Flags);
            completedField = endingType.GetField("isCompleted", Flags);
            barTextField = barType.GetField("barText", Flags);
            barStateField = barType.GetField("state", Flags);
            textEndField = barType.GetField("textEndImage", Flags);
            fadeImageField = fadeType.GetField("fadeImage", Flags);
            currentInteractableField = typeof(GamePlayerInput).GetField("currentInteractableObj", Flags);
            conversationTextField = typeof(CharacterConversationUIHandler).GetField("text", Flags);
            conversationSentencesField = typeof(CharacterConversationUIHandler).GetField("currentSentences", Flags);
            conversationIndexField = typeof(CharacterConversationUIHandler).GetField("sentenceIndex", Flags);
            eventTextField = typeof(TextEventUIHandler).GetField("text", Flags);
            eventSentencesField = typeof(TextEventUIHandler).GetField("currentSentences", Flags);
            eventIndexField = typeof(TextEventUIHandler).GetField("sentenceIndex", Flags);
            textProperty = typeof(TMP_Text).GetProperty("text", Flags);
            showTextEnd = promptType.GetMethod("Show", Flags);
            finishOpening = openingType.GetMethod("OnFinishedSentence", Flags);
            finishEnding = endingType.GetMethod("OnFinishedSentence", Flags);
            MethodInfo openingStart = AccessTools.Method(openingType, "Start", Type.EmptyTypes);
            MethodInfo endingStart = AccessTools.Method(endingType, "Start", Type.EmptyTypes);
            MethodInfo typeText = AccessTools.Method(barType, "TypeText", new[] { typeof(string) });
            MethodInfo fadeAwake = AccessTools.Method(fadeType, "Awake", Type.EmptyTypes);
            if (bottomBarField == null || finishedField == null || completedField == null ||
                barTextField == null || barStateField == null || textEndField == null ||
                fadeImageField == null || currentInteractableField == null ||
                conversationTextField == null || conversationSentencesField == null || conversationIndexField == null ||
                eventTextField == null || eventSentencesField == null || eventIndexField == null ||
                textProperty == null || showTextEnd == null || finishOpening == null || finishEnding == null ||
                openingStart == null || endingStart == null || typeText == null || fadeAwake == null)
            {
                GameplayQoLPlugin.Log.LogError("Dialogue game members were not found.");
                return;
            }
            harmony = new Harmony(GameplayQoLPlugin.PluginId + ".dialogue");
            try
            {
                harmony.Patch(openingStart, prefix: new HarmonyMethod(typeof(DialogueModule), nameof(CaptureOpening)));
                harmony.Patch(endingStart, prefix: new HarmonyMethod(typeof(DialogueModule), nameof(CaptureEnding)));
                harmony.Patch(typeText, prefix: new HarmonyMethod(typeof(DialogueModule), nameof(CompleteNovelText)));
                harmony.Patch(fadeAwake, prefix: new HarmonyMethod(typeof(DialogueModule), nameof(SkipOpeningFade)));
                GameplayQoLPlugin.Log.LogInfo("Dialogue module ready: instant novel text and one-second skip.");
            }
            catch (Exception ex)
            {
                harmony.UnpatchSelf();
                harmony = null;
                GameplayQoLPlugin.Log.LogError("Dialogue patches failed: " + ex);
            }
        }

        private static void CaptureOpening(object __instance)
        {
            openingController = __instance as MonoBehaviour;
            openingBar = bottomBarField.GetValue(__instance);
        }

        private static void CaptureEnding(object __instance)
        {
            endingController = __instance as MonoBehaviour;
            endingBar = bottomBarField.GetValue(__instance);
        }

        private void Update()
        {
            SkipContext next = GetContext();
            if (next != context)
            {
                context = next;
                ResetHold();
            }
            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;
            HoldSource held = keyboard != null && keyboard.spaceKey.isPressed ? HoldSource.Space :
                gamepad != null && gamepad.buttonEast.isPressed ? HoldSource.GamepadEast : HoldSource.None;
            if (held == HoldSource.None || context == SkipContext.None)
            {
                ResetHold();
                return;
            }
            if (held != source)
            {
                ResetHold();
                source = held;
                return;
            }
            if (triggered) return;
            holdTime = Mathf.Min(HoldSeconds, holdTime + Time.unscaledDeltaTime);
            if (holdTime < HoldSeconds) return;
            triggered = true;
            try
            {
                switch (context)
                {
                    case SkipContext.Opening: finishOpening.Invoke(openingController, null); StopBgm(); break;
                    case SkipContext.Ending: finishEnding.Invoke(endingController, null); StopBgm(); break;
                    case SkipContext.Conversation: SkipConversation(); break;
                    case SkipContext.TextEvent: SkipEvent(); break;
                }
                holdTime = 0f;
            }
            catch (Exception ex)
            {
                if (!skipErrorLogged) { skipErrorLogged = true; GameplayQoLPlugin.Log.LogError("Dialogue skip failed: " + ex); }
            }
        }

        private void ResetHold()
        {
            source = HoldSource.None;
            holdTime = 0f;
            triggered = false;
        }

        private static SkipContext GetContext()
        {
            if (openingController != null && openingController.isActiveAndEnabled &&
                openingController.gameObject.scene.name == OpeningScene &&
                !(bool)finishedField.GetValue(openingController)) return SkipContext.Opening;
            if (endingController != null && endingController.isActiveAndEnabled &&
                endingController.gameObject.scene.name == EndingScene &&
                !(bool)completedField.GetValue(endingController)) return SkipContext.Ending;
            if (SceneManager.GetActiveScene().name != "VillageScene") return SkipContext.None;
            if (villageInput == null) villageInput = FindObjectOfType<GamePlayerInput>();
            if (villageInput == null) return SkipContext.None;
            if (conversationUi == null) conversationUi = FindObjectOfType<CharacterConversationUIHandler>();
            if (eventUi == null) eventUi = FindObjectOfType<TextEventUIHandler>();
            TMP_Text conversationText = conversationUi == null ? null : conversationTextField.GetValue(conversationUi) as TMP_Text;
            if (conversationText != null && conversationText.gameObject.activeInHierarchy) return SkipContext.Conversation;
            TMP_Text eventText = eventUi == null ? null : eventTextField.GetValue(eventUi) as TMP_Text;
            return eventText != null && eventText.gameObject.activeInHierarchy ? SkipContext.TextEvent : SkipContext.None;
        }

        private static int Remaining(object handler, FieldInfo sentencesField, FieldInfo indexField)
        {
            ICollection sentences = sentencesField.GetValue(handler) as ICollection;
            int index = (int)indexField.GetValue(handler);
            return sentences == null || index < 0 || index >= sentences.Count ? 0 : sentences.Count - index;
        }

        private static void SkipConversation()
        {
            GameObject target = currentInteractableField.GetValue(villageInput) as GameObject;
            CharacterBase character = target == null ? null : target.GetComponent<CharacterBase>();
            if (character == null) throw new InvalidOperationException("Speaking character not found.");
            int remaining = Remaining(conversationUi, conversationSentencesField, conversationIndexField);
            for (int i = 0; i < remaining; i++) character.ShowNextSentence();
        }

        private static void SkipEvent()
        {
            int remaining = Remaining(eventUi, eventSentencesField, eventIndexField);
            for (int i = 0; i < remaining; i++) eventUi.ShowNextSentence();
        }

        private static void StopBgm()
        {
            AudioPlayHandler audio = FindObjectOfType<AudioPlayHandler>();
            if (audio != null) audio.StopBGM();
        }

        private static bool SkipOpeningFade(object __instance)
        {
            Component component = __instance as Component;
            if (component == null || component.gameObject.scene.name != OpeningScene) return true;
            try
            {
                Image image = fadeImageField.GetValue(__instance) as Image;
                if (image == null) return true;
                Color color = image.color;
                color.a = 0f;
                image.color = color;
                return false;
            }
            catch (Exception ex)
            {
                if (instance != null && !instance.fadeErrorLogged)
                { instance.fadeErrorLogged = true; GameplayQoLPlugin.Log.LogError("Opening fade skip failed: " + ex); }
                return true;
            }
        }

        private static bool CompleteNovelText(object __instance, string __0, ref IEnumerator __result)
        {
            if (!ReferenceEquals(__instance, openingBar) && !ReferenceEquals(__instance, endingBar)) return true;
            try
            {
                textProperty.SetValue(barTextField.GetValue(__instance), __0, null);
                barStateField.SetValue(__instance, Enum.ToObject(barStateField.FieldType, 1));
                showTextEnd.Invoke(textEndField.GetValue(__instance), null);
                __result = EmptyCoroutine();
                return false;
            }
            catch (Exception ex)
            {
                if (instance != null && !instance.completionErrorLogged)
                { instance.completionErrorLogged = true; GameplayQoLPlugin.Log.LogError("Novel instant text failed: " + ex); }
                return true;
            }
        }

        private static IEnumerator EmptyCoroutine() { yield break; }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            context = SkipContext.None;
            ResetHold();
            openingBar = endingBar = null;
            openingController = endingController = null;
            villageInput = null;
            conversationUi = null;
            eventUi = null;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            harmony?.UnpatchSelf();
            OnSceneLoaded(default, LoadSceneMode.Single);
            if (instance == this) instance = null;
        }
    }
}
