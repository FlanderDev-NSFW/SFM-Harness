using System;
using System.Collections.Generic;
using Harness.Logic;
using Il2CppInterop.Runtime;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace Harness
{
    /// <summary>
    /// Builds the model as rigid meshes and places them on her each frame. The belt follows her outline at the waist, the guides and pistons line up with
    /// the game's own anchor bones in her pelvis (chains hang from the guides), the hoops sit on her thighs. Straps, springs and wire stretch between those; rings swing to
    /// their pull. Sizes come from a bake of her body, positions from the bones (a bake only roughly matches them).
    /// </summary>
    internal sealed class HarnessRig
    {
        private const string Tag = "Harness.";
        // Beta's sizes: the belt's opening, the hoop's opening, the belt's height, and the point of the model that goes at her crotch.
        private const float BeltInX = 0.136f, BeltInZ = 0.118f, BeltY = 0.219f, HoopInX = 0.0805f, HoopInZ = 0.0765f, Room = 1.04f, WaistShare = 0.45f, StrokeMetres = 0.09f, PistonTip = 0.237f;

        /// <summary>A hoop shaped to her thigh: the model's hoop in a frame with Y along the thigh, moved onto the thigh's outline.</summary>
        private sealed class Hoop
        {
            public Quaternion ToFrame; public BeltShape Shape; public float Tall = 1f; public Mesh Mesh; public Vector3[] Rest;
            public Vector3 Local(Vector3 v)
            {
                if (Shape == null) return v;
                Vector3 f = ToFrame * v;
                var w = Shape.Warp(new NVector3(f.x, f.y, f.z), HoopInX, HoopInZ, 0f, Tall, Tall);
                return Quaternion.Inverse(ToFrame) * new Vector3(w.X, w.Y, w.Z);
            }
        }

        private readonly Hoop _hoopFitL = new Hoop(), _hoopFitR = new Hoop();

        private sealed class Stretch { public Transform T; public Vector3 Start, End, Hint; public float Length; public Transform From, To; public Vector3 FromOrigin, ToOrigin; }

        private readonly HarnessFile _file;
        private GameObject _top;
        private Transform _belt, _guideF, _guideR, _thighL, _thighR, _pistonF, _pistonR, _anchorF, _anchorR, _axisObjF;
        private Mesh _beltMesh; private Vector3[] _beltRest; private BeltShape _shape; private float _beltTall = 1f, _beltThick = 1f;
        private Vector3 _cF, _cR; private float _guideGap; private bool _needSurvey = true;
        private readonly Dictionary<string, Transform> _own = new Dictionary<string, Transform>();
        private readonly List<Stretch> _stretches = new List<Stretch>();
        private readonly Transform[] _cords = new Transform[4];
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private readonly PistonDrive _driveF = new PistonDrive(), _driveR = new PistonDrive();
        private Vector3 _hipL, _hipR, _hoopL, _hoopR, _axisF, _axisR;
        private Vector3 _waist;
        private float _lowerScale = 1f, _nextFit, _thighLength;
        private int _fits;
        private bool _visible;
        public string Status = "";
        /// <summary>Diagnostics: 0 to 1 holds both pistons at that travel; below 0 is normal. Never saved.</summary>
        public static float ForcedStroke = -1f;

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
            _cF = Sock("SOCKET_guide_front"); _cR = Sock("SOCKET_guide_rear"); _guideGap = (_cF - _cR).magnitude;
            _belt = Node("Belt", _top.transform); _guideF = Node("GuideFront", _top.transform); _guideR = Node("GuideRear", _top.transform); _thighL = Node("ThighL", _top.transform); _thighR = Node("ThighR", _top.transform);
            _pistonF = Node("PistonFront", _guideF); _pistonR = Node("PistonRear", _guideR);
            var mats = new Material[_file.Materials.Count];
            Material source = FindToon();
            for (int i = 0; i < mats.Length; i++) mats[i] = MakeMaterial(source, _file.Materials[i]);
            var ends = StretchEnds();
            foreach (var g in _file.Groups)
            {
                string node = GroupMap.NodeOf(g.Name);
                if (node != GroupMap.Own)
                {
                    Transform parent = node switch { GroupMap.LeftThigh => _thighL, GroupMap.RightThigh => _thighR, GroupMap.PistonFront => _pistonF, GroupMap.PistonRear => _pistonR, GroupMap.Belt => _belt, GroupMap.GuideFront => _guideF, _ => _guideR };
                    Vector3 origin = node == GroupMap.LeftThigh ? _hoopL : node == GroupMap.RightThigh ? _hoopR : node == GroupMap.GuideFront || node == GroupMap.PistonFront ? _cF : node == GroupMap.GuideRear || node == GroupMap.PistonRear ? _cR : Vector3.zero;
                    var t = AddGroup(g, parent, origin, Quaternion.identity, mats, layer);
                    if (node == GroupMap.Belt) { _beltMesh = t.GetComponent<MeshFilter>().sharedMesh; _beltRest = _beltMesh.vertices; }
                    if (node == GroupMap.LeftThigh) { _hoopFitL.Mesh = t.GetComponent<MeshFilter>().sharedMesh; _hoopFitL.Rest = _hoopFitL.Mesh.vertices; _hoopFitL.ToFrame = Quaternion.FromToRotation((_hipL - _hoopL).normalized, Vector3.up); }
                    if (node == GroupMap.RightThigh) { _hoopFitR.Mesh = t.GetComponent<MeshFilter>().sharedMesh; _hoopFitR.Rest = _hoopFitR.Mesh.vertices; _hoopFitR.ToFrame = Quaternion.FromToRotation((_hipR - _hoopR).normalized, Vector3.up); }
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
            _fits = 0; _nextFit = 0f; _needSurvey = true;
            SetVisible(false);
        }

        private Dictionary<string, Stretch> StretchEnds()
        {
            var d = new Dictionary<string, Stretch>();
            Stretch S(string a, Transform from, Vector3 fromOrigin, string b, Transform to, Vector3 toOrigin) =>
                new Stretch { Start = Sock(a), End = Sock(b), Hint = Vector3.forward, From = from, To = to, FromOrigin = fromOrigin, ToOrigin = toOrigin };
            d["vstrap_R"] = S("SOCKET_vstrap_top_R", _belt, Vector3.zero, "SOCKET_vstrap_bottom_R", _guideF, _cF);
            d["vstrap_L"] = S("SOCKET_vstrap_top_L", _belt, Vector3.zero, "SOCKET_vstrap_bottom_L", _guideF, _cF);
            d["rear_strap"] = S("SOCKET_rear_strap_top", _belt, Vector3.zero, "SOCKET_rear_strap_bottom", _guideR, _cR);
            d["wire_up"] = S("WIRE_tip", _guideF, _cF, "WIRE_mid", _belt, Vector3.zero);
            d["wire_down"] = S("WIRE_mid", _belt, Vector3.zero, "WIRE_end", _thighL, _hoopL);
            foreach (string tag in new[] { "front", "rear" })
                foreach (string side in new[] { "R", "L" })
                {
                    string name = "spring_" + tag + "_" + side;
                    var g = _file.Groups.Find(x => x.Name == name);
                    Vector3 c = tag == "front" ? _cF : _cR;
                    d[name] = new Stretch { Start = U(g.Pivot), End = Sock("SPRING_top_" + tag + "_" + side), Hint = Vector3.forward, From = tag == "front" ? _guideF : _guideR, To = tag == "front" ? _pistonF : _pistonR, FromOrigin = c, ToOrigin = c };
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

        private static void Aim(Transform a, Transform b, Vector3 target)
        {
            Vector3 from = b.position - a.position, to = target - a.position;
            if (from.sqrMagnitude > 1e-8f && to.sqrMagnitude > 1e-8f) a.rotation = Quaternion.FromToRotation(from, to) * a.rotation;
        }

        /// <summary>LateUpdate: for one moment her arms straight out to the sides, bake, put them back, and measure from that bake.</summary>
        public void Survey(Animator anim)
        {
            if (!_needSurvey) return;
            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
            Vector3 hl = B(HumanBodyBones.LeftUpperLeg).position, hr = B(HumanBodyBones.RightUpperLeg).position, mid = (hl + hr) * 0.5f, chest = B(HumanBodyBones.Chest).position;
            Vector3 right = (hr - hl).normalized;
            var moved = new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm };
            var saved = new Quaternion[moved.Length];
            for (int k = 0; k < moved.Length; k++) saved[k] = B(moved[k]).localRotation;
            var smr = HarnessController.FindBody(anim);
            if (smr == null) return;
            Vector3[] w;
            try
            {
                Aim(B(HumanBodyBones.LeftUpperArm), B(HumanBodyBones.LeftLowerArm), B(HumanBodyBones.LeftUpperArm).position - right);
                Aim(B(HumanBodyBones.LeftLowerArm), B(HumanBodyBones.LeftHand), B(HumanBodyBones.LeftLowerArm).position - right);
                Aim(B(HumanBodyBones.RightUpperArm), B(HumanBodyBones.RightLowerArm), B(HumanBodyBones.RightUpperArm).position + right);
                Aim(B(HumanBodyBones.RightLowerArm), B(HumanBodyBones.RightHand), B(HumanBodyBones.RightLowerArm).position + right);
                var mesh = new Mesh();
                smr.BakeMesh(mesh, true);
                Transform t = smr.transform;
                Vector3[] raw = mesh.vertices;
                w = new Vector3[raw.Length];
                for (int i = 0; i < raw.Length; i++) w[i] = t.position + t.rotation * Vector3.Scale(t.lossyScale, raw[i]);
                UnityEngine.Object.Destroy(mesh);
            }
            finally { for (int k = 0; k < moved.Length; k++) B(moved[k]).localRotation = saved[k]; }
            Vector3 up = Vector3.ProjectOnPlane(chest - mid, right).normalized, fwd = Vector3.Cross(right, up);
            Vector3 kl = B(HumanBodyBones.LeftLowerLeg).position, kr = B(HumanBodyBones.RightLowerLeg).position;
            float top = Vector3.Dot(chest - mid, up), h0 = top * WaistShare;
            var a = Slice(w, mid + up * h0, up, right, fwd);
            if (a.W < 0.02f) return;
            float scx = a.W / 2 / BeltInX, scz = a.D / 2 / BeltInZ, sy = (scx + scz) / 2, half = 0.039f * sy;
            // everything over the belt's height, near enough to the middle not to be an arm
            var pts = new List<System.Numerics.Vector2>();
            foreach (var p in w)
            {
                Vector3 q = p - mid;
                float y = Vector3.Dot(q, up) - h0, x = Vector3.Dot(q, right), z = Vector3.Dot(q, fwd);
                if (Mathf.Abs(y) <= half && Mathf.Abs(x - a.X) <= a.W * 0.75f) pts.Add(new System.Numerics.Vector2(x, z));
            }
            var shape = BeltShape.From(pts, 0.003f);
            if (shape == null) return;
            _shape = shape; _beltTall = sy; _beltThick = sy; _lowerScale = sy;
            _waist = new Vector3(0f, h0, 0f);
            var warped = new Vector3[_beltRest.Length];
            for (int i = 0; i < warped.Length; i++) warped[i] = U(_shape.Warp(new NVector3(_beltRest[i].x, _beltRest[i].y, _beltRest[i].z), BeltInX, BeltInZ, BeltY, _beltTall, _beltThick));
            _beltMesh.vertices = warped;
            _beltMesh.RecalculateNormals();
            _beltMesh.RecalculateBounds();
            _thighLength = (kl - hl).magnitude;
            Quaternion rotS = Quaternion.LookRotation(fwd, up);
            FitThigh(_hoopFitL, w, hl, kl, hr, kr, _hipL, _hoopL, rotS, sy);
            FitThigh(_hoopFitR, w, hr, kr, hl, kl, _hipR, _hoopR, rotS, sy);
            _driveF.Reset(); _driveR.Reset();
            _needSurvey = false; _fits++;
            float r0 = _shape.At(0f), r90 = _shape.At(Mathf.PI / 2), r180 = _shape.At(Mathf.PI), r270 = _shape.At(-Mathf.PI / 2);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] fit: waist at " + h0.ToString("0.000") + " of " + top.ToString("0.000") + ", outline front " + r0.ToString("0.000") + " right " + r90.ToString("0.000") + " back " + r180.ToString("0.000") + " left " + r270.ToString("0.000") + ", centre " + _shape.CentreX.ToString("0.000") + "," + _shape.CentreZ.ToString("0.000") + ", size " + sy.ToString("0.00") + ", thigh radii " + _hoopFitL.Shape?.At(0f).ToString("0.000") + " " + _hoopFitL.Shape?.At(Mathf.PI / 2).ToString("0.000") + " / " + _hoopFitR.Shape?.At(0f).ToString("0.000") + " " + _hoopFitR.Shape?.At(Mathf.PI / 2).ToString("0.000"));
        }

        private static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
            return (p - (a + ab * t)).magnitude;
        }

        private void FitThigh(Hoop h, Vector3[] w, Vector3 hip, Vector3 knee, Vector3 otherHip, Vector3 otherKnee, Vector3 modelHip, Vector3 modelHoop, Quaternion rot, float sy)
        {
            float f = Plugin.HoopAlongThigh.Value, len = (knee - hip).magnitude, half = 0.024f * sy + 0.004f;
            Vector3 at = hip + (knee - hip) * f, oat = otherHip + (otherKnee - otherHip) * f;
            Quaternion q = ThighTurn(hip, knee, modelHip, modelHoop, rot), inv = Quaternion.Inverse(q);
            var pts = new List<System.Numerics.Vector2>();
            foreach (var p in w)
            {
                if ((p - at).sqrMagnitude > (p - oat).sqrMagnitude || (p - at).sqrMagnitude > len * len * 0.36f) continue;   // the other leg's skin, or far away
                Vector3 l = h.ToFrame * (inv * (p - at));
                if (Mathf.Abs(l.y) <= half) pts.Add(new System.Numerics.Vector2(l.x, l.z));
            }
            var shape = BeltShape.From(pts, 0.002f);
            if (shape == null) return;
            h.Shape = shape; h.Tall = sy;
            var v = new Vector3[h.Rest.Length];
            for (int i = 0; i < v.Length; i++) v[i] = h.Local(h.Rest[i]);
            h.Mesh.vertices = v;
            h.Mesh.RecalculateNormals();
            h.Mesh.RecalculateBounds();
        }

        private static Quaternion ThighTurn(Vector3 hip, Vector3 knee, Vector3 modelHip, Vector3 modelHoop, Quaternion rot) =>
            Quaternion.FromToRotation(rot * (modelHoop - modelHip).normalized, (knee - hip).normalized) * rot;

        // --- every frame ---------------------------------------------------------------------------------------------------------

        public void Place(Animator anim)
        {
            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
            Vector3 hl = B(HumanBodyBones.LeftUpperLeg).position, hr = B(HumanBodyBones.RightUpperLeg).position, mid = (hl + hr) * 0.5f;
            Vector3 kl = B(HumanBodyBones.LeftLowerLeg).position, kr = B(HumanBodyBones.RightLowerLeg).position;
            // measured twice just after it goes on (bones settle after a load), then again only when her size changes
            if (Time.time >= _nextFit && (_fits < 2 || Mathf.Abs((kl - hl).magnitude - _thighLength) > 0.02f * _thighLength)) { _needSurvey = true; _nextFit = Time.time + 2f; }
            if (_shape == null) return;
            Vector3 right = (hr - hl).normalized, up = Vector3.ProjectOnPlane(B(HumanBodyBones.Spine).position - mid, right).normalized;
            Vector3 fwd = Vector3.Cross(right, up);
            Quaternion rot = Quaternion.LookRotation(fwd, up);
            _belt.SetPositionAndRotation(mid + rot * new Vector3(0f, _waist.y, 0f), rot);
            float ls = _lowerScale * Plugin.ScaleTrim.Value;
            PlaceGuides(anim, rot, ls);
            PlaceThigh(_thighL, hl, kl, _hipL, _hoopL, rot);
            PlaceThigh(_thighR, hr, kr, _hipR, _hoopR, rot);

            // Beta's anchor_L and eyelet_L hang on her right thigh, anchor_R and eyelet_R on her left
            Vector3 barAL = Bar("anchor_L", _thighR, _hoopFitR, _hoopR), barAR = Bar("anchor_R", _thighL, _hoopFitL, _hoopL), barEL = Bar("eyelet_L", _thighR, _hoopFitR, _hoopR), barER = Bar("eyelet_R", _thighL, _hoopFitL, _hoopL);
            Vector3 eyeF0 = _guideF.TransformPoint(Sock("SOCKET_cord_front") - _cF), eyeR0 = _guideR.TransformPoint(Sock("SOCKET_cord_rear") - _cR);
            float dt = Time.deltaTime, thigh = (kl - hl).magnitude;
            // the same cords with her legs straight down, which is where they go slack
            Vector3 rAL = RestBar("anchor_L", hr, kr, _hipR, _hoopR, _hoopFitR, rot, up), rAR = RestBar("anchor_R", hl, kl, _hipL, _hoopL, _hoopFitL, rot, up);
            Vector3 rEL = RestBar("eyelet_L", hr, kr, _hipR, _hoopR, _hoopFitR, rot, up), rER = RestBar("eyelet_R", hl, kl, _hipL, _hoopL, _hoopFitL, rot, up);
            float cordF = (barAL - barER).magnitude + (barER - eyeF0).magnitude, cordR = (barAR - barEL).magnitude + (barEL - eyeR0).magnitude;
            float restF = (rAL - rER).magnitude + (rER - eyeF0).magnitude, restR = (rAR - rEL).magnitude + (rEL - eyeR0).magnitude;
            _driveF.Update(cordF, restF, thigh, dt);
            _driveR.Update(cordR, restR, thigh, dt);
            float force = ForcedStroke;
            _pistonF.localPosition = -_axisF * ((force >= 0f ? force : _driveF.Stroke) * StrokeMetres);
            _pistonR.localPosition = -_axisR * ((force >= 0f ? force : _driveR.Stroke) * StrokeMetres);
            Vector3 eyeF = _pistonF.TransformPoint(Sock("SOCKET_cord_front") - _cF), eyeR = _pistonR.TransformPoint(Sock("SOCKET_cord_rear") - _cR);

            Vector3 seatAL = Swing("anchor_L", "SOCKET_anchor_L", barAL, (barER - barAL).normalized, _thighR, ls);
            Vector3 seatAR = Swing("anchor_R", "SOCKET_anchor_R", barAR, (barEL - barAR).normalized, _thighL, ls);
            Vector3 seatEL = Swing("eyelet_L", "SOCKET_eyelet_L", barEL, ((barAR - barEL).normalized + (eyeR - barEL).normalized).normalized, _thighR, ls);
            Vector3 seatER = Swing("eyelet_R", "SOCKET_eyelet_R", barER, ((barAL - barER).normalized + (eyeF - barER).normalized).normalized, _thighL, ls);
            Line(_cords[0], seatAL, seatER, ls); Line(_cords[1], seatER, eyeF, ls); Line(_cords[2], seatAR, seatEL, ls); Line(_cords[3], seatEL, eyeR, ls);
            End("end_front_thigh", seatAL, seatER, "SOCKET_anchor_L", "SOCKET_eyelet_R", _thighR, ls);
            End("end_rear_thigh", seatAR, seatEL, "SOCKET_anchor_R", "SOCKET_eyelet_L", _thighL, ls);
            End("end_front_piston", eyeF, seatER, "SOCKET_cord_front", "SOCKET_eyelet_R", _guideF, ls);
            End("end_rear_piston", eyeR, seatEL, "SOCKET_cord_rear", "SOCKET_eyelet_L", _guideR, ls);
            foreach (var st in _stretches)
            {
                Vector3 a = Point(st.From, st.Start, st.FromOrigin), b = Point(st.To, st.End, st.ToOrigin), d = b - a;
                st.T.SetPositionAndRotation(a, Quaternion.LookRotation(d, rot * st.Hint));
                st.T.localScale = new Vector3(ls, ls, d.magnitude / st.Length);
            }
            Status = "size " + ls.ToString("0.00") + " stroke " + _driveF.Stroke.ToString("0.00") + " " + _driveR.Stroke.ToString("0.00") + " cord " + cordF.ToString("0.000") + " of " + restF.ToString("0.000") + ", " + cordR.ToString("0.000") + " of " + restR.ToString("0.000");
        }

        private Vector3 Point(Transform node, Vector3 model, Vector3 origin) =>
            node == _belt ? _belt.TransformPoint(U(_shape.Warp(new NVector3(model.x, model.y, model.z), BeltInX, BeltInZ, BeltY, _beltTall, _beltThick)))
            : node == _thighL ? node.TransformPoint(_hoopFitL.Local(model - origin)) : node == _thighR ? node.TransformPoint(_hoopFitR.Local(model - origin)) : node.TransformPoint(model - origin);

        /// <summary>
        /// The pistons line up with the game's own anchors in her pelvis, with each guide band just below its anchor, as in the worn image. The front
        /// guide takes the game's direction. The rear guide hangs from it by the hinge, at the model's spacing, aimed at the rear anchor.
        /// </summary>
        private void PlaceGuides(Animator anim, Quaternion rot, float ls)
        {
            if (_anchorF == null)
            {
                Transform m = anim.transform.Find("Armature/Hips/PlayerBoneScaleAdjuster/Manko"), n = anim.transform.Find("Armature/Hips/PlayerBoneScaleAdjuster/Anal");
                if (m == null || n == null) { Plugin.Logger.LogWarning("anchor bones not found"); return; }
                _anchorF = m.Find("PussyDildoTarget"); _anchorR = n.Find("RotateTarget/AnalDildoTarget"); _axisObjF = m.Find("RotateTarget/piston_machine_dildo (1)");
            }
            Vector3 tf = _anchorF.position, tr = _anchorR.position, uF = -_axisObjF.forward;
            float reach = Plugin.GuideDrop.Value * ls;
            Vector3 cF = tf - uF * reach, want = tr - uF * reach;
            Vector3 cR = cF + (want - cF).normalized * (_guideGap * ls), uR = (tr - cR).normalized;
            _guideF.SetPositionAndRotation(cF, Quaternion.FromToRotation(rot * _axisF, uF) * rot);
            _guideR.SetPositionAndRotation(cR, Quaternion.FromToRotation(rot * _axisR, uR) * rot);
            _guideF.localScale = _guideR.localScale = Vector3.one * ls;
        }

        private Vector3 RestBar(string key, Vector3 hip, Vector3 knee, Vector3 modelHip, Vector3 modelHoop, Hoop h, Quaternion rot, Vector3 up)
        {
            float len = (knee - hip).magnitude;
            Quaternion q = Quaternion.FromToRotation(rot * (modelHoop - modelHip).normalized, -up) * rot;
            return hip - up * (len * Plugin.HoopAlongThigh.Value) + q * h.Local(U(_file.Groups.Find(x => x.Name == "ring_" + key).Pivot) - modelHoop);
        }

        private Vector3 Bar(string key, Transform node, Hoop h, Vector3 hoop)
        {
            HGroup g = _file.Groups.Find(x => x.Name == "ring_" + key);
            return node.TransformPoint(h.Local(U(g.Pivot) - hoop));
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

        private void PlaceThigh(Transform node, Vector3 hip, Vector3 knee, Vector3 modelHip, Vector3 modelHoop, Quaternion rot)
        {
            node.SetPositionAndRotation(hip + (knee - hip) * Plugin.HoopAlongThigh.Value, ThighTurn(hip, knee, modelHip, modelHoop, rot));
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
            _owned.Clear(); _own.Clear(); _stretches.Clear(); _anchorF = null; _shape = null; _hoopFitL.Shape = null; _hoopFitR.Shape = null;
            _visible = false;
        }
    }
}
