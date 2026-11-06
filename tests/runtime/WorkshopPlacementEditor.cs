// Opt-in local placement tool. This file is never compiled into Gundomizer.
using System;
using System.IO;
using UnityEngine;

public sealed class WorkshopPlacementEditor : MonoBehaviour
{
    private Transform target;
    private Camera view;
    private string directory;
    private Action<string> log;
    private Vector3 initialPosition, initialScale, center;
    private Quaternion initialRotation;
    private Rect window = new Rect(10, 10, 320, 660);
    private int mode, dragAxis = -1;
    private Vector2 lastMouse;
    private float yaw, pitch, frontYaw, distance = .55f, nextSave;
    private string lastSnapshot;
    private bool orbiting;
    private static readonly Color[] Colors = { new Color(1, .3f, .3f), new Color(.3f, 1, .3f), new Color(.3f, .6f, 1) };

    [Serializable]
    private sealed class Placement
    {
        public string parent;
        public Vector3 localPosition, localEulerAngles, localScale;
        public Quaternion localRotation;
    }

    public void Setup(Transform edited, Camera camera, string path, Action<string> logger)
    {
        target = edited; view = camera; directory = path; log = logger;
        initialPosition = target.localPosition; initialRotation = target.localRotation; initialScale = target.localScale;
        var savedPath = Path.Combine(directory, "workshop-placement.json");
        if (File.Exists(savedPath))
        {
            var saved = JsonUtility.FromJson<Placement>(File.ReadAllText(savedPath));
            target.localPosition = saved.localPosition; target.localRotation = saved.localRotation; target.localScale = saved.localScale;
        }
        center = target.position + Vector3.up * .035f;
        yaw = view.transform.eulerAngles.y; pitch = Signed(view.transform.eulerAngles.x);
        view.orthographic = false; view.fieldOfView = 45;
        Save();
    }

    private void LateUpdate()
    {
        if (target == null) return;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        view.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
        view.transform.position = center - view.transform.forward * distance;
        if (Time.unscaledTime >= nextSave) { nextSave = Time.unscaledTime + .25f; Save(); }
    }

    private void OnGUI()
    {
        if (target == null) return;
        GUI.depth = -1000;
        ProcessCameraEvent(Event.current, GUIUtility.GetControlID(4720, FocusType.Passive));
        window = GUI.Window(4719, window, DrawWindow, "Gundomizer — live placement (test only)");
        DrawHandles();
    }

