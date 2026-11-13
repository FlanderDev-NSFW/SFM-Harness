using System;
using System.Collections.Generic;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace Harness
{
    /// <summary>
    /// Zeta's cords lie on her skin where a straight line would pass through it. For that the rig has to know where her skin is under the cords on
    /// every frame, and baking her whole body each frame costs milliseconds. So it keeps a small patch: the skin within reach of the two cords, and
    /// moves just those vertices with her hips and thighs each frame. All of it stays in memory; only counts and gaps are logged.
    /// </summary>
    internal sealed partial class HarnessRig
    {
        /// <summary>
        /// Her mesh cannot be read for its bone weights, so the patch learns them: each thigh is swung a little, once, and how far each vertex follows
        /// tells how much of it belongs to that thigh. The rest belongs to her hips.
        /// </summary>
        private sealed class SkinPatch
        {
            public int[] Vert;                 // which of her vertices
            public int[] Tri;                  // triangles, as places in Vert
            public NVector3[] InHips, InRight, InLeft, World;   // each vertex as her hips and each thigh carry it
            public float[] Right, Left;        // how much of it follows each thigh
            public Transform Hips, RightLeg, LeftLeg;
            public bool Flip;                  // her triangles wind the other way round from their normals
        }

        private SkinPatch _patch; private bool _patchFailed;
        private readonly Transform[][] _wrapSeg = { new Transform[WrapSegs], new Transform[WrapSegs] };
        private const int WrapSegs = 6, WrapSamples = 20;
        private readonly NVector3[][] _path = { new NVector3[WrapSegs + 1], new NVector3[WrapSegs + 1] }; private readonly int[] _pathN = new int[2];
        private readonly List<int> _wrapNear = new List<int>();
        private readonly NVector3[] _push = new NVector3[WrapSamples + 1]; private readonly float[] _pushH = new float[WrapSamples + 1]; private readonly int[] _hull = new int[WrapSamples + 1];

        private void MakeWrapSegments(Mesh tube, Material mat, int layer)
        {
            for (int c = 0; c < 2; c++)
                for (int j = 1; j < WrapSegs; j++)
                {
                    var go = new GameObject("Cord" + c + "Bend" + j);
                    go.layer = layer;
                    go.transform.SetParent(_top.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = tube;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = mat; r.allowOcclusionWhenDynamic = false;
                    go.transform.localScale = Vector3.zero;
                    _wrapSeg[c][j] = go.transform; _zetaOnly.Add(go);
                }
            _wrapSeg[0][0] = _cords[0]; _wrapSeg[1][0] = _cords[1];
        }

        /// <summary>Her skin within reach of the cords: everything near either piston eye or either ring. A cord is short, so no part of it leaves that.</summary>
        private void BuildPatch(Animator anim, Vector3 eyeF, Vector3 eyeR, Vector3 ringB, Vector3 ringF, Vector3 right)
        {
            _patchFailed = true;   // until it succeeds; a refit tries again
            var smr = HarnessController.FindBody(anim);
            if (smr == null) return;
            Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips), rl = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg), ll = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            if (hips == null || rl == null || ll == null) return;
            var w = Bake(smr, out int[] tris);
            Vector3[] nl = _bakeMesh.normals; Quaternion rot = smr.transform.rotation;
            NVector3 e0 = N(eyeF), e1 = N(eyeR), r0 = N(ringB), r1 = N(ringF);
            const float nearEye = 0.09f * 0.09f, nearRing = 0.08f * 0.08f;
            var local = new int[w.Length]; var verts = new List<int>();
            for (int i = 0; i < w.Length; i++)
            {
                NVector3 p = N(w[i]);
                bool take = NVector3.DistanceSquared(p, e0) < nearEye || NVector3.DistanceSquared(p, e1) < nearEye || NVector3.DistanceSquared(p, r0) < nearRing || NVector3.DistanceSquared(p, r1) < nearRing;
                local[i] = take ? verts.Count : -1;
                if (take) verts.Add(i);
            }
            var tl = new List<int>();
            for (int i = 0; i < tris.Length; i += 3)
                if (local[tris[i]] >= 0 && local[tris[i + 1]] >= 0 && local[tris[i + 2]] >= 0) { tl.Add(local[tris[i]]); tl.Add(local[tris[i + 1]]); tl.Add(local[tris[i + 2]]); }
            if (tl.Count == 0) return;
            int n = verts.Count;
            var p_ = new SkinPatch { Vert = verts.ToArray(), Tri = tl.ToArray(), InHips = new NVector3[n], InRight = new NVector3[n], InLeft = new NVector3[n], World = new NVector3[n], Right = new float[n], Left = new float[n], Hips = hips, RightLeg = rl, LeftLeg = ll };
            // which way her triangles face, by her own normals
            int agree = 0, differ = 0;
            for (int t = 0; t < p_.Tri.Length; t += 30)
            {
                NVector3 a = N(w[p_.Vert[p_.Tri[t]]]), fn = NVector3.Cross(N(w[p_.Vert[p_.Tri[t + 1]]]) - a, N(w[p_.Vert[p_.Tri[t + 2]]]) - a);
                if (NVector3.Dot(fn, N(rot * nl[p_.Vert[p_.Tri[t]]])) >= 0f) agree++; else differ++;
            }
            p_.Flip = differ > agree;
            // each thigh swung forward a little: a vertex that goes all the way with it is all thigh
            Quaternion swing = Quaternion.AngleAxis(-18f, right);
            var nq = NQ(swing);
            foreach (bool isRight in new[] { true, false })
            {
                Transform leg = isRight ? rl : ll; Quaternion saved = leg.rotation; Vector3[] moved;
                try { leg.rotation = swing * saved; moved = Bake(smr, out _); }
                finally { leg.rotation = saved; }
                NVector3 pivot = N(leg.position); float[] share = isRight ? p_.Right : p_.Left;
                for (int k = 0; k < n; k++)
                {
                    NVector3 p0 = N(w[p_.Vert[k]]), rigid = pivot + NVector3.Transform(p0 - pivot, nq) - p0, went = N(moved[p_.Vert[k]]) - p0;
                    float r2 = rigid.LengthSquared();
                    float s_ = r2 < 4e-6f ? 0f : NVector3.Dot(went, rigid) / r2;
                    share[k] = s_ < 0f ? 0f : s_ > 1f ? 1f : s_;
                }
            }
            var hq = NQ(Quaternion.Inverse(hips.rotation)); var rq = NQ(Quaternion.Inverse(rl.rotation)); var lq = NQ(Quaternion.Inverse(ll.rotation));
            NVector3 hp = N(hips.position), rp = N(rl.position), lp = N(ll.position);
            for (int k = 0; k < n; k++)
            {
                float sum = p_.Right[k] + p_.Left[k];
                if (sum > 1f) { p_.Right[k] /= sum; p_.Left[k] /= sum; }
                NVector3 p0 = N(w[p_.Vert[k]]);
                p_.InHips[k] = NVector3.Transform(p0 - hp, hq); p_.InRight[k] = NVector3.Transform(p0 - rp, rq); p_.InLeft[k] = NVector3.Transform(p0 - lp, lq);
            }
            _patch = p_; _patchFailed = false;
            Plugin.Logger.LogInfo("[SFM-ACCEPT] wrap: skin patch of " + n + " vertices, " + p_.Tri.Length / 3 + " triangles");
        }

        /// <summary>Moves the patch with her hips and thighs.</summary>
        private static void Skin(SkinPatch p)
        {
            var hq = NQ(p.Hips.rotation); var rq = NQ(p.RightLeg.rotation); var lq = NQ(p.LeftLeg.rotation);
            NVector3 hp = N(p.Hips.position), rp = N(p.RightLeg.position), lp = N(p.LeftLeg.position);
            for (int k = 0; k < p.World.Length; k++)
            {
                float r = p.Right[k], l = p.Left[k], h = 1f - r - l;
                NVector3 v = default;
                if (h > 0f) v += (hp + NVector3.Transform(p.InHips[k], hq)) * h;
                if (r > 0f) v += (rp + NVector3.Transform(p.InRight[k], rq)) * r;
                if (l > 0f) v += (lp + NVector3.Transform(p.InLeft[k], lq)) * l;
                p.World[k] = v;
            }
        }

        /// <summary>
        /// The path of a taut cord from a to b over her skin: straight where nothing is in the way; where the straight line would pass under her skin,
        /// it rises to lie on it, and between those places it runs straight again (the hull of the lifted points). The first and last stretch, where the
        /// cord sits in its clip, are left alone.
        /// </summary>
        private int Wrap(SkinPatch p, NVector3 a, NVector3 b, float clear, NVector3[] path)
        {
            path[0] = a; path[1] = b;
            float len = NVector3.Distance(a, b);
            if (p == null || len < 0.04f) return 2;
            NVector3 lo = NVector3.Min(a, b) - new NVector3(0.03f), hi = NVector3.Max(a, b) + new NVector3(0.03f);
            _wrapNear.Clear();
            var W = p.World; var T = p.Tri;
            for (int t = 0; t < T.Length; t += 3)
            {
                NVector3 v0 = W[T[t]], v1 = W[T[t + 1]], v2 = W[T[t + 2]];
                if ((v0.X < lo.X && v1.X < lo.X && v2.X < lo.X) || (v0.X > hi.X && v1.X > hi.X && v2.X > hi.X) || (v0.Y < lo.Y && v1.Y < lo.Y && v2.Y < lo.Y) || (v0.Y > hi.Y && v1.Y > hi.Y && v2.Y > hi.Y) || (v0.Z < lo.Z && v1.Z < lo.Z && v2.Z < lo.Z) || (v0.Z > hi.Z && v1.Z > hi.Z && v2.Z > hi.Z)) continue;
                _wrapNear.Add(t);
            }
            if (_wrapNear.Count == 0) return 2;
            bool any = false;
            for (int k = 0; k <= WrapSamples; k++)
            {
                float f = k / (float)WrapSamples, along = f * len;
                NVector3 s = a + (b - a) * f;
                _push[k] = s; _pushH[k] = 0f;
                if (along < 0.012f || along > len - 0.012f) continue;
                int best = -1; float bd = 1e9f; NVector3 bq = default;
                foreach (int t in _wrapNear)
                {
                    NVector3 q = ClosestOnTri(s, W[T[t]], W[T[t + 1]], W[T[t + 2]]); float d = NVector3.DistanceSquared(s, q);
                    if (d < bd) { bd = d; best = t; bq = q; }
                }
                if (best < 0 || bd > 0.015f * 0.015f) continue;   // further in than that it is squeezed between her thighs, and there is nowhere to lift it to
                NVector3 v0 = W[T[best]], fn = NVector3.Cross(W[T[best + 1]] - v0, W[T[best + 2]] - v0);
                float fl = fn.Length(); if (fl < 1e-12f) continue;
                fn = fn / (p.Flip ? -fl : fl);
                float side = NVector3.Dot(s - bq, fn), dist = MathF.Sqrt(bd);
                if (side >= 0f && dist >= clear) continue;
                NVector3 lifted = side < 0f ? bq + fn * clear : bq + (s - bq) * (clear / MathF.Max(dist, 1e-5f));
                _push[k] = lifted; _pushH[k] = NVector3.Distance(lifted, s); any = true;
            }
            if (!any) return 2;
            // the taut line over the lifted points: their upper hull, by how far each was lifted
            int h = 0;
            for (int k = 0; k <= WrapSamples; k++)
            {
                if (k != 0 && k != WrapSamples && _pushH[k] <= 0f) continue;
                while (h >= 2)
                {
                    int i0 = _hull[h - 2], i1 = _hull[h - 1];
                    float cross = (i1 - i0) * (_pushH[k] - _pushH[i0]) - (_pushH[i1] - _pushH[i0]) * (k - i0);
                    if (cross >= 0f) h--; else break;
                }
                _hull[h++] = k;
            }
            // more bends than there are pieces to draw: keep the highest
            while (h > WrapSegs + 1)
            {
                int low = 1; for (int j = 2; j < h - 1; j++) if (_pushH[_hull[j]] < _pushH[_hull[low]]) low = j;
                for (int j = low; j < h - 1; j++) _hull[j] = _hull[j + 1];
                h--;
            }
            for (int j = 0; j < h; j++) path[j] = _push[_hull[j]];
            path[0] = a; path[h - 1] = b;
            return h;
        }

        private void DrawPath(int cord, float ls)
        {
            NVector3[] path = _path[cord]; int n = _pathN[cord];
            for (int j = 0; j < WrapSegs; j++)
            {
                if (j < n - 1) Line(_wrapSeg[cord][j], V(path[j]), V(path[j + 1]), ls);
                else if (j > 0) _wrapSeg[cord][j].localScale = Vector3.zero;
            }
        }
    }
}
