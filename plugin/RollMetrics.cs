using System.Globalization;
using UnityEngine;

namespace Gundomizer
{
    internal sealed class RollMetrics
    {
        private readonly float started = Time.realtimeSinceStartup;
        private float requestStarted;
        private bool requestPending;
        private float waitSeconds;
        private int waitedRequests;
        internal int SectionCount;
        internal int FilteredCount;
        internal int RequestedPrefabs;
        internal int CheckedPrefabs;
        internal string Outcome = "cancelled";

        internal void BeginRequest()
        {
            ++RequestedPrefabs;
            requestStarted = Time.realtimeSinceStartup;
            requestPending = false;
        }

        internal void NotePending() { requestPending = true; }

        internal void EndRequest()
        {
            if (!requestPending) return;
            ++waitedRequests;
            waitSeconds += Time.realtimeSinceStartup - requestStarted;
            requestPending = false;
        }

        internal void Log()
        {
            EndRequest(); // Include a load cancelled while its coroutine was suspended.
            Plugin.Log.LogInfo(string.Format(CultureInfo.InvariantCulture,
                "Compatible search {0}: section={1}, filtered={2}, prefab requests={3}, checked={4}, " +
                "requests that waited={5}, observed load wait={6:F0}ms, total={7:F0}ms.",
                Outcome, SectionCount, FilteredCount, RequestedPrefabs, CheckedPrefabs,
                waitedRequests, waitSeconds * 1000f, (Time.realtimeSinceStartup - started) * 1000f));
        }
    }
}
