using System;
using System.Collections.Generic;
using Harness.Logic;
using Il2CppInterop.Runtime;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace Harness
{
    /// <summary>
    /// Builds the model as rigid meshes and places them on her each frame. The belt sits at her waist, the lower assembly (guides, pistons,
    /// front chain, ring) at the bottom of her pelvis, the hoops on her thighs. Straps, springs and wire stretch between those; rings swing to
    /// their pull. Sizes come from a bake of her body, positions from the bones (a bake only roughly matches them).
    /// </summary>
    internal sealed class HarnessRig
    {
        private const string Tag = "Harness.";
        // Beta's sizes: the belt's opening, the hoop's opening, the belt's height, and the point of the model that goes at her crotch.
        private const float BeltInX = 0.136f, BeltInZ = 0.118f, BeltY = 0.219f, HoopInX = 0.0805f, HoopInZ = 0.0765f, Room = 1.04f, WaistShare = 0.45f, CrotchShare = 0.32f, StrokeMetres = 0.09f;
        private static readonly Vector3 Crotch = new Vector3(0f, -0.06f, 0.034f);

        private sealed class Stretch { public Transform T; public Vector3 Start, End, Hint; public float Length; public Transform From, To; public Vector3 FromOrigin, ToOrigin; }

        private readonly HarnessFile _file;
        private GameObject _top;
        private Transform _belt, _lower, _thighL, _thighR, _pistonF, _pistonR;
        private readonly Dictionary<string, Transform> _own = new Dictionary<string, Transform>();
        private readonly List<Stretch> _stretches = new List<Stretch>();
        private readonly Transform[] _cords = new Transform[4];
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private readonly PistonDrive _driveF = new PistonDrive(), _driveR = new PistonDrive();
        private Vector3 _hipL, _hipR, _hoopL, _hoopR, _axisF, _axisR;
        private Vector3 _scale = Vector3.one, _waist, _crotch, _hoopScaleL = Vector3.one, _hoopScaleR = Vector3.one, _hoopOffL, _hoopOffR;
        private float _lowerScale = 1f, _nextFit, _thighLength;
        private int _fits;
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
            _axisF = (Sock("AXIS_front") - Sock("SOCKET_guide_front")).normalized; _axisR = (Sock("AXIS_rear") - Sock("SOCKET_guide_rear")).normalized;
            _belt = Node("Belt", _top.transform); _lower = Node("Lower", _top.transform); _thighL = Node("ThighL", _top.transform); _thighR = Node("ThighR", _top.transform);
            _pistonF = Node("PistonFront", _lower); _pistonR = Node("PistonRear", _lower);
            var mats = new Material[_file.Materials.Count];
            Material source = FindToon();
            for (int i = 0; i < mats.Length; i++) mats[i] = MakeMaterial(source, _file.Materials[i]);
            var ends = StretchEnds();
            foreach (var g in _file.Groups)
            {
                string node = GroupMap.NodeOf(g.Name);
                if (node != GroupMap.Own)
                {
                    Transform parent = node switch { GroupMap.LeftThigh => _thighL, GroupMap.RightThigh => _thighR, GroupMap.PistonFront => _pistonF, GroupMap.PistonRear => _pistonR, GroupMap.Belt => _belt, _ => _lower };
                    Vector3 origin = node == GroupMap.LeftThigh ? _hoopL : node == GroupMap.RightThigh ? _hoopR : Vector3.zero;
                    AddGroup(g, parent, origin, Quaternion.identity, mats, layer);
                }
                else if (ends.TryGetValue(g.Name, out var st))
                {
                    Vector3 d = st.End - st.Start;
                    st.Length = d.magnitude;
                    st.T = AddGroup(g, _top.transform, st.Start, Quaternion.Inverse(Quaternion.LookRotation(d, st.Hint)), mats, layer);
                    _stretches.Add(st);
                }
                else _own[g.Name] = AddGroup(g, _top.transform, U(g.Pivot), Quaternion.identity, mats, layer);
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
            _driveF.Reset(); _driveR.Reset();
            _fits = 0; _nextFit = 0f;
            SetVisible(false);
        }

        private Dictionary<string, Stretch> StretchEnds()
        {
            var d = new Dictionary<string, Stretch>();
            Stretch S(string a, Transform from, Vector3 fromOrigin, string b, Transform to, Vector3 toOrigin) =>
                new Stretch { Start = Sock(a), End = Sock(b), Hint = Vector3.forward, From = from, To = to, FromOrigin = fromOrigin, ToOrigin = toOrigin };
            d["vstrap_R"] = S("SOCKET_vstrap_top_R", _belt, Vector3.zero, "SOCKET_vstrap_bottom_R", _lower, Vector3.zero);
            d["vstrap_L"] = S("SOCKET_vstrap_top_L", _belt, Vector3.zero, "SOCKET_vstrap_bottom_L", _lower, Vector3.zero);
            d["rear_strap"] = S("SOCKET_rear_strap_top", _belt, Vector3.zero, "SOCKET_rear_strap_bottom", _lower, Vector3.zero);
            d["wire_up"] = S("WIRE_tip", _lower, Vector3.zero, "WIRE_mid", _belt, Vector3.zero);
            d["wire_down"] = S("WIRE_mid", _belt, Vector3.zero, "WIRE_end", _thighL, _hoopL);
            foreach (string tag in new[] { "front", "rear" })
                foreach (string side in new[] { "R", "L" })
                {
                    string name = "spring_" + tag + "_" + side;
                    var g = _file.Groups.Find(x => x.Name == name);
                    d[name] = new Stretch { Start = U(g.Pivot), End = Sock("SPRING_top_" + tag + "_" + side), Hint = Vector3.forward, From = _lower, To = tag == "front" ? _pistonF : _pistonR };
                }
            return d;
        }

        private static Transform Node(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private Transform AddGroup(HGroup g, Transform parent, Vector3 origin, Quaternion turn, Material[] mats, int layer)
        {
            var go = new GameObject(g.Name);
            go.layer = layer;
            go.transform.SetParent(parent, false);
            var verts = new List<Vector3>(); var normals = new List<Vector3>();
            var subs = new List<int[]>(); var used = new Material[g.Parts.Count];
            for (int k = 0; k < g.Parts.Count; k++)
            {
                HPart p = g.Parts[k]; int b = verts.Count;
                for (int i = 0; i < p.Positions.Length; i++) { verts.Add(turn * (U(p.Positions[i]) - origin)); normals.Add(turn * U(p.Normals[i])); }
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
            return go.transform;
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

        // --- fit -----------------------------------------------------------------------------------------------------------------

        private static (float W, float D, float X, float Z) Slice(Vector3[] w, Vector3 at, Vector3 up, Vector3 right, Vector3 fwd)
        {
            var xs = new List<float>(); var zs = new List<float>();
            foreach (var p in w) { Vector3 q = p - at; if (Mathf.Abs(Vector3.Dot(q, up)) < 0.012f) xs.Add(Vector3.Dot(q, right)); }
            var sx = SliceStats.CentralSpan(xs, 0.03f);
            foreach (var p in w) { Vector3 q = p - at; if (Mathf.Abs(Vector3.Dot(q, up)) < 0.012f) { float x = Vector3.Dot(q, right); if (x >= sx.Min && x <= sx.Max) zs.Add(Vector3.Dot(q, fwd)); } }
            var sz = SliceStats.CentralSpan(zs, 0.03f);
            return (sx.Max - sx.Min, sz.Max - sz.Min, (sx.Max + sx.Min) / 2, (sz.Max + sz.Min) / 2);
        }

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
            Vector3 chest = B(HumanBodyBones.Chest).position, kl = B(HumanBodyBones.LeftLowerLeg).position, kr = B(HumanBodyBones.RightLowerLeg).position;
            Vector3 right = (hr - hl).normalized, up = Vector3.ProjectOnPlane(chest - mid, right).normalized, fwd = Vector3.Cross(right, up);
            float top = Vector3.Dot(chest - mid, up), h0 = top * WaistShare, step = top * 0.12f;
            // her arms may hang against her sides: skin nearer an arm than about half its distance from the spine isn't torso
            var torso = new List<Vector3>(w.Length);
            Vector3 aL0 = B(HumanBodyBones.LeftUpperArm).position, aL1 = B(HumanBodyBones.LeftLowerArm).position, aL2 = B(HumanBodyBones.LeftHand).position;
            Vector3 aR0 = B(HumanBodyBones.RightUpperArm).position, aR1 = B(HumanBodyBones.RightLowerArm).position, aR2 = B(HumanBodyBones.RightHand).position;
            foreach (var p in w)
            {
                float axis = SegDist(p, mid, chest) * 0.45f;
                if (SegDist(p, aL0, aL1) < axis || SegDist(p, aL1, aL2) < axis || SegDist(p, aR0, aR1) < axis || SegDist(p, aR1, aR2) < axis) continue;
                torso.Add(p);
            }
            Vector3[] tw = torso.ToArray();
            var a = Slice(tw, mid + up * h0, up, right, fwd); var b0 = Slice(tw, mid + up * (h0 - step), up, right, fwd); var c = Slice(tw, mid + up * (h0 + step), up, right, fwd);
            if (a.W < 0.02f) return;
            float width = Mathf.Max(a.W, Mathf.Max(b0.W, c.W)), depth = Mathf.Max(a.D, Mathf.Max(b0.D, c.D));
            float scx = width / 2 * Room / BeltInX, scz = depth / 2 * Room / BeltInZ, scy = (scx + scz) / 2;
            _scale = new Vector3(scx, scy, scz);
            _waist = new Vector3(a.X, h0, a.Z);
            _lowerScale = scy;
            // crotch: her thighs may touch, so the height comes from the leg's length, and front to back from the skin down the middle
            _thighLength = (kl - hl).magnitude;
            float ch = -CrotchShare * _thighLength, cz = 0f, f0 = 9f, f1 = -9f;
            foreach (var p in w)
            {
                Vector3 q = p - mid;
                if (Mathf.Abs(Vector3.Dot(q, up) - ch) < 0.02f && Mathf.Abs(Vector3.Dot(q, right)) < 0.02f) { float z = Vector3.Dot(q, fwd); f0 = Mathf.Min(f0, z); f1 = Mathf.Max(f1, z); }
            }
            if (f1 > f0) cz = (f0 + f1) / 2;
            _crotch = new Vector3(0f, ch, cz);
            (_hoopScaleL, _hoopOffL) = FitThigh(w, hl, kl, hr, kr, right, scy);
            (_hoopScaleR, _hoopOffR) = FitThigh(w, hr, kr, hl, kl, right, scy);
            _driveF.Reset(); _driveR.Reset();
            Plugin.Logger.LogInfo("[SFM-ACCEPT] fit: belt " + width.ToString("0.000") + " x " + depth.ToString("0.000") + " at " + h0.ToString("0.000") + " of " + top.ToString("0.000") + ", crotch " + _crotch + ", scale " + _scale + ", hoops " + _hoopScaleL + " " + _hoopScaleR);
        }

        private static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
            return (p - (a + ab * t)).magnitude;
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

        // --- every frame ---------------------------------------------------------------------------------------------------------

        public void Place(Animator anim)
        {
            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
            Vector3 hl = B(HumanBodyBones.LeftUpperLeg).position, hr = B(HumanBodyBones.RightUpperLeg).position, mid = (hl + hr) * 0.5f;
            Vector3 kl = B(HumanBodyBones.LeftLowerLeg).position, kr = B(HumanBodyBones.RightLowerLeg).position;
            // measured twice just after it goes on (bones settle after a load), then again only when her size changes
            if (Time.time >= _nextFit && (_fits < 2 || Mathf.Abs((kl - hl).magnitude - _thighLength) > 0.02f * _thighLength)) { Fit(anim); _fits++; _nextFit = Time.time + 2f; }
            Vector3 right = (hr - hl).normalized, up = Vector3.ProjectOnPlane(B(HumanBodyBones.Spine).position - mid, right).normalized;
            Vector3 fwd = Vector3.Cross(right, up);
            Quaternion rot = Quaternion.LookRotation(fwd, up);
            Vector3 S = _scale * Plugin.ScaleTrim.Value;
            Vector3 waist = mid + right * _waist.x + up * _waist.y + fwd * _waist.z;
            _belt.SetPositionAndRotation(waist - rot * new Vector3(0, BeltY * S.y, 0), rot);
            _belt.localScale = S;
            float ls = _lowerScale * Plugin.ScaleTrim.Value;
            Vector3 crotch = mid + up * _crotch.y + fwd * _crotch.z;
            _lower.SetPositionAndRotation(crotch - rot * (Crotch * ls), rot);
            _lower.localScale = Vector3.one * ls;
            PlaceThigh(_thighL, hl, kl, _hipL, _hoopL, rot, right, _hoopScaleL, _hoopOffL);
            PlaceThigh(_thighR, hr, kr, _hipR, _hoopR, rot, right, _hoopScaleR, _hoopOffR);

            // Beta's anchor_L and eyelet_L hang on her right thigh, anchor_R and eyelet_R on her left
            Vector3 barAL = Bar("anchor_L", _thighR, _hoopR), barAR = Bar("anchor_R", _thighL, _hoopL), barEL = Bar("eyelet_L", _thighR, _hoopR), barER = Bar("eyelet_R", _thighL, _hoopL);
            Vector3 eyeF0 = _lower.TransformPoint(Sock("SOCKET_cord_front")), eyeR0 = _lower.TransformPoint(Sock("SOCKET_cord_rear"));
            float dt = Time.deltaTime, thigh = (kl - hl).magnitude;
            // the same cords with her legs straight down, which is where they go slack
            Vector3 rAL = RestBar("anchor_L", hr, kr, _hipR, _hoopR, rot, up, right, _hoopScaleR, _hoopOffR), rAR = RestBar("anchor_R", hl, kl, _hipL, _hoopL, rot, up, right, _hoopScaleL, _hoopOffL);
            Vector3 rEL = RestBar("eyelet_L", hr, kr, _hipR, _hoopR, rot, up, right, _hoopScaleR, _hoopOffR), rER = RestBar("eyelet_R", hl, kl, _hipL, _hoopL, rot, up, right, _hoopScaleL, _hoopOffL);
            float cordF = (barAL - barER).magnitude + (barER - eyeF0).magnitude, cordR = (barAR - barEL).magnitude + (barEL - eyeR0).magnitude;
            float restF = (rAL - rER).magnitude + (rER - eyeF0).magnitude, restR = (rAR - rEL).magnitude + (rEL - eyeR0).magnitude;
            _driveF.Update(cordF, restF, thigh, dt);
            _driveR.Update(cordR, restR, thigh, dt);
            _pistonF.localPosition = -_axisF * (_driveF.Stroke * StrokeMetres);
            _pistonR.localPosition = -_axisR * (_driveR.Stroke * StrokeMetres);
            Vector3 eyeF = _pistonF.TransformPoint(Sock("SOCKET_cord_front")), eyeR = _pistonR.TransformPoint(Sock("SOCKET_cord_rear"));

            Vector3 seatAL = Swing("anchor_L", "SOCKET_anchor_L", barAL, (barER - barAL).normalized, _thighR, ls);
            Vector3 seatAR = Swing("anchor_R", "SOCKET_anchor_R", barAR, (barEL - barAR).normalized, _thighL, ls);
            Vector3 seatEL = Swing("eyelet_L", "SOCKET_eyelet_L", barEL, ((barAR - barEL).normalized + (eyeR - barEL).normalized).normalized, _thighR, ls);
            Vector3 seatER = Swing("eyelet_R", "SOCKET_eyelet_R", barER, ((barAL - barER).normalized + (eyeF - barER).normalized).normalized, _thighL, ls);
            Line(_cords[0], seatAL, seatER, ls); Line(_cords[1], seatER, eyeF, ls); Line(_cords[2], seatAR, seatEL, ls); Line(_cords[3], seatEL, eyeR, ls);
            End("end_front_thigh", seatAL, seatER, "SOCKET_anchor_L", "SOCKET_eyelet_R", _thighR, ls);
            End("end_rear_thigh", seatAR, seatEL, "SOCKET_anchor_R", "SOCKET_eyelet_L", _thighL, ls);
            End("end_front_piston", eyeF, seatER, "SOCKET_cord_front", "SOCKET_eyelet_R", _lower, ls);
            End("end_rear_piston", eyeR, seatEL, "SOCKET_cord_rear", "SOCKET_eyelet_L", _lower, ls);
            foreach (var st in _stretches)
            {
                Vector3 a = st.From.TransformPoint(st.Start - st.FromOrigin), b = st.To.TransformPoint(st.End - st.ToOrigin), d = b - a;
                st.T.SetPositionAndRotation(a, Quaternion.LookRotation(d, rot * st.Hint));
                st.T.localScale = new Vector3(ls, ls, d.magnitude / st.Length);
            }
            Status = "scale " + S + " stroke " + _driveF.Stroke.ToString("0.00") + " " + _driveR.Stroke.ToString("0.00") + " cord " + cordF.ToString("0.000") + " of " + restF.ToString("0.000") + ", " + cordR.ToString("0.000") + " of " + restR.ToString("0.000");
        }

        private Vector3 RestBar(string key, Vector3 hip, Vector3 knee, Vector3 modelHip, Vector3 modelHoop, Quaternion rot, Vector3 up, Vector3 right, Vector3 scale, Vector3 off)
        {
            float len = (knee - hip).magnitude;
            Quaternion q = Quaternion.FromToRotation(rot * (modelHoop - modelHip).normalized, -up) * rot;
            Vector3 a = Vector3.ProjectOnPlane(right, up).normalized, b = Vector3.Cross(a, -up);
            Vector3 centre = hip - up * (len * Plugin.HoopAlongThigh.Value) + a * off.x + b * off.z;
            return centre + q * Vector3.Scale(scale, U(_file.Groups.Find(x => x.Name == "ring_" + key).Pivot) - modelHoop);
        }

        private Vector3 Bar(string key, Transform node, Vector3 hoop)
        {
            HGroup g = _file.Groups.Find(x => x.Name == "ring_" + key);
            return node.TransformPoint(U(g.Pivot) - hoop);
        }

        /// <summary>Swings a ring about its bar to face its pull. Returns the point where the cord bears on it.</summary>
        private Vector3 Swing(string key, string seat, Vector3 bar, Vector3 pull, Transform node, float scale)
        {
            Transform t = _own["ring_" + key];
            HGroup g = _file.Groups.Find(x => x.Name == "ring_" + key);
            Vector3 d0 = Sock(seat) - U(g.Pivot);
            Quaternion q = Quaternion.FromToRotation(node.rotation * d0.normalized, pull) * node.rotation;
            t.SetPositionAndRotation(bar, q);
            t.localScale = Vector3.one * scale;
            return bar + pull * (d0.magnitude * scale);
        }

        private void End(string group, Vector3 from, Vector3 to, string seatA, string seatB, Transform node, float scale)
        {
            Transform t = _own[group];
            Vector3 d0 = (Sock(seatB) - Sock(seatA)).normalized;
            t.SetPositionAndRotation(from, Quaternion.FromToRotation(node.rotation * d0, (to - from).normalized) * node.rotation);
            t.localScale = Vector3.one * scale;
        }

        private void PlaceThigh(Transform node, Vector3 hip, Vector3 knee, Vector3 modelHip, Vector3 modelHoop, Quaternion rot, Vector3 right, Vector3 scale, Vector3 off)
        {
            Vector3 axis = knee - hip, da = axis.normalized, dm = rot * (modelHoop - modelHip).normalized;
            Quaternion q = Quaternion.FromToRotation(dm, da) * rot;
            Vector3 a = Vector3.ProjectOnPlane(right, da).normalized, b = Vector3.Cross(a, da);
            node.SetPositionAndRotation(hip + axis * Plugin.HoopAlongThigh.Value + a * off.x + b * off.z, q);
            node.localScale = scale;
        }

        private static void Line(Transform t, Vector3 a, Vector3 b, float s)
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
            _owned.Clear(); _own.Clear(); _stretches.Clear();
            _visible = false;
        }
    }
}
