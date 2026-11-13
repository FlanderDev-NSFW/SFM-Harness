using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Scripts.InGame;
using ExposureUnnoticed2.Scripts.Sound;
using Harness.Logic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using RestraintHub;
using SfmKit.Game;
using SfmKit.Pure;
using UnityEngine;

namespace Harness
{
    /// <summary>
    /// The harness's state, ticked every frame. Bridge diagnostics (session started with --allow-invoke):
    /// game call Harness.HarnessController PutOnNow, game call Harness.HarnessController MeasureNow, game get Harness.HarnessController::Status.
    /// </summary>
    public sealed class HarnessController : MonoBehaviour
    {
        internal static HarnessController Instance;
        public HarnessController(IntPtr pointer) : base(pointer) { }

        internal readonly LockMachine Machine = new LockMachine();
        public static string Status = "off";

        private static int _failures;
        private HarnessFile _file;
        private HarnessRig _rig;
        private Camera.CameraCallback _preCull;
        private int _placedFrame = -1;

        internal static void Fail(string where, Exception e)
        {
            if (_failures++ < 20) Plugin.Logger.LogWarning(where + " failed: " + e);
        }

        private void Awake() => Instance = this;

        private void Start()
        {
            _preCull = DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new Action<Camera>(OnPreCull));
            Camera.onPreCull = Camera.onPreCull == null
                ? _preCull
                : Il2CppSystem.Delegate.Combine(Camera.onPreCull, _preCull).Cast<Camera.CameraCallback>();
        }

        private void OnDestroy()
        {
            if (_preCull != null && Camera.onPreCull != null)
                Camera.onPreCull = Il2CppSystem.Delegate.Remove(Camera.onPreCull, _preCull)?.Cast<Camera.CameraCallback>();
            _rig?.Destroy();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            Perf.Frame(Time.unscaledDeltaTime);
            try { Tick(); }
            catch (Exception e) { Fail("Update", e); }
        }

        [HideFromIl2Cpp]
        private void Tick()
        {
            if (PlayerController.Instance == null || GameState.GameStateData == null) return;
            Machine.AttemptSeconds = Plugin.AttemptSeconds.Value;
            Machine.BaseChance = Plugin.SuccessChance.Value;
            bool struggling = Plugin.Entry.StruggleHeld && Machine.State == LockState.Locked;
            switch (Machine.Tick(Time.deltaTime, struggling, UnityEngine.Random.value))
            {
                case LockMachine.Event.Locked: GameUi.PlaySe(InGameSoundManager.SeType.AttachHandcuffs); Plugin.Logger.LogInfo("[SFM-ACCEPT] lock: locked"); break;
                case LockMachine.Event.Unlocking: GameUi.PlaySe(InGameSoundManager.SeType.Unlock); break;
                case LockMachine.Event.Unlocked: Plugin.Logger.LogInfo("[SFM-ACCEPT] release: off"); break;
            }
            if (Time.frameCount % 30 == 0) Status = Machine.State + (_rig == null ? "" : ", " + _rig.Status);
        }

        // Measuring needs her arms out of the way, so the rig moves them for one moment here, before skinning; nothing of that is drawn.
        private void LateUpdate()
        {
            try
            {
                if (!Machine.Worn || _rig == null || !_rig.Built || PlayerController.Instance == null) return;
                var anim = PlayerController.Instance.Pca == null ? null : PlayerController.Instance.Pca.Animator;
                long t = Perf.Now;
                if (anim != null) _rig.Survey(anim);
                Perf.Add(Perf.Survey, t);
            }
            catch (Exception e) { Fail("LateUpdate", e); }
        }

        private void OnPreCull(Camera cam)
        {
            try
            {
                if (_placedFrame == Time.frameCount) return;
                _placedFrame = Time.frameCount;
                bool show = Machine.Worn && PlayerController.Instance != null;
                if (!show) { _rig?.SetVisible(false); return; }
                var anim = PlayerController.Instance.Pca == null ? null : PlayerController.Instance.Pca.Animator;
                if (anim == null) return;
                EnsureRig(anim);
                _rig.SetVisible(true);
                long t = Perf.Now;
                _rig.Place(anim);
                Perf.Add(Perf.Place, t);
            }
            catch (Exception e) { Fail("onPreCull", e); }
        }

