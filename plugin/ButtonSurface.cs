using UnityEngine;
using UnityEngine.UI;

namespace Gundomizer
{
    // Use the normal UI material: rounded geometry clips the sliding texture, and vertex colors
    // add a restrained top-to-bottom bevel without another texture, mask, or shader bundle.
    public sealed class ButtonSurface : RawImage
    {
        internal bool RoundLeft = true;
        internal bool RoundRight = true;
        private const int Steps = 6;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            if (rect.width <= 2f || rect.height <= 2f) return;
            float radius = Mathf.Min(7f, Mathf.Min(rect.width, rect.height) * 0.25f);
            const float bevel = 0.8f;
            var inner = new Rect(rect.x + bevel, rect.y + bevel, rect.width - 2f * bevel, rect.height - 2f * bevel);
            AddVertex(mesh, rect.center, rect, false);
            int count = 4 * (Steps + 1);
            for (int i = 0; i < count; ++i)
            {
                AddVertex(mesh, Outline(inner, radius - bevel, i), rect, false);
                AddVertex(mesh, Outline(rect, radius, i), rect, true);
            }
            for (int i = 0; i < count; ++i)
            {
                int a = 1 + i * 2;
                int b = 1 + ((i + 1) % count) * 2;
                mesh.AddTriangle(0, a, b);
                mesh.AddTriangle(a, a + 1, b + 1);
                mesh.AddTriangle(a, b + 1, b);
            }
        }

        private Vector2 Outline(Rect rect, float radius, int index)
        {
            int corner = index / (Steps + 1);
            bool right = corner < 2;
            bool top = corner == 0 || corner == 3;
            if (!(right ? RoundRight : RoundLeft)) radius = 0f;
            var center = new Vector2(right ? rect.xMax - radius : rect.xMin + radius,
                top ? rect.yMax - radius : rect.yMin + radius);
            float angle = (90f - corner * 90f - (index % (Steps + 1)) * 90f / Steps) * Mathf.Deg2Rad;
            return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        private void AddVertex(VertexHelper mesh, Vector2 point, Rect rect, bool edge)
        {
            float y = Mathf.InverseLerp(rect.yMin, rect.yMax, point.y);
            float shade = edge ? Mathf.Lerp(0.50f, 1.18f, y) : Mathf.Lerp(0.76f, 1f, y);
            var ink = new Color(color.r * shade, color.g * shade, color.b * shade, color.a);
            var uv = new Vector2(uvRect.x + (point.x - rect.xMin) / rect.width * uvRect.width,
                uvRect.y + y * uvRect.height);
            mesh.AddVert(new Vector3(point.x, point.y, 0), ink, uv);
        }
    }
}
