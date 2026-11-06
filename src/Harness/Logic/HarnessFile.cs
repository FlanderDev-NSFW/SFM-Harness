using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace Harness.Logic
{
    public sealed class HMaterial { public string Name; public Vector3 Color; public byte Kind; }

    public sealed class HSocket { public string Name; public Vector3 Position, XAxis, YAxis; }

    public sealed class HPart { public int Material; public Vector3[] Positions, Normals; public int[] Indices; }

    public sealed class HGroup { public string Name; public Vector3 Pivot; public List<HPart> Parts = new List<HPart>(); }

    /// <summary>The model written by gamma/export_harness.py in the 3D model repository (format described there). Already in Unity's axes.</summary>
    public sealed class HarnessFile
    {
        public const uint Magic = 0x314E5248; // "HRN1"
        public List<HMaterial> Materials = new List<HMaterial>();
        public Dictionary<string, HSocket> Sockets = new Dictionary<string, HSocket>();
        public List<HGroup> Groups = new List<HGroup>();

        public static HarnessFile Read(Stream stream)
        {
            using var r = new BinaryReader(stream);
            if (r.ReadUInt32() != Magic) throw new InvalidDataException("not a harness model (magic)");
            uint version = r.ReadUInt32();
            if (version != 1) throw new InvalidDataException("unsupported model version " + version);
            var f = new HarnessFile();
            for (uint i = r.ReadUInt32(); i > 0; i--) f.Materials.Add(new HMaterial { Name = S(r), Color = V(r), Kind = r.ReadByte() });
            for (uint i = r.ReadUInt32(); i > 0; i--)
            {
                var s = new HSocket { Name = S(r), Position = V(r), XAxis = V(r), YAxis = V(r) };
                f.Sockets[s.Name] = s;
            }
            for (uint i = r.ReadUInt32(); i > 0; i--)
            {
                var g = new HGroup { Name = S(r), Pivot = V(r) };
                for (uint k = r.ReadUInt32(); k > 0; k--)
                {
                    var p = new HPart { Material = checked((int)r.ReadUInt32()) };
                    if (p.Material >= f.Materials.Count) throw new InvalidDataException("bad material index");
                    int n = checked((int)r.ReadUInt32());
                    p.Positions = new Vector3[n];
                    p.Normals = new Vector3[n];
                    for (int v = 0; v < n; v++) p.Positions[v] = V(r);
                    for (int v = 0; v < n; v++) p.Normals[v] = V(r);
                    int ni = checked((int)r.ReadUInt32());
                    if (ni % 3 != 0) throw new InvalidDataException("index count is not a multiple of 3");
                    p.Indices = new int[ni];
                    for (int v = 0; v < ni; v++)
                    {
                        uint idx = r.ReadUInt32();
                        if (idx >= n) throw new InvalidDataException("index out of range");
                        p.Indices[v] = (int)idx;
                    }
                    g.Parts.Add(p);
                }
                f.Groups.Add(g);
            }
            return f;
        }

        private static string S(BinaryReader r) => Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));
        private static Vector3 V(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        public int TriangleCount()
        {
            int t = 0;
            foreach (var g in Groups) foreach (var p in g.Parts) t += p.Indices.Length / 3;
            return t;
        }
    }

    /// <summary>Which rig node a group hangs on. Beta's R side (+X there) is her left.</summary>
    public static class GroupMap
    {
        public const string Root = "root", LeftThigh = "thighL", RightThigh = "thighR", PistonFront = "pistonF", PistonRear = "pistonR";

        public static string NodeOf(string group)
        {
            if (group == "end_front_thigh") return RightThigh;  // the front cord starts on Beta's L hoop: her right thigh
            if (group == "end_rear_thigh") return LeftThigh;
            if (group.StartsWith("thigh_", StringComparison.Ordinal) || group.StartsWith("ring_", StringComparison.Ordinal))
                return group.EndsWith("_R", StringComparison.Ordinal) ? LeftThigh : RightThigh;
            if (group.Contains("piston"))
                return group.Contains("front") ? PistonFront : PistonRear;
            return Root;
        }
    }

    /// <summary>The width of a body slice, ignoring anything separated from the middle by a gap (arms beside the waist).</summary>
    public static class SliceStats
    {
        public static (float Min, float Max) CentralSpan(List<float> values, float gap)
        {
            if (values.Count == 0) return (0f, 0f);
            values.Sort();
            int mid = values.Count / 2, lo = mid, hi = mid;
            while (lo > 0 && values[lo] - values[lo - 1] < gap) lo--;
            while (hi < values.Count - 1 && values[hi + 1] - values[hi] < gap) hi++;
            return (values[lo], values[hi]);
        }
    }
}
