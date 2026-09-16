// Capture an authentic, deliberately framed game view for the README. Not shipped.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using FistVR;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ReadmePreview
{
    private static object Call(object target, string name, params object[] args) => AccessTools.Method(target.GetType(), name).Invoke(target, args);
    public static IEnumerator Run(ItemSpawnerV2 spawner, MonoBehaviour controller, string directory, Action<string> log, bool checkSearch)
    {
        var hands = GM.CurrentMovementManager.Hands;
        var heldField = AccessTools.Field(typeof(FVRViveHand), "m_currentInteractable");
        var priorHeld = hands.Select(h => h.CurrentInteractable).ToArray();
        var priorEnabled = hands.Select(h => h.enabled).ToArray();
        var priorHead = GM.CurrentPlayerBody.Head.position;
        GameObject gun = null;
        try
        {
            foreach (var hand in hands) { hand.enabled = false; heldField.SetValue(hand, null); }
            GM.CurrentPlayerBody.Head.position = spawner.transform.position + Vector3.back;
            var entry = IM.GetSpawnerID("SMGUziMini");
            var request = entry.MainObject.GetGameObjectAsync();
            float deadline = Time.realtimeSinceStartup + 90;
            while (request.keepWaiting && Time.realtimeSinceStartup < deadline) yield return null;
            if (!request.IsCompleted || request.Result == null) throw new Exception("Preview fixture failed to load");
            gun = Object.Instantiate(request.Result, spawner.transform.position + Vector3.up, Quaternion.identity);
            foreach (var rb in gun.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
            heldField.SetValue(hands[0], gun.GetComponent<FVRPhysicalObject>());
            Call(controller, "RefreshHeldItem", new object[] { null });
            var bridge = AccessTools.Field(controller.GetType(), "bridge").GetValue(controller);
            if (checkSearch)
            {
                var checks = PerformanceChecks.Run(spawner, controller, bridge, log);
                try { while (checks.MoveNext()) yield return checks.Current; }
                finally { (checks as IDisposable).Dispose(); }
            }
            spawner.BTN_SetPageMode(1);
            spawner.BTN_SimpleMode_SwitchToSimpleMode();
            Call(bridge, "SelectEntry", entry);
            yield return new WaitForSecondsRealtime(4); // Let real status tooltips expire.
            yield return new WaitForEndOfFrame();
            var root = (RectTransform)AccessTools.Field(controller.GetType(), "uiRoot").GetValue(controller);
            Capture(root.parent as RectTransform, Path.Combine(directory, "readme-cover.png"));
            log("README COVER CAPTURED");
        }
        finally
        {
            Call(controller, "CancelRoll");
            for (int i = 0; i < hands.Length; ++i) { heldField.SetValue(hands[i], priorHeld[i]); hands[i].enabled = priorEnabled[i]; }
            GM.CurrentPlayerBody.Head.position = priorHead;
            if (gun != null) Object.Destroy(gun);
        }
    }

    private static void Capture(RectTransform canvas, string path)
    {
        Canvas.ForceUpdateCanvases();
        var cameraObject = new GameObject("Gundomizer README camera");
        var camera = cameraObject.AddComponent<Camera>();
        const int width = 1800, height = 720;
        var target = new RenderTexture(width, height, 24);
        var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            // Frame the lower panel, keeping the controls prominent in a wide capture.
            var center = canvas.TransformPoint(canvas.rect.center - new Vector2(0, canvas.rect.height * .1f));
            camera.transform.position = center - canvas.forward * 2;
            camera.transform.rotation = canvas.rotation;
            camera.orthographic = true;
            camera.orthographicSize = canvas.rect.width * canvas.lossyScale.x * height / width * .515f;
            camera.nearClipPlane = .01f; camera.farClipPlane = 4;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.04f, .04f, .04f);
            camera.cullingMask = 1 << canvas.gameObject.layer;
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null;
            Object.Destroy(cameraObject); Object.Destroy(target); Object.Destroy(pixels);
        }
    }
}