        [HideFromIl2Cpp]
        private void EnsureRig(Animator anim)
        {
            if (_rig != null && _rig.Built) return;
            if (_file == null)
            {
                using Stream s = typeof(Plugin).Assembly.GetManifestResourceStream("Harness.assets.harness.hrn") ?? throw new FileNotFoundException("embedded model");
                _file = HarnessFile.Read(s);
            }
            _rig = new HarnessRig(_file);
            _rig.Build(anim.gameObject.layer);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] rig: built, " + _file.TriangleCount() + " triangles");
        }


        [HideFromIl2Cpp]
        internal string PutOn()
        {
            if (!Machine.RequestLock()) return "it is already on";
            GameUi.PlaySe(InGameSoundManager.SeType.CommonEquip);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] ring: put on");
            return null;
        }

        [HideFromIl2Cpp]
        internal void TakeOff()
        {
            if (Machine.Worn) Plugin.Logger.LogInfo("[SFM-ACCEPT] release: taken off");
            Machine.Reset();
        }

        [HideFromIl2Cpp] internal void OnSaveLoaded(bool locked) => Machine.Restore(locked, 0f);

        [HideFromIl2Cpp] internal void OnNewGame() => Machine.Reset();

        public static string PutOnNow() => Instance == null ? "no controller" : Instance.PutOn() ?? Instance.Machine.State.ToString();

        public static string Stroke(string v) { HarnessRig.ForcedStroke = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture); return "stroke " + v; }

        public static string GapNow() { if (Instance?._rig == null) return "no rig"; Instance._rig.WantGap = true; return "gap report on the next frame"; }

        public static string RefitNow() { if (Instance?._rig == null) return "no rig"; Instance._rig.Refit_Request(); return "refit next frame"; }

        public static string CordLog(string seconds) { if (Instance?._rig == null) return "no rig"; Instance._rig.CordLogUntil = Time.time + float.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture); return "logging cords"; }

        /// <summary>Diagnostics. Zeta's slack and full pull for the front, then the rear piston, as shares of her thigh: "-0.08,0.12,-0.03,0.16".</summary>
        public static string ZetaTune(string v)
        {
            if (Instance?._rig == null) return "no rig";
            var p = v.Split(','); var ci = System.Globalization.CultureInfo.InvariantCulture;
            Instance._rig.TuneZeta(float.Parse(p[0], ci), float.Parse(p[1], ci), float.Parse(p[2], ci), float.Parse(p[3], ci)); return "zeta " + v;
        }

        /// <summary>Diagnostics. 0 draws Zeta's cords straight, 1 lays them on her skin again.</summary>
        public static string WrapNow(string on) { HarnessRig.WrapOn = on == "1"; return "wrap " + on; }

        public static string ModeNow(string m) { Plugin.Mode.Value = m == "gamma" ? DriveMode.Gamma : DriveMode.Zeta; return "mode " + Plugin.Mode.Value; }

        public static string PerfNow(string on) { Perf.On = on == "1"; return "perf " + on; }

        public static string MarkersNow(string on) { if (Instance?._rig == null) return "no rig"; HarnessRig.ShowMarkers = on == "1"; Instance._rig.WantGap = true; return "markers " + on; }

        /// <summary>Diagnostics. Near clip in metres, so an arm in front of the waist can be cut out of a shot; 0 restores it.</summary>
        public static string NearNow(string v)
        {
            float n = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
            if (Camera.main == null) return "no camera";
            if (_near < 0f) _near = Camera.main.nearClipPlane;
            Camera.main.nearClipPlane = n > 0f ? n : _near;
            return "near " + Camera.main.nearClipPlane;
        }
        private static float _near = -1f;

        public static string HideNow(string on) { Instance?._rig?.Hide(on == "1"); return "hidden " + on; }

        public static string GapWatch(string seconds) { if (Instance?._rig == null) return "no rig"; Instance._rig.WatchUntil = Time.time + float.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture); return "watching"; }

        public static string ProbeNow() { HarnessRig.Probe = true; return "probe"; }

        public static string TakeOffNow() { Instance?.TakeOff(); return "off"; }

        /// <summary>Slices her body at many heights and logs the sizes: what the fit has to work from.</summary>
        public static string MeasureNow()
        {
            try
            {
                var anim = PlayerController.Instance.Pca.Animator;
                Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
                Vector3 hl = B(HumanBodyBones.LeftUpperLeg).position, hr = B(HumanBodyBones.RightUpperLeg).position, mid = (hl + hr) * 0.5f;
                Vector3 right = (hr - hl).normalized, up = Vector3.ProjectOnPlane(B(HumanBodyBones.Spine).position - mid, right).normalized, fwd = Vector3.Cross(right, up);
                var smr = FindBody(anim);
                if (smr == null) return "no body";
                var mesh = new Mesh();
                smr.BakeMesh(mesh, true);
                Transform t = smr.transform;
                Vector3[] raw = mesh.vertices;
                var world = new Vector3[raw.Length];
                for (int i = 0; i < raw.Length; i++) world[i] = t.position + t.rotation * Vector3.Scale(t.lossyScale, raw[i]);
                UnityEngine.Object.Destroy(mesh);
                var sb = new StringBuilder();
                sb.Append("[SFM-ACCEPT] measure: scale ").Append(anim.transform.lossyScale.x.ToString("0.000")).Append(" body ").Append(t.lossyScale.x.ToString("0.000"))
                  .Append(" hipJoints ").Append((hr - hl).magnitude.ToString("0.000"))
                  .Append(" thighL ").Append((B(HumanBodyBones.LeftLowerLeg).position - hl).magnitude.ToString("0.000"))
                  .Append(" hipsAboveFeet ").Append((mid.y - B(HumanBodyBones.LeftFoot).position.y).ToString("0.000"))
                  .Append(" spine ").Append((B(HumanBodyBones.Spine).position - mid).magnitude.ToString("0.000"))
                  .Append(" chest ").Append((B(HumanBodyBones.Chest).position - mid).magnitude.ToString("0.000"));
                Plugin.Logger.LogInfo(sb.ToString());
                for (float h = -0.14f; h <= 0.401f; h += 0.03f) Plugin.Logger.LogInfo("[SFM-ACCEPT] torso " + h.ToString("+0.00;-0.00") + " " + Slice(world, mid + up * h, up, right, fwd, 0.015f, 0.05f));
                foreach (int side in new[] { 0, 1 })
                {
                    Vector3 hip = side == 0 ? hl : hr, knee = B(side == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg).position, d = (knee - hip).normalized;
                    Vector3 sr = Vector3.ProjectOnPlane(right, d).normalized, sf = Vector3.Cross(sr, d);
                    for (float f = 0.2f; f < 1f; f += 0.1f) Plugin.Logger.LogInfo("[SFM-ACCEPT] thigh" + (side == 0 ? "L " : "R ") + f.ToString("0.0") + " " + SliceNear(world, hip + (knee - hip) * f, d, sr, sf, 0.012f, 0.13f));
                }
                return "measured " + raw.Length + " vertices";
            }
            catch (Exception e) { return "failed: " + e.Message; }
        }

        [HideFromIl2Cpp]
        internal static SkinnedMeshRenderer FindBody(Animator anim)
        {
            Transform b = anim.transform.Find("Selestia_body");
            return b == null ? null : b.GetComponent<SkinnedMeshRenderer>();
        }

        [HideFromIl2Cpp]
        private static string Slice(Vector3[] v, Vector3 at, Vector3 n, Vector3 a, Vector3 b, float half, float gap)
        {
            var xs = new List<float>(); var zs = new List<float>();
            foreach (var p in v)
            {
                Vector3 q = p - at;
                if (Mathf.Abs(Vector3.Dot(q, n)) > half) continue;
                xs.Add(Vector3.Dot(q, a));
            }
            var sx = SliceStats.CentralSpan(xs, gap);
            foreach (var p in v)
            {
                Vector3 q = p - at;
                if (Mathf.Abs(Vector3.Dot(q, n)) > half) continue;
                float x = Vector3.Dot(q, a);
                if (x >= sx.Min && x <= sx.Max) zs.Add(Vector3.Dot(q, b));
            }
            var sz = SliceStats.CentralSpan(zs, gap);
            return "width " + (sx.Max - sx.Min).ToString("0.000") + " depth " + (sz.Max - sz.Min).ToString("0.000") + " front " + sz.Max.ToString("0.000") + " back " + sz.Min.ToString("0.000") + " x " + ((sx.Max + sx.Min) / 2).ToString("0.000") + " n " + xs.Count;
        }

        [HideFromIl2Cpp]
        private static string SliceNear(Vector3[] v, Vector3 at, Vector3 n, Vector3 a, Vector3 b, float half, float radius)
        {
            float ax0 = 9, ax1 = -9, bz0 = 9, bz1 = -9; int c = 0;
            foreach (var p in v)
            {
                Vector3 q = p - at;
                if (Mathf.Abs(Vector3.Dot(q, n)) > half) continue;
                float x = Vector3.Dot(q, a), z = Vector3.Dot(q, b);
                if (x * x + z * z > radius * radius) continue;
                ax0 = Mathf.Min(ax0, x); ax1 = Mathf.Max(ax1, x); bz0 = Mathf.Min(bz0, z); bz1 = Mathf.Max(bz1, z); c++;
            }
            return "across " + (ax1 - ax0).ToString("0.000") + " deep " + (bz1 - bz0).ToString("0.000") + " centre " + ((ax0 + ax1) / 2).ToString("0.000") + "," + ((bz0 + bz1) / 2).ToString("0.000") + " n " + c;
        }
    }
}
