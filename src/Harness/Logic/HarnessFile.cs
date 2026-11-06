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
        public const string Belt = "belt", GuideFront = "guideF", GuideRear = "guideR", LeftThigh = "thighL", RightThigh = "thighR", PistonFront = "pistonF", PistonRear = "pistonR", Own = "own";

        public static string NodeOf(string group)
        {
            if (group == "belt") return Belt;
            if (group == "guide_front") return GuideFront;
            if (group == "guide_rear") return GuideRear;
            if (group == "piston_front") return PistonFront;
            if (group == "piston_rear") return PistonRear;
            if (group == "thigh_R") return LeftThigh;
            if (group == "thigh_L") return RightThigh;
            return Own; // rings, cord ends, stretched straps, springs and wire are placed one by one
        }
    }

    /// <summary>
    /// Her outline as a belt under tension sees it: around her middle, angle by angle, the outermost skin over the belt's height, then the
    /// convex hull of that, because a tight strap bridges hollows such as the small of her back. Angle 0 is her front, positive towards her right.
    /// </summary>
    public sealed class BeltShape
    {
        public const int Bins = 72;
        public float CentreX, CentreZ;
        public readonly float[] Radius = new float[Bins];

        public static BeltShape From(List<Vector2> points, float room, Vector2? centre = null)
        {
            var s = new BeltShape();
            if (points.Count < 8) return null;
            float x0 = 9, x1 = -9, z0 = 9, z1 = -9;
            foreach (var p in points) { x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); z0 = Math.Min(z0, p.Y); z1 = Math.Max(z1, p.Y); }
            s.CentreX = centre?.X ?? (x0 + x1) / 2; s.CentreZ = centre?.Y ?? (z0 + z1) / 2;
            var hull = Hull(points);
            for (int i = 0; i < Bins; i++)
            {
                float a = i * 2f * MathF.PI / Bins, dx = MathF.Sin(a), dz = MathF.Cos(a), best = 0f;
                for (int k = 0; k < hull.Count; k++)
                {
                    Vector2 p = hull[k] - new Vector2(s.CentreX, s.CentreZ), q = hull[(k + 1) % hull.Count] - new Vector2(s.CentreX, s.CentreZ), e = q - p;
                    float den = dx * e.Y - dz * e.X;
                    if (MathF.Abs(den) < 1e-9f) continue;
                    float t = (p.X * e.Y - p.Y * e.X) / den, u = (p.X * dz - p.Y * dx) / den;
                    if (t > best && u >= -1e-4f && u <= 1.0001f) best = t;
                }
                s.Radius[i] = best + room;
            }
            return s;
        }

        private static List<Vector2> Hull(List<Vector2> pts)
        {
            var p = new List<Vector2>(pts);
            p.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
            var h = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                foreach (var q in p)
                {
                    while (h.Count >= start + 2 && Cross(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1);
                    h.Add(q);
                }
                h.RemoveAt(h.Count - 1);
                p.Reverse();
            }
            return h;
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        public float At(float angle)
        {
            float f = angle / (2f * MathF.PI) * Bins;
            f -= MathF.Floor(f / Bins) * Bins;
            int i = (int)f % Bins, j = (i + 1) % Bins; float t = f - MathF.Floor(f);
            return Radius[i] + (Radius[j] - Radius[i]) * t;
        }

        /// <summary>Moves a point of the model belt onto her outline: its distance from the model's inner oval is kept, scaled by thick.</summary>
        public Vector3 Warp(Vector3 v, float insideX, float insideZ, float beltY, float tall, float thick)
        {
            float r = MathF.Sqrt(v.X * v.X + v.Z * v.Z);
            if (r < 1e-5f) return new Vector3(CentreX, (v.Y - beltY) * tall, CentreZ);
            float sn = v.X / r, cs = v.Z / r, inner = 1f / MathF.Sqrt(sn * sn / (insideX * insideX) + cs * cs / (insideZ * insideZ));
            float nr = At(MathF.Atan2(sn, cs)) + (r - inner) * thick;
            return new Vector3(CentreX + sn * nr, (v.Y - beltY) * tall, CentreZ + cs * nr);
        }
    }

    /// <summary>
    /// The belt as a band of leather: straight from its lower edge to its upper edge at every angle (it can't follow a hollow up and down), and
    /// raised where needed so that line clears her skin at mid height too.
    /// </summary>
    public sealed class BeltBand
    {
        public const int Bins = BeltShape.Bins;
        public float CentreX, CentreZ;
        public readonly float[] Lower = new float[Bins], Upper = new float[Bins], Inner = new float[Bins];

        /// <summary>The model's inside radius by angle: the innermost point near each angle, so the pads are what meets her skin.</summary>
        public static float[] ModelInner(IList<Vector3> verts, float beltY, float half)
        {
            var raw = new float[Bins];
            for (int i = 0; i < Bins; i++) raw[i] = 9f;
            foreach (var v in verts)
            {
                if (MathF.Abs(v.Y - beltY) > half) continue;
                int b = Bin(MathF.Atan2(v.X, v.Z));
                raw[b] = MathF.Min(raw[b], MathF.Sqrt(v.X * v.X + v.Z * v.Z));
            }
            var o = new float[Bins];
            for (int i = 0; i < Bins; i++)
            {
                float m = 9f;
                for (int k = -2; k <= 2; k++) m = MathF.Min(m, raw[(i + k + Bins) % Bins]);
                o[i] = m;
            }
            return o;
        }

        public static int Bin(float angle)
        {
            float f = angle / (2f * MathF.PI) * Bins;
            int i = (int)MathF.Floor(f + 0.5f) % Bins;
            return i < 0 ? i + Bins : i;
        }

        public static BeltBand From(List<Vector2> lower, List<Vector2> mid, List<Vector2> upper, Vector2 centre, float room, float[] inner)
        {
            BeltShape a = BeltShape.From(lower, room, centre), m = BeltShape.From(mid, room, centre), b = BeltShape.From(upper, room, centre);
            if (a == null || m == null || b == null) return null;
            var o = new BeltBand { CentreX = centre.X, CentreZ = centre.Y };
            for (int i = 0; i < Bins; i++)
            {
                float lift = MathF.Max(0f, m.Radius[i] - (a.Radius[i] + b.Radius[i]) / 2);
                o.Lower[i] = a.Radius[i] + lift; o.Upper[i] = b.Radius[i] + lift; o.Inner[i] = inner[i];
            }
            return o;
        }

        /// <summary>Moves the band out by what each angle lacks, and a little to either side so the push doesn't leave a kink.</summary>
        public void Lift(float[] need)
        {
            var o = new float[Bins];
            for (int i = 0; i < Bins; i++)
            {
                float n = MathF.Max(need[i], 0.6f * MathF.Max(need[(i + 1) % Bins], need[(i + Bins - 1) % Bins]));
                o[i] = MathF.Max(n, 0f);
            }
            for (int i = 0; i < Bins; i++) { Lower[i] += o[i]; Upper[i] += o[i]; }
        }

        private static float Lerp(float[] t, float angle)
        {
            float f = angle / (2f * MathF.PI) * Bins;
            f -= MathF.Floor(f / Bins) * Bins;
            int i = (int)f % Bins, j = (i + 1) % Bins; float u = f - MathF.Floor(f);
            return t[i] + (t[j] - t[i]) * u;
        }

        /// <summary>The band's inside at an angle and a height (-1 lower edge, 1 upper edge).</summary>
        public float Inside(float angle, float height)
        {
            float u = Math.Clamp((height + 1f) / 2f, 0f, 1f);
            return Lerp(Lower, angle) + (Lerp(Upper, angle) - Lerp(Lower, angle)) * u;
        }

        public Vector3 Warp(Vector3 v, float beltY, float halfModel, float tall, float thick)
        {
            float r = MathF.Sqrt(v.X * v.X + v.Z * v.Z);
            if (r < 1e-5f) return new Vector3(CentreX, (v.Y - beltY) * tall, CentreZ);
            float a = MathF.Atan2(v.X, v.Z), sn = v.X / r, cs = v.Z / r, h = (v.Y - beltY) / halfModel;
            float nr = Inside(a, h) + (r - Lerp(Inner, a)) * thick;
            return new Vector3(CentreX + sn * nr, (v.Y - beltY) * tall, CentreZ + cs * nr);
        }
    }

    /// <summary>Turns cord length into piston travel. The cord is slack for a little past its length with her legs straight down, then pulls.
    /// All lengths are shares of her thigh, so it works the same on any body.</summary>
    public sealed class PistonDrive
    {
        public const float Slack = 0.12f, FullAt = 0.50f, Lag = 0.06f;
        public float Stroke { get; private set; } // 0..1

        public void Reset() { Stroke = 0f; }

        public void Update(float cordLength, float restLength, float thigh, float dt)
        {
            float want = ((cordLength - restLength) / thigh - Slack) / FullAt;
            want = want < 0f ? 0f : want > 1f ? 1f : want;
            float k = dt <= 0f ? 1f : 1f - (float)Math.Exp(-dt / Lag);
            Stroke += (want - Stroke) * k;
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
