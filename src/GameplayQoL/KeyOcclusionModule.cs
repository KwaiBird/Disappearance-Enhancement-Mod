using System;
using System.Reflection;
using HarmonyLib;
using InstantHorror.Scripts;
using InstantHorror.Scripts.InteractionSystems;
using InstantHorror.Scripts.InteractionSystems.PickableItems;
using InstantHorror.Scripts.InteractionSystems.Lock;
using UnityEngine;
using UnityEngine.SceneManagement;
using GamePlayerInput = InstantHorror.Scripts.PlayerInput;

namespace Disappearance.GameplayQoL
{
    internal sealed class KeyOcclusionModule : MonoBehaviour
    {
        private static readonly PropertyInfo StateProperty = AccessTools.Property(typeof(GamePlayerInput), "GameStateHandler");
        private static readonly FieldInfo HitField = AccessTools.Field(typeof(GamePlayerInput), "currentHitItem");
        private static readonly FieldInfo CameraField = AccessTools.Field(typeof(GamePlayerInput), "cameraTransform");
        private static readonly FieldInfo MaskField = AccessTools.Field(typeof(GamePlayerInput), "interactableMask");
        private static readonly FieldInfo KeyTypeField = AccessTools.Field(typeof(DoorKey), "keyType");
        private static readonly FieldInfo InteractingField = AccessTools.Field(typeof(GamePlayerInput), "currentInteractableObj");
        private const float Range = 2f;
        private Harmony harmony;

        private void Awake()
        {
            harmony = new Harmony(GameplayQoLPlugin.PluginId + ".keyocclusion");
            try
            {
                var first = new HarmonyMethod(typeof(KeyOcclusionModule), nameof(BeforeInteract))
                    { priority = Priority.First };
                var marker = new HarmonyMethod(typeof(KeyOcclusionModule), nameof(BeforeMarker))
                    { priority = Priority.First };
                harmony.Patch(AccessTools.Method(typeof(GamePlayerInput), "InteractObj"), prefix: first);
                harmony.Patch(AccessTools.Method(typeof(GamePlayerInput), "ShowInteractableItemMarker"), prefix: marker);
                GameplayQoLPlugin.Log.LogInfo("Village altar key proximity interaction ready: range=2m, direct visibility required.");
            }
            catch (Exception ex)
            {
                harmony.UnpatchSelf();
                harmony = null;
                GameplayQoLPlugin.Log.LogError("Key occlusion patch failed: " + ex);
            }
        }

        private static bool BeforeInteract(GamePlayerInput __instance)
        {
            DoorKey key = FindKey(__instance, out bool blocked);
            if (key == null && !blocked)
                key = FindDirectForestKey(__instance, out blocked);
            if (blocked) return false;
            if (key == null) return true;
            InteractingField.SetValue(__instance, key.gameObject);
            key.Interact(__instance);
            return false;
        }

        private static bool BeforeMarker(GamePlayerInput __instance)
        {
            DoorKey key = FindKey(__instance, out bool blocked);
            if (key == null && !blocked) key = FindDirectForestKey(__instance, out blocked);
            GameObject previous = HitField?.GetValue(__instance) as GameObject;
            InteractableItemBase marker = previous == null ? null : previous.GetComponent<InteractableItemBase>();
            if (key == null && !blocked)
            {
                // A previous assisted key must lose focus even when the native ray now hits a door.
                if (marker is DoorKey oldKey && (TargetKey(marker.GetComponent<Collider>()) != null || ForestKeyTarget.IsForest(oldKey)))
                    marker.OnCursorProperty.Value = InteractableItemBase.OnCursor.Exit;
                return true;
            }
            if (key != null && previous == key.gameObject)
            {
                key.OnCursorProperty.Value = InteractableItemBase.OnCursor.Enter;
                key.ItemMarkerUI?.ShowMouseMarker();
                return false;
            }
            if (marker != null) marker.OnCursorProperty.Value = InteractableItemBase.OnCursor.Exit;
            HitField?.SetValue(__instance, key == null ? null : key.gameObject);
            if (key != null)
            {
                key.OnCursorProperty.Value = InteractableItemBase.OnCursor.Enter;
                key.ItemMarkerUI?.ShowMouseMarker();
            }
            return false;
        }

        private static DoorKey FindKey(GamePlayerInput player, out bool blocked)
        {
            blocked = false;
            if (player == null || SceneManager.GetActiveScene().name != "VillageScene" ||
                CameraField == null || MaskField == null || KeyTypeField == null || InteractingField == null ||
                (StateProperty?.GetValue(player) as GameStateHandler)?.CurrentState.Value != GameState.PlayGame)
                return null;
            Transform eye = CameraField.GetValue(player) as Transform;
            if (eye == null || !(MaskField.GetValue(player) is LayerMask mask)) return null;
            Ray sight = new Ray(eye.position, eye.forward);
            // Preserve native priority when the exact ray hits another interactable.
            if (Physics.Raycast(sight, out RaycastHit direct, Range, mask.value))
            {
                DoorKey exact = TargetKey(direct.collider);
                if (exact == null) return null;
                blocked = !InteractionMarkerVisibility.HasSight(eye.position, direct.point, exact.transform);
                return blocked ? null : exact;
            }
            DoorKey best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (Collider collider in Physics.OverlapSphere(eye.position, Range, mask.value, QueryTriggerInteraction.Ignore))
            {
                DoorKey candidate = TargetKey(collider);
                if (candidate == null) continue;
                Vector3 point = collider.ClosestPoint(eye.position);
                Vector3 delta = point - eye.position;
                if (delta.sqrMagnitude > Range * Range) continue;
                if (!InteractionMarkerVisibility.HasSight(eye.position, point, candidate.transform)) continue;
                if (delta.sqrMagnitude >= bestDistance) continue;
                best = candidate;
                bestDistance = delta.sqrMagnitude;
            }
            return best;
        }

        private static DoorKey TargetKey(Collider collider)
        {
            if (collider == null) return null;
            DoorKey key = collider.GetComponent<DoorKey>();
            return key != null && key.enabled && KeyTypeField.GetValue(key) is KeyType type &&
                type == KeyType.PoopGuyFens ? key : null;
        }

        private static DoorKey FindDirectForestKey(GamePlayerInput player, out bool blocked)
        {
            blocked = false;
            if (player == null || SceneManager.GetActiveScene().name != "VillageScene" ||
                CameraField == null || MaskField == null || KeyTypeField == null ||
                (StateProperty?.GetValue(player) as GameStateHandler)?.CurrentState.Value != GameState.PlayGame)
                return null;
            Transform eye = CameraField.GetValue(player) as Transform;
            return eye == null || !(MaskField.GetValue(player) is LayerMask mask) ? null :
                ForestKeyTarget.Resolve(eye, mask.value, Range, out blocked);
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            harmony = null;
        }
    }
}
