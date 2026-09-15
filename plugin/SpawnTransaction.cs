using System;
using FistVR;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gundomizer
{
    internal static class SpawnTransaction
    {
        internal static bool TrySpawn(GameObject prefab, Vector3 position, Quaternion rotation,
            ItemSpawnerID entry, out GameObject spawned, out string problem)
        {
            spawned = null;
            problem = null;
            if (prefab == null) { problem = "The prefab is missing."; return false; }
            GameObject staging = null;
            GameObject clone = null;
            string failure = null;
            Application.LogCallback capture = (message, stack, type) =>
            {
                // Unity can log Awake/OnEnable exceptions without throwing out of Instantiate.
                // Observe only this synchronous activation, and keep the original log visible.
                if (type == LogType.Exception && failure == null) failure = message + "\n" + stack;
            };
            Application.logMessageReceived += capture;
            try
            {
                staging = new GameObject("Gundomizer spawn staging");
                staging.SetActive(false);
                // Inactive ancestry defers Awake, so the instance is known and can be removed
                // even if a component fails when we activate it. Never modify the shared prefab.
                clone = Object.Instantiate(prefab, staging.transform, false);
                clone.SetActive(false);
                // Awake must see the same root/hierarchy as an ordinary native spawn.
                clone.transform.SetParent(null, true);
                clone.transform.position = position;
                clone.transform.rotation = rotation;
                var physical = clone.GetComponent<FVRPhysicalObject>();
                if (physical == null) failure = "The prefab has no root physical object.";
                else
                {
                    clone.SetActive(true);
                    if (failure == null) physical.IDSpawnedFrom = entry;
                }
            }
            catch (Exception ex) { failure = ex.ToString(); }
            finally
            {
                try
                {
                    if (failure != null)
                    {
                        // Stop Update/interaction on the failed instance before the next frame.
                        if (clone != null) { clone.SetActive(false); Object.Destroy(clone); }
                    }
                }
                finally
                {
                    Application.logMessageReceived -= capture;
                    if (staging != null) Object.Destroy(staging);
                }
            }
            if (failure != null) { problem = failure; return false; }
            spawned = clone;
            return true;
        }
    }
}
