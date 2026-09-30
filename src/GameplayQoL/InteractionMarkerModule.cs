using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using InstantHorror.Scripts.InteractionSystems;
using InstantHorror.Scripts.InteractionSystems.Door;
using InstantHorror.Scripts.InteractionSystems.DialPadlock;
using InstantHorror.Scripts.InteractionSystems.PickableItems;
using UnityEngine;

namespace Disappearance.GameplayQoL
{
    internal sealed class InteractionMarkerModule : MonoBehaviour
    {
        private static InteractionMarkerModule instance;
        private readonly List<InteractionMarkerVisibility> markers = new List<InteractionMarkerVisibility>();
        private Harmony harmony;

        private void Awake()
        {
            instance = this;
            harmony = new Harmony(GameplayQoLPlugin.PluginId + ".interactionmarkers");
            try
            {
                harmony.Patch(AccessTools.Method(typeof(ItemMarkerUIHandler), "InstantiateItemMarkerUI"),
                    prefix: new HarmonyMethod(typeof(InteractionMarkerModule), nameof(BeforeCreate)),
                    postfix: new HarmonyMethod(typeof(InteractionMarkerModule), nameof(AfterCreate)));
                GameplayQoLPlugin.Log.LogInfo("Key/fusuma marker occlusion ready; fusuma closed=single center, open=panel centers; padlock gate marker reuse/range ready.");
            }
            catch (Exception exception)
            {
                harmony.UnpatchSelf();
                GameplayQoLPlugin.Log.LogError("Interaction marker patch failed: " + exception);
            }
        }

        private static readonly FieldInfo LockField = AccessTools.Field(typeof(DoorBase), "lockBase");

        private static bool IsPadlockGate(InteractableItemBase owner) =>
            owner is FenceDoor && LockField?.GetValue(owner) is DialPadlock;

        private static bool BeforeCreate(Transform parent, ref ItemMarkerUI __result)
        {
            InteractableItemBase owner = parent == null ? null : parent.GetComponent<InteractableItemBase>();
            if (!IsPadlockGate(owner) || owner.ItemMarkerUI == null) return true;
            // Re-entering a trigger after the crew's controller disable/teleport must not
            // replace the native reference while leaving its previous marker behind.
            __result = owner.ItemMarkerUI;
            return false;
        }

        private static void AfterCreate(Transform parent, ItemMarkerUI __result)
        {
            if (instance == null || __result == null || parent == null) return;
            InteractableItemBase owner = parent.GetComponent<InteractableItemBase>();
            bool gate = IsPadlockGate(owner);
            if (!(owner is DoorKey) && !(owner is FusumaDoorChild) && !gate) return;
            Collider collider = owner.GetComponent<Collider>();
            if (collider == null || collider.isTrigger) return;
            if (__result.GetComponent<InteractionMarkerVisibility>() != null) return;
            var visibility = __result.gameObject.AddComponent<InteractionMarkerVisibility>();
            visibility.Initialize(collider, owner is FusumaDoorChild, gate);
            instance.markers.Add(visibility);
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            foreach (var marker in markers)
                if (marker != null) { marker.Restore(); Destroy(marker); }
            markers.Clear();
            if (instance == this) instance = null;
        }
    }

    // Changes canvas.enabled only; native game-state canvas activation remains authoritative.
    internal sealed class InteractionMarkerVisibility : MonoBehaviour
    {
        private static readonly FieldInfo CanvasField = AccessTools.Field(typeof(ItemMarkerUI), "canvas");
        private static readonly FieldInfo LeftField = AccessTools.Field(typeof(FusumaDoorParent), "leftDoor");
        private static readonly FieldInfo RightField = AccessTools.Field(typeof(FusumaDoorParent), "rightDoor");
        private static readonly FieldInfo OpenField = AccessTools.Field(typeof(DoorBase), "isOpen");
        private static readonly FieldInfo MaskField = AccessTools.Field(typeof(InstantHorror.Scripts.PlayerInput), "interactableMask");
        private Collider target;
        private InteractableItemBase owner;
        private Canvas canvas;
        private bool originalEnabled;
        private Vector3 originalLocalPosition;
        private bool centerPanel;
        private bool padlockGate;
        private int gateMask = Physics.DefaultRaycastLayers;
        private bool ready;

        internal void Initialize(Collider collider, bool isPanel, bool isGate)
        {
            target = collider;
            owner = collider.GetComponent<InteractableItemBase>();
            centerPanel = isPanel;
            padlockGate = isGate;
            if (isGate)
            {
                var player = FindObjectOfType<InstantHorror.Scripts.PlayerInput>();
                if (player != null && MaskField?.GetValue(player) is LayerMask mask)
                    gateMask = mask.value;
            }
            canvas = CanvasField?.GetValue(GetComponent<ItemMarkerUI>()) as Canvas;
            if (canvas == null) return;
            originalEnabled = canvas.enabled;
            originalLocalPosition = transform.localPosition;
            ready = true;
            LateUpdate();
        }

