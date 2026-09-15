using FistVR;
using UnityEngine;

namespace Gundomizer
{
    internal static class PrefabGuard
    {
        // Unity logs Awake exceptions inside Instantiate without necessarily throwing back to its
        // caller. Catching the roll cannot undo a component that has already failed initialization.
        // Check the known native requirements before creating any instances.
        internal static string Problem(GameObject prefab)
        {
            foreach (var sight in prefab.GetComponentsInChildren<ReflexSightController>(true))
            {
                if (!ActiveOnSpawn(sight.transform, prefab.transform)) continue;
                bool missingComponent = false;
                if (sight.Components != null)
                    foreach (var component in sight.Components)
                        if (component == null) { missingComponent = true; break; }
                string problem = SpawnSafetyPolicy.ReflexSightProblem(sight.UISpawnPoint != null,
                    sight.Components != null, missingComponent);
                if (problem != null) return sight.name + ": " + problem;
            }
            return null;
        }

        private static bool ActiveOnSpawn(Transform current, Transform root)
        {
            // The spawned root is explicitly activated. Dormant optional children do not run Awake
            // during this spawn; a disabled component on an active child still does run Awake.
            while (current != null && current != root)
            {
                if (!current.gameObject.activeSelf) return false;
                current = current.parent;
            }
            return current == root;
        }
    }
}
