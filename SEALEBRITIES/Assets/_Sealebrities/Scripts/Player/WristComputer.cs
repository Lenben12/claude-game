using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Q40 wrist dive computer on the LEFT wrist of the first-person view model (drawn by the view-model overlay camera,
    /// layer 30 – PlayerViewModelCamera moves every renderer below the main camera there).
    ///
    /// Round case with an orange bezel on a dark strap around the glove sleeve; a self-lit segment display:
    ///  * depth in big digits ("12 m"),
    ///  * the gear's safe depth bottom-left ("▼15") – orange when exceeded, blinking red more than 5 m below it,
    ///  * the distance to the boat bottom-right ("34 m") and a ring arrow on the bezel pointing at it – yellow for the
    ///    boat, turquoise for the dock (when the boat is more than 600 m away or not in the world).
    /// Shown under water and while swimming at the surface (PlayerHands decides). The face turns around the forearm
    /// toward the eye and the digits stay upright on screen, so it is readable in every hand pose (empty hand: a
    /// "glance at the wrist" rest pose; lamp hand: on the lamp wrist). Size ≤ 150 × 120 px at 1080p.
    /// Built once; per frame only transforms move and meshes/materials are swapped when a value changes – no allocations.
    public sealed class WristComputer
    {
        // ---- layout (metres, mount space: +X along the forearm toward the hand, +Y = face normal, +Z = 12 o'clock)
        /// Mount position in the left mitten's palm space (palm centre at the origin, fingers +Z): on the sleeve right
        /// behind the cuff.
        public static readonly Vector3 WristLocal = new Vector3(0f, 0f, -0.105f);
        const float StrapRadius = 0.0355f, StrapTube = 0.0045f;
        const float FaceY = 0.0472f, DisplayR = 0.0232f;
        const float BigH = 0.0118f, SmallH = 0.0066f;
        const float BigZ = 0.0046f, SmallZ = -0.0105f;
        const float BoatRange = 600f;

        static readonly Color CaseC = LowPoly.Hex("#3E4359");
        static readonly Color BezelC = LowPoly.Hex("#FF8830");
        static readonly Color StrapC = LowPoly.Hex("#2B2533");
        static readonly Color ButtonC = LowPoly.Hex("#C1C7CE");
        static readonly Color DisplayC = LowPoly.Hex("#151A26");
        static readonly Color GhostC = LowPoly.Hex("#222A3B");
        static readonly Color DigitC = LowPoly.Hex("#E8FFF7");
        static readonly Color MaxC = LowPoly.Hex("#9FB4C8");
        static readonly Color WarnC = LowPoly.Hex("#FF9A3C");
        static readonly Color DangerC = LowPoly.Hex("#FF4A4A");
        static readonly Color DangerDimC = LowPoly.Hex("#5A2430");
        static readonly Color BoatC = LowPoly.Hex("#FFD350");
        static readonly Color DockC = LowPoly.Hex("#5FD6E2");

        // ---- segment meshes (shared, rebuilt after a domain reset)
        const int SegA = 1, SegB = 2, SegC = 4, SegD = 8, SegE = 16, SegF = 32, SegG = 64;
        static readonly int[] DigitMasks =
        {
            SegA | SegB | SegC | SegD | SegE | SegF, SegB | SegC, SegA | SegB | SegG | SegE | SegD, SegA | SegB | SegG | SegC | SegD,
            SegF | SegG | SegB | SegC, SegA | SegF | SegG | SegC | SegD, SegA | SegF | SegG | SegE | SegD | SegC, SegA | SegB | SegC,
            127, SegA | SegB | SegC | SegD | SegF | SegG
        };
        static Mesh[] digits;
        static Mesh dashMesh, ghostMesh, unitMesh, maxMesh, arrowMesh, discMesh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { digits = null; dashMesh = ghostMesh = unitMesh = maxMesh = arrowMesh = discMesh = null; }

        public Transform Mount { get; }
        readonly Transform dial, arrow;
        readonly MeshFilter[] bigF = new MeshFilter[3], maxF = new MeshFilter[3], distF = new MeshFilter[3];
        readonly MeshRenderer[] bigR = new MeshRenderer[3], maxR = new MeshRenderer[3], distR = new MeshRenderer[3];
        readonly MeshRenderer maxMark, arrowR, distUnit;
        readonly Material matMax, matWarn, matDanger, matDangerDim, matBoat, matDock;
        int shownDepth = -2, shownMax = -2, shownDist = -2, shownWarn = -1, shownTarget = -1;
        bool visible = true;
        float arrowAngle;
        bool arrowValid;

        public WristComputer(Transform palm)
        {
            EnsureMeshes();
            Mount = DiverMeshes.Node(palm, "WristComputer", WristLocal);

            Material caseM = PlayerHands.ViewModelMat(LowPoly.Mat(CaseC));
            Material bezel = PlayerHands.ViewModelMat(LowPoly.Mat(BezelC));
            Material strap = PlayerHands.ViewModelMat(LowPoly.Mat(StrapC));
            Material button = PlayerHands.ViewModelMat(LowPoly.Mat(ButtonC));
            Material display = PlayerHands.ViewModelMat(MaterialLibrary.Unlit(DisplayC));
            Material ghost = PlayerHands.ViewModelMat(MaterialLibrary.Unlit(GhostC));
            Material digit = PlayerHands.ViewModelMat(MaterialLibrary.Unlit(DigitC));
            matMax = PlayerHands.ViewModelMat(MaterialLibrary.Unlit(MaxC));
            matWarn = PlayerHands.ViewModelMat(MaterialLibrary.Unlit(WarnC));
            matDanger = PlayerHands.ViewModelMat(MaterialLibrary.Unlit(DangerC));
            matDangerDim = PlayerHands.ViewModelMat(MaterialLibrary.Unlit(DangerDimC));
            matBoat = PlayerHands.ViewModelMat(MaterialLibrary.Unlit(BoatC));
            matDock = PlayerHands.ViewModelMat(MaterialLibrary.Unlit(DockC));

            // strap: a wide band around the forearm (ring axis = mount X)
            Part(Mount, "Strap", DiverMeshes.Torus(StrapRadius, StrapTube, 28, 8), strap, Vector3.zero, new Vector3(1f, 3.1f, 1f), new Vector3(0f, 0f, 90f));
            // case: soft puck with a rim, orange bezel ring, two push buttons at 4 and 8 o'clock
            var prof = new[]
            {
                new Vector2(0f, 0.0335f), new Vector2(0.024f, 0.0335f), new Vector2(0.0285f, 0.0356f), new Vector2(0.030f, 0.0400f),
                new Vector2(0.0292f, 0.0448f), new Vector2(0.0262f, 0.0466f), new Vector2(0f, 0.0466f)
            };
            Part(Mount, "Case", DiverMeshes.Cached("wrist_case", () => DiverMeshes.LatheY(prof, 28)), caseM, Vector3.zero, Vector3.one, default);
            Part(Mount, "Bezel", DiverMeshes.Torus(0.0267f, 0.0034f, 32, 8), bezel, new Vector3(0f, 0.0464f, 0f), Vector3.one, default);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 dir = new Vector3(s * 0.866f, 0f, -0.5f);
                var b = Part(Mount, "Button", DiverMeshes.Capsule(0.0034f, 0.0075f, 12), button, dir * 0.0288f + new Vector3(0f, 0.0405f, 0f), Vector3.one, default);
                b.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
            }
            Part(Mount, "Display", discMesh, display, new Vector3(0f, FaceY, 0f), Vector3.one * DisplayR, default);

            // dial: everything on the display that turns to stay upright on screen
            dial = DiverMeshes.Node(Mount, "Dial", Vector3.zero);
            float pitch = BigH * 0.71f;
            float x0 = -pitch * 1.25f;
            for (int i = 0; i < 3; i++)
                Slot(dial, "Big" + i, new Vector3(x0 + i * pitch, FaceY, BigZ), BigH, ghost, digit, out bigF[i], out bigR[i]);
            float unitH = SmallH * 0.95f;
            Part(dial, "Unit", unitMesh, digit, new Vector3(x0 + 2f * pitch + BigH * 0.28f + unitH * 0.55f, FaceY + 0.0002f, BigZ - BigH * 0.5f + unitH * 0.5f), Vector3.one * unitH, default);

            float sp = SmallH * 0.73f;
            maxMark = Part(dial, "MaxMark", maxMesh, matMax, new Vector3(-0.0166f, FaceY + 0.0002f, SmallZ), Vector3.one * SmallH * 0.72f, default);
            for (int i = 0; i < 3; i++)
                Slot(dial, "Max" + i, new Vector3(-0.0124f + i * sp, FaceY, SmallZ), SmallH, ghost, matMax, out maxF[i], out maxR[i]);
            for (int i = 0; i < 3; i++)
                Slot(dial, "Dist" + i, new Vector3(0.0028f + i * sp, FaceY, SmallZ), SmallH, ghost, matBoat, out distF[i], out distR[i]);
            distUnit = Part(dial, "DistUnit", unitMesh, matBoat, new Vector3(0.0028f + 2f * sp + SmallH * 0.28f + unitH * 0.42f, FaceY + 0.0002f, SmallZ), Vector3.one * unitH * 0.8f, default);

            // ring arrow riding on the bezel
            arrow = DiverMeshes.Node(dial, "Arrow", Vector3.zero);
            arrowR = Part(arrow, "Pointer", arrowMesh, matBoat, new Vector3(0f, 0.0503f, 0.0262f), Vector3.one * 0.0085f, default);

            SetVisible(false);
            PlayerViewModelCamera.MarkDirty();
        }

        static MeshRenderer Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Vector3 scale, Vector3 euler)
        {
            var r = DiverMeshes.Part(parent, name, mesh, mat, pos, scale, euler, false);
            r.receiveShadows = false;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return r;
        }

        /// One LCD digit slot: a faint "8" behind, the live digit on top.
        static void Slot(Transform parent, string name, Vector3 pos, float h, Material ghost, Material mat, out MeshFilter f, out MeshRenderer r)
        {
            Part(parent, name + "Ghost", ghostMesh, ghost, pos, Vector3.one * h, default);
            r = Part(parent, name, digits[8], mat, pos + new Vector3(0f, 0.0002f, 0f), Vector3.one * h, default);
            f = r.GetComponent<MeshFilter>();
            r.enabled = false;
        }

        void SetVisible(bool v)
        {
            if (visible == v) return;
            visible = v;
            if (Mount.gameObject.activeSelf != v) Mount.gameObject.SetActive(v);
            if (v) arrowValid = false;
        }

        // ------------------------------------------------------------------ per frame
        /// show = the left hand shows a pose with the watch on it, in or on the water (decided by PlayerHands).
        public void Tick(PlayerController pc, Transform camT, Transform handRoot, bool show, float dt)
        {
            SetVisible(show && pc != null && camT != null && handRoot != null);
            if (!visible) return;
            Orient(camT, handRoot);
            UpdateDepth(pc);
            UpdateTarget(pc, camT, dt);
        }

        /// Turns the face around the forearm toward the eye (a strap can slide round a wrist) and keeps the digits
        /// upright on screen.
        void Orient(Transform camT, Transform handRoot)
        {
            Vector3 a = handRoot.forward;
            Vector3 toEye = camT.position - Mount.position;
            Vector3 n = toEye - Vector3.Dot(toEye, a) * a;
            Vector3 back = handRoot.up - Vector3.Dot(handRoot.up, a) * a;
            if (n.sqrMagnitude < 1e-10f) n = back;
            if (n.sqrMagnitude < 1e-10f) return;
            n = n.normalized * 0.85f + (back.sqrMagnitude > 1e-10f ? back.normalized * 0.15f : Vector3.zero);
            if (n.sqrMagnitude < 1e-10f) return;
            n.Normalize();
            Vector3 fwd = Vector3.Cross(a, n);
            if (fwd.sqrMagnitude < 1e-10f) return;
            Mount.rotation = Quaternion.LookRotation(fwd, n);
            Vector3 up = Mount.InverseTransformDirection(camT.up);
            float phi = Mathf.Atan2(up.x, up.z) * Mathf.Rad2Deg;
            dial.localRotation = Quaternion.Euler(0f, phi, 0f);
        }

        void UpdateDepth(PlayerController pc)
        {
            float depthM = pc.Depth;
            int d = Mathf.Clamp(Mathf.RoundToInt(depthM), 0, 999);
            if (d != shownDepth)
            {
                shownDepth = d;
                SetNumber(bigF, bigR, d, false);
            }

            float maxSafe = pc.MaxSafeDepth;
            int m = maxSafe > 900f || float.IsNaN(maxSafe) ? -1 : Mathf.Clamp(Mathf.RoundToInt(maxSafe), 0, 999);
            if (m != shownMax)
            {
                shownMax = m;
                SetNumber(maxF, maxR, m, true);
            }

            // 0 fine, 1 exceeded (orange), 2/3 more than 5 m below the rating (red, blinking 2 Hz)
            int warn = 0;
            if (m >= 0 && pc.IsUnderwater)
            {
                float over = depthM - maxSafe;
                if (over > 5f) warn = Mathf.Repeat(Time.unscaledTime * 2f, 1f) < 0.62f ? 2 : 3;
                else if (over > 0f) warn = 1;
            }
            if (warn != shownWarn)
            {
                shownWarn = warn;
                Material mat = warn == 0 ? matMax : warn == 1 ? matWarn : warn == 2 ? matDanger : matDangerDim;
                maxMark.sharedMaterial = mat;
                for (int i = 0; i < 3; i++) maxR[i].sharedMaterial = mat;
            }
        }

        void UpdateTarget(PlayerController pc, Transform camT, float dt)
        {
            Vector3 me = pc.transform.position;
            bool boat = TryBoat(me, out Vector3 target);
            if (!boat) target = DockTarget();
            float dx = target.x - me.x, dz = target.z - me.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            int di = Mathf.Clamp(Mathf.RoundToInt(dist), 0, 999);
            if (di != shownDist)
            {
                shownDist = di;
                SetNumber(distF, distR, di, false);
            }
            int tgt = boat ? 1 : 0;
            if (tgt != shownTarget)
            {
                shownTarget = tgt;
                Material mat = boat ? matBoat : matDock;
                arrowR.sharedMaterial = mat;
                distUnit.sharedMaterial = mat;
                for (int i = 0; i < 3; i++) distR[i].sharedMaterial = mat;
            }

            // bearing relative to where the player looks: 12 o'clock = straight ahead
            Vector3 f = camT.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-6f) f = pc.transform.forward;
            float bearing = dist < 0.5f ? arrowAngle : Vector3.SignedAngle(f, new Vector3(dx, 0f, dz), Vector3.up);
            arrowAngle = arrowValid ? Mathf.LerpAngle(arrowAngle, bearing, 1f - Mathf.Exp(-14f * dt)) : bearing;
            arrowValid = true;
            arrow.localRotation = Quaternion.Euler(0f, arrowAngle, 0f);
        }

        static bool TryBoat(Vector3 me, out Vector3 pos)
        {
            pos = Vector3.zero;
            bool have = false;
            if (BoatTelemetry.Valid)
            {
                pos = BoatTelemetry.Position;
                have = true;
            }
            else
            {
                var b = Refs.Boat;
                if (PlayerController.Alive(b))
                {
                    var r = b.Root;
                    if (r != null) { pos = r.position; have = true; }
                }
            }
            if (!have) return false;
            float dx = pos.x - me.x, dz = pos.z - me.z;
            return dx * dx + dz * dz <= BoatRange * BoatRange;
        }

        static Vector3 DockTarget()
        {
            Vector3 d = WorldInfo.DockEndPosition;
            return d != Vector3.zero ? d : WorldLayout.DockEnd;
        }

        /// value &lt; 0 shows "--". leftAlign = digits start in the first slot (safe depth), otherwise they fill the
        /// slots from the right (depth, distance).
        void SetNumber(MeshFilter[] f, MeshRenderer[] r, int value, bool leftAlign)
        {
            if (value < 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    bool on = leftAlign ? i < 2 : i > 0;
                    r[i].enabled = on;
                    if (on) f[i].sharedMesh = dashMesh;
                }
                return;
            }
            int count = value >= 100 ? 3 : value >= 10 ? 2 : 1;
            int first = leftAlign ? 0 : 3 - count;
            int v = value;
            for (int i = 2; i >= 0; i--)
            {
                bool on = i >= first && i < first + count;
                r[i].enabled = on;
            }
            for (int k = count - 1; k >= 0; k--)
            {
                int slot = first + k;
                f[slot].sharedMesh = digits[v % 10];
                v /= 10;
            }
        }

        // ------------------------------------------------------------------ meshes (flat, facing +Y)
        static void EnsureMeshes()
        {
            if (digits != null && digits[0] != null && discMesh != null) return;
            digits = new Mesh[10];
            for (int i = 0; i < 10; i++) digits[i] = SegMesh(DigitMasks[i], "SB_Wrist_D" + i);
            dashMesh = SegMesh(SegG, "SB_Wrist_Dash");
            ghostMesh = SegMesh(127, "SB_Wrist_Ghost");

            var v = new List<Vector3>(32);
            var t = new List<int>(48);
            // "m": top bar and three legs (lower-case cell: z −0.5 … 0.15)
            AddSeg(v, t, new Vector2(-0.3f, 0.15f), new Vector2(0.3f, 0.15f), 0.075f, 0f);
            AddSeg(v, t, new Vector2(-0.3f, 0.15f), new Vector2(-0.3f, -0.5f), 0.075f, 0f);
            AddSeg(v, t, new Vector2(0f, 0.15f), new Vector2(0f, -0.5f), 0.075f, 0f);
            AddSeg(v, t, new Vector2(0.3f, 0.15f), new Vector2(0.3f, -0.5f), 0.075f, 0f);
            unitMesh = Finish("SB_Wrist_Unit", v, t);

            v.Clear(); t.Clear();
            V(v, -0.45f, 0.4f); V(v, 0.45f, 0.4f); V(v, 0f, -0.42f);
            t.Add(0); t.Add(1); t.Add(2);
            maxMesh = Finish("SB_Wrist_Max", v, t);

            v.Clear(); t.Clear();
            V(v, 0f, 0.5f); V(v, 0.38f, -0.28f); V(v, 0f, -0.06f); V(v, -0.38f, -0.28f);
            t.Add(0); t.Add(1); t.Add(2); t.Add(0); t.Add(2); t.Add(3);
            arrowMesh = Finish("SB_Wrist_Arrow", v, t);

            v.Clear(); t.Clear();
            const int n = 32;
            V(v, 0f, 0f);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                V(v, Mathf.Cos(a), Mathf.Sin(a));
            }
            for (int i = 0; i < n; i++) { t.Add(0); t.Add(1 + i); t.Add(1 + (i + 1) % n); }
            discMesh = Finish("SB_Wrist_Disc", v, t);
        }

        /// 7-segment digit in a unit cell (height 1, width 0.56), centred, in the XZ plane (+Z up, +X right).
        static Mesh SegMesh(int mask, string name)
        {
            const float W = 0.56f, T = 0.15f;
            float x = W * 0.5f - T * 0.5f, z = 0.5f - T * 0.5f, h = T * 0.5f, gap = 0.028f;
            var v = new List<Vector3>(48);
            var t = new List<int>(72);
            if ((mask & SegA) != 0) AddSeg(v, t, new Vector2(-x, z), new Vector2(x, z), h, gap);
            if ((mask & SegB) != 0) AddSeg(v, t, new Vector2(x, z), new Vector2(x, 0f), h, gap);
            if ((mask & SegC) != 0) AddSeg(v, t, new Vector2(x, 0f), new Vector2(x, -z), h, gap);
            if ((mask & SegD) != 0) AddSeg(v, t, new Vector2(-x, -z), new Vector2(x, -z), h, gap);
            if ((mask & SegE) != 0) AddSeg(v, t, new Vector2(-x, 0f), new Vector2(-x, -z), h, gap);
            if ((mask & SegF) != 0) AddSeg(v, t, new Vector2(-x, z), new Vector2(-x, 0f), h, gap);
            if ((mask & SegG) != 0) AddSeg(v, t, new Vector2(-x, 0f), new Vector2(x, 0f), h, gap);
            return Finish(name, v, t);
        }

        /// One hexagonal LCD segment from a to b (half thickness h, shortened by gap at both ends).
        static void AddSeg(List<Vector3> v, List<int> t, Vector2 a, Vector2 b, float h, float gap)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return;
            d /= len;
            Vector2 n = new Vector2(-d.y, d.x);
            a += d * gap;
            b -= d * gap;
            int s = v.Count;
            V(v, a); V(v, a + d * h + n * h); V(v, b - d * h + n * h); V(v, b); V(v, b - d * h - n * h); V(v, a + d * h - n * h);
            t.Add(s); t.Add(s + 1); t.Add(s + 2);
            t.Add(s); t.Add(s + 2); t.Add(s + 3);
            t.Add(s); t.Add(s + 3); t.Add(s + 4);
            t.Add(s); t.Add(s + 4); t.Add(s + 5);
        }

        static void V(List<Vector3> v, Vector2 p) => v.Add(new Vector3(p.x, 0f, p.y));
        static void V(List<Vector3> v, float x, float z) => v.Add(new Vector3(x, 0f, z));

        /// Every triangle faces +Y (Unity front faces: clockwise seen from the viewer = cross product toward the viewer).
        static Mesh Finish(string name, List<Vector3> v, List<int> t)
        {
            for (int i = 0; i + 2 < t.Count; i += 3)
            {
                Vector3 c = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (c.y < 0f) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; }
            }
            var nrm = new List<Vector3>(v.Count);
            for (int i = 0; i < v.Count; i++) nrm.Add(Vector3.up);
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetNormals(nrm);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
