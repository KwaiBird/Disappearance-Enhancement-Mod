using System;
using HarmonyLib;
using InstantHorror.Scripts;
using InstantHorror.Scripts.AudioManagementSystems;
using InstantHorror.Scripts.GameEventSystems.FadeInOut;
using InstantHorror.Scripts.InteractionSystems.Door;
using InstantHorror.Scripts.SceneManagementSystems;
using UnityEngine;

namespace Disappearance.GameplayQoL
{
    // The additional point reconstructs only this encounter, not arbitrary village progress.
    internal sealed class CrewCheckpoint
    {
        private EncounterWithTvCrew encounter;
        private FenceDoor gate;
        internal bool Completed { get; private set; }

        internal void Reset() { encounter = null; gate = null; Completed = false; }

        private bool Resolve()
        {
            if (encounter == null) encounter = UnityEngine.Object.FindObjectOfType<EncounterWithTvCrew>();
            if (gate == null)
                foreach (var candidate in UnityEngine.Object.FindObjectsOfType<FenceDoor>())
                    if ((SceneDependentGameEvent)AccessTools.Field(typeof(FenceDoor), "gameEvent").GetValue(candidate) == SceneDependentGameEvent.EncounterWithTvCrew)
                    { gate = candidate; break; }
            return encounter != null && gate != null && gate.gameObject.scene == encounter.gameObject.scene;
        }

        private T Field<T>(string name) => (T)AccessTools.Field(typeof(EncounterWithTvCrew), name).GetValue(encounter);

        internal bool IsDepartureText(TextEvent text) => Resolve() && text != null && text == Field<TextEvent>("textEvent");
        internal bool HidesMarker(InteractableItemBase owner) => Completed && gate != null && owner == gate;

        internal void Complete()
        {
            if (!Resolve()) return;
            Completed = true;
            // The native door already consumed this encounter on natural arrival.
            AccessTools.Field(typeof(FenceDoor), "isInteracted").SetValue(gate, true);
        }

        internal bool Restore(PlayerSpawnHandler spawn)
        {
            if (!Resolve()) return false;
            var padlock = AccessTools.Field(typeof(DoorBase), "lockBase").GetValue(gate) as LockBase;
            var flags = UnityEngine.Object.FindObjectOfType<GameEventFlagHandler>();
            var player = UnityEngine.Object.FindObjectOfType<StarterAssets.FirstPersonController>();
            var input = UnityEngine.Object.FindObjectOfType<InstantHorror.Scripts.PlayerInput>();
            var fade = UnityEngine.Object.FindObjectOfType<FadeInOutUIHandler>();
            var position = Field<Transform>("setPlayerPosition");
            var controller = Field<CharacterController>("characterController");
            var conversation = Field<GameObject>("conversationTvCrews");
            var timeline = Field<GameObject>("tvCrewsTimeline");
            var audio = UnityEngine.Object.FindObjectOfType<AudioPlayHandler>();
            if (padlock == null || flags == null || player == null || input == null || fade == null ||
                position == null || controller == null || conversation == null || timeline == null || audio == null) return false;
            var state = AccessTools.Property(typeof(InstantHorror.Scripts.PlayerInput), "GameStateHandler").GetValue(input) as GameStateHandler;
            if (state == null) return false;
            state.ChangeState(GameState.OnEvent);
            padlock.IsUnlocked = true;
            flags.OpenedEncounterWithTvCrewFenceDoor = true;
            AccessTools.Field(typeof(FenceDoor), "isInteracted").SetValue(gate, true);
            AccessTools.Field(typeof(DoorBase), "isOpen").SetValue(gate, true);
            gate.GetComponent<Animator>().SetTrigger("Open");
            conversation.SetActive(false);
            timeline.SetActive(false);
            controller.enabled = false;
            player.transform.SetPositionAndRotation(position.position, position.rotation);
            controller.enabled = true;
            audio.PlayBGM(BackgroundMusicType.ForestAmbient);
            Completed = true;
            fade.FadeIn(() =>
            {
                if (player == null || spawn == null || gate == null) return;
                CheckpointModule.FinishCrewRestore(gate.gameObject.scene.handle);
                state.ChangeState(GameState.PlayGame);
                spawn.SetCanMove(true);
            });
            return true;
        }
    }
}