    private void DrawWindow(int id)
    {
        GUILayout.Label("Drag the colored handles.\nRight-drag outside panel: orbit | Wheel: zoom");
        mode = GUILayout.Toolbar(mode, new[] { "Move", "Rotate" });
        GUILayout.Space(6);
        GUILayout.Label("Position offset (cm, parent axes)");
        var scale = target.parent.lossyScale;
        var offset = Vector3.Scale(target.localPosition - initialPosition, scale) * 100;
        var changed = Sliders(offset, -20, 20);
        if (changed != offset)
            target.localPosition = initialPosition + new Vector3(changed.x / scale.x, changed.y / scale.y, changed.z / scale.z) / 100;
        GUILayout.Label("Local rotation (degrees)");
        var rotation = target.localEulerAngles;
        rotation = new Vector3(Signed(rotation.x), Signed(rotation.y), Signed(rotation.z));
        var rotated = Sliders(rotation, -180, 180);
        if (rotated != rotation) target.localRotation = Quaternion.Euler(rotated);
        GUILayout.Label("Size");
        float multiplier = target.localScale.x / initialScale.x;
        float resized = GUILayout.HorizontalSlider(multiplier, .3f, 2);
        if (Mathf.Abs(resized - multiplier) > .0001f) target.localScale = initialScale * resized;
        GUILayout.Label("Local position: " + target.localPosition.ToString("F4"));
        GUILayout.Label("Local rotation: " + target.localEulerAngles.ToString("F2"));
        GUILayout.Label("Autosaved: workshop-placement.json");
        if (GUILayout.Button("Save placement")) { Save(); log("PLACEMENT SAVED " + lastSnapshot); }
        if (GUILayout.Button("Reset placement")) { target.localPosition = initialPosition; target.localRotation = initialRotation; target.localScale = initialScale; }
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("View front")) { pitch = 20; yaw = frontYaw; }
        if (GUILayout.Button("View above")) { pitch = 65; yaw = frontYaw; }
        GUILayout.EndHorizontal();
        GUILayout.Label("Camera orbit (also available without dragging)");
        GUILayout.Label("Yaw"); yaw = GUILayout.HorizontalSlider(yaw, -180, 180);
        GUILayout.Label("Pitch"); pitch = GUILayout.HorizontalSlider(pitch, -80, 85);
        GUILayout.Label("Zoom"); distance = GUILayout.HorizontalSlider(distance, .12f, 2);
        GUI.DragWindow(new Rect(0, 0, window.width, 22));
    }

    // Unity's IMGUI mouse deltas work even when a game's InputManager has no Mouse X/Y axes.
    public void ProcessCameraEvent(Event e, int controlId)
    {
        if (e.type == EventType.MouseDown && e.button == 1 && !window.Contains(e.mousePosition))
        {
            orbiting = true;
            if (controlId != 0) GUIUtility.hotControl = controlId;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && e.button == 1 && orbiting)
        {
            yaw = Signed(yaw + e.delta.x * .35f);
            pitch = Mathf.Clamp(pitch + e.delta.y * .35f, -80, 85);
            e.Use();
        }
        else if (e.type == EventType.MouseUp && e.button == 1 && orbiting)
        {
            orbiting = false;
            if (controlId != 0) GUIUtility.hotControl = 0;
            e.Use();
        }
        else if (e.type == EventType.ScrollWheel && !window.Contains(e.mousePosition))
        {
            distance = Mathf.Clamp(distance * Mathf.Exp(e.delta.y * .08f), .12f, 2); e.Use();
        }
    }

    public void FramePlatform(Vector3 point, Vector3 front)
    {
        center = point; pitch = 30; distance = .65f;
        yaw = frontYaw = Signed(Mathf.Atan2(-front.x, -front.z) * Mathf.Rad2Deg);
    }

    private static Vector3 Sliders(Vector3 value, float min, float max)
    {
        for (int i = 0; i < 3; ++i)
        {
            GUILayout.BeginHorizontal();
            var prior = GUI.color; GUI.color = Colors[i]; GUILayout.Label("XYZ"[i] + "", GUILayout.Width(18)); GUI.color = prior;
            value[i] = GUILayout.HorizontalSlider(value[i], min, max);
            GUILayout.Label(value[i].ToString("F2"), GUILayout.Width(55));
            GUILayout.EndHorizontal();
        }
        return value;
    }

    private Vector2 Screen(Vector3 point)
    {
        var p = view.WorldToScreenPoint(point);
        return new Vector2(p.x, UnityEngine.Screen.height - p.y);
    }

    private void DrawHandles()
    {
        var e = Event.current; Vector2 origin = Screen(target.position);
        if (window.Contains(e.mousePosition) && dragAxis < 0) return;
        for (int i = 0; i < 3; ++i)
        {
            Vector3 axis = target.parent.TransformDirection(i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward).normalized;
            if (mode == 0)
            {
                Vector2 end = Screen(target.position + axis * .08f);
                Line(origin, end, Colors[i]);
                var handle = new Rect(end.x - 12, end.y - 12, 24, 24);
                var prior = GUI.color; GUI.color = Colors[i]; GUI.Box(handle, "XYZ"[i] + ""); GUI.color = prior;
                if (e.type == EventType.MouseDown && e.button == 0 && handle.Contains(e.mousePosition)) { dragAxis = i; lastMouse = e.mousePosition; e.Use(); }
            }
            else
            {
                Vector3 tangent = Vector3.Cross(axis, Mathf.Abs(axis.y) < .8f ? Vector3.up : Vector3.right).normalized;
                Vector3 other = Vector3.Cross(axis, tangent);
                Vector2 previous = Screen(target.position + tangent * .065f);
                for (int n = 1; n <= 48; ++n)
                {
                    float angle = n * Mathf.PI * 2 / 48;
                    Vector2 next = Screen(target.position + (tangent * Mathf.Cos(angle) + other * Mathf.Sin(angle)) * .065f);
                    Line(previous, next, Colors[i]);
                    if (e.type == EventType.MouseDown && e.button == 0 && DistanceToSegment(e.mousePosition, previous, next) < 7)
                    { dragAxis = i; lastMouse = e.mousePosition; e.Use(); }
                    previous = next;
                }
            }
        }
        if (dragAxis >= 0 && e.type == EventType.MouseDrag)
        {
            Vector3 localAxis = dragAxis == 0 ? Vector3.right : dragAxis == 1 ? Vector3.up : Vector3.forward;
            Vector2 delta = e.mousePosition - lastMouse; lastMouse = e.mousePosition;
            if (mode == 0)
            {
                Vector3 axis = target.parent.TransformDirection(localAxis).normalized;
                Vector2 projection = Screen(target.position + axis) - origin;
                if (projection.sqrMagnitude > 1) target.position += axis * (Vector2.Dot(delta, projection) / projection.sqrMagnitude);
            }
            else target.localRotation = Quaternion.AngleAxis((delta.x - delta.y) * .6f, localAxis) * target.localRotation;
            e.Use();
        }
        if (e.type == EventType.MouseUp) dragAxis = -1;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var delta = b - a;
        return Vector2.Distance(point, a + delta * Mathf.Clamp01(Vector2.Dot(point - a, delta) / Mathf.Max(.001f, delta.sqrMagnitude)));
    }

    private static void Line(Vector2 a, Vector2 b, Color color)
    {
        var matrix = GUI.matrix; var prior = GUI.color;
        GUI.color = color; GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, a);
        GUI.DrawTexture(new Rect(a.x, a.y - 1, Vector2.Distance(a, b), 2), Texture2D.whiteTexture);
        GUI.matrix = matrix; GUI.color = prior;
    }

    private void Save()
    {
        var snapshot = JsonUtility.ToJson(new Placement { parent = target.parent.name, localPosition = target.localPosition,
            localEulerAngles = target.localEulerAngles, localRotation = target.localRotation, localScale = target.localScale }, true);
        if (snapshot == lastSnapshot) return;
        File.WriteAllText(Path.Combine(directory, "workshop-placement.json"), snapshot); lastSnapshot = snapshot;
    }

    private static float Signed(float angle) => Mathf.Repeat(angle + 180, 360) - 180;
}
