using UnityEngine;
using UnityEngine.UI;

namespace Gundomizer
{
    public sealed class ButtonIcon : MaskableGraphic
    {
        internal bool Compatible;
        internal bool Ammo;
        internal bool Dropdown;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            const float viewWidth = 100f;
            float scale = Mathf.Min(rect.width / viewWidth, rect.height / 100f);
            var origin = new Vector2(rect.center.x - viewWidth * scale * 0.5f, rect.center.y + 50f * scale);
            var shapes = Ammo ? IconGeometry.Cartridge : Dropdown ? IconGeometry.Chevron : Compatible ? IconGeometry.Link : IconGeometry.Dice;
            foreach (var shape in shapes)
            {
                int start = mesh.currentVertCount;
                var ink = shape.Cutout ? new Color(0.055f, 0.065f, 0.08f, color.a) : color;
                for (int i = 0; i < shape.Points.Length; i += 2)
                    mesh.AddVert(new Vector3(origin.x + shape.Points[i] * scale,
                        origin.y - shape.Points[i + 1] * scale, 0f), ink, Vector2.zero);
                for (int i = 1; i < shape.Points.Length / 2 - 1; ++i)
                    mesh.AddTriangle(start, start + i, start + i + 1);
            }
        }
    }
}
