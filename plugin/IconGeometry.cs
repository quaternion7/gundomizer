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
        internal static readonly IconShape[] GunAndHand = MakeGunAndHand();
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

        private static IconShape[] MakeGunAndHand()
        {
            return new[]
            {
                RoundedRect(4, 25, 69, 16, 3), // slide
                RoundedRect(67, 30, 11, 8, 1),
                RoundedRect(8, 40, 52, 8, 2),
                RoundedRect(12, 22, 8, 4, 1),
                RoundedRect(65, 22, 7, 4, 1),
                new IconShape(false, 21,45, 40,45, 31,80, 13,77), // grip
                RoundedRect(36, 43, 23, 23, 5), // trigger guard
                RoundedRect(40, 48, 14, 13, 3, true),
                RoundedRect(45, 47, 4, 9, 1),
                RoundedRect(107, 30, 9, 34, 4.5f), // hand, four separated fingers
                RoundedRect(118, 19, 9, 44, 4.5f),
                RoundedRect(129, 23, 9, 41, 4.5f),
                RoundedRect(140, 33, 8, 33, 4),
                RoundedRect(107, 48, 41, 35, 11),
                new IconShape(false, 99,49, 119,66, 112,78, 94,59),
                Circle(99,54,6,false),
                RoundedRect(115, 78, 28, 12, 2)
            };
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
