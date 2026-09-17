using System;
using System.Collections.Generic;

namespace Gundomizer
{
    // Vector silhouettes, independent of platform fonts and emoji availability in Unity 5.6.
    // Coordinates use a top-left origin. Every shape is convex for a simple triangle fan.
    internal sealed class IconShape
    {
        internal readonly float[] Points;
        internal readonly bool Cutout;
        internal IconShape(bool cutout, params float[] points) { Points = points; Cutout = cutout; }
    }

    internal static class IconGeometry
    {
        internal static readonly IconShape[] Dice = MakeDice();
        internal static readonly IconShape[] Link = MakeLink();
        internal static readonly IconShape[] Cartridge = {
            new IconShape(false, 34,35, 34,23, 50,5, 66,23, 66,35),
            new IconShape(false, 32,39, 68,39, 68,85, 32,85),
            new IconShape(false, 28,89, 72,89, 72,96, 28,96)
        };
        internal static readonly IconShape[] Chevron = {
            new IconShape(false, 12,35, 50,65, 50,80, 12,50),
            new IconShape(false, 50,65, 88,35, 88,50, 50,80)
        };

        private static IconShape[] MakeDice()
        {
            var shapes = new List<IconShape> { RoundedRect(12, 12, 76, 76, 13) };
            foreach (var point in new[] { new[] { 32f, 32f }, new[] { 68f, 32f }, new[] { 50f, 50f },
                new[] { 32f, 68f }, new[] { 68f, 68f } })
                shapes.Add(Circle(point[0], point[1], 7f, true));
            return shapes.ToArray();
        }

        private static IconShape[] MakeLink()
        {
            // Two open links and a joining bar. Real gaps retain the rainbow behind them.
            var shapes = new List<IconShape>();
            foreach (bool mirror in new[] { false, true })
            {
                var half = new List<IconShape>
                {
                    new IconShape(false, 58,29, 66,29, 66,37, 58,37),
                    new IconShape(false, 58,63, 66,63, 66,71, 58,71),
                    Circle(58, 33, 4, false), Circle(58, 67, 4, false)
                };
                for (int step = 0; step < 12; ++step)
                {
                    double a = (-90 + step * 15) * Math.PI / 180;
                    double b = a + Math.PI / 12;
                    half.Add(new IconShape(false,
                        66 + 21 * (float)Math.Cos(a), 50 + 21 * (float)Math.Sin(a),
                        66 + 21 * (float)Math.Cos(b), 50 + 21 * (float)Math.Sin(b),
                        66 + 13 * (float)Math.Cos(b), 50 + 13 * (float)Math.Sin(b),
                        66 + 13 * (float)Math.Cos(a), 50 + 13 * (float)Math.Sin(a)));
                }
                foreach (var shape in half)
                {
                    if (mirror) for (int i = 0; i < shape.Points.Length; i += 2) shape.Points[i] = 100 - shape.Points[i];
                    shapes.Add(shape);
                }
            }
            shapes.Add(RoundedRect(36, 46, 28, 8, 4));
            foreach (var shape in shapes)
                for (int i = 0; i < shape.Points.Length; i += 2)
                {
                    float x = shape.Points[i] - 50, y = shape.Points[i + 1] - 50;
                    shape.Points[i] = 50 + (x + y) * .70710678f;
                    shape.Points[i + 1] = 50 + (y - x) * .70710678f;
                }
            return shapes.ToArray();
        }
        private static IconShape Circle(float x, float y, float radius, bool cutout)
        {
            var points = new float[48];
            for (int i = 0; i < 24; ++i)
            {
                double angle = i * Math.PI * 2 / 24;
                points[i * 2] = x + (float)Math.Cos(angle) * radius;
                points[i * 2 + 1] = y + (float)Math.Sin(angle) * radius;
            }
            return new IconShape(cutout, points);
        }

        private static IconShape RoundedRect(float x, float y, float width, float height, float radius, bool cutout = false)
        {
            var points = new List<float>();
            var centers = new[] { new[] { x + width - radius, y + radius },
                new[] { x + width - radius, y + height - radius },
                new[] { x + radius, y + height - radius }, new[] { x + radius, y + radius } };
            for (int corner = 0; corner < 4; ++corner)
                for (int step = 0; step <= 6; ++step)
                {
                    double angle = (-90 + corner * 90 + step * 15) * Math.PI / 180;
                    points.Add(centers[corner][0] + (float)Math.Cos(angle) * radius);
                    points.Add(centers[corner][1] + (float)Math.Sin(angle) * radius);
                }
            return new IconShape(cutout, points.ToArray());
        }
    }
}
