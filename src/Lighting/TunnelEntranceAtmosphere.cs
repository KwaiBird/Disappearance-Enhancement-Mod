using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Disappearance.Lighting
{
    // The identifiers below are from the extracted VillageScene. No global light,
    // renderer, collider, or shared-material mutation is allowed here.
    internal sealed class TunnelEntranceAtmosphere
    {
        private Transform gate, player;
        private Scene scene;
        private Vector3 entrance, outward;
        private float startDistance;
        private MeshRenderer wood;
        private Material[] originals, replacements;

        internal void Capture(Scene target, Action<string> log)
        {
            Restore();
            if (target.name != "VillageScene") return;
            foreach (MeshFilter filter in Resources.FindObjectsOfTypeAll<MeshFilter>())
            {
                if (filter.gameObject.scene != target || filter.name != "TunnelGate" ||
                    filter.transform.parent == null || filter.transform.parent.name != "Forest" ||
                    filter.sharedMesh == null || filter.sharedMesh.name != "wall_s_e") continue;
                Bounds actual = filter.sharedMesh.bounds, expected = EmitterGeometry.EntranceBounds;
                if ((actual.center - expected.center).sqrMagnitude > .01f ||
                    (actual.size - expected.size).sqrMagnitude > .01f) continue;
                Transform board = filter.transform.parent.Find("Boards/Board (4)");
                Transform door = filter.transform.Find("FenceDoor");
                Transform plank = filter.transform.Find("Fence/jov_tunnel01.close");
                if (board == null || door == null || plank == null) continue;
                gate = filter.transform;
                scene = target;
                entrance = gate.TransformPoint(new Vector3(0, 0, door.localPosition.z));
                outward = gate.forward; outward.y = 0; outward.Normalize();
                Vector3 toKey = board.position - entrance; toKey.y = 0;
                if (Vector3.Dot(toKey, outward) <= 0) { Restore(); continue; }
                startDistance = TunnelApproachFade.StartDistance(toKey.magnitude);
                GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
                if (playerObject != null && playerObject.scene == target) player = playerObject.transform;
                // The wood faces inward in the original mesh. A local two-sided
                // clone lets the outside face write depth and clip overlapping
                // halo pixels while the wire fence openings remain visible.
                MeshFilter woodMesh = plank.GetComponent<MeshFilter>();
                wood = plank.GetComponent<MeshRenderer>();
                if (wood != null && woodMesh != null && woodMesh.sharedMesh != null &&
                    woodMesh.sharedMesh.name == "jov_tunnel01.close")
                {
                    originals = wood.sharedMaterials;
                    replacements = new Material[originals.Length];
                    for (int i = 0; i < originals.Length; i++)
                    {
                        if (originals[i] == null) continue;
                        replacements[i] = new Material(originals[i]) { name = originals[i].name + " (tunnel wood depth)" };
                        if (replacements[i].HasProperty("_Cull")) replacements[i].SetFloat("_Cull", (float)CullMode.Off);
                        if (replacements[i].HasProperty("_ZWrite")) replacements[i].SetFloat("_ZWrite", 1);
                    }
                    wood.sharedMaterials = replacements;
                }
                log("Tunnel approach scope: VillageScene/Forest/TunnelGate, entrance=" + entrance.ToString("F3") +
                    ", key distance=" + toKey.magnitude.ToString("F2") + ", fade start=" + startDistance.ToString("F2") +
                    ", full=" + TunnelApproachFade.FullDistance + ", wood=" + (originals != null));
                return;
            }
            log("Tunnel approach scope unavailable; distance fade not applied.");
        }

        internal bool Owns(Transform emitter)
        {
            bool targetScene = gate != null && scene.IsValid() &&
                scene == SceneManager.GetActiveScene() && scene.name == "VillageScene";
            return targetScene && emitter != null && emitter.gameObject.scene == scene &&
                (emitter == gate || emitter.IsChildOf(gate));
        }

        internal float Strength(Transform emitter)
        {
            if (player == null || !Owns(emitter)) return 1f;
            Vector3 delta = player.position - entrance; delta.y = 0;
            return TunnelApproachFade.Evaluate(true, true,
                Vector3.Dot(delta, outward) > 0, delta.magnitude, startDistance);
        }

        internal void Restore()
        {
            if (wood != null && originals != null) wood.sharedMaterials = originals;
            if (replacements != null)
                foreach (Material material in replacements) if (material != null) Object.Destroy(material);
            gate = player = null; wood = null; originals = replacements = null;
            scene = default; startDistance = 0;
        }
    }
}
