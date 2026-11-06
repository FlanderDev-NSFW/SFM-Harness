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
            Check(m.Groups.Count == 18, "18 moving groups");
            Check(m.TriangleCount() > 50000, "whole model present");
            Check(m.Sockets.ContainsKey("SOCKET_cord_front") && m.Sockets.ContainsKey("BODY_hoop_R"), "sockets present");
            Check(m.Sockets["BODY_hip_R"].Position.X < 0, "Beta's right is her left: -X in Unity");
            Check(m.Sockets["SOCKET_guide_front"].Position.Z > m.Sockets["SOCKET_guide_rear"].Position.Z, "front guide is towards +Z");
            Check(GroupMap.NodeOf("ring_eyelet_R") == GroupMap.LeftThigh && GroupMap.NodeOf("end_front_piston") == GroupMap.PistonFront && GroupMap.NodeOf("hips") == GroupMap.Root, "groups hang on the right nodes");
            var span = SliceStats.CentralSpan(new System.Collections.Generic.List<float> { -0.3f, -0.1f, 0f, 0.1f, 0.34f }, 0.15f);
            Check(span.Min == -0.1f && span.Max == 0.1f, "arms beside the waist are left out");

            Console.WriteLine(_passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }
    }
}
