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
            Check(m.Groups.Count == 37, "37 moving groups");
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
            var ring = new System.Collections.Generic.List<System.Numerics.Vector2>(); var ring2 = new System.Collections.Generic.List<System.Numerics.Vector2>(); var ring3 = new System.Collections.Generic.List<System.Numerics.Vector2>();
            for (int i = 0; i < 90; i++) { float a = i * 0.0698f; ring.Add(new(0.2f * MathF.Sin(a), 0.2f * MathF.Cos(a))); ring2.Add(new(0.18f * MathF.Sin(a), 0.18f * MathF.Cos(a))); ring3.Add(new(0.1f * MathF.Sin(a), 0.1f * MathF.Cos(a))); }
            var inner = new float[BeltBand.Bins]; Array.Fill(inner, 0.13f);
            var band = BeltBand.From(ring, ring3, ring3, System.Numerics.Vector2.Zero, 0f, inner);
            Check(Math.Abs(band.Inside(0f, -1f) - 0.2f) < 0.002f && Math.Abs(band.Inside(0f, 1f) - 0.1f) < 0.002f, "band: edges follow her");
            var band2 = BeltBand.From(ring3, ring, ring3, System.Numerics.Vector2.Zero, 0f, inner);
            Check(band2.Inside(1f, 0f) >= 0.199f, "band: a bulge at mid height lifts the band over it");
            var bend = new[] { new System.Numerics.Vector3(0, 0, 0), new System.Numerics.Vector3(0, 1, 0), new System.Numerics.Vector3(1, 1, 0) };
            var jn = ChainPath.Walk(bend, bend[0], new[] { 0.5f, 0.5f, 0.5f, 2f });
            Check(Math.Abs(jn[1].Y - 0.5f) < 1e-4f && Math.Abs(jn[2].Y - 1f) < 1e-4f, "chain: joints walk up the path");
            Check(Math.Abs(System.Numerics.Vector3.Distance(jn[2], jn[3]) - 0.5f) < 1e-3f && jn[3].X > 0.4f, "chain: pieces keep their length round a bend");
            Check(Math.Abs(System.Numerics.Vector3.Distance(jn[3], jn[4]) - 2f) < 1e-3f, "chain: past the end it carries straight on");
            var off = ChainPath.Walk(bend, new System.Numerics.Vector3(-0.6f, 0f, 0f), new[] { 0.3f, 0.3f, 0.3f });
            bool exact = true; for (int i = 0; i < 3; i++) exact &= Math.Abs(System.Numerics.Vector3.Distance(off[i], off[i + 1]) - 0.3f) < 1e-4f;
            Check(exact, "chain: pieces never stretch, even starting off the path");
            Check(off[3].Y > off[1].Y && off[1].Y > 0.1f, "chain: off the path it climbs, not straight across");
            var wc = new[] { new System.Numerics.Vector3(0, 0, 0), new System.Numerics.Vector3(0, 0.01f, 0), new System.Numerics.Vector3(1, 1, 0), new System.Numerics.Vector3(2, 1, 0) };
            var ws = WirePath.Smooth(wc, 4);
            Check(ws.Length == 13 && ws[4] == wc[1] && ws[8] == wc[2] && ws[12] == wc[3], "wire: passes through its points");
            Check(ws[2].Y > -0.01f && ws[2].Y < 0.02f, "wire: no overshoot where points crowd");
            var lift = new float[BeltBand.Bins]; lift[0] = 0.01f; band.Lift(lift);
            Check(Math.Abs(band.Lower[0] - 0.21f) < 0.002f && band.Lower[1] > band.Lower[3], "band: a push out spreads to its neighbours");
            band = BeltBand.From(ring, ring3, ring3, System.Numerics.Vector2.Zero, 0f, inner);
            var wb = band.Warp(new System.Numerics.Vector3(0f, 0.2f, 0.14f), 0.2f, 0.04f, 0.5f, 0.5f);
            Check(Math.Abs(wb.Z - 0.155f) < 0.002f, "band: thickness kept outside her");
            var span = SliceStats.CentralSpan(new System.Collections.Generic.List<float> { -0.3f, -0.1f, 0f, 0.1f, 0.34f }, 0.15f);
            Check(span.Min == -0.1f && span.Max == 0.1f, "arms beside the waist are left out");

            Console.WriteLine(_passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }
    }
}
