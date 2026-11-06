using System;
using System.Collections.Generic;
using Harness.Logic;
using Il2CppInterop.Runtime;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace Harness
{
    /// <summary>Builds the model's groups as rigid meshes on a few nodes, and places the nodes on her bones each frame.</summary>
    internal sealed class HarnessRig
    {
        private const string Tag = "Harness.";
        private readonly HarnessFile _file;
        private GameObject _top;
        private Transform _root, _thighL, _thighR, _pistonF, _pistonR;
        private readonly Transform[] _cords = new Transform[4];
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private Vector3 _hipL, _hipR, _hoopL, _hoopR;
        private Vector3 _anchorL, _anchorR, _eyeletL, _eyeletR, _cordF, _cordR;
        private bool _visible;
        public string Status = "";

        public HarnessRig(HarnessFile file) { _file = file; }

        public bool Built => _top != null;

        private static Vector3 U(NVector3 v) => new Vector3(v.X, v.Y, v.Z);
        private Vector3 Sock(string n) => U(_file.Sockets[n].Position);

        public void Build(int layer)
        {
            Destroy();
            _top = new GameObject("Harness");
            UnityEngine.Object.DontDestroyOnLoad(_top);
            // Beta's R side is her left
            _hipL = Sock("BODY_hip_R"); _hipR = Sock("BODY_hip_L"); _hoopL = Sock("BODY_hoop_R"); _hoopR = Sock("BODY_hoop_L");
            _anchorL = Sock("SOCKET_bar_anchor_R"); _anchorR = Sock("SOCKET_bar_anchor_L"); _eyeletL = Sock("SOCKET_bar_eyelet_R"); _eyeletR = Sock("SOCKET_bar_eyelet_L");
            _cordF = Sock("SOCKET_cord_front"); _cordR = Sock("SOCKET_cord_rear");
            _root = Node("Root", _top.transform); _thighL = Node("ThighL", _top.transform); _thighR = Node("ThighR", _top.transform);
            _pistonF = Node("PistonFront", _root); _pistonR = Node("PistonRear", _root);
            var mats = new Material[_file.Materials.Count];
            Material source = FindToon();
            for (int i = 0; i < mats.Length; i++) mats[i] = MakeMaterial(source, _file.Materials[i]);
            foreach (var g in _file.Groups)
            {
                string node = GroupMap.NodeOf(g.Name);
                Transform parent = node switch { GroupMap.LeftThigh => _thighL, GroupMap.RightThigh => _thighR, GroupMap.PistonFront => _pistonF, GroupMap.PistonRear => _pistonR, _ => _root };
                Vector3 origin = node == GroupMap.LeftThigh ? _hoopL : node == GroupMap.RightThigh ? _hoopR : Vector3.zero;
                AddGroup(g, parent, origin, mats, layer);
            }
            Mesh tube = Tube(0.0014f, 6);
            _owned.Add(tube);
            int cordMat = _file.Materials.FindIndex(m => m.Name == "cord");
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("Cord" + i);
                go.layer = layer;
                go.transform.SetParent(_top.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = tube;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mats[cordMat];
                r.allowOcclusionWhenDynamic = false;
                _cords[i] = go.transform;
            }
            SetVisible(false);
        }

        private Transform Node(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private void AddGroup(HGroup g, Transform parent, Vector3 origin, Material[] mats, int layer)
        {
            var go = new GameObject(g.Name);
            go.layer = layer;
            go.transform.SetParent(parent, false);
            var verts = new List<Vector3>(); var normals = new List<Vector3>();
            var subs = new List<int[]>(); var used = new Material[g.Parts.Count];
            for (int k = 0; k < g.Parts.Count; k++)
            {
                HPart p = g.Parts[k]; int b = verts.Count;
                for (int i = 0; i < p.Positions.Length; i++) { verts.Add(U(p.Positions[i]) - origin); normals.Add(U(p.Normals[i])); }
                var idx = new int[p.Indices.Length];
                for (int i = 0; i < idx.Length; i++) idx[i] = p.Indices[i] + b;
                subs.Add(idx); used[k] = mats[p.Material];
            }
            var mesh = new Mesh { name = Tag + g.Name };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts.ToArray();
            mesh.normals = normals.ToArray();
            mesh.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
            mesh.RecalculateBounds();
            _owned.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = used;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.allowOcclusionWhenDynamic = false;
        }

        private static Mesh Tube(float radius, int sides)
        {
            var v = new Vector3[sides * 2]; var n = new Vector3[sides * 2]; var t = new int[sides * 6];
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides; var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                v[i] = d * radius; v[i + sides] = d * radius + Vector3.forward; n[i] = d; n[i + sides] = d;
                int j = (i + 1) % sides, o = i * 6;
                t[o] = i; t[o + 1] = i + sides; t[o + 2] = j; t[o + 3] = j; t[o + 4] = i + sides; t[o + 5] = j + sides;
            }
            var m = new Mesh { name = Tag + "Cord", vertices = v, normals = n, triangles = t };
            m.RecalculateBounds();
            return m;
        }

        private static Material FindToon()
        {
            try
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>()))
                {
                    var mat = o.TryCast<Material>();
                    if (mat != null && mat.name != null && mat.name.StartsWith("tejyou", StringComparison.Ordinal)) return mat;
                }
            }
            catch (Exception e) { Plugin.Logger.LogWarning("material lookup failed: " + e.Message); }
            return null;
        }

        private Material MakeMaterial(Material source, HMaterial m)
        {
            Material mat;
            if (source != null) mat = new Material(source);
            else mat = new Material(Shader.Find("UnityChanToonShader/Toon_DoubleShadeWithFeather") ?? Shader.Find("Standard"));
            mat.name = Tag + m.Name;
            Texture white = Texture2D.whiteTexture;
            foreach (string tex in new[] { "_MainTex", "_BaseMap", "_1st_ShadeMap", "_2nd_ShadeMap" })
                if (mat.HasProperty(tex)) mat.SetTexture(tex, white);
            var c = new Color(m.Color.X, m.Color.Y, m.Color.Z, 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_1st_ShadeColor")) mat.SetColor("_1st_ShadeColor", c * 0.68f);
            if (mat.HasProperty("_2nd_ShadeColor")) mat.SetColor("_2nd_ShadeColor", c * 0.45f);
            _owned.Add(mat);
            return mat;
        }

        public void SetVisible(bool on)
        {
            if (_top == null || _visible == on) return;
            _visible = on;
            _top.SetActive(on);
        }

        // Beta's inside sizes: the belt's opening (half width, half depth) and the hoop's (across, front to back), and the belt's height.
        private const float BeltInX = 0.136f, BeltInZ = 0.118f, BeltY = 0.219f, HoopInX = 0.0805f, HoopInZ = 0.0765f, Room = 1.04f;
        private Vector3 _scale = Vector3.one, _beltOffset, _hoopScaleL = Vector3.one, _hoopScaleR = Vector3.one, _hoopOffL, _hoopOffR;
        private float _nextFit;

        /// <summary>Sizes come from a bake of her body; positions stay on the bones (a bake only roughly matches them).</summary>
        private void Fit(Animator anim)
        {
            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
            var smr = HarnessController.FindBody(anim);
            if (smr == null) return;
            var mesh = new Mesh();
            smr.BakeMesh(mesh, true);
            Transform t = smr.transform;
            Vector3[] raw = mesh.vertices;
            var w = new Vector3[raw.Length];
            for (int i = 0; i < raw.Length; i++) w[i] = t.position + t.rotation * Vector3.Scale(t.lossyScale, raw[i]);
            UnityEngine.Object.Destroy(mesh);
            Vector3 hl = B(HumanBodyBones.LeftUpperLeg).position, hr = B(HumanBodyBones.RightUpperLeg).position, mid = (hl + hr) * 0.5f;
            Vector3 chest = B(HumanBodyBones.Chest).position;
            Vector3 right = (hr - hl).normalized, up = Vector3.ProjectOnPlane(chest - mid, right).normalized, fwd = Vector3.Cross(right, up);
            float top = Vector3.Dot(chest - mid, up), best = 9f, bh = 0f, bcx = 0f, bcz = 0f, bd = 0f;
            for (float h = 0.2f * top; h <= top; h += 0.1f * top)
            {
                var xs = new List<float>(); var zs = new List<float>();
                Vector3 at = mid + up * h;
                foreach (var p in w) { Vector3 q = p - at; if (Mathf.Abs(Vector3.Dot(q, up)) < 0.012f) xs.Add(Vector3.Dot(q, right)); }
                var sx = SliceStats.CentralSpan(xs, 0.03f);
                foreach (var p in w) { Vector3 q = p - at; if (Mathf.Abs(Vector3.Dot(q, up)) < 0.012f) { float x = Vector3.Dot(q, right); if (x >= sx.Min && x <= sx.Max) zs.Add(Vector3.Dot(q, fwd)); } }
                var sz = SliceStats.CentralSpan(zs, 0.03f);
                if (sx.Max - sx.Min > 0.02f && sx.Max - sx.Min < best) { best = sx.Max - sx.Min; bh = h; bcx = (sx.Max + sx.Min) / 2; bcz = (sz.Max + sz.Min) / 2; bd = sz.Max - sz.Min; }
            }
            if (best > 8f) return;
            float scx = best / 2 * Room / BeltInX, scz = bd / 2 * Room / BeltInZ, scy = (scx + scz) / 2;
            _scale = new Vector3(scx, scy, scz);
            _beltOffset = new Vector3(bcx, bh, bcz);
            Vector3 kl = B(HumanBodyBones.LeftLowerLeg).position, kr = B(HumanBodyBones.RightLowerLeg).position;
            (_hoopScaleL, _hoopOffL) = FitThigh(w, hl, kl, hr, kr, right, scy);
            (_hoopScaleR, _hoopOffR) = FitThigh(w, hr, kr, hl, kl, right, scy);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] fit: waist " + best.ToString("0.000") + " x " + bd.ToString("0.000") + " at " + bh.ToString("0.000") + ", scale " + _scale + ", hoops " + _hoopScaleL + " " + _hoopScaleR);
        }

        private static (Vector3, Vector3) FitThigh(Vector3[] w, Vector3 hip, Vector3 knee, Vector3 otherHip, Vector3 otherKnee, Vector3 right, float sy)
        {
            Vector3 d = (knee - hip).normalized, at = hip + (knee - hip) * Plugin.HoopAlongThigh.Value, oat = otherHip + (otherKnee - otherHip) * Plugin.HoopAlongThigh.Value;
            Vector3 a = Vector3.ProjectOnPlane(right, d).normalized, b = Vector3.Cross(a, d);
            float a0 = 9, a1 = -9, b0 = 9, b1 = -9;
            foreach (var p in w)
            {
                Vector3 q = p - at;
                if (Mathf.Abs(Vector3.Dot(q, d)) > 0.012f) continue;
                if ((p - at).sqrMagnitude > (p - oat).sqrMagnitude || q.sqrMagnitude > 0.04f) continue;   // the other leg's skin
                float x = Vector3.Dot(q, a), z = Vector3.Dot(q, b);
                a0 = Mathf.Min(a0, x); a1 = Mathf.Max(a1, x); b0 = Mathf.Min(b0, z); b1 = Mathf.Max(b1, z);
            }
            if (a1 < a0) return (Vector3.one * sy, Vector3.zero);
            return (new Vector3((a1 - a0) / 2 * Room / HoopInX, sy, (b1 - b0) / 2 * Room / HoopInZ), new Vector3((a0 + a1) / 2, 0, (b0 + b1) / 2));
        }

        public void Place(Animator anim)
        {
            if (Time.time >= _nextFit) { _nextFit = Time.time + 5f; Fit(anim); }
            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
            Vector3 hl = B(HumanBodyBones.LeftUpperLeg).position, hr = B(HumanBodyBones.RightUpperLeg).position, mid = (hl + hr) * 0.5f;
            Vector3 right = (hr - hl).normalized, up = Vector3.ProjectOnPlane(B(HumanBodyBones.Spine).position - mid, right).normalized;
            Vector3 fwd = Vector3.Cross(right, up);
            Quaternion rot = Quaternion.LookRotation(fwd, up);
            Vector3 S = _scale * Plugin.ScaleTrim.Value;
            Vector3 waist = mid + right * _beltOffset.x + up * _beltOffset.y + fwd * _beltOffset.z;
            _root.SetPositionAndRotation(waist - rot * new Vector3(0, BeltY * S.y, 0), rot);
            _root.localScale = S;
            PlaceThigh(_thighL, hl, B(HumanBodyBones.LeftLowerLeg).position, _hipL, _hoopL, rot, right, _hoopScaleL, _hoopOffL);
            PlaceThigh(_thighR, hr, B(HumanBodyBones.RightLowerLeg).position, _hipR, _hoopR, rot, right, _hoopScaleR, _hoopOffR);
            Vector3 aL = _thighL.TransformPoint(_anchorL - _hoopL), aR = _thighR.TransformPoint(_anchorR - _hoopR), eL = _thighL.TransformPoint(_eyeletL - _hoopL), eR = _thighR.TransformPoint(_eyeletR - _hoopR);
            Vector3 pF = _root.TransformPoint(_cordF), pR = _root.TransformPoint(_cordR);
            float cs = S.y;
            Stretch(_cords[0], aR, eL, cs); Stretch(_cords[1], eL, pF, cs); Stretch(_cords[2], aL, eR, cs); Stretch(_cords[3], eR, pR, cs);
            Status = "scale " + S;
        }

        private void PlaceThigh(Transform node, Vector3 hip, Vector3 knee, Vector3 modelHip, Vector3 modelHoop, Quaternion rot, Vector3 right, Vector3 scale, Vector3 off)
        {
            Vector3 axis = knee - hip, da = axis.normalized, dm = rot * (modelHoop - modelHip).normalized;
            Quaternion q = Quaternion.FromToRotation(dm, da) * rot;
            Vector3 a = Vector3.ProjectOnPlane(right, da).normalized, b = Vector3.Cross(a, da);
            node.SetPositionAndRotation(hip + axis * Plugin.HoopAlongThigh.Value + a * off.x + b * off.z, q);
            node.localScale = scale;
        }

        private static void Stretch(Transform t, Vector3 a, Vector3 b, float s)
        {
            Vector3 d = b - a;
            t.SetPositionAndRotation(a, Quaternion.LookRotation(d, Vector3.up));
            t.localScale = new Vector3(s, s, d.magnitude);
        }

        public void Destroy()
        {
            if (_top != null) UnityEngine.Object.Destroy(_top);
            _top = null;
            foreach (var o in _owned) if (o != null) UnityEngine.Object.Destroy(o);
            _owned.Clear();
            _visible = false;
        }
    }
}
