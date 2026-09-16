using System.Reflection;
using FistVR;
using HarmonyLib;
using UnityEngine;

namespace Gundomizer
{
    internal static class AssetAccess
    {
        private static readonly FieldInfo Loading = AccessTools.Field(typeof(AnvilAsset), "m_loadingState");
        private static AnvilCallback<GameObject> outstanding;
        internal static AnvilCallback<GameObject> Cached(FVRObject obj)
            => obj == null || Loading == null ? null : Loading.GetValue(obj) as AnvilCallback<GameObject>;

        internal static GameObject Peek(FVRObject obj)
        {
            var request = Cached(obj);
            // Result forces a synchronous load unless completion has already been established.
            // Never call GetGameObject[Async], Pump or keepWaiting while indexing.
            return request != null && request.IsCompleted ? request.Result : null;
        }

        internal static bool LoadPending => outstanding != null && !outstanding.IsCompleted;

        internal static bool TryRequest(FVRObject obj, out AnvilCallback<GameObject> request, out bool started)
        {
            request = Cached(obj);
            started = false;
            if (request != null) return true; // Borrow the game's existing request, including loads by other mods.
            if (LoadPending) return false;
            // A cancelled roll never cancels Anvil's shared request or frees this gate early.
            // This prevents successive clicks/panels from accumulating outstanding loads.
            request = obj.GetGameObjectAsync();
            started = true;
            outstanding = request;
            return true;
        }
    }
}
