using System.Reflection;
using HarmonyLib;
using InstantHorror.Scripts.InteractionSystems.Lock;
using InstantHorror.Scripts.InteractionSystems.PickableItems;
using UnityEngine;

namespace Disappearance.GameplayQoL
{
    // The forest key is mounted on a board whose physical collider extends in front of its mesh.
    // Ignore only that mounting surface; unrelated walls and neighbouring boards still occlude it.
    internal static class ForestKeyTarget
    {
        private static readonly FieldInfo TypeField = AccessTools.Field(typeof(DoorKey), "keyType");
        internal static bool IsForest(DoorKey key) => key != null && key.isActiveAndEnabled &&
            TypeField.GetValue(key) is KeyType type && type == KeyType.TunnelFens;

        internal static Collider MountingSurface(DoorKey key)
        {
            if (!IsForest(key)) return null;
            Transform board = key.transform.parent;
            return board != null && board.parent != null && board.parent.name == "Boards" &&
                board.parent.parent != null && board.parent.parent.name == "Forest"
                ? board.GetComponent<Collider>() : null;
        }

        internal static bool HasSight(Vector3 origin, Vector3 point, DoorKey key) =>
            InteractionMarkerVisibility.HasSight(origin, point, key.transform, mountingSurface: MountingSurface(key));

        internal static DoorKey Resolve(Transform eye, int mask, float range, out bool blocked)
        {
            blocked = false;
            Ray ray = new Ray(eye.position, eye.forward);
            if (Physics.Raycast(ray, out RaycastHit exact, range, mask, QueryTriggerInteraction.Ignore))
            {
                DoorKey key = exact.collider.GetComponent<DoorKey>();
                if (!IsForest(key)) return null;
                blocked = !HasSight(eye.position, exact.point, key);
                return blocked ? null : key;
            }
            // Match the marker and acquisition using the same modest aim allowance.
            if (!Physics.SphereCast(ray, .12f, out RaycastHit assisted, range, mask, QueryTriggerInteraction.Ignore)) return null;
            DoorKey candidate = assisted.collider.GetComponent<DoorKey>();
            if (!IsForest(candidate) || (assisted.point - eye.position).sqrMagnitude > range * range) return null;
            blocked = !HasSight(eye.position, assisted.point, candidate);
            return blocked ? null : candidate;
        }
    }
}
