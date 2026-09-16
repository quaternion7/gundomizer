using System;
using System.Collections;
using System.Collections.Generic;
using FistVR;
using UnityEngine;

namespace Gundomizer
{
    // Session-only, weak references to already-loaded components. No prefab/bundle ownership and
    // no quadratic item-to-item matrix. Reading the component's current connector avoids stale
    // numeric facts when a mod changes a loaded prefab in place.
    internal static class ConnectorIndex
    {
        internal const int Capacity = 16384;
        private sealed class Record
        {
            internal WeakReference Source;
            internal WeakReference Component;
            internal WeakReference Callback;
        }
        private static readonly Dictionary<int, Record> records = new Dictionary<int, Record>();
        private static readonly Queue<int> order = new Queue<int>();
        private static bool running;
        internal static int Count => records.Count;
        internal static int Sweeps;
        internal static float MaxSliceMilliseconds;

        internal static void Observe(FVRObject source, GameObject prefab)
        {
            if (source == null || prefab == null) return;
            int key = source.GetInstanceID();
            var component = prefab.GetComponent<FVRPhysicalObject>();
            if (component == null) return;
            PersistentConnectorIndex.Observe(source, component);
            Record record;
            if (records.TryGetValue(key, out record))
            {
                record.Source.Target = source; record.Component.Target = component;
                record.Callback.Target = AssetAccess.Cached(source);
                return;
            }
            if (records.Count >= Capacity) records.Remove(order.Dequeue());
            records.Add(key, new Record { Source = new WeakReference(source), Component = new WeakReference(component),
                Callback = new WeakReference(AssetAccess.Cached(source)) });
            order.Enqueue(key);
        }

        internal static FVRPhysicalObject Find(FVRObject source)
        {
            if (source == null) return null;
            // Live fields always outrank disk metadata, including prefabs loaded since the sweep.
            var loaded = AssetAccess.Peek(source);
            if (loaded != null) Observe(source, loaded);
            Record record;
            if (!records.TryGetValue(source.GetInstanceID(), out record) || record.Source.Target as FVRObject != source
                || record.Callback.Target != AssetAccess.Cached(source)) return null;
            var component = record.Component.Target as FVRPhysicalObject;
            return component == null ? null : component; // Honor Unity's destroyed-object semantics too.
        }

        internal static void Start(MonoBehaviour host)
        {
            if (running) return;
            running = true;
            host.StartCoroutine(Sweep());
        }

        private static IEnumerator Sweep()
        {
            try
            {
                while (true)
                {
                    var catalog = ManagerSingleton<IM>.Instance == null ? null : IM.OD;
                    if (catalog != null && !GM.IsAsyncLoading)
                    {
                        // Enumerate in short main-thread slices. Mod registration can invalidate
                        // an enumerator between frames; abandon that sweep and retry later.
                        var entries = catalog.Values.GetEnumerator();
                        try
                        {
                            bool more = true;
                            while (more)
                            {
                                float started = Time.realtimeSinceStartup;
                                int count = 0;
                                do
                                {
                                    try { more = entries.MoveNext(); }
                                    catch (InvalidOperationException) { more = false; }
                                    if (!more) break;
                                    var obj = entries.Current;
                                    try { Observe(obj, AssetAccess.Peek(obj)); }
                                    catch (Exception ex) { Plugin.Log.LogDebug("Index entry unavailable: " + ex.Message); }
                                } while (++count < 32 && (Time.realtimeSinceStartup - started) < 0.00075f);
                                MaxSliceMilliseconds = Mathf.Max(MaxSliceMilliseconds, (Time.realtimeSinceStartup - started) * 1000f);
                                yield return null;
                            }
                        }
                        finally { entries.Dispose(); }
                        ++Sweeps;
                    }
                    yield return new WaitForSecondsRealtime(10f);
                }
            }
            finally { running = false; records.Clear(); order.Clear(); }
        }
    }
}
