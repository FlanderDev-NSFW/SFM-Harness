using System;
using Harness.Logic;

namespace Harness.Tests
{
    internal static class Program
    {
        private static int _passed, _failed;

        private static void Check(bool ok, string name)
        {
            if (ok) { _passed++; Console.WriteLine("  PASS  " + name); }
            else { _failed++; Console.WriteLine("  FAIL  " + name); }
        }

        private static int Main()
        {
            string file = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Harness", "assets", "harness.hrn"));
            var m = HarnessFile.Read(System.IO.File.OpenRead(file));
            Check(m.Groups.Count == 24, "24 moving groups");
            Check(m.TriangleCount() > 50000, "whole model present");
            Check(m.Sockets.ContainsKey("SOCKET_cord_front") && m.Sockets.ContainsKey("BODY_hoop_R"), "sockets present");
            Check(m.Sockets["BODY_hip_R"].Position.X < 0, "Beta's right is her left: -X in Unity");
            Check(m.Sockets["SOCKET_guide_front"].Position.Z > m.Sockets["SOCKET_guide_rear"].Position.Z, "front guide is towards +Z");
            Check(GroupMap.NodeOf("thigh_R") == GroupMap.LeftThigh && GroupMap.NodeOf("guide_rear") == GroupMap.GuideRear && GroupMap.NodeOf("piston_front") == GroupMap.PistonFront && GroupMap.NodeOf("ring_eyelet_R") == GroupMap.Own, "groups hang on the right nodes");
            var d = new PistonDrive();
            d.Update(1f, 1f, 1f, 0f); Check(d.Stroke == 0f, "legs straight: no travel");
            d.Update(1.1f, 1f, 1f, 0f); Check(d.Stroke == 0f, "slack takes up the first stretch");
            d.Update(2f, 1f, 1f, 0f); Check(d.Stroke == 1f, "full travel, and no further");
            d.Update(1.37f, 1f, 1f, 0f); Check(Math.Abs(d.Stroke - 0.5f) < 0.001f, "half way");
            d.Update(1.37f, 1f, 2f, 0f); Check(d.Stroke < 0.5f, "a longer thigh needs a longer pull");
            var box = new System.Collections.Generic.List<System.Numerics.Vector2> { new(-0.2f, -0.1f), new(0.2f, -0.1f), new(0.2f, 0.1f), new(-0.2f, 0.1f), new(0f, -0.05f), new(0f, 0f), new(0.1f, 0f), new(-0.1f, 0f) };
            var shape = BeltShape.From(box, 0f);
            Check(Math.Abs(shape.At(0f) - 0.1f) < 0.002f, "outline: distance to her front");
            Check(Math.Abs(shape.At(MathF.PI / 2) - 0.2f) < 0.002f, "outline: distance to her side");
            Check(Math.Abs(shape.At(MathF.PI) - 0.1f) < 0.002f, "a hollow at the back is bridged");
            var w = shape.Warp(new System.Numerics.Vector3(0.14f, 0.219f, 0f), 0.14f, 0.12f, 0.219f, 1f, 1f);
            Check(Math.Abs(w.X - 0.2f) < 0.002f && Math.Abs(w.Y) < 1e-5f, "the model's inner side lands on her outline");
            var w2 = shape.Warp(new System.Numerics.Vector3(0.15f, 0.25f, 0f), 0.14f, 0.12f, 0.219f, 0.5f, 0.5f);
            Check(Math.Abs(w2.X - 0.205f) < 0.002f && Math.Abs(w2.Y - 0.0155f) < 1e-4f, "thickness and height scale");
            var span = SliceStats.CentralSpan(new System.Collections.Generic.List<float> { -0.3f, -0.1f, 0f, 0.1f, 0.34f }, 0.15f);
            Check(span.Min == -0.1f && span.Max == 0.1f, "arms beside the waist are left out");

            Console.WriteLine(_passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }
    }
}
