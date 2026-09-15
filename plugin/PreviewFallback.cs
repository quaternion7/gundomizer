using UnityEngine;
using UnityEngine.UI;

namespace Gundomizer
{
    // An empty native Image draws a solid rectangle. Hide just that graphic and render a generic
    // package silhouette in its place; keep the native object, selection, history and spawn button.
    public sealed class PreviewFallback : MaskableGraphic
    {
        private Image preview;
        private bool originalEnabled;
        private bool replacing;

        internal void Initialize(Image image)
        {
            preview = image;
            raycastTarget = false;
            color = new Color(0.65f, 0.67f, 0.63f, 1f);
        }

        internal void Refresh(bool selected)
        {
            bool missing = selected && preview != null && preview.sprite == null;
            if (missing && !replacing)
            {
                originalEnabled = preview.enabled;
                preview.enabled = false;
                replacing = true;
            }
            else if (!missing) Restore();
            gameObject.SetActive(missing);
        }

        private void Restore()
        {
            if (replacing && preview != null) preview.enabled = originalEnabled;
            replacing = false;
        }

        protected override void OnDestroy() { Restore(); base.OnDestroy(); }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            float scale = Mathf.Min(rect.width, rect.height) * 0.52f;
            // Three separated faces keep the cube legible as a placeholder at native preview size.
            Face(mesh, rect.center, scale, 1f, new[] { 0f, .46f, .43f, .23f, 0f, 0f, -.43f, .23f });
            Face(mesh, rect.center, scale, .82f, new[] { -.43f, .18f, -.025f, -.04f, -.025f, -.49f, -.43f, -.26f });
            Face(mesh, rect.center, scale, .64f, new[] { .025f, -.04f, .43f, .18f, .43f, -.26f, .025f, -.49f });
        }

        private void Face(VertexHelper mesh, Vector2 center, float scale, float shade, float[] points)
        {
            int start = mesh.currentVertCount;
            var ink = new Color(color.r * shade, color.g * shade, color.b * shade, color.a);
            for (int i = 0; i < points.Length; i += 2)
                mesh.AddVert(new Vector3(center.x + points[i] * scale, center.y + points[i + 1] * scale, 0), ink, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
