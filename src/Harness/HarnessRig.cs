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
    internal sealed partial class HarnessRig
    {
        private const string Tag = "Harness.";
        // Beta's sizes: the belt's opening, the hoop's opening, the belt's height, and the point of the model that goes at her crotch.
        private const float BeltInX = 0.136f, BeltInZ = 0.118f, BeltY = 0.219f, HoopInX = 0.0805f, HoopInZ = 0.0765f, Room = 1.04f, WaistShare = 0.45f, StrokeMetres = 0.09f, PistonTip = 0.237f;

        /// <summary>A thigh strap, fitted the way the belt is: a band shaped from her skin at three heights, checked against its real mesh, loosening as she moves.</summary>
        private sealed class Hoop
        {
            public Quaternion ToFrame; public System.Numerics.Quaternion ToFrameN, FromFrameN; public BeltBand Band; public float Tall = 1f, Half; public Mesh Mesh; public Vector3[] Rest; public float[] Inner, Lifted; public float[][] Bins;
            public int[] Tris, Near; public Transform Node, Bone, Knee; public Vector3 LP; public Quaternion LQ; public Hoop Other;
            public readonly List<Seat> Fixed = new List<Seat>();
            public Vector3 Local(Vector3 v)
            {
                if (Band == null) return v;
                var w = Band.Warp(NVector3.Transform(new NVector3(v.x, v.y, v.z), ToFrameN), 0f, HoopHalf, Tall, Tall);
                return V(NVector3.Transform(w, FromFrameN));
            }
        }

        /// <summary>A rigid part on a strap, such as the pouch or the buckle: it rides the strap without bending.</summary>
        private sealed class Seat { public Transform T; public Vector3 P; }

        private const float HoopHalf = 0.024f;
        private Transform _dev;

        private readonly Hoop _hoopFitL = new Hoop(), _hoopFitR = new Hoop();

        private sealed class Stretch { public Transform T; public Vector3 Start, End, Hint; public float Length; public string Name; public Mesh Mesh; public Vector3[] Rest; public float[] Bow; public Vector3 Out;
            public float BowAt(float t) { if (Bow == null) return 0f; float f = Mathf.Clamp01(t) * (Bow.Length - 1); int k = Mathf.Min((int)f, Bow.Length - 2); return Mathf.Lerp(Bow[k], Bow[k + 1], f - k); } public Transform From, To; public Vector3 FromOrigin, ToOrigin; }

        private readonly HarnessFile _file;
        private GameObject _top;
        private Transform _belt, _guideF, _guideR, _thighL, _thighR, _pistonF, _pistonR, _anchorF, _anchorR, _axisObjF, _axisObjR;
        private readonly Transform[] _linkF = new Transform[4], _linkR = new Transform[3];
        private Vector3[] _pathF, _pathR;
        private Transform _ringR, _ringL; private float _ringLiftR, _ringLiftL;
        private Mesh _beltMesh; private Vector3[] _beltRest; private BeltBand _band; private float _beltTall = 1f, _beltThick = 1f; private float[] _modelInner;
        private bool[] _arm; private Vector3 _lh, _ls; private Quaternion _qh, _qs; private GameObject _markers; private Material _markerMat;
        public void Refit_Request() { _needMeasure = true; }
        public float CordLogUntil; private readonly System.Text.StringBuilder _cordLog = new System.Text.StringBuilder();
        public bool WantGap; public static bool ShowMarkers; public static bool Probe; public float WatchUntil; private float _nextWatch;
        public void Hide(bool off) { if (_top != null) foreach (var r in _top.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = !off; }
        private const float BeltHalf = 0.039f, Clear = 0.002f, MaxLift = 0.012f;
        private float _beltHalf, _nextSettle; private float[] _lifted; private float[][] _beltBins; private int[] _waistTris, _beltTris; private int _settled; private int _wantBow; private float _reachF = -9f, _reachR = -9f; public float SettleMs, LookMs; private int _looks;
        private Vector3 _cF, _cR; private bool _needMeasure = true;
        private readonly Dictionary<string, Transform> _own = new Dictionary<string, Transform>();
        private readonly List<Stretch> _stretches = new List<Stretch>();
        private readonly Transform[] _cords = new Transform[4];
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private readonly PistonDrive _driveF = new PistonDrive(), _driveR = new PistonDrive();
        // Zeta: each piston follows its own cord to her right thigh. Each cord's length is set so that it starts to pull as her right leg passes the middle
        // of its swing, and the piston is fully out at the end of that swing. The game's walk swings her thighs from about 42 degrees forward to 17 back,
        // round a middle 14 degrees forward. Where that puts each ring differs from body to body, so the lengths are worked out on her (ZetaFit); these are
        // the large body's, measured through a walk, and stand in until then.
        public const float ZetaFrontSlack = -0.08f, ZetaFrontFull = 0.12f, ZetaRearSlack = -0.03f, ZetaRearFull = 0.16f;
        private const float WalkMiddle = -14f, WalkBack = 17f, WalkForward = -42f;   // thigh swing, degrees, forward negative
        private bool _zetaManual, _zetaLearned; private int _zetaFitAt = -1; private float _zetaLogged = float.NaN;
        // ...and then from her own stride: over each few seconds of walking, how far each cord's pull really ranges. A cord starts to pull 23% of the way
        // up that range and its piston is fully out at 78%, which is what suited the large body.
        private const float LearnWindow = 3f, LearnStart = 0.23f, LearnFull = 0.78f, LearnLeast = 0.10f;
        private float _learnEnd, _loF, _hiF, _loR, _hiR; private bool _learnOpen;
        public static bool WrapOn = true;
        private readonly PistonDrive _zetaF = new PistonDrive(ZetaFrontSlack, ZetaFrontFull), _zetaR = new PistonDrive(ZetaRearSlack, ZetaRearFull);
        private readonly List<GameObject> _gammaOnly = new List<GameObject>(), _zetaOnly = new List<GameObject>(); private int _shownMode = -1;
        public void TuneZeta(float fs, float ff, float rs, float rf) { _zetaF.Slack = fs; _zetaF.FullAt = ff; _zetaR.Slack = rs; _zetaR.FullAt = rf; _zetaManual = true; }
        private Vector3 _hipL, _hipR, _hoopL, _hoopR, _axisF, _axisR;
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

        private Dictionary<string, HGroup> _groupsByName;
        private HGroup Group(string name)
        {
            if (_groupsByName == null) { _groupsByName = new Dictionary<string, HGroup>(); foreach (var g in _file.Groups) _groupsByName[g.Name] = g; }
            return _groupsByName[name];
        }

        // Plain C# maths. In this interop every UnityEngine.Vector3 / Quaternion operator and every Mathf call goes into the game's native code, which
        // is fine a few hundred times a frame and ruinous eighteen thousand times in a loop. Hot loops use these instead.
        private static NVector3 N(Vector3 v) => new NVector3(v.x, v.y, v.z);
        private static System.Numerics.Quaternion NQ(Quaternion q) => new System.Numerics.Quaternion(q.x, q.y, q.z, q.w);
        private static Vector3 V(NVector3 v) { Vector3 r; r.x = v.X; r.y = v.Y; r.z = v.Z; return r; }

        public void Build(int layer)
        {
            Destroy();
            _top = new GameObject("Harness");
            UnityEngine.Object.DontDestroyOnLoad(_top);
            // Beta's R side is her left
            _hipL = Sock("BODY_hip_R"); _hipR = Sock("BODY_hip_L"); _hoopL = Sock("BODY_hoop_R"); _hoopR = Sock("BODY_hoop_L");
            _axisF = (Sock("AXIS_front") - Sock("SOCKET_guide_front")).normalized; _axisR = (Sock("AXIS_rear") - Sock("SOCKET_guide_rear")).normalized;
            _cF = Sock("SOCKET_guide_front"); _cR = Sock("SOCKET_guide_rear");
            _belt = Node("Belt", _top.transform); _guideF = Node("GuideFront", _top.transform); _guideR = Node("GuideRear", _top.transform); _thighL = Node("ThighL", _top.transform); _thighR = Node("ThighR", _top.transform);
            _pistonF = Node("PistonFront", _guideF); _pistonR = Node("PistonRear", _guideR);
            for (int i = 0; i < _linkF.Length; i++) _linkF[i] = Node("LinkF" + i, _top.transform);
            for (int i = 0; i < _linkR.Length; i++) _linkR[i] = Node("LinkR" + i, _top.transform);
            var mats = new Material[_file.Materials.Count];
            Material source = FindToon();
            for (int i = 0; i < mats.Length; i++) mats[i] = MakeMaterial(source, _file.Materials[i]);
            _dev = Node("Device", _thighL); _lockRing = Node("LockRing", _top.transform); _lock = Node("Lock", _top.transform); _lockReady = false; _ringR = Node("RingR", _top.transform); _ringL = Node("RingL", _top.transform);
            var ends = StretchEnds();
            foreach (var g in _file.Groups)
            {
                if (g.Name == "lock_ring") { AddGroup(g, _lockRing, U(g.Pivot), Quaternion.identity, mats, layer); continue; }
                if (g.Name == "lock") { AddGroup(g, _lock, U(g.Pivot), Quaternion.identity, mats, layer); continue; }
                if (g.Name.StartsWith("dring_")) { AddGroup(g, g.Name.EndsWith("_R") ? _ringR : _ringL, U(g.Pivot), Quaternion.identity, mats, layer); continue; }
                if (g.Name.StartsWith("rigid_"))
                {
                    bool her_left = g.Name == "rigid_dev" || g.Name.EndsWith("_R");   // Beta's R side is her left
                    Transform seat = g.Name == "rigid_dev" ? _dev : Node(g.Name, her_left ? _thighL : _thighR);
                    if (g.Name.StartsWith("rigid_tab_")) (g.Name.StartsWith("rigid_tab_z") ? _zetaOnly : _gammaOnly).Add(seat.gameObject);
                    AddGroup(g, seat, U(g.Pivot), Quaternion.identity, mats, layer);
                    Vector3 p = U(g.Pivot) - (her_left ? _hoopL : _hoopR); seat.localPosition = p;
                    (her_left ? _hoopFitL : _hoopFitR).Fixed.Add(new Seat { T = seat, P = p });
                    continue;
                }
                if (g.Name == "wire_up") continue;   // drawn along the chain and strap each frame
                if (g.Name.StartsWith("link_"))
                {
                    int i = g.Name[6] - '0';
                    AddGroup(g, g.Name[5] == 'f' ? _linkF[i] : _linkR[i], U(g.Pivot), Quaternion.identity, mats, layer);
                    continue;
                }
                string node = GroupMap.NodeOf(g.Name);
                if (node != GroupMap.Own)
                {
                    Transform parent = node switch { GroupMap.LeftThigh => _thighL, GroupMap.RightThigh => _thighR, GroupMap.PistonFront => _pistonF, GroupMap.PistonRear => _pistonR, GroupMap.Belt => _belt, GroupMap.GuideFront => _guideF, _ => _guideR };
                    Vector3 origin = node == GroupMap.LeftThigh ? _hoopL : node == GroupMap.RightThigh ? _hoopR : node == GroupMap.GuideFront || node == GroupMap.PistonFront ? _cF : node == GroupMap.GuideRear || node == GroupMap.PistonRear ? _cR : Vector3.zero;
                    var t = AddGroup(g, parent, origin, Quaternion.identity, mats, layer);
                    if (node == GroupMap.Belt)
                    {
                        _beltMesh = t.GetComponent<MeshFilter>().sharedMesh; _beltRest = _beltMesh.vertices;
                        var nv = new NVector3[_beltRest.Length];
                        for (int i = 0; i < nv.Length; i++) nv[i] = new NVector3(_beltRest[i].x, _beltRest[i].y, _beltRest[i].z);
                        _modelInner = BeltBand.ModelInner(nv, BeltY, 0.03f);
                    }
                    if (node == GroupMap.LeftThigh) { _hoopFitL.Mesh = t.GetComponent<MeshFilter>().sharedMesh; _hoopFitL.Rest = _hoopFitL.Mesh.vertices; _hoopFitL.ToFrame = Quaternion.FromToRotation((_hipL - _hoopL).normalized, Vector3.up); }
                    if (node == GroupMap.RightThigh) { _hoopFitR.Mesh = t.GetComponent<MeshFilter>().sharedMesh; _hoopFitR.Rest = _hoopFitR.Mesh.vertices; _hoopFitR.ToFrame = Quaternion.FromToRotation((_hipR - _hoopR).normalized, Vector3.up); }
                }
                else if (ends.TryGetValue(g.Name, out var st))
                {
                    Vector3 d = st.End - st.Start;
                    st.Length = d.magnitude;
                    st.T = AddGroup(g, _top.transform, st.Start, Quaternion.Inverse(Quaternion.LookRotation(d, st.Hint)), mats, layer);
                    st.Name = g.Name; st.Mesh = st.T.GetComponent<MeshFilter>().sharedMesh; st.Rest = st.Mesh.vertices;
                    _stretches.Add(st);
                }
                else _own[g.Name] = AddGroup(g, _top.transform, U(g.Pivot), Quaternion.identity, mats, layer);
            }
            SetupHoop(_hoopFitL, _thighL, _hoopFitR); SetupHoop(_hoopFitR, _thighR, _hoopFitL);
            MakeWire(mats[_file.Materials.FindIndex(m => m.Name == "pink")], layer);
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
            MakeWrapSegments(tube, mats[cordMat], layer);
            _driveF.Reset(); _driveR.Reset(); _zetaF.Reset(); _zetaR.Reset();
            foreach (var kv in _own)
            {
                if (kv.Key.StartsWith("ring_z") || kv.Key.StartsWith("end_z")) _zetaOnly.Add(kv.Value.gameObject);
                else if (kv.Key.StartsWith("ring_anchor_") || kv.Key.StartsWith("ring_eyelet_") || kv.Key.StartsWith("end_front_") || kv.Key.StartsWith("end_rear_")) _gammaOnly.Add(kv.Value.gameObject);
            }
            _gammaOnly.Add(_cords[2].gameObject); _gammaOnly.Add(_cords[3].gameObject); _shownMode = -1;
            _fits = 0; _nextFit = 0f; _needMeasure = true; _markerMat = mats[_file.Materials.FindIndex(m => m.Name == "pink")];
            SetVisible(false);
        }

        private Dictionary<string, Stretch> StretchEnds()
        {
            var d = new Dictionary<string, Stretch>();
            Stretch S(string a, Transform from, Vector3 fromOrigin, string b, Transform to, Vector3 toOrigin) =>
                new Stretch { Start = Sock(a), End = Sock(b), Hint = Vector3.forward, From = from, To = to, FromOrigin = fromOrigin, ToOrigin = toOrigin };
            d["vstrap_R"] = S("SOCKET_vstrap_top_R", _ringR, Sock("SOCKET_chape_R"), "SOCKET_vstrap_bottom_R", _linkF[3], Sock("SOCKET_link_f3"));
            d["vstrap_L"] = S("SOCKET_vstrap_top_L", _ringL, Sock("SOCKET_chape_L"), "SOCKET_vstrap_bottom_L", _linkF[3], Sock("SOCKET_link_f3"));
            d["rear_strap"] = S("SOCKET_rear_strap_top", _belt, Vector3.zero, "SOCKET_rear_strap_bottom", _linkR[2], Sock("SOCKET_link_r2"));
            d["hinge_link"] = S("SOCKET_hinge_front", _guideF, _cF, "SOCKET_hinge_rear", _guideR, _cR); d["hinge_link"].Hint = Vector3.up;
            d["wire_down"] = S("WIRE_mid", _belt, Vector3.zero, "WIRE_end", _dev, Sock("SOCKET_remote"));
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
                t[o] = i; t[o + 1] = j; t[o + 2] = i + sides; t[o + 3] = j; t[o + 4] = j + sides; t[o + 5] = i + sides;
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

        /// <summary>
        /// LateUpdate, once: for one moment her arms straight out, bake, and note which vertices are arm. Vertex order never changes, so the note
        /// holds in every pose. Nothing is measured from this bake: posed here, it isn't the body she is drawn with.
        /// </summary>
        public void Survey(Animator anim)
        {
            if (_arm != null) return;
            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
            var moved = new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm };
            var saved = new Quaternion[moved.Length];
            for (int k = 0; k < moved.Length; k++) saved[k] = B(moved[k]).localRotation;
            var smr = HarnessController.FindBody(anim);
            if (smr == null) return;
            Vector3 hl = B(HumanBodyBones.LeftUpperLeg).position, hr = B(HumanBodyBones.RightUpperLeg).position, mid = (hl + hr) * 0.5f, right = (hr - hl).normalized;
            var still = Bake(smr, out _);
            try
            {
                Aim(B(HumanBodyBones.LeftUpperArm), B(HumanBodyBones.LeftLowerArm), B(HumanBodyBones.LeftUpperArm).position - right);
                Aim(B(HumanBodyBones.LeftLowerArm), B(HumanBodyBones.LeftHand), B(HumanBodyBones.LeftLowerArm).position - right);
                Aim(B(HumanBodyBones.RightUpperArm), B(HumanBodyBones.RightLowerArm), B(HumanBodyBones.RightUpperArm).position + right);
                Aim(B(HumanBodyBones.RightLowerArm), B(HumanBodyBones.RightHand), B(HumanBodyBones.RightLowerArm).position + right);
                var w = Bake(smr, out _);
                var arm = new bool[w.Length]; int n = 0;
                for (int i = 0; i < w.Length; i++)
                {
                    float ex = w[i].x - still[i].x, ey = w[i].y - still[i].y, ez = w[i].z - still[i].z;
                    if (ex * ex + ey * ey + ez * ez > 0.0001f) { arm[i] = true; n++; }
                }   // arm = whatever moved with the arms
                _arm = arm;
                Plugin.Logger.LogInfo("[SFM-ACCEPT] labels: " + n + " of " + w.Length + " vertices are arm");
            }
            finally { for (int k = 0; k < moved.Length; k++) B(moved[k]).localRotation = saved[k]; }
        }

        /// <summary>Her skin as drawn right now. Call in onPreCull, when her bones are final.</summary>
        private int[] _bodyTris;
        private Vector3[] Bake(SkinnedMeshRenderer smr, out int[] triangles)
        {
            _bakeMesh ??= new Mesh();
            smr.BakeMesh(_bakeMesh, true);
            _bodyTris ??= _bakeMesh.triangles;   // the body's triangles never change, so fetch them once
            triangles = _bodyTris;
            return World(smr, _bakeMesh);
        }

        private Mesh _bakeMesh;
        private Vector3[] Bake(SkinnedMeshRenderer smr) { _bakeMesh ??= new Mesh(); long t = Perf.Now; smr.BakeMesh(_bakeMesh, true); Perf.Add(Perf.BakeMesh, t); return World(smr, _bakeMesh); }

        private static Vector3[] World(SkinnedMeshRenderer smr, Mesh mesh)
        {
            Transform t = smr.transform;
            long t0 = Perf.Now;
            Vector3[] raw = mesh.vertices;
            Perf.Add(Perf.BakeFetch, t0);
            long t1 = Perf.Now;
            var w = new Vector3[raw.Length];
            NVector3 pos = N(t.position), sc = N(t.lossyScale); var q = NQ(t.rotation);
            for (int i = 0; i < raw.Length; i++)
            {
                var p = NVector3.Transform(new NVector3(raw[i].x * sc.X, raw[i].y * sc.Y, raw[i].z * sc.Z), q) + pos;
                w[i].x = p.X; w[i].y = p.Y; w[i].z = p.Z;
            }
            Perf.Add(Perf.BakeWorld, t1);
            return w;
        }

        /// <summary>Exact outline of her torso at one height: every triangle edge the plane crosses. Coordinates along right and fwd from origin.</summary>
        private List<System.Numerics.Vector2> Cut(Vector3[] w, int[] tris, Vector3 origin, Vector3 up, Vector3 right, Vector3 fwd, bool[] skip = null)
        {
            var o = new List<System.Numerics.Vector2>(256);
            float ox = origin.x, oy = origin.y, oz = origin.z, ux = up.x, uy = up.y, uz = up.z, rx = right.x, ry = right.y, rz = right.z, fx = fwd.x, fy = fwd.y, fz = fwd.z;
            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                if (skip != null && (skip[a] || skip[b] || skip[c])) continue;
                float da = (w[a].x - ox) * ux + (w[a].y - oy) * uy + (w[a].z - oz) * uz;
                float db = (w[b].x - ox) * ux + (w[b].y - oy) * uy + (w[b].z - oz) * uz;
                float dc = (w[c].x - ox) * ux + (w[c].y - oy) * uy + (w[c].z - oz) * uz;
                if ((da > 0 && db > 0 && dc > 0) || (da < 0 && db < 0 && dc < 0)) continue;
                CutEdge(o, w, a, b, da, db, ox, oy, oz, rx, ry, rz, fx, fy, fz);
                CutEdge(o, w, b, c, db, dc, ox, oy, oz, rx, ry, rz, fx, fy, fz);
                CutEdge(o, w, c, a, dc, da, ox, oy, oz, rx, ry, rz, fx, fy, fz);
            }
            return o;
        }

        private static void CutEdge(List<System.Numerics.Vector2> o, Vector3[] w, int a, int b, float da, float db, float ox, float oy, float oz, float rx, float ry, float rz, float fx, float fy, float fz)
        {
            if ((da > 0) == (db > 0)) return;
            float t = da / (da - db);
            float px = w[a].x + (w[b].x - w[a].x) * t - ox, py = w[a].y + (w[b].y - w[a].y) * t - oy, pz = w[a].z + (w[b].z - w[a].z) * t - oz;
            o.Add(new System.Numerics.Vector2(px * rx + py * ry + pz * rz, px * fx + py * fy + pz * fz));
        }

        /// <summary>onPreCull: size the belt from the skin as drawn, in the frame of the bones as drawn, the same ones it is then placed on.</summary>
        private void Measure(Animator anim)
        {
            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
            var smr = HarnessController.FindBody(anim);
            if (smr == null || _arm == null) return;
            var w = Bake(smr, out int[] tris);
            if (w.Length != _arm.Length) return;
            Vector3 hl = B(HumanBodyBones.LeftUpperLeg).position, hr = B(HumanBodyBones.RightUpperLeg).position, mid = (hl + hr) * 0.5f, chest = B(HumanBodyBones.Chest).position;
            Vector3 kl = B(HumanBodyBones.LeftLowerLeg).position, kr = B(HumanBodyBones.RightLowerLeg).position;
            Vector3 right = (hr - hl).normalized, up = Vector3.ProjectOnPlane(chest - mid, right).normalized, fwd = Vector3.Cross(right, up);
            float top = Vector3.Dot(chest - mid, up), h0 = top * Plugin.BeltHeight.Value;
            var cm = Cut(w, tris, mid + up * h0, up, right, fwd, _arm);
            if (cm.Count < 8) return;
            float x0 = 9, x1 = -9, z0 = 9, z1 = -9;
            foreach (var p in cm) { x0 = Mathf.Min(x0, p.X); x1 = Mathf.Max(x1, p.X); z0 = Mathf.Min(z0, p.Y); z1 = Mathf.Max(z1, p.Y); }
            float sx0 = 9, sx1 = -9, sz0 = 9, sz1 = -9;   // hardware size belongs to her, not to where the belt happens to sit
            foreach (var p in Cut(w, tris, mid + up * (top * WaistShare), up, right, fwd, _arm)) { sx0 = Mathf.Min(sx0, p.X); sx1 = Mathf.Max(sx1, p.X); sz0 = Mathf.Min(sz0, p.Y); sz1 = Mathf.Max(sz1, p.Y); }
            float size = ((sx1 - sx0) / 2 / BeltInX + (sz1 - sz0) / 2 / BeltInZ) / 2, sy = size * Plugin.Size.Value, half = BeltHalf * sy;
            var centre = new System.Numerics.Vector2((x0 + x1) / 2, (z0 + z1) / 2);
            var lower = Cut(w, tris, mid + up * (h0 - half), up, right, fwd, _arm); var upper = Cut(w, tris, mid + up * (h0 + half), up, right, fwd, _arm);
            var band = BeltBand.From(lower, cm, upper, centre, 0.0015f, _modelInner);
            if (band == null) return;
            _band = band; _beltTall = sy; _beltThick = sy; _lowerScale = sy;
            // the belt rides between hips and spine, like the skin under it
            Quaternion rot = Quaternion.LookRotation(fwd, up); Vector3 at = mid + up * h0 + right * 0f;
            Transform hb = B(HumanBodyBones.Hips), sb = B(HumanBodyBones.Spine);
            _lh = Quaternion.Inverse(hb.rotation) * (at - hb.position); _qh = Quaternion.Inverse(hb.rotation) * rot;
            _ls = Quaternion.Inverse(sb.rotation) * (at - sb.position); _qs = Quaternion.Inverse(sb.rotation) * rot;
            // the band is a model of the belt; the mesh is what she wears. Check the mesh against her skin and push out wherever it falls short:
            // straight mesh edges cut the corners of her outline, most of all at her sides.
            _beltHalf = half; _lifted = new float[BeltBand.Bins]; _settled = 0; _nextSettle = Time.time + 1f; _settleGap = 0.25f; _poses.Clear();
            var near = new List<int>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                if (_arm[a] || _arm[b] || _arm[c] || Math.Abs((w[a].x - at.x) * up.x + (w[a].y - at.y) * up.y + (w[a].z - at.z) * up.z) > 0.12f) continue;
                near.Add(a); near.Add(b); near.Add(c);
            }
            _waistTris = near.ToArray();
            Refit();
            for (int pass = 0; pass < 3; pass++) if (!Push(SkinNow(w, at, up, right, fwd))) break;
            _thighLength = (kl - hl).magnitude;
            for (int pass = 0; pass < 2; pass++)
            {
                FitThigh(_hoopFitL, B(HumanBodyBones.LeftUpperLeg), B(HumanBodyBones.LeftLowerLeg), w, tris, _hipL, _hoopL, rot, sy);
                FitThigh(_hoopFitR, B(HumanBodyBones.RightUpperLeg), B(HumanBodyBones.RightLowerLeg), w, tris, _hipR, _hoopR, rot, sy);
            }
            _driveF.Reset(); _driveR.Reset();
            _needMeasure = false; _fits++; _patch = null; _patchFailed = false; _zetaLearned = false; _learnOpen = false; _wantBow = 3; _ringLiftR = _ringLiftL = 0f;
            Plugin.Logger.LogInfo("[SFM-ACCEPT] fit: waist at " + h0.ToString("0.000") + " of " + top.ToString("0.000") + ", " + (x1 - x0).ToString("0.000") + " x " + (z1 - z0).ToString("0.000") + ", band height " + (half * 2).ToString("0.000") + ", size " + sy.ToString("0.00"));
        }

        /// <summary>
        /// The check that counts, and it shares nothing with the fit: the real belt mesh where it is drawn now, cut at five heights, against her skin
        /// as drawn now, cut at the same heights. Negative means skin through the belt. Only gaps are logged, never her outline.
        /// </summary>
        private void GapReport(Animator anim)
        {
            var smr = HarnessController.FindBody(anim);
            if (smr == null || _arm == null || _band == null) return;
            var w = Bake(smr, out int[] tris);
            Quaternion rot = _belt.rotation; Vector3 pos = _belt.position, up = rot * Vector3.up, right = rot * Vector3.right, fwd = rot * Vector3.forward;
            Vector3[] bl = _beltMesh.vertices; int[] bt = _beltMesh.triangles;
            for (int i = 0; i < bl.Length; i++) bl[i] = _belt.TransformPoint(bl[i]);
            float half = BeltHalf * _beltTall * 0.92f;
            var marks = new List<Vector3>();
            var sb = new System.Text.StringBuilder("[SFM-ACCEPT] gap:"); var bins = new System.Text.StringBuilder("[SFM-ACCEPT] gapbins");
            foreach (float h in new[] { -1f, -0.5f, 0f, 0.5f, 1f })
            {
                Vector3 o = pos + up * (h * half);
                float[] skin, belt;
                var cut = Cut(w, tris, o, up, right, fwd, _arm);
                skin = Bins(cut, true); belt = Bins(Cut(bl, bt, o, up, right, fwd), false);
                if (ShowMarkers) foreach (var p in cut) marks.Add(o + right * p.X + fwd * p.Y);
                float min = 9f, sum = 0f, worst = 0f; int n = 0, under = 0;
                bins.Append(" |").Append(h.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                for (int i = 0; i < BeltBand.Bins; i++)
                {
                    if (skin[i] <= 0f || belt[i] > 8f) { bins.Append(" x"); continue; }
                    float g = belt[i] - skin[i];
                    bins.Append(' ').Append(Mathf.RoundToInt(g * 10000f));
                    n++; sum += g; if (g < 0f) under++;
                    if (g < min) { min = g; worst = i * 360f / BeltBand.Bins; }
                }
                sb.Append(" h").Append(h.ToString("+0.0;-0.0")).Append(": min ").Append((min * 1000).ToString("0.0")).Append(" at ").Append(worst.ToString("0")).Append(", mean ").Append((sum / Mathf.Max(n, 1) * 1000).ToString("0.0")).Append(", through ").Append(under).Append("/").Append(n).Append(";");
            }
            Plugin.Logger.LogInfo(sb.ToString()); Plugin.Logger.LogInfo(bins.ToString());
            var hg = new System.Text.StringBuilder("[SFM-ACCEPT] hoopgap:");
            foreach (var h in new[] { _hoopFitL, _hoopFitR })
            {
                if (h.Band == null) { hg.Append(" unfitted"); continue; }
                var sk = HoopSkin(h, w, h.Node.position, h.Node.rotation * Quaternion.Inverse(h.ToFrame));
                float lo = 9f, sum = 0f; int n = 0, under = 0;
                for (int k = 0; k < sk.Length; k++)
                    for (int i = 0; i < sk[k].Length; i++)
                    {
                        if (sk[k][i] <= 0f || h.Bins[k][i] > 8f) continue;
                        float g = h.Bins[k][i] - sk[k][i]; if (g < -0.03f) continue;
                        n++; sum += g; lo = Mathf.Min(lo, g); if (g < 0f) under++;
                    }
                hg.Append(' ').Append(h == _hoopFitL ? "left" : "right").Append(" min ").Append((lo * 1000).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(" mean ").Append((sum / Mathf.Max(n, 1) * 1000).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(" through ").Append(under).Append('/').Append(n);
            }
            Plugin.Logger.LogInfo(hg.ToString());
            Plugin.Logger.LogInfo("[SFM-ACCEPT] lockcheck: " + LockCheck(w, tris, bl, bt) + " swing " + _la.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " " + _lb.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
            Plugin.Logger.LogInfo("[SFM-ACCEPT] ringcheck: " + RingCheck(_ringR, "R", w, tris, bl, bt) + " | " + RingCheck(_ringL, "L", w, tris, bl, bt));
            Plugin.Logger.LogInfo("[SFM-ACCEPT] chain: " + ChainReport(_linkF, 1f, w, tris, rot) + " | " + ChainReport(_linkR, -1f, w, tris, rot));
            Plugin.Logger.LogInfo("[SFM-ACCEPT] parts: stroke " + _driveF.Stroke.ToString("0.00") + " " + PartReport("front", _guideF, _axisF, w, tris, rot) + " " + PartReport("rear", _guideR, _axisR, w, tris, rot));
            if (_markers != null) { UnityEngine.Object.Destroy(_markers); _markers = null; }
            if (ShowMarkers && marks.Count > 0) { _markers = MarkerObject(marks); _markers.transform.SetParent(_belt, true); }   // she sways; the marks ride with the belt
        }

        private static readonly float[] Heights = { -0.92f, -0.5f, 0f, 0.5f, 0.92f };

        private float[][] SkinNow(Vector3[] w, Vector3 at, Vector3 up, Vector3 right, Vector3 fwd)
        {
            var o = new float[Heights.Length][];
            for (int k = 0; k < o.Length; k++) o[k] = Bins(Cut(w, _waistTris, at + up * (Heights[k] * _beltHalf), up, right, fwd), true);
            return o;
        }

        /// <summary>Shapes the mesh from the band, and notes where its inside really is, by angle and height, in the belt's own frame.</summary>
        private void Refit()
        {
            long trf = Perf.Now;
            RefitInner();
            Perf.Add(Perf.Refit, trf);
        }

        private void RefitInner()
        {
            long t0 = Perf.Now;
            var v = new Vector3[_beltRest.Length];
            for (int i = 0; i < v.Length; i++) v[i] = U(_band.Warp(new NVector3(_beltRest[i].x, _beltRest[i].y, _beltRest[i].z), BeltY, BeltHalf, _beltTall, _beltThick));
            Perf.Add(Perf.RefitWarp, t0);
            long t1 = Perf.Now;
            _beltMesh.vertices = v;
            _beltMesh.RecalculateNormals();
            _beltMesh.RecalculateBounds();
            _beltTris ??= _beltMesh.triangles;
            Perf.Add(Perf.RefitUpload, t1);
            long t2 = Perf.Now;
            _beltBins = new float[Heights.Length][];
            for (int k = 0; k < Heights.Length; k++) _beltBins[k] = Bins(Cut(v, _beltTris, Vector3.up * (Heights[k] * _beltHalf), Vector3.up, Vector3.right, Vector3.forward), false);
            Perf.Add(Perf.RefitCuts, t2);
        }

        /// <summary>
        /// The band is a model of the belt; the mesh is what she wears. Moves it out wherever the mesh falls short of this skin: straight mesh edges
        /// cut the corners of her outline, and her skin moves against her bones as she shifts her weight. It only ever loosens, by no more than MaxLift,
        /// so a deep bend can't balloon it. False when nothing needed doing.
        /// </summary>
        private bool Push(float[][] skin)
        {
            if (!Loosen(_band, _beltBins, skin, _lifted)) return false;
            Refit();
            return true;
        }

        private static bool Loosen(BeltBand band, float[][] mesh, float[][] skin, float[] lifted)
        {
            var need = new float[BeltBand.Bins]; float most = 0f;
            for (int k = 0; k < skin.Length; k++)
                for (int i = 0; i < need.Length; i++)
                    if (skin[k][i] > 0f && mesh[k][i] < 8f && skin[k][i] < mesh[k][i] + 0.03f) need[i] = Mathf.Max(need[i], Clear - (mesh[k][i] - skin[k][i]));
            for (int i = 0; i < need.Length; i++)
            {
                need[i] = Mathf.Clamp(need[i], 0f, MaxLift - lifted[i]);
                lifted[i] += need[i]; most = Mathf.Max(most, need[i]);
            }
            if (most < 0.0005f) return false;
            band.Lift(need);
            return true;
        }

        /// <summary>A look at her skin now and then. Often at first and while it keeps finding something, then rarely.</summary>
        private readonly HashSet<int> _poses = new HashSet<int>(); private float _settleGap = 0.25f;

        /// <summary>Her pose, coarsely: each thigh's lift and her bend, in 15 degree steps.</summary>
        private static int PoseKey(Animator anim)
        {
            Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips), spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            NVector3 hp = N(hips.position), up = NVector3.Normalize(N(spine.position) - hp);
            int Lift(HumanBodyBones a, HumanBodyBones b)
            {
                NVector3 d = NVector3.Normalize(N(anim.GetBoneTransform(b).position) - N(anim.GetBoneTransform(a).position));
                return (int)(MathF.Acos(Math.Clamp(NVector3.Dot(d, -up), -1f, 1f)) * (180f / MathF.PI) / 15f);
            }
            int bend = (int)(MathF.Acos(Math.Clamp(up.Y, -1f, 1f)) * (180f / MathF.PI) / 15f);
            return Lift(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg) * 10000 + Lift(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg) * 100 + bend;
        }

        private void Settle(Animator anim)
        {
            if (_waistTris == null) return;
            // a pose she hasn't been in since the fit: look again soon, it may press skin somewhere new
            int pose = PoseKey(anim);
            if (_poses.Add(pose) && _nextSettle > Time.time + 0.4f) _nextSettle = Time.time + 0.4f;
            if (Time.time < _nextSettle) return;
            var smr = HarnessController.FindBody(anim);
            if (smr == null) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long tbk = Perf.Now;
            var w = Bake(smr);
            Perf.Add(Perf.Bake, tbk);
            long tcu = Perf.Now;
            float tb = (float)clock.Elapsed.TotalMilliseconds;
            Quaternion rot = _belt.rotation;
            var skin = SkinNow(w, _belt.position, rot * Vector3.up, rot * Vector3.right, rot * Vector3.forward);
            float tc = (float)clock.Elapsed.TotalMilliseconds;
            Perf.Add(Perf.Cuts, tcu);
            bool moved = Push(skin);
            long thl = Perf.Now;
            foreach (var h in new[] { _hoopFitL, _hoopFitR })
                if (h.Band != null) moved |= HoopPush(h, HoopSkin(h, w, h.Node.position, h.Node.rotation * Quaternion.Inverse(h.ToFrame)));
            Perf.Add(Perf.HoopLook, thl);
            LookMs = Mathf.Max(LookMs, tc); if (_looks++ % 40 == 0) Plugin.Logger.LogInfo("[SFM-ACCEPT] look: bake " + tb.ToString("0.0") + " cut " + (tc - tb).ToString("0.0") + " slowest " + LookMs.ToString("0.0") + " ms");
            _settled = moved ? 0 : _settled + 1;
            _settleGap = moved ? 0.25f : Math.Min(_settleGap * 1.6f, 10f);
            _nextSettle = Time.time + _settleGap;
            SettleMs = Mathf.Max(SettleMs, (float)clock.Elapsed.TotalMilliseconds);
            if (moved) { float m = 0f; foreach (float l in _lifted) m = Mathf.Max(m, l); Plugin.Logger.LogInfo("[SFM-ACCEPT] settle: loosened, most " + (m * 1000).ToString("0.0") + " mm, slowest look " + SettleMs.ToString("0.0") + " ms"); }
        }

        /// <summary>
        /// A strap lies on her, it doesn't cut straight through her belly. Finds how far under the skin the straight line runs along each strap, and bows
        /// the strap's mesh out by that much. Ends stay where they are, so the hardware still meets.
        /// </summary>
        private void Bow(Animator anim)
        {
            var smr = HarnessController.FindBody(anim);
            if (smr == null) return;
            var w = Bake(smr, out int[] all);
            // everything here happens round her pelvis: cut only the skin within reach of it, not the whole body
            NVector3 hc = N(anim.GetBoneTransform(HumanBodyBones.Hips).position);
            var near = new List<int>(all.Length / 3);
            for (int i = 0; i < all.Length; i += 3)
            {
                int a = all[i], b = all[i + 1], c = all[i + 2];
                if (_arm[a] || _arm[b] || _arm[c]) continue;
                float ex = w[a].x - hc.X, ey = w[a].y - hc.Y, ez = w[a].z - hc.Z;
                if (ex * ex + ey * ey + ez * ez > 0.16f) continue;
                near.Add(a); near.Add(b); near.Add(c);
            }
            int[] tris = near.ToArray();
            Quaternion rot = _belt.rotation; Vector3 pos = _belt.position, up = rot * Vector3.up, right = rot * Vector3.right, fwd = rot * Vector3.forward;
            Vector3 mid = (anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position + anim.GetBoneTransform(HumanBodyBones.RightUpperLeg).position) * 0.5f;
            mid += right * Vector3.Dot(pos + right * _band.CentreX - mid, right);
            Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            float gs = _guideF.lossyScale.x;
            _pathF = Trace(w, tris, hips, _guideF.TransformPoint(Sock("SOCKET_link_f0") - _cF), pos, 1f, 0.03f * gs, 0.03f * gs, 0.002f + 0.006f * gs, right, up, fwd, mid);
            _pathR = Trace(w, tris, hips, _guideR.TransformPoint(Sock("SOCKET_link_r0") - _cR), pos, -1f, 0.012f * gs, 0.03f * gs, 0.002f + 0.004f * gs, right, up, fwd, mid);
            float gsn = _guideF.lossyScale.x;
            // her skin as drawn is what counts, unless her thighs are closed over the axis in this pose; then the same seat with her legs apart, which sits lower, is the limit
            float drawn = SeatOnAxis(w, _anchorF.position, -_axisObjF.forward, gsn), apart = SeatOnAxis(LegsApart(anim, smr, up, right), _anchorF.position, -_axisObjF.forward, gsn);
            _reachF = (drawn > -8f ? Mathf.Min(drawn, apart) : apart) + Plugin.FrontSeat.Value * 0.001f; _reachR = Snug(w, _anchorR.position, -_axisObjR.forward, gsn);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] snug: front " + (_reachF * 1000).ToString("0.0") + " rear " + (_reachR * 1000).ToString("0.0") + " mm out from the anchors; model units " + (_reachF / gsn).ToString("0.0000") + " " + (_reachR / gsn).ToString("0.0000"));
            _ringLiftR = Mathf.Max(0f, _ringLiftR + RingLift(w, tris, _ringR, "R", _lowerScale, pos, up, right, fwd));
            _ringLiftL = Mathf.Max(0f, _ringLiftL + RingLift(w, tris, _ringL, "L", _lowerScale, pos, up, right, fwd));
            const int n = 10;
            var log = new System.Text.StringBuilder("[SFM-ACCEPT] bow:");
            foreach (var st in _stretches)
            {
                if (st.Name != "vstrap_R" && st.Name != "vstrap_L" && st.Name != "rear_strap" && st.Name != "wire_down") continue;
                Vector3 a = st.T.position, b = a + st.T.rotation * (Vector3.forward * (st.Length * st.T.localScale.z));
                var bow = new float[n + 2]; Vector3 outward = Vector3.zero; float most = 0f;
                for (int i = 1; i <= n; i++)
                {
                    Vector3 p = a + (b - a) * (i / (n + 1f));
                    Vector3 c = pos + up * Vector3.Dot(p - pos, up) + right * _band.CentreX + fwd * _band.CentreZ;
                    float px = Vector3.Dot(p - c, right), pz = Vector3.Dot(p - c, fwd), rp = Mathf.Sqrt(px * px + pz * pz);
                    if (rp < 0.02f) continue;
                    float ux = px / rp, uz = pz / rp, rim = 0f;
                    outward += right * ux + fwd * uz;
                    foreach (var q in Cut(w, tris, p, up, right, fwd, _arm))
                    {
                        float qx = q.X + px, qz = q.Y + pz, along = qx * ux + qz * uz, aside = Mathf.Abs(qx * uz - qz * ux);
                        if (aside < 0.012f && along > 0f && along < rp + 0.06f) rim = Mathf.Max(rim, along);
                    }
                    bow[i] = rim > 0f ? Mathf.Max(0f, rim - rp + 0.003f) : 0f;
                    most = Mathf.Max(most, bow[i]);
                }
                for (int pass = 0; pass < 2; pass++)
                {
                    var o = (float[])bow.Clone();
                    for (int i = 1; i <= n; i++) o[i] = Mathf.Max(bow[i], (bow[i - 1] + bow[i + 1]) / 2);
                    bow = o;
                }
                Vector3 d = (b - a).normalized, lo = Quaternion.Inverse(st.T.rotation) * Vector3.ProjectOnPlane(outward, d).normalized, sc = st.T.localScale;
                var v = new Vector3[st.Rest.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    float f = Math.Clamp(st.Rest[i].z / st.Length, 0f, 1f) * (n + 1); int k = Math.Min((int)f, n);
                    float off = bow[k] + (bow[k + 1] - bow[k]) * (f - k);
                    v[i].x = st.Rest[i].x + lo.x * off / sc.x; v[i].y = st.Rest[i].y + lo.y * off / sc.y; v[i].z = st.Rest[i].z + lo.z * off / sc.z;
                }
                st.Mesh.vertices = v; st.Mesh.RecalculateNormals(); st.Mesh.RecalculateBounds(); st.Out = lo;
                // how far the strap's outer face stands off the line between its ends, along its length, for whatever lies on top of it
                var face = new float[n + 2]; var hit = new bool[n + 2];
                for (int i = 0; i < v.Length; i++)
                {
                    int k = Math.Clamp((int)MathF.Round(v[i].z / st.Length * (n + 1)), 0, n + 1); float h = v[i].x * sc.x * lo.x + v[i].y * sc.y * lo.y;
                    if (!hit[k] || h > face[k]) { face[k] = h; hit[k] = true; }
                }
                for (int i = 1; i < face.Length; i++) if (!hit[i]) { face[i] = face[i - 1]; hit[i] = hit[i - 1]; }
                for (int i = face.Length - 2; i >= 0; i--) if (!hit[i]) face[i] = face[i + 1];
                st.Bow = face;
                log.Append(' ').Append(st.Name).Append(' ').Append((most * 1000).ToString("0")).Append(" mm");
            }
            Plugin.Logger.LogInfo(log.ToString());
        }

        /// <summary>Radius by angle about the band's centre: the outermost point in each bin, or the innermost.</summary>
        private float[] Bins(List<System.Numerics.Vector2> pts, bool outer, BeltBand band = null)
        {
            band ??= _band;
            var o = new float[BeltBand.Bins];
            if (!outer) for (int i = 0; i < o.Length; i++) o[i] = 9f;
            foreach (var p in pts)
            {
                float dx = p.X - band.CentreX, dz = p.Y - band.CentreZ, r = MathF.Sqrt(dx * dx + dz * dz); int b = BeltBand.Bin(MathF.Atan2(dx, dz));
                o[b] = outer ? Math.Max(o[b], r) : Math.Min(o[b], r);
            }
            return o;
        }

        /// <summary>
        /// How a piston and its guide sit in her, along the piston axis in 2 cm steps from the guide. Per step: how deep her skin comes inside the part's
        /// own radius on each side (mm: L her left, R her right, F front, B back), then whether the axis there is inside (i) or outside (o) her body.
        /// </summary>
        private string PartReport(string name, Transform guide, Vector3 axisModel, Vector3[] w, int[] tris, Quaternion rot)
        {
            Vector3 c = guide.position, ax = (guide.rotation * axisModel).normalized, right = rot * Vector3.right, fwd = rot * Vector3.forward, up = rot * Vector3.up;
            float cap = 0.055f * guide.lossyScale.x, z0 = 9f, z1 = -9f, step = 0.02f;
            var prof = new Dictionary<int, float>();
            foreach (var mf in guide.GetComponentsInChildren<MeshFilter>())
                foreach (var v in mf.sharedMesh.vertices)
                {
                    Vector3 p = mf.transform.TransformPoint(v) - c; float z = Vector3.Dot(p, ax), r = (p - ax * z).magnitude;
                    if (r > cap) continue;
                    int b = Mathf.FloorToInt(z / step);
                    prof[b] = prof.TryGetValue(b, out float o) ? Mathf.Max(o, r) : r;
                    z0 = Mathf.Min(z0, z); z1 = Mathf.Max(z1, z);
                }
            var deep = new Dictionary<int, float[]>();
            for (int i = 0; i < w.Length; i++)
            {
                if (_arm[i]) continue;
                Vector3 p = w[i] - c; float z = Vector3.Dot(p, ax); int b = Mathf.FloorToInt(z / step);
                if (!prof.TryGetValue(b, out float pr)) continue;
                Vector3 rad = p - ax * z; float r = rad.magnitude;
                if (r >= pr) continue;
                float x = Vector3.Dot(rad, right), f = Vector3.Dot(rad, fwd);
                int side = Mathf.Abs(x) > Mathf.Abs(f) ? (x < 0f ? 0 : 1) : (f > 0f ? 2 : 3);
                if (!deep.TryGetValue(b, out var d)) deep[b] = d = new float[4];
                d[side] = Mathf.Max(d[side], pr - r);
            }
            var o2 = new System.Text.StringBuilder(name + " len " + ((z1 - z0) * 100).ToString("0.0") + "cm:");
            Vector3 centre = _belt.position + right * _band.CentreX + fwd * _band.CentreZ;
            for (int b = Mathf.FloorToInt(z0 / step); b <= Mathf.FloorToInt(z1 / step); b++)
            {
                Vector3 p = c + ax * ((b + 0.5f) * step);
                Vector3 axisAt = centre + up * Vector3.Dot(p - centre, up);
                float px = Vector3.Dot(p - axisAt, right), pz = Vector3.Dot(p - axisAt, fwd), rp = Mathf.Sqrt(px * px + pz * pz), rim = 0f;
                if (rp > 0.001f)
                    foreach (var q in Cut(w, tris, p, up, right, fwd, _arm))
                    {
                        float qx = q.X + px, qz = q.Y + pz, along = (qx * px + qz * pz) / rp, aside = Mathf.Abs(qx * pz - qz * px) / rp;
                        if (aside < 0.012f && along > 0f) rim = Mathf.Max(rim, along);
                    }
                o2.Append(" [").Append(b * 2).Append(rp < rim ? " i" : " o");
                if (deep.TryGetValue(b, out var d))
                    for (int k = 0; k < 4; k++) if (d[k] > 0.0005f) o2.Append(' ').Append("LRFB"[k]).Append((d[k] * 1000).ToString("0"));
                o2.Append(']');
            }
            return o2.ToString();
        }

        /// <summary>How deep each chain piece's mesh sits under her skin as drawn now, near her midline, in mm. Worst vertex per piece, cut at the piece's own heights.</summary>
        private string ChainReport(Transform[] links, float side, Vector3[] w, int[] tris, Quaternion rot)
        {
            Vector3 up = rot * Vector3.up, right = rot * Vector3.right, fwd = rot * Vector3.forward, o = _belt.position + right * _band.CentreX;
            const float step = 0.004f;
            var skin = new Dictionary<int, float>();
            float half = side > 0f ? 0.012f : 0.005f;
            foreach (float x in new[] { -half, 0f, half })
                foreach (var q in Cut(w, tris, o + right * x, right, up, fwd, _arm))
                {
                    int b = Mathf.RoundToInt(q.X / step); float f = q.Y * side;
                    skin[b] = skin.TryGetValue(b, out float v) ? Mathf.Max(v, f) : f;
                }
            var sb = new System.Text.StringBuilder();
            foreach (var t in links)
            {
                float deep = 0f;
                foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
                    foreach (var v in mf.sharedMesh.vertices)
                    {
                        Vector3 p = mf.transform.TransformPoint(v) - o;
                        if (Mathf.Abs(Vector3.Dot(p, right)) > half + 0.002f || !skin.TryGetValue(Mathf.RoundToInt(Vector3.Dot(p, up) / step), out float sk)) continue;
                        deep = Mathf.Max(deep, sk - Vector3.Dot(p, fwd) * side);
                    }
                sb.Append(' ').Append(t.name).Append(' ').Append((deep * 1000).ToString("0"));
            }
            return sb.ToString();
        }

        /// <summary>The ring and the top of the V strap against her skin and the belt: how deep any of it sits, in mm, measured on the real meshes. Above the belt's lower edge the slider is meant to meet the ring, so only skin counts there.</summary>
        private string RingCheck(Transform ring, string side, Vector3[] w, int[] tris, Vector3[] beltWorld, int[] beltTris)
        {
            Quaternion rot = _belt.rotation; Vector3 pos = _belt.position, up = rot * Vector3.up, right = rot * Vector3.right, fwd = rot * Vector3.forward;
            var pts = new List<Vector3>();
            foreach (var mf in ring.GetComponentsInChildren<MeshFilter>()) foreach (var v in mf.sharedMesh.vertices) pts.Add(mf.transform.TransformPoint(v));
            var st = _stretches.Find(x => x.Name == "vstrap_" + side);
            var sv = st.Mesh.vertices;
            for (int i = 0; i < sv.Length; i++) if (sv[i].z < st.Length * 0.25f) pts.Add(st.T.TransformPoint(sv[i]));
            float skinDeep = 0f, beltDeep = 0f, lo = 9f, hi = -9f;
            foreach (var p in pts) { float h = Vector3.Dot(p - pos, up); lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h); }
            for (float h = lo; h <= hi; h += 0.004f)
            {
                Vector3 o = pos + up * h;
                float[] skin = Bins(Cut(w, tris, o, up, right, fwd, _arm), true), belt = Bins(Cut(beltWorld, beltTris, o, up, right, fwd), true);
                foreach (var p in pts)
                {
                    if (Mathf.Abs(Vector3.Dot(p - o, up)) > 0.002f) continue;
                    float dx = Vector3.Dot(p - pos, right) - _band.CentreX, dz = Vector3.Dot(p - pos, fwd) - _band.CentreZ, r = Mathf.Sqrt(dx * dx + dz * dz); int b = BeltBand.Bin(Mathf.Atan2(dx, dz));
                    skinDeep = Mathf.Max(skinDeep, skin[b] - r);
                    if (h < -0.0335f * _beltTall) beltDeep = Mathf.Max(beltDeep, belt[b] - r);
                }
            }
            return side + " skin " + (skinDeep * 1000).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " belt " + (beltDeep * 1000).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>The heart and its ring against her skin, and the heart against the belt and buckle, in mm, on the real meshes. The ring is meant to touch the staple, so only skin counts for it.</summary>
        private string LockCheck(Vector3[] w, int[] tris, Vector3[] beltWorld, int[] beltTris)
        {
            Quaternion rot = _belt.rotation; Vector3 pos = _belt.position, up = rot * Vector3.up, right = rot * Vector3.right, fwd = rot * Vector3.forward;
            var lockPts = new List<Vector3>(); var ringPts = new List<Vector3>();
            foreach (var mf in _lock.GetComponentsInChildren<MeshFilter>()) foreach (var v in mf.sharedMesh.vertices) lockPts.Add(mf.transform.TransformPoint(v));
            foreach (var mf in _lockRing.GetComponentsInChildren<MeshFilter>()) foreach (var v in mf.sharedMesh.vertices) ringPts.Add(mf.transform.TransformPoint(v));
            float skinDeep = 0f, beltDeep = 0f, lo = 9f, hi = -9f;
            foreach (var p in lockPts) { float h = Vector3.Dot(p - pos, up); lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h); }
            foreach (var p in ringPts) { float h = Vector3.Dot(p - pos, up); lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h); }
            for (float h = lo; h <= hi + 0.001f; h += 0.002f)
            {
                Vector3 o = pos + up * h;
                float[] skin = Bins(Cut(w, tris, o, up, right, fwd, _arm), true);
                float[] belt = Bins(Cut(beltWorld, beltTris, o, up, right, fwd), true);
                void Probe(List<Vector3> pts, bool againstBelt)
                {
                    foreach (var p in pts)
                    {
                        if (Mathf.Abs(Vector3.Dot(p - o, up)) > 0.001f) continue;
                        float dx = Vector3.Dot(p - pos, right) - _band.CentreX, dz = Vector3.Dot(p - pos, fwd) - _band.CentreZ, r = Mathf.Sqrt(dx * dx + dz * dz); int k = BeltBand.Bin(Mathf.Atan2(dx, dz));
                        skinDeep = Mathf.Max(skinDeep, skin[k] - r);
                        if (againstBelt) beltDeep = Mathf.Max(beltDeep, belt[k] - r);
                    }
                }
                Probe(lockPts, true); Probe(ringPts, false);
            }
            // and against the thigh straps, which come up close when she crouches: inside a strap's band, between its inside and outside faces
            float strapDeep = 0f;
            foreach (var hp in new[] { _hoopFitL, _hoopFitR })
            {
                if (hp.Band == null) continue;
                Quaternion fr = hp.Node.rotation * Quaternion.Inverse(hp.ToFrame);
                foreach (var p in lockPts)
                {
                    Vector3 q = Quaternion.Inverse(fr) * (p - hp.Node.position);
                    if (Mathf.Abs(q.y) > hp.Half) continue;
                    float dx = q.x - hp.Band.CentreX, dz = q.z - hp.Band.CentreZ, r = Mathf.Sqrt(dx * dx + dz * dz), a = Mathf.Atan2(dx, dz);
                    float inner = hp.Band.Inside(a, q.y / hp.Half), outer = inner + 0.0045f * hp.Tall;
                    if (r > inner - 0.002f && r < outer) strapDeep = Mathf.Max(strapDeep, outer - r);
                }
            }
            return "lock strap " + (strapDeep * 1000).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " skin " + (skinDeep * 1000).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " belt " + (beltDeep * 1000).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Tiny bright cubes on the outline measured: if they sit on her skin in a screenshot, the data matches what is drawn.</summary>
        private GameObject MarkerObject(List<Vector3> pts)
        {
            float e = 0.0016f;
            var v = new List<Vector3>(); var t = new List<int>();
            foreach (var p in pts)
            {
                int b = v.Count;
                for (int i = 0; i < 8; i++) v.Add(p + new Vector3((i & 1) == 0 ? -e : e, (i & 2) == 0 ? -e : e, (i & 4) == 0 ? -e : e));
                foreach (int k in new[] { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 }) t.Add(b + k);
            }
            var m = new Mesh { name = Tag + "Markers", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = v.ToArray(); m.triangles = t.ToArray(); m.RecalculateNormals(); m.RecalculateBounds();
            var go = new GameObject("Markers");
            go.layer = _top.layer;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _markerMat; r.allowOcclusionWhenDynamic = false;
            UnityEngine.Object.DontDestroyOnLoad(go);
            return go;
        }

        private static float SegDist(Vector3 p, Vector3 a, Vector3 b) => SegDist(N(p), N(a), N(b));

        private static float SegDist(NVector3 p, NVector3 a, NVector3 b)
        {
            NVector3 ab = b - a;
            float t = Math.Clamp(NVector3.Dot(p - a, ab) / Math.Max(ab.LengthSquared(), 1e-8f), 0f, 1f);
            return (p - (a + ab * t)).Length();
        }

        private void SetupHoop(Hoop h, Transform node, Hoop other)
        {
            h.Node = node; h.Other = other; h.Tris = h.Mesh.triangles;
            h.ToFrameN = NQ(h.ToFrame); h.FromFrameN = System.Numerics.Quaternion.Inverse(h.ToFrameN);
            var nv = new NVector3[h.Rest.Length];
            for (int i = 0; i < nv.Length; i++) { Vector3 f = h.ToFrame * h.Rest[i]; nv[i] = new NVector3(f.x, f.y, f.z); }
            h.Inner = BeltBand.Soft(BeltBand.ModelInner(nv, 0f, 0.03f), 3);
        }

        /// <summary>One cut across her thigh: her skin in the strap's plane, the other leg's left out.</summary>
        private List<System.Numerics.Vector2> ThighCut(Hoop h, Vector3[] w, Vector3 at, Quaternion frame, float height)
        {
            Vector3 x = frame * Vector3.right, y = frame * Vector3.up, z = frame * Vector3.forward, o = at + y * height;
            var keep = new List<System.Numerics.Vector2>();
            NVector3 ah = N(h.Bone.position), ak = N(h.Knee.position), bh = N(h.Other.Bone.position), bk = N(h.Other.Knee.position), no = N(o), nx = N(x), nz = N(z);
            foreach (var q in Cut(w, h.Near, o, y, x, z))
            {
                NVector3 p = no + nx * q.X + nz * q.Y;
                if (SegDist(p, ah, ak) <= SegDist(p, bh, bk)) keep.Add(q);
            }
            return keep;
        }

        private float[][] HoopSkin(Hoop h, Vector3[] w, Vector3 at, Quaternion frame)
        {
            var o = new float[Heights.Length][];
            for (int k = 0; k < o.Length; k++) o[k] = Bins(ThighCut(h, w, at, frame, Heights[k] * h.Half), true, h.Band);
            return o;
        }

        private void HoopRefit(Hoop h)
        {
            long trf = Perf.Now;
            HoopRefitInner(h);
            Perf.Add(Perf.Refit, trf);
        }

        private void HoopRefitInner(Hoop h)
        {
            var v = new Vector3[h.Rest.Length]; var f = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) { v[i] = h.Local(h.Rest[i]); f[i] = V(NVector3.Transform(N(v[i]), h.ToFrameN)); }
            h.Mesh.vertices = v; h.Mesh.RecalculateNormals(); h.Mesh.RecalculateBounds();
            h.Bins = new float[Heights.Length][];
            for (int k = 0; k < Heights.Length; k++) h.Bins[k] = Bins(Cut(f, h.Tris, Vector3.up * (Heights[k] * h.Half), Vector3.up, Vector3.right, Vector3.forward), false, h.Band);
            foreach (var st in h.Fixed)
            {
                // where the strap takes this point, and how it turns and tips the strap there: measured along the strap and across it
                Vector3 c = h.ToFrame * st.P; Quaternion back = Quaternion.Inverse(h.ToFrame);
                Vector3 t0 = back * (Vector3.Cross(Vector3.up, new Vector3(c.x, 0f, c.z)).normalized * 0.008f), b0 = back * (Vector3.up * 0.008f);
                Vector3 t1 = h.Local(st.P + t0) - h.Local(st.P - t0), b1 = h.Local(st.P + b0) - h.Local(st.P - b0);
                st.T.localPosition = h.Local(st.P);
                st.T.localRotation = Quaternion.LookRotation(Vector3.Cross(t1, b1), b1) * Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(t0, b0), b0));
                st.T.localScale = Vector3.one * h.Tall;
            }
        }

        private bool HoopPush(Hoop h, float[][] skin)
        {
            if (!Loosen(h.Band, h.Bins, skin, h.Lifted)) return false;
            HoopRefit(h);
            return true;
        }

        private void FitThigh(Hoop h, Transform bone, Transform knee, Vector3[] w, int[] tris, Vector3 modelHip, Vector3 modelHoop, Quaternion rot, float sy)
        {
            Vector3 hp = bone.position, kp = knee.position, at = hp + (kp - hp) * Plugin.HoopAlongThigh.Value;
            Quaternion q = ThighTurn(hp, kp, modelHip, modelHoop, rot), frame = q * Quaternion.Inverse(h.ToFrame);
            h.Bone = bone; h.Knee = knee;
            if (h.Other.Bone == null) return;   // both legs are needed to tell their skin apart
            var near = new List<int>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                float ex = w[a].x - at.x, ey = w[a].y - at.y, ez = w[a].z - at.z;
                if (_arm[a] || _arm[b] || _arm[c] || ex * ex + ey * ey + ez * ez > 0.05f) continue;
                near.Add(a); near.Add(b); near.Add(c);
            }
            h.Near = near.ToArray();
            float half = HoopHalf * sy;
            Vector3 y = frame * Vector3.up;
            var mid = ThighCut(h, w, at, frame, 0f);
            if (mid.Count < 8) return;
            float x0 = 9, x1 = -9, z0 = 9, z1 = -9;
            foreach (var p in mid) { x0 = Mathf.Min(x0, p.X); x1 = Mathf.Max(x1, p.X); z0 = Mathf.Min(z0, p.Y); z1 = Mathf.Max(z1, p.Y); }
            var band = BeltBand.From(ThighCut(h, w, at, frame, -half), mid, ThighCut(h, w, at, frame, half), new System.Numerics.Vector2((x0 + x1) / 2, (z0 + z1) / 2), 0.0015f, h.Inner);
            if (band == null) return;
            band.Smooth(2);
            h.Band = band; h.Tall = sy; h.Half = half; h.Lifted = new float[BeltBand.Bins];
            h.LP = Quaternion.Inverse(bone.rotation) * (at - hp); h.LQ = Quaternion.Inverse(bone.rotation) * q;
            HoopRefit(h);
            for (int pass = 0; pass < 3; pass++) if (!HoopPush(h, HoopSkin(h, w, at, frame))) break;
            Plugin.Logger.LogInfo("[SFM-ACCEPT] thigh: " + (x1 - x0).ToString("0.000") + " x " + (z1 - z0).ToString("0.000") + ", " + near.Count / 3 + " triangles");
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
            if (Time.time >= _nextFit && (_fits < 2 || Mathf.Abs((kl - hl).magnitude - _thighLength) > 0.02f * _thighLength)) { _needMeasure = true; _nextFit = Time.time + 2f; }
            if (_needMeasure) { long tm = Perf.Now; Measure(anim); Perf.Add(Perf.Measure, tm); }
            if (_band == null) return;
            Vector3 right = (hr - hl).normalized, up = Vector3.ProjectOnPlane(B(HumanBodyBones.Spine).position - mid, right).normalized;
            Vector3 fwd = Vector3.Cross(right, up);
            Quaternion rot = Quaternion.LookRotation(fwd, up);
            Transform hb = B(HumanBodyBones.Hips), sb = B(HumanBodyBones.Spine);
            _belt.SetPositionAndRotation(Vector3.Lerp(hb.position + hb.rotation * _lh, sb.position + sb.rotation * _ls, 0.5f), Quaternion.Slerp(hb.rotation * _qh, sb.rotation * _qs, 0.5f));
            float ls = _lowerScale * Plugin.ScaleTrim.Value;
            float ps = ls * Plugin.PelvisSize.Value;
            PlaceGuides(anim, rot, ps);
            PlaceThigh(_thighL, _hoopFitL, hl, kl, _hipL, _hoopL, rot);
            PlaceThigh(_thighR, _hoopFitR, hr, kr, _hipR, _hoopR, rot);

            long tc = Perf.Now;
            // Beta's anchor_L and eyelet_L hang on her right thigh, anchor_R and eyelet_R on her left
            Vector3 barAL = Bar("anchor_L", _thighR, _hoopFitR, _hoopR), barAR = Bar("anchor_R", _thighL, _hoopFitL, _hoopL), barEL = Bar("eyelet_L", _thighR, _hoopFitR, _hoopR), barER = Bar("eyelet_R", _thighL, _hoopFitL, _hoopL);
            Vector3 eyeF0 = _guideF.TransformPoint(Sock("SOCKET_cord_front") - _cF), eyeR0 = _guideR.TransformPoint(Sock("SOCKET_cord_rear") - _cR);
            float dt = Time.deltaTime, thigh = (kl - hl).magnitude;
            // the same cords with her legs straight down, which is where they go slack
            Vector3 rAL = RestBar("anchor_L", hr, kr, _hipR, _hoopR, _hoopFitR, rot, up), rAR = RestBar("anchor_R", hl, kl, _hipL, _hoopL, _hoopFitL, rot, up);
            Vector3 rEL = RestBar("eyelet_L", hr, kr, _hipR, _hoopR, _hoopFitR, rot, up), rER = RestBar("eyelet_R", hl, kl, _hipL, _hoopL, _hoopFitL, rot, up);
            float cordF = (barAL - barER).magnitude + (barER - eyeF0).magnitude, cordR = (barAR - barEL).magnitude + (barEL - eyeR0).magnitude;
            float restF = (rAL - rER).magnitude + (rER - eyeF0).magnitude, restR = (rAR - rEL).magnitude + (rEL - eyeR0).magnitude;
            bool zeta = Plugin.Mode.Value == DriveMode.Zeta;
            if (_shownMode != (zeta ? 1 : 0))
            {
                _shownMode = zeta ? 1 : 0;
                foreach (var o in _gammaOnly) o.SetActive(!zeta);
                foreach (var o in _zetaOnly) o.SetActive(zeta);
            }
            // Zeta: the ring on the back of her right thigh strap pulls the front piston, the ring on its front the rear piston
            Vector3 zB = default, zF = default;
            if (zeta)
            {
                zB = Bar("zback_L", _thighR, _hoopFitR, _hoopR); zF = Bar("zfront_L", _thighR, _hoopFitR, _hoopR);
                Vector3 rB = RestBar("zback_L", hr, kr, _hipR, _hoopR, _hoopFitR, rot, up), rZ = RestBar("zfront_L", hr, kr, _hipR, _hoopR, _hoopFitR, rot, up);
                cordF = (zB - eyeF0).magnitude; cordR = (zF - eyeR0).magnitude; restF = (rB - eyeF0).magnitude; restR = (rZ - eyeR0).magnitude;
                if (!_zetaManual && !_zetaLearned && _hoopFitR.Band != null && (_zetaFitAt < 0 || Time.frameCount - _zetaFitAt > 120)) ZetaFit(hr, kr, eyeF0, eyeR0, restF, restR, rot, up, right, thigh);
                if (!_zetaManual) ZetaLearn((cordF - restF) / thigh, (cordR - restR) / thigh, Vector3.Dot(kr - hr, up) < -0.57f * (kr - hr).magnitude && Vector3.Dot(kl - hl, up) < -0.57f * (kl - hl).magnitude);
                _zetaF.Update(cordF, restF, thigh, dt); _zetaR.Update(cordR, restR, thigh, dt);
            }
            else { _driveF.Update(cordF, restF, thigh, dt); _driveR.Update(cordR, restR, thigh, dt); }
            float force = ForcedStroke, sF = zeta ? _zetaF.Stroke : _driveF.Stroke, sR = zeta ? _zetaR.Stroke : _driveR.Stroke;
            if (CordLogUntil > Time.time)
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                _cordLog.Append(Time.time.ToString("0.000", ci)).Append(' ').Append(((cordF - restF) / thigh).ToString("0.0000", ci)).Append(' ').Append(((cordR - restR) / thigh).ToString("0.0000", ci))
                    .Append(' ').Append(Vector3.SignedAngle(-up, (kr - hr).normalized, right).ToString("0.0", ci)).Append(' ').Append(Vector3.SignedAngle(-up, (kl - hl).normalized, right).ToString("0.0", ci))
                    .Append(' ').Append(sF.ToString("0.000", ci)).Append(' ').Append(sR.ToString("0.000", ci)).Append(';');
            }
            else if (_cordLog.Length > 0)
            {
                Plugin.Logger.LogInfo("[SFM-ACCEPT] cordlog: " + _cordLog); _cordLog.Clear();
                if (_gapLog.Length > 0) { Plugin.Logger.LogInfo("[SFM-ACCEPT] cordgap: " + _gapLog); _gapLog.Clear(); }
            }
            _pistonF.localPosition = -_axisF * ((force >= 0f ? force : sF) * StrokeMetres);
            _pistonR.localPosition = -_axisR * ((force >= 0f ? force : sR) * StrokeMetres);
            Vector3 eyeF = _pistonF.TransformPoint(Sock("SOCKET_cord_front") - _cF), eyeR = _pistonR.TransformPoint(Sock("SOCKET_cord_rear") - _cR);

            if (zeta)
            {
                Vector3 seatB = Swing("zback_L", "SOCKET_zback_L", zB, (eyeF - zB).normalized, _thighR, ls);
                Vector3 seatF = Swing("zfront_L", "SOCKET_zfront_L", zF, (eyeR - zF).normalized, _thighR, ls);
                // each cord lies on her skin where a straight line would go through it
                long tp = Perf.Now;
                if (_patch == null && !_patchFailed && _hoopFitR.Band != null) BuildPatch(anim, eyeF0, eyeR0, zB, zF, right);
                if (_patch != null && WrapOn) Skin(_patch);
                float clear = 0.0014f * ls + 0.0012f;
                SkinPatch skin = WrapOn ? _patch : null;
                _pathN[0] = Wrap(skin, N(seatB), N(eyeF), clear, _path[0]); _pathN[1] = Wrap(skin, N(seatF), N(eyeR), clear, _path[1]);
                DrawPath(0, ls); DrawPath(1, ls);
                Perf.Add(Perf.Wrap, tp);
                // the clips point along the cord's first and last stretch
                End("end_zback_thigh", seatB, V(_path[0][1]), "SOCKET_zback_L", "SOCKET_cord_front", _thighR, ls);
                End("end_zfront_thigh", seatF, V(_path[1][1]), "SOCKET_zfront_L", "SOCKET_cord_rear", _thighR, ls);
                End("end_zback_piston", eyeF, V(_path[0][_pathN[0] - 2]), "SOCKET_cord_front", "SOCKET_zback_L", _guideF, ps);
                End("end_zfront_piston", eyeR, V(_path[1][_pathN[1] - 2]), "SOCKET_cord_rear", "SOCKET_zfront_L", _guideR, ps);
                if (CordLogUntil > Time.time && Time.frameCount % 6 == 0) CordGap(anim, mid, right, up);
            }
            else
            {
                Vector3 seatAL = Swing("anchor_L", "SOCKET_anchor_L", barAL, (barER - barAL).normalized, _thighR, ls);
                Vector3 seatAR = Swing("anchor_R", "SOCKET_anchor_R", barAR, (barEL - barAR).normalized, _thighL, ls);
                Vector3 seatEL = Swing("eyelet_L", "SOCKET_eyelet_L", barEL, ((barAR - barEL).normalized + (eyeR - barEL).normalized).normalized, _thighR, ls);
                Vector3 seatER = Swing("eyelet_R", "SOCKET_eyelet_R", barER, ((barAL - barER).normalized + (eyeF - barER).normalized).normalized, _thighL, ls);
                Line(_cords[0], seatAL, seatER, ls); Line(_cords[1], seatER, eyeF, ls); Line(_cords[2], seatAR, seatEL, ls); Line(_cords[3], seatEL, eyeR, ls);
                End("end_front_thigh", seatAL, seatER, "SOCKET_anchor_L", "SOCKET_eyelet_R", _thighR, ls);
                End("end_rear_thigh", seatAR, seatEL, "SOCKET_anchor_R", "SOCKET_eyelet_L", _thighL, ls);
                End("end_front_piston", eyeF, seatER, "SOCKET_cord_front", "SOCKET_eyelet_R", _guideF, ps);
                End("end_rear_piston", eyeR, seatEL, "SOCKET_cord_rear", "SOCKET_eyelet_L", _guideR, ps);
            }
            Perf.Add(Perf.Cords, tc);
            long tr = Perf.Now;
            SwingRing(_ringR, "R", _ringLiftR, rot, ls); SwingRing(_ringL, "L", _ringLiftL, rot, ls);
            Perf.Add(Perf.Rings, tr);
            long tl = Perf.Now;
            HangLock(ls);
            Perf.Add(Perf.Lock, tl);
            long ts = Perf.Now;
            foreach (var st in _stretches)
            {
                Vector3 a = Point(st.From, st.Start, st.FromOrigin), b = Point(st.To, st.End, st.ToOrigin), d = b - a;
                st.T.SetPositionAndRotation(a, Quaternion.LookRotation(d, rot * st.Hint));
                float sw = st.Name.StartsWith("spring_") || st.Name == "hinge_link" ? ps : ls;
                st.T.localScale = new Vector3(sw, sw, d.magnitude / st.Length);
            }
            Perf.Add(Perf.Stretches, ts);
            if (_wantBow > 0) { _wantBow--; long tb = Perf.Now; Bow(anim); Perf.Add(Perf.Bow, tb); }
            long tw = Perf.Now;
            DrawWire(rot, ls, ps);
            Perf.Add(Perf.Wire, tw);
            long tse = Perf.Now;
            Settle(anim);
            Perf.Add(Perf.Settle, tse);
            if (WatchUntil > Time.time && Time.time >= _nextWatch) { _nextWatch = Time.time + 0.2f; WantGap = true; }
            if (WantGap) { WantGap = false; GapReport(anim); }
            if (Time.frameCount % 30 == 0) Status = "size " + ls.ToString("0.00") + " stroke " + sF.ToString("0.00") + " " + sR.ToString("0.00") + " cord " + cordF.ToString("0.000") + " of " + restF.ToString("0.000") + ", " + cordR.ToString("0.000") + " of " + restR.ToString("0.000");
        }

        private Vector3 Point(Transform node, Vector3 model, Vector3 origin) =>
            node == _belt ? _belt.TransformPoint(U(_band.Warp(new NVector3(model.x, model.y, model.z), BeltY, BeltHalf, _beltTall, _beltThick)))
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
                _anchorF = m.Find("PussyDildoTarget"); _anchorR = n.Find("RotateTarget/AnalDildoTarget"); _axisObjF = m.Find("RotateTarget/piston_machine_dildo (1)"); _axisObjR = n.Find("RotateTarget/Parent/piston_machine_dildo");
            }
            // each guide on its own axis, as the game aims its own machine; the joints and the shared link take up the difference
            Vector3 tf = _anchorF.position, tr = _anchorR.position, uF = -_axisObjF.forward, uR = -_axisObjR.forward;
            float fallback = Plugin.GuideDrop.Value * ls;
            Vector3 cF = tf - uF * (_reachF > -8f ? _reachF : fallback), cR = tr - uR * (_reachR > -8f ? _reachR : fallback);
            _guideF.SetPositionAndRotation(cF, Quaternion.FromToRotation(rot * _axisF, uF) * rot);
            _guideR.SetPositionAndRotation(cR, Quaternion.FromToRotation(rot * _axisR, uR) * rot);
            _guideF.localScale = _guideR.localScale = Vector3.one * ls;
            Transform hb = anim.GetBoneTransform(HumanBodyBones.Hips);
            long tch = Perf.Now;
            LayChain(_linkF, "f", _guideF, _cF, _pathF, hb, rot, ls);
            LayChain(_linkR, "r", _guideR, _cR, _pathR, hb, rot, ls);
            Perf.Add(Perf.Chains, tch);
            if (Probe)
            {
                Probe = false;
                float link = (_guideR.TransformPoint(Sock("SOCKET_hinge_rear") - _cR) - _guideF.TransformPoint(Sock("SOCKET_hinge_front") - _cF)).magnitude, model = (Sock("SOCKET_hinge_rear") - Sock("SOCKET_hinge_front")).magnitude * ls;
                Vector3 tipF = cF + uF * (PistonTip * ls), tipR = cR + uR * (PistonTip * ls);
                Plugin.Logger.LogInfo("[SFM-ACCEPT] joints: link " + (link * 100).ToString("0.0") + " cm, stretched " + (link / model).ToString("0.00") + "; guides " + ((cF - cR).magnitude * 100).ToString("0.0") + " cm apart, tops " + ((tipF - tipR).magnitude * 100).ToString("0.0") + "; angle " + Vector3.Angle(uF, uR).ToString("0"));
            }
        }

        /// <summary>
        /// The chain from a guide's lug up her body. It hinges only on its bars, and they all stay parallel to the lug's bar, so the whole chain lies in one
        /// plane square to that bar and every wrap sits square on the bars at both its ends. Joints follow the path traced along her skin, flattened into that plane.
        /// </summary>
        private void LayChain(Transform[] links, string tag, Transform guide, Vector3 c, Vector3[] path, Transform hips, Quaternion rot, float ls)
        {
            int n = links.Length;
            Vector3 bar = guide.rotation * Vector3.right;
            var joint = new Vector3[n + 1]; var model = new Vector3[n + 1];
            for (int i = 0; i <= n; i++) { model[i] = Sock("SOCKET_link_" + tag + i); joint[i] = guide.TransformPoint(model[i] - c); }
            if (path != null)
            {
                var lens = new float[n]; var w = new NVector3[path.Length];
                for (int i = 0; i < n; i++) lens[i] = (model[i + 1] - model[i]).magnitude * ls;
                NVector3 hp = N(hips.position), nb = N(bar), j0 = N(joint[0]); var hq = NQ(hips.rotation);
                for (int i = 0; i < w.Length; i++)
                {
                    NVector3 p = hp + NVector3.Transform(N(path[i]), hq);
                    w[i] = p - nb * NVector3.Dot(p - j0, nb);
                }
                var j = ChainPath.Walk(w, new NVector3(joint[0].x, joint[0].y, joint[0].z), lens);
                for (int i = 0; i <= n; i++) joint[i] = U(j[i]);
            }
            for (int i = 0; i < n; i++)
            {
                Vector3 from = (model[i + 1] - model[i]).normalized, to = Vector3.ProjectOnPlane(joint[i + 1] - joint[i], bar).normalized;
                links[i].SetPositionAndRotation(joint[i], Quaternion.LookRotation(Vector3.Cross(bar, to), to) * Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(Vector3.right, from), from)));
                links[i].localScale = Vector3.one * ls;
            }
        }

        /// <summary>
        /// Her midline, from the guides up to the belt: a cut down her middle, front or back, as a path a little off her skin. Kept in her hips' frame,
        /// which is what the guides ride on. At the back it spans the cleft, as a strap would.
        /// </summary>
        private Vector3[] Trace(Vector3[] w, int[] tris, Transform hips, Vector3 start, Vector3 end, float side, float span, float spanTop, float lift, Vector3 right, Vector3 up, Vector3 fwd, Vector3 mid)
        {
            const float step = 0.006f;
            float h0 = Vector3.Dot(start - mid, up), h1 = Vector3.Dot(end - mid, up);
            int n = Mathf.CeilToInt((h1 - h0) / step) + 1;
            if (n < 3) return null;
            float[] Profile(float width, out bool[] seen)
            {
                var v = new float[n]; var hit = new bool[n];
                foreach (float x in width > 0f ? new[] { -width, 0f, width } : new[] { 0f })
                    foreach (var q in Cut(w, tris, mid + right * x, right, up, fwd, _arm))
                    {
                        int b = Mathf.RoundToInt((q.X - h0) / step);
                        if (b < 0 || b >= n) continue;
                        float f = q.Y * side;
                        if (!hit[b] || f > v[b]) { v[b] = f; hit[b] = true; }
                    }
                seen = hit; return v;
            }
            var val = Profile(span, out var seen); var wide = Profile(spanTop, out var seenTop);
            // from the lug the chain starts narrow, on the floor of a hollow, and rises to the wider pieces' level higher up
            for (int i = 0; i < n; i++) if (seen[i] && seenTop[i]) val[i] = Mathf.Lerp(val[i], wide[i], Mathf.Clamp01(i * step / 0.03f));
            var pts = new List<Vector3>();
            float last = Vector3.Dot(start - mid, fwd) * side;
            for (int i = 0; i < n; i++)
            {
                if (seen[i]) last = val[i] + lift;
                Vector3 p = mid + up * (h0 + i * step) + fwd * (last * side);
                pts.Add(Quaternion.Inverse(hips.rotation) * (p - hips.position));
            }
            return pts.ToArray();
        }

        private const int WireSides = 6, WirePer = 4, WireCtrl = 7;
        private Mesh _wire; private Vector3[] _wireV, _wireN;

        private void MakeWire(Material pink, int layer)
        {
            int rings = (WireCtrl - 1) * WirePer + 1;
            var t = new int[(rings - 1) * WireSides * 6];
            for (int r = 0, o = 0; r + 1 < rings; r++)
                for (int i = 0; i < WireSides; i++)
                {
                    int a = r * WireSides + i, b = r * WireSides + (i + 1) % WireSides, c = a + WireSides, d = b + WireSides;
                    t[o++] = a; t[o++] = b; t[o++] = c; t[o++] = b; t[o++] = d; t[o++] = c;
                }
            _wireV = new Vector3[rings * WireSides]; _wireN = new Vector3[rings * WireSides];
            _wire = new Mesh { name = Tag + "Wire", vertices = _wireV, normals = _wireN, triangles = t };
            _owned.Add(_wire);
            var go = new GameObject("wire_up"); go.layer = layer; go.transform.SetParent(_top.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _wire;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = pink; mr.allowOcclusionWhenDynamic = false;
        }

        /// <summary>The wire from the capsule's tip straight to the outside of the right V strap, up it, to the belt keeper.</summary>
        private void DrawWire(Quaternion rot, float ls, float ps)
        {
            var st = _stretches.Find(x => x.Name == "vstrap_R");
            Vector3 tip = _linkF[1].TransformPoint(Sock("SOCKET_capsule_wire") - Sock("SOCKET_link_f1"));
            Vector3 over = _linkF[2].rotation * Vector3.forward, top = st.T.position, bottom = top + st.T.rotation * (Vector3.forward * (st.Length * st.T.localScale.z));
            Vector3 face = st.Bow != null ? st.T.rotation * st.Out : over;
            Vector3 OnStrap(float t) => bottom + (top - bottom) * t + face * (st.BowAt(1f - t) + 0.0035f * ls);
            var c = new NVector3[WireCtrl];
            void Set(int i, Vector3 p) => c[i] = new NVector3(p.x, p.y, p.z);
            Set(0, tip); Set(1, OnStrap(0.03f)); Set(2, OnStrap(0.3f)); Set(3, OnStrap(0.55f));
            Set(4, Point(_belt, Sock("SOCKET_wire_belt_a"), Vector3.zero)); Set(5, Point(_belt, Sock("SOCKET_wire_belt_b"), Vector3.zero)); Set(6, Point(_belt, Sock("WIRE_mid"), Vector3.zero));
            var line = WirePath.Smooth(c, WirePer);
            float radius = 0.0016f * ls;
            NVector3 side = N(rot * Vector3.right);
            for (int r = 0; r < line.Length; r++)
            {
                NVector3 p = line[r], d = line[Math.Min(r + 1, line.Length - 1)] - line[Math.Max(r - 1, 0)];
                d = d.LengthSquared() > 1e-12f ? NVector3.Normalize(d) : NVector3.UnitY;
                side = NVector3.Normalize(side - d * NVector3.Dot(side, d)); NVector3 other = NVector3.Cross(d, side);
                for (int i = 0; i < WireSides; i++)
                {
                    float a = i * 2f * MathF.PI / WireSides; NVector3 n = side * MathF.Cos(a) + other * MathF.Sin(a);
                    _wireV[r * WireSides + i] = V(p + n * radius); _wireN[r * WireSides + i] = V(n);
                }
            }
            _wire.vertices = _wireV; _wire.normals = _wireN; _wire.RecalculateBounds();
        }

        /// <summary>
        /// How far out along its axis a guide sits so its band rests on her: the skin nearest the outside, inside most of the band's width, against the band's upper edge.
        /// The rim itself may touch skin to the sides, so the outer quarter doesn't count.
        /// </summary>
        /// <summary>Her skin for one moment with her legs apart. The front axis runs out between her thighs, and whether they touch it depends on the pose she happens to be in.</summary>
        private Vector3[] LegsApart(Animator anim, SkinnedMeshRenderer smr, Vector3 up, Vector3 right)
        {
            Transform l = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg), r = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            Transform lk = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg), rk = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            Quaternion sl = l.localRotation, sr = r.localRotation;
            float side = Mathf.Sign(Vector3.Dot(r.position - l.position, right));
            try
            {
                Aim(l, lk, l.position + (-up - right * side * 0.6f));
                Aim(r, rk, r.position + (-up + right * side * 0.6f));
                return Bake(smr, out _);
            }
            finally { l.localRotation = sl; r.localRotation = sr; }
        }

        /// <summary>
        /// The front seat. Her surface slopes across the ring there, so resting the ring on its lowest skin leaves it hanging clear with the shaft showing. Instead
        /// the band's upper edge meets the middle height of the skin round it, so one side beds in a little and the other stands a little clear.
        /// </summary>
        private float SeatOnAxis(Vector3[] w, Vector3 anchor, Vector3 u, float ps)
        {
            float rb = 0.0465f * ps;
            Vector3 e1 = Vector3.Cross(u, Vector3.right).normalized, e2 = Vector3.Cross(u, e1);
            var ring = new float[12];
            for (int i = 0; i < ring.Length; i++) ring[i] = 9f;
            NVector3 na = N(anchor), nu = N(u), n1 = N(e1), n2 = N(e2);
            for (int i = 0; i < w.Length; i++)
            {
                if (_arm[i]) continue;
                NVector3 p = new NVector3(w[i].x, w[i].y, w[i].z) - na; float z = NVector3.Dot(p, nu);
                if (z < -0.08f || z > 0.08f) continue;
                NVector3 q = p - nu * z; float r = q.Length();
                if (r < rb * 0.6f || r > rb) continue;
                float a = MathF.Atan2(NVector3.Dot(q, n2), NVector3.Dot(q, n1));
                int k = Math.Clamp((int)((a + MathF.PI) / (2f * MathF.PI) * ring.Length), 0, ring.Length - 1);
                if (z < ring[k]) ring[k] = z;
            }
            var hit = new List<float>();
            foreach (float z in ring) if (z < 8f) hit.Add(z);
            if (hit.Count == 0) return -9f;
            hit.Sort();
            float median = hit[hit.Count / 2];
            Plugin.Logger.LogInfo("[SFM-ACCEPT] seat: skin round the band " + (hit[0] * 1000).ToString("0.0") + " to " + (hit[hit.Count - 1] * 1000).ToString("0.0") + ", middle " + (median * 1000).ToString("0.0") + " mm from the anchor");
            return BandTopOffset(ps) + 0.001f - median;
        }

        private static float BandTopOffset(float ps) => 0.015f * ps;

        private float Snug(Vector3[] w, Vector3 anchor, Vector3 u, float ps)
        {
            float rb = 0.0465f * ps * 0.75f, lowest = 9f, centre = 9f;
            NVector3 na = N(anchor), nu = N(u);
            for (int i = 0; i < w.Length; i++)
            {
                if (_arm[i]) continue;
                NVector3 p = new NVector3(w[i].x, w[i].y, w[i].z) - na; float z = NVector3.Dot(p, nu);
                if (z < -0.08f || z > 0.08f) continue;
                float r2 = (p - nu * z).LengthSquared();
                if (r2 < rb * rb && z < lowest) lowest = z;
                if (r2 < rb * rb * 0.25f && z < centre) centre = z;
            }
            // thighs closing in at the sides can reach under the ring; they don't push it further out than a little past her middle
            if (centre < 8f) lowest = Mathf.Max(lowest, centre - 0.006f);
            return lowest > 8f ? -9f : 0.015f * ps + 0.001f - lowest;
        }

        /// <summary>The small ring hanging from the slider on the belt, pulled towards the trapezoid by the V strap, and held off her skin where the hip widens under it.</summary>
        private Transform _lockRing, _lock; private bool _lockReady; private float _la, _lav, _lb, _lbv; private Vector3 _lp, _lv, _lacc;

        /// <summary>
        /// The heart padlock: its ring hangs on the staple's outer bar and can only swing outward, away from the tabs it rests on; the lock hangs on the
        /// ring and swings side to side. Both follow gravity and her movement, springy and damped, so it sways a little as she walks and settles when she stops.
        /// </summary>
        private void HangLock(float ls)
        {
            Vector3 bm = Sock("SOCKET_lock_bar"), xm = Sock("SOCKET_lock_x"), um = Sock("SOCKET_lock_up"), pm = Sock("SOCKET_lock_pivot");
            Vector3 b = Point(_belt, bm, Vector3.zero), tw = (Point(_belt, xm, Vector3.zero) - b).normalized, uw = Vector3.ProjectOnPlane(Point(_belt, um, Vector3.zero) - b, tw).normalized;
            Vector3 tm = (xm - bm).normalized, umm = Vector3.ProjectOnPlane(um - bm, tm).normalized;
            Quaternion rb = Quaternion.LookRotation(Vector3.Cross(tw, uw), uw) * Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(tm, umm), umm));
            float dt = Mathf.Clamp(Time.deltaTime, 0.001f, 0.05f);
            if (!_lockReady) { _lp = b; _lv = Vector3.zero; _lacc = Vector3.zero; }
            Vector3 v = (b - _lp) / dt; _lacc = Vector3.Lerp(_lacc, Vector3.ClampMagnitude((v - _lv) / dt, 20f), 0.3f); _lp = b; _lv = v;
            Vector3 g = new Vector3(0f, -9.81f, 0f) - _lacc * 0.6f;
            Vector3 d0 = rb * (pm - bm).normalized, outW = rb * Vector3.forward, axis1 = Vector3.Cross(d0, outW).normalized;
            float aT = Mathf.Clamp(Vector3.SignedAngle(d0, Vector3.ProjectOnPlane(g, axis1), axis1), 0f, 75f);
            Quaternion q1Rest = Quaternion.identity;
            if (!_lockReady) { _la = aT; _lav = 0f; }
            _lav += (90f * (aT - _la) - 9f * _lav) * dt; _la += _lav * dt;
            if (_la < 0f) { _la = 0f; if (_lav < 0f) _lav = 0f; }   // the tabs stop it swinging back
            if (_la > 75f) { _la = 75f; if (_lav > 0f) _lav = 0f; }
            Quaternion q1 = Quaternion.AngleAxis(_la, axis1);
            _lockRing.SetPositionAndRotation(b, q1 * rb); _lockRing.localScale = Vector3.one * ls;
            Vector3 piv = _lockRing.TransformPoint(pm - bm), rod = q1 * outW, e0 = Vector3.ProjectOnPlane(q1 * (rb * -umm), rod);
            float bT = Mathf.Clamp(Vector3.SignedAngle(e0, Vector3.ProjectOnPlane(g, rod), rod), -50f, 50f);
            if (!_lockReady) { _lb = bT; _lbv = 0f; _lockReady = true; }
            _lbv += (90f * (bT - _lb) - 9f * _lbv) * dt; _lb = Mathf.Clamp(_lb + _lbv * dt, -50f, 50f);
            _lock.SetPositionAndRotation(piv, Quaternion.AngleAxis(_lb, rod) * q1 * rb); _lock.localScale = Vector3.one * ls;
        }

        private void SwingRing(Transform ring, string side, float lift, Quaternion rot, float ls)
        {
            // it hangs on a flat bar: the inner circle rests on the bar's two top corners, or on one corner once it swings, so its centre sits that far below the bar
            Vector3 model = Sock("SOCKET_vstrap_top_" + side) - Sock("SOCKET_chape_" + side);
            Vector3 span = Point(_belt, Sock("SOCKET_span_" + side), Vector3.zero), across = (Point(_belt, Sock("SOCKET_span_x_" + side), Vector3.zero) - span).normalized;
            Vector3 pull = _linkF[3].TransformPoint(Sock("SOCKET_vstrap_bottom_" + side) - Sock("SOCKET_link_f3"));
            Vector3 centre = _belt.position + _belt.rotation * new Vector3(_band.CentreX, 0f, _band.CentreZ), up = _belt.rotation * Vector3.up;
            Vector3 outward = Vector3.ProjectOnPlane(span - centre, up).normalized;
            float rc = RingR * ls, ri = (RingR - RingRod) * ls, w = SpanW * ls;
            Vector3 d = ((pull - span).normalized * (2f * rc) + outward * (lift + 0.0055f * ls)).normalized;
            // it can only tip out as far as the band lets it: past about 12 degrees its rod would cut the band's faces
            float tip = Vector3.Dot(d, outward), most = Mathf.Sin(12f * Mathf.Deg2Rad);
            if (tip > most) d = (Vector3.ProjectOnPlane(d, outward).normalized * Mathf.Cos(12f * Mathf.Deg2Rad) + outward * most).normalized;
            float dt = Mathf.Abs(Vector3.Dot(d, across)), L = -w * dt + Mathf.Sqrt(Mathf.Max(ri * ri - w * w * (1f - dt * dt), 0f));
            Vector3 top = span + d * (L - rc);
            ring.SetPositionAndRotation(top, Quaternion.FromToRotation(rot * model.normalized, d) * rot);
            ring.localScale = Vector3.one * ls;
        }

        private const float RingR = 0.0078f, RingRod = 0.0016f, SpanW = 0.0039f;

        private float RingLift(Vector3[] w, int[] tris, Transform ring, string side, float ls, Vector3 pos, Vector3 up, Vector3 right, Vector3 fwd)
        {
            Vector3 bar = ring.TransformPoint(Sock("SOCKET_vstrap_top_" + side) - Sock("SOCKET_chape_" + side));
            Vector3 c = pos + up * Vector3.Dot(bar - pos, up) + right * _band.CentreX + fwd * _band.CentreZ;
            float px = Vector3.Dot(bar - c, right), pz = Vector3.Dot(bar - c, fwd), rp = Mathf.Sqrt(px * px + pz * pz), rim = 0f;
            float ux = px / rp, uz = pz / rp;
            foreach (var q in Cut(w, tris, bar, up, right, fwd, _arm))
            {
                float qx = q.X + px, qz = q.Y + pz, along = qx * ux + qz * uz;
                if (Mathf.Abs(qx * uz - qz * ux) < 0.012f && along > 0f && along < rp + 0.06f) rim = Mathf.Max(rim, along);
            }
            return rim > 0f ? rim - rp + 0.003f : 0f;
        }

        private readonly System.Text.StringBuilder _gapLog = new System.Text.StringBuilder();

        /// <summary>Diagnostics while cords are logged: how far each Zeta cord stays outside her skin, nearest vertex and its normal, mm (negative = inside).
        /// The last 15 mm at each end are left out, where the cord meets its ring or eye.</summary>
        private void CordGap(Animator anim, Vector3 mid, Vector3 right, Vector3 up)
        {
            var smr = HarnessController.FindBody(anim);
            if (smr == null) return;
            var w = Bake(smr, out int[] tris);
            Vector3[] nl = _bakeMesh.normals; Quaternion r = smr.transform.rotation;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            _gapLog.Append(Time.time.ToString("0.000", ci));
            for (int c = 0; c < 2; c++)
            {
                NVector3[] path = _path[c]; int pn = _pathN[c];
                float len = 0f; for (int j = 0; j + 1 < pn; j++) len += NVector3.Distance(path[j], path[j + 1]);
                float worst = 9f, at = 0f; Vector3 wq = Vector3.zero;
                NVector3 lo = path[0], hi = path[0]; for (int j = 1; j < pn; j++) { lo = NVector3.Min(lo, path[j]); hi = NVector3.Max(hi, path[j]); }
                lo -= new NVector3(0.08f); hi += new NVector3(0.08f);
                var inBox = new bool[w.Length];
                for (int i = 0; i < w.Length; i++) { var q = w[i]; inBox[i] = q.x > lo.X && q.x < hi.X && q.y > lo.Y && q.y < hi.Y && q.z > lo.Z && q.z < hi.Z; }
                var near = new List<int>();
                for (int i = 0; i < tris.Length; i += 3) if (inBox[tris[i]] || inBox[tris[i + 1]] || inBox[tris[i + 2]]) near.Add(i);
                for (int k = 0; k <= 32; k++)
                {
                    float f = k / 32f, along = f * len;
                    if (along < 0.015f || along > len - 0.015f) continue;
                    // the point that far along the bent cord
                    NVector3 p = path[pn - 1]; float run = 0f;
                    for (int j = 0; j + 1 < pn; j++)
                    {
                        float sl = NVector3.Distance(path[j], path[j + 1]);
                        if (along <= run + sl) { p = path[j] + (path[j + 1] - path[j]) * ((along - run) / MathF.Max(sl, 1e-6f)); break; }
                        run += sl;
                    }
                    int best = -1; float bd = 1e9f; NVector3 bq = default;
                    foreach (int i in near)
                    {
                        NVector3 q = ClosestOnTri(p, N(w[tris[i]]), N(w[tris[i + 1]]), N(w[tris[i + 2]])); float d = NVector3.DistanceSquared(p, q);
                        if (d < bd) { bd = d; best = i; bq = q; }
                    }
                    if (best < 0) continue;
                    // the face's own normal, turned to agree with her vertex normals (which point out of her)
                    NVector3 a = N(w[tris[best]]), fn = NVector3.Normalize(NVector3.Cross(N(w[tris[best + 1]]) - a, N(w[tris[best + 2]]) - a));
                    NVector3 vn = N(r * (nl[tris[best]] + nl[tris[best + 1]] + nl[tris[best + 2]]));
                    if (NVector3.Dot(fn, vn) < 0f) fn = -fn;
                    float s = NVector3.Dot(p - bq, fn), g = s < 0f ? -MathF.Sqrt(bd) : MathF.Sqrt(bd);
                    if (g < worst) { worst = g; at = f; wq = new Vector3(bq.X, bq.Y, bq.Z); }
                }
                _gapLog.Append(' ').Append((worst * 1000f).ToString("0.0", ci)).Append('@').Append(at.ToString("0.00", ci)).Append(':').Append((Vector3.Dot(wq - mid, right) * 1000f).ToString("0", ci)).Append(',').Append((Vector3.Dot(wq - mid, up) * 1000f).ToString("0", ci)).Append(',').Append(pn - 2);
            }
            // how well the patch the cords are laid on matches her skin as drawn, mm
            if (_patch != null)
            {
                float err = 0f;
                for (int k = 0; k < _patch.Vert.Length; k++) err = MathF.Max(err, NVector3.DistanceSquared(_patch.World[k], N(w[_patch.Vert[k]])));
                _gapLog.Append(" e").Append((MathF.Sqrt(err) * 1000f).ToString("0.0", ci));
            }
            _gapLog.Append(';');
        }

        private static NVector3 ClosestOnTri(NVector3 p, NVector3 a, NVector3 b, NVector3 c)
        {
            NVector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = NVector3.Dot(ab, ap), d2 = NVector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            NVector3 bp = p - b; float d3 = NVector3.Dot(ab, bp), d4 = NVector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            NVector3 cp = p - c; float d5 = NVector3.Dot(ab, cp), d6 = NVector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float den = 1f / (va + vb + vc); return a + ab * (vb * den) + ac * (vc * den);
        }

        /// <summary>
        /// Sets each Zeta cord's length to this body: where its ring would be with her right thigh at the middle of the walk's swing, and at the end of the
        /// swing that pulls it. From the first the cord starts to pull; at the second its piston is fully out.
        /// </summary>
        private void ZetaFit(Vector3 hip, Vector3 knee, Vector3 eyeF, Vector3 eyeR, float restF, float restR, Quaternion rot, Vector3 up, Vector3 right, float thigh)
        {
            _zetaFitAt = Time.frameCount;
            float len = (knee - hip).magnitude;
            float Pull(string key, Vector3 eye, float rest, float angle)
            {
                Vector3 dir = Quaternion.AngleAxis(angle, right) * -up;
                Quaternion q = Quaternion.FromToRotation(rot * (_hoopR - _hipR).normalized, dir) * rot;
                Vector3 bar = hip + dir * (len * Plugin.HoopAlongThigh.Value) + q * _hoopFitR.Local(U(Group("ring_" + key).Pivot) - _hoopR);
                return ((bar - eye).magnitude - rest) / thigh;
            }
            float fMid = Pull("zback_L", eyeF, restF, WalkMiddle), fEnd = Pull("zback_L", eyeF, restF, WalkBack);
            float rMid = Pull("zfront_L", eyeR, restR, WalkMiddle), rEnd = Pull("zfront_L", eyeR, restR, WalkForward);
            _zetaF.Slack = fMid; _zetaF.FullAt = Mathf.Max(fEnd - fMid, 0.04f); _zetaR.Slack = rMid; _zetaR.FullAt = Mathf.Max(rEnd - rMid, 0.04f);
            float sum = fMid + fEnd + rMid + rEnd;
            if (float.IsNaN(_zetaLogged) || Mathf.Abs(sum - _zetaLogged) > 0.01f)
            {
                _zetaLogged = sum;
                Plugin.Logger.LogInfo("[SFM-ACCEPT] zetafit: front starts at " + fMid.ToString("0.000") + ", full after " + _zetaF.FullAt.ToString("0.000") + "; rear starts at " + rMid.ToString("0.000") + ", full after " + _zetaR.FullAt.ToString("0.000"));
            }
        }

        /// <summary>
        /// Sets each Zeta cord's length from her own walk. Only while both thighs are within a walking swing (not raised, as in a crouch), and only from a
        /// stretch in which the cords really moved; standing still teaches nothing and changes nothing.
        /// </summary>
        private void ZetaLearn(float pullF, float pullR, bool walkingPose)
        {
            float now = Time.time;
            if (!walkingPose) { _learnOpen = false; return; }
            if (!_learnOpen) { _learnOpen = true; _learnEnd = now + LearnWindow; _loF = _hiF = pullF; _loR = _hiR = pullR; return; }
            if (pullF < _loF) _loF = pullF; if (pullF > _hiF) _hiF = pullF; if (pullR < _loR) _loR = pullR; if (pullR > _hiR) _hiR = pullR;
            if (now < _learnEnd) return;
            _learnOpen = false;
            float rf = _hiF - _loF, rr = _hiR - _loR;
            if (rf < LearnLeast || rr < LearnLeast) return;
            float k = _zetaLearned ? 0.5f : 1f;   // the first stretch is taken whole, later ones half and half
            float sF = _loF + rf * LearnStart, fF = rf * (LearnFull - LearnStart), sR = _loR + rr * LearnStart, fR = rr * (LearnFull - LearnStart);
            _zetaF.Slack += (sF - _zetaF.Slack) * k; _zetaF.FullAt += (fF - _zetaF.FullAt) * k; _zetaR.Slack += (sR - _zetaR.Slack) * k; _zetaR.FullAt += (fR - _zetaR.FullAt) * k;
            if (!_zetaLearned) Plugin.Logger.LogInfo("[SFM-ACCEPT] zetalearn: front starts at " + _zetaF.Slack.ToString("0.000") + ", full after " + _zetaF.FullAt.ToString("0.000") + "; rear starts at " + _zetaR.Slack.ToString("0.000") + ", full after " + _zetaR.FullAt.ToString("0.000"));
            _zetaLearned = true;
        }

        private Vector3 RestBar(string key, Vector3 hip, Vector3 knee, Vector3 modelHip, Vector3 modelHoop, Hoop h, Quaternion rot, Vector3 up)
        {
            float len = (knee - hip).magnitude;
            Quaternion q = Quaternion.FromToRotation(rot * (modelHoop - modelHip).normalized, -up) * rot;
            return hip - up * (len * Plugin.HoopAlongThigh.Value) + q * h.Local(U(Group("ring_" + key).Pivot) - modelHoop);
        }

        private Vector3 Bar(string key, Transform node, Hoop h, Vector3 hoop)
        {
            HGroup g = Group("ring_" + key);
            return node.TransformPoint(h.Local(U(g.Pivot) - hoop));
        }

        /// <summary>Swings a ring about its bar to face its pull. Returns the point where the cord bears on it.</summary>
        private Vector3 Swing(string key, string seat, Vector3 bar, Vector3 pull, Transform node, float scale)
        {
            Transform t = _own["ring_" + key];
            HGroup g = Group("ring_" + key);
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

        private void PlaceThigh(Transform node, Hoop h, Vector3 hip, Vector3 knee, Vector3 modelHip, Vector3 modelHoop, Quaternion rot)
        {
            if (h.Band != null) node.SetPositionAndRotation(h.Bone.position + h.Bone.rotation * h.LP, h.Bone.rotation * h.LQ);   // rides her thigh bone, twist and all
            else node.SetPositionAndRotation(hip + (knee - hip) * Plugin.HoopAlongThigh.Value, ThighTurn(hip, knee, modelHip, modelHoop, rot));
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
            _owned.Clear(); _own.Clear(); _stretches.Clear(); _gammaOnly.Clear(); _zetaOnly.Clear(); _patch = null; _patchFailed = false; _anchorF = null; _band = null; if (_markers != null) UnityEngine.Object.Destroy(_markers); _markers = null; _hoopFitL.Band = null; _hoopFitR.Band = null; _hoopFitL.Fixed.Clear(); _hoopFitR.Fixed.Clear(); _hoopFitL.Bone = _hoopFitR.Bone = null;
            _visible = false;
        }
    }
}