        private void LateUpdate()
        {
            if (!ready || target == null || canvas == null) return;
            Camera eye = Camera.main;
            if (eye == null) { canvas.enabled = false; return; }
            if (centerPanel && UpdatePanel(eye.transform.position)) return;
            Vector3 point = target.ClosestPoint(eye.transform.position);
            if (padlockGate)
            {
                if (CheckpointModule.HideCrewGateMarker(owner)) { canvas.enabled = false; return; }
                bool near = (point - eye.transform.position).sqrMagnitude <= 9f;
                ItemMarkerUI ui = GetComponent<ItemMarkerUI>();
                bool aimed = near && owner.OnCursorProperty.Value == InteractableItemBase.OnCursor.Enter &&
                    Physics.Raycast(eye.transform.position, eye.transform.forward,
                    out RaycastHit hit, 2f, gateMask, QueryTriggerInteraction.Ignore) &&
                    hit.collider == target;
                if (aimed) ui.ShowMouseMarker(); else ui.ShowInteractableMarker();
                canvas.enabled = originalEnabled && near && target.enabled &&
                    HasSight(eye.transform.position, point, target.transform);
                return;
            }
            canvas.enabled = originalEnabled && target.enabled &&
                (owner is DoorKey key ? ForestKeyTarget.HasSight(eye.transform.position, point, key) :
                HasSight(eye.transform.position, point, target.transform));
        }

        private bool UpdatePanel(Vector3 eye)
        {
            FusumaDoorParent pair = owner.GetComponentInParent<FusumaDoorParent>();
            FusumaDoorChild left = pair == null ? null : LeftField?.GetValue(pair) as FusumaDoorChild;
            FusumaDoorChild right = pair == null ? null : RightField?.GetValue(pair) as FusumaDoorChild;
            Collider leftCollider = left == null ? null : left.GetComponent<Collider>();
            Collider rightCollider = right == null ? null : right.GetComponent<Collider>();
            if (leftCollider == null || rightCollider == null || OpenField == null)
            {
                transform.position = target.bounds.center;
                return false;
            }
            bool open = (bool)OpenField.GetValue(pair);
            ItemMarkerUI ui = GetComponent<ItemMarkerUI>();
            bool focused = owner.OnCursorProperty.Value == InteractableItemBase.OnCursor.Enter;
            Vector3 point;
            if (open)
            {
                point = target.bounds.center;
            }
            else
            {
                // Reuse one live native marker even when only one panel's proximity trigger is entered.
                ItemMarkerUI chosen = Available(left.ItemMarkerUI) ? left.ItemMarkerUI : right.ItemMarkerUI;
                if (ui != chosen) { canvas.enabled = false; return true; }
                point = (leftCollider.bounds.center + rightCollider.bounds.center) * 0.5f;
                focused = left.OnCursorProperty.Value == InteractableItemBase.OnCursor.Enter ||
                    right.OnCursorProperty.Value == InteractableItemBase.OnCursor.Enter;
            }
            transform.position = point;
            if (focused) ui.ShowMouseMarker(); else ui.ShowInteractableMarker();
            canvas.enabled = originalEnabled && target.enabled &&
                HasSight(eye, point, pair.transform, leftCollider, rightCollider);
            return true;
        }

        private static bool Available(ItemMarkerUI marker) => marker != null && marker.gameObject.activeInHierarchy;

        internal static bool HasSight(Vector3 origin, Vector3 point, Transform owner,
            Collider leftPanel = null, Collider rightPanel = null, Collider mountingSurface = null)
        {
            Vector3 delta = point - origin;
            float distance = delta.magnitude;
            if (distance <= 0.001f) return true;
            foreach (RaycastHit hit in Physics.RaycastAll(origin, delta / distance,
                distance - 0.001f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Transform obstacle = hit.collider.transform;
                if (hit.collider == mountingSurface) continue;
                if (obstacle == owner || obstacle.IsChildOf(owner)) continue;
                // The extracted fusuma has separate front/back door groups. Its coincident reverse
                // surface is part of the same physical panel, not an external wall hiding the marker.
                if (hit.collider.GetComponent<FusumaDoorChild>() != null &&
                    (SamePanelSurface(hit.collider, leftPanel) || SamePanelSurface(hit.collider, rightPanel))) continue;
                return false;
            }
            return true;
        }

        private static bool SamePanelSurface(Collider hit, Collider panel) => panel != null &&
            (hit.bounds.center - panel.bounds.center).sqrMagnitude < 0.01f &&
            (hit.bounds.size - panel.bounds.size).sqrMagnitude < 0.01f;

        internal void Restore()
        {
            if (!ready) return;
            if (canvas != null) canvas.enabled = originalEnabled;
            transform.localPosition = originalLocalPosition;
            ready = false;
        }

        private void OnDisable()
        {
            // Native pause/dialogue can disable the canvas root temporarily. Keep tracking on resume.
            if (!ready) return;
            if (canvas != null) canvas.enabled = originalEnabled;
            transform.localPosition = originalLocalPosition;
        }
    }
}
