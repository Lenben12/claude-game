using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// One NPC in the world. The HOST (or offline game) moves it (GoTo / Place / SetMotion …); co-op clients mirror the
    /// host's replicated state (NpcNet) and interpolate. Every machine animates it procedurally (walk waddle, idle
    /// breathing, blinking, talking mouth, look-at, gestures, work loops). Ticked by NpcDirector (no Update here).
    [DisallowMultipleComponent]
    public sealed class NpcActor : MonoBehaviour, INpc, IInteractable, IHittable
    {
        public const byte FlagOffer = 1, FlagConversation = 2, FlagChoice = 4, FlagKiosk = 8;
        /// Round 3 (hermits): fighting / attacking, wary (shoos you, no talk), friendly (teaches, jobs).
        public const byte FlagHostile = 16, FlagWary = 32, FlagFriendly = 64;

        public NpcDef Def;
        public int Index;
        public NpcBody Body;

        // ---------------------------------------------------------------- replicated state (host → clients)
        public bool Present;
        public NpcMotion Motion = NpcMotion.Idle;
        public NpcGesture Gesture;
        public byte GestureSeq;
        public int MarkerValue;
        public byte Flags;
        Vector3 pos;
        float yaw;

        // ---------------------------------------------------------------- host movement
        readonly List<Vector3> path = new List<Vector3>(12);
        int pathIndex;
        float moveSpeed = 1.4f;
        float snapTimer;
        float targetY;
        public Vector3? FacePoint;
        /// Host: arrival callback (one shot).
        System.Action onArrive;

        // ---------------------------------------------------------------- client interpolation
        Vector3 netPos;
        float netYaw;
        bool netValid;

        // ---------------------------------------------------------------- animation state
        float speedSmooth, phase, breath, blinkTimer, blinkT, talkLevel, talkTimer, gestureT = -1f, gestureLen = 1.2f, idleShift;
        byte lastGestureSeq;
        NpcGesture playingGesture;
        Vector3 visPos, lastVisPos;
        float visYaw;
        Quaternion headRot = Quaternion.identity;
        Transform markerRoot, markerBang, markerQuestion;
        CapsuleCollider col;
        bool visible = true, colLying;
        float rng01;

        public Vector3 Position => pos;
        public float Yaw => yaw;
        public bool Moving => pathIndex < path.Count;
        /// Host: the NPC was placed in the world at least once this session (schedules place before they walk).
        public bool Placed { get; private set; }
        public bool HasOffer => (Flags & FlagOffer) != 0;
        public bool InConversation => (Flags & FlagConversation) != 0;

        // ---------------------------------------------------------------- INpc
        public string Id => Def != null ? Def.id : "";
        public Transform Root => transform;
        public string DisplayName => Def != null ? Def.Name : "";
        public bool IsTalking => talkTimer > 0f;
        public int Marker => MarkerValue;

        // ---------------------------------------------------------------- setup
        public void Init(NpcDef def, int index, NpcBody body)
        {
            Def = def;
            Index = index;
            Body = body;
            rng01 = (NpcUtil.StableHash(def.id) & 1023) / 1023f;
            blinkTimer = 2f + rng01 * 3f;
            col = gameObject.AddComponent<CapsuleCollider>();
            col.radius = Mathf.Max(0.32f, def.height * 0.2f);
            col.height = def.height + 0.1f;
            col.center = new Vector3(0f, def.height * 0.5f, 0f);
            col.direction = 1;
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            BuildMarker();
            NpcRegistry.Register(this);
        }

        void OnDestroy() { NpcRegistry.Unregister(this); }

        void BuildMarker()
        {
            markerRoot = NpcMesh.Node(transform, "Marker", new Vector3(0f, Def.height + 0.75f, 0f));
            Material yellow = NpcMesh.Glow(LowPoly.Hex("#F2C94C"), 1.4f), green = NpcMesh.Glow(LowPoly.Hex("#6FD36B"), 1.4f);
            markerBang = NpcMesh.Node(markerRoot, "Bang", Vector3.zero);
            NpcMesh.Part(markerBang, "Bar", NpcMesh.Capsule(0.065f, 0.36f, 14), yellow, new Vector3(0f, 0.02f, 0f), Vector3.one, default, false);
            NpcMesh.Part(markerBang, "Dot", NpcMesh.Sphere(10, 16), yellow, new Vector3(0f, -0.1f, 0f), Vector3.one * 0.13f, default, false);
            markerQuestion = NpcMesh.Node(markerRoot, "Question", Vector3.zero);
            var hook = new[]
            {
                new Vector3(-0.1f, 0.26f, 0f), new Vector3(-0.07f, 0.34f, 0f), new Vector3(0.02f, 0.37f, 0f), new Vector3(0.1f, 0.31f, 0f),
                new Vector3(0.1f, 0.22f, 0f), new Vector3(0.02f, 0.14f, 0f), new Vector3(0f, 0.06f, 0f)
            };
            NpcMesh.Part(markerQuestion, "Hook", NpcMesh.Sweep("marker_q", hook, 0.055f, 0.05f, 10), green, Vector3.zero, Vector3.one, default, false);
            NpcMesh.Part(markerQuestion, "Dot", NpcMesh.Sphere(10, 16), green, new Vector3(0f, -0.08f, 0f), Vector3.one * 0.12f, default, false);
            markerRoot.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- host API
        /// Host: teleports the NPC (no walking) and clears its path.
        public void Place(Vector3 p, float yawDeg)
        {
            path.Clear();
            pathIndex = 0;
            onArrive = null;
            p.y = NpcUtil.SurfaceY(p);
            Placed = true;
            pos = p;
            targetY = p.y;
            yaw = yawDeg;
            visPos = p;
            lastVisPos = p;
            visYaw = yawDeg;
            transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yawDeg, 0f));
        }

        /// Host: teleports the NPC to an exact point (no ground snap) – lying in a hammock, sitting on a bench.
        public void PlaceExact(Vector3 p, float yawDeg)
        {
            Place(p, yawDeg);
            pos = p;
            targetY = p.y;
            visPos = p;
            lastVisPos = p;
            transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yawDeg, 0f));
        }

        /// Host: walks to dest (straight line, ground snapped) – waypoints optional. Speed m/s.
        public void GoTo(Vector3 dest, float speed, System.Action arrive = null)
        {
            path.Clear();
            pathIndex = 0;
            path.Add(dest);
            moveSpeed = speed;
            onArrive = arrive;
        }

        public void GoVia(Vector3[] waypoints, float speed, System.Action arrive = null)
        {
            path.Clear();
            pathIndex = 0;
            if (waypoints != null) for (int i = 0; i < waypoints.Length; i++) path.Add(waypoints[i]);
            moveSpeed = speed;
            onArrive = arrive;
        }

        public void Stop()
        {
            path.Clear();
            pathIndex = 0;
            onArrive = null;
        }

        /// Target of the current walk (or the position when standing).
        public Vector3 Destination => path.Count > 0 ? path[path.Count - 1] : pos;

        public void PlayGesture(NpcGesture g)
        {
            Gesture = g;
            unchecked { GestureSeq++; }
        }

        public void SetPresent(bool present)
        {
            Present = present;
            if (!present) Stop();
        }

        /// Called by NpcBabble / the director when this NPC speaks a line.
        public void BeginTalking(float seconds)
        {
            talkTimer = Mathf.Max(talkTimer, seconds);
        }

        /// One syllable of babble: opens the mouth for a moment (0..1).
        public void TalkPulse(float open01)
        {
            talkLevel = Mathf.Max(talkLevel, Mathf.Clamp01(open01));
        }

        // ---------------------------------------------------------------- net (clients)
        public void ApplyNet(Vector3 p, float yawDeg, bool present, NpcMotion motion, NpcGesture g, byte seq, int marker, byte flags)
        {
            bool first = !netValid || !Present;
            netPos = p;
            netYaw = yawDeg;
            netValid = true;
            Present = present;
            Motion = motion;
            Gesture = g;
            GestureSeq = seq;
            MarkerValue = marker;
            Flags = flags;
            if (first || (p - pos).sqrMagnitude > 36f)
            {
                pos = p;
                yaw = yawDeg;
                visPos = p;
                lastVisPos = p;
                visYaw = yawDeg;
            }
        }

        public void ResetNet() { netValid = false; Placed = false; }

        // ---------------------------------------------------------------- tick
        /// Host simulation step (movement). Runs before Animate.
        public void HostTick(float dt)
        {
            if (!Present) return;
            if (pathIndex < path.Count)
            {
                Vector3 target = path[pathIndex];
                Vector3 d = target - pos;
                d.y = 0f;
                float dist = d.magnitude;
                float step = moveSpeed * dt;
                if (dist <= Mathf.Max(0.05f, step))
                {
                    pos.x = target.x;
                    pos.z = target.z;
                    pathIndex++;
                    if (pathIndex >= path.Count)
                    {
                        path.Clear();
                        pathIndex = 0;
                        var cb = onArrive;
                        onArrive = null;
                        cb?.Invoke();
                    }
                }
                else
                {
                    Vector3 dir = d / dist;
                    pos += dir * step;
                    float want = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                    yaw = Mathf.MoveTowardsAngle(yaw, want, 360f * dt);
                }
                snapTimer -= dt;
                if (snapTimer <= 0f)
                {
                    snapTimer = 0.12f;
                    targetY = NpcUtil.SurfaceY(pos);
                }
                pos.y = Mathf.MoveTowards(pos.y, targetY, 6f * dt);
                if (Motion != NpcMotion.Run && Motion != NpcMotion.Swim) Motion = moveSpeed > 3f ? NpcMotion.Run : NpcMotion.Walk;
                else if (Motion == NpcMotion.Run && moveSpeed <= 3f) Motion = NpcMotion.Walk;
            }
            else
            {
                if (Motion == NpcMotion.Walk || Motion == NpcMotion.Run) Motion = NpcMotion.Idle;
                if (FacePoint.HasValue)
                {
                    Vector3 d = FacePoint.Value - pos;
                    d.y = 0f;
                    if (d.sqrMagnitude > 0.04f) yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 220f * dt);
                }
            }
        }

        /// Visual step on every machine: interpolation (clients), culling, procedural animation, marker.
        public void Animate(float dt, Vector3 cameraPos, Vector3? lookAt)
        {
            if (!Net.HasAuthority && netValid)
            {
                float k = 1f - Mathf.Exp(-8f * dt);
                pos = Vector3.Lerp(pos, netPos, k);
                yaw = Mathf.LerpAngle(yaw, netYaw, k);
            }

            float camD = (pos - cameraPos).sqrMagnitude;
            bool show = Present && Motion != NpcMotion.Hidden && camD < 170f * 170f;
            if (show != visible)
            {
                visible = show;
                gameObject.SetActive(show);
            }
            if (!show) { talkTimer = 0f; return; }

            visPos = pos;
            visYaw = Mathf.LerpAngle(visYaw, yaw, 1f - Mathf.Exp(-12f * dt));
            transform.SetPositionAndRotation(visPos, Quaternion.Euler(0f, visYaw, 0f));
            // round 3: lying in a hammock the capsule lies down too (no invisible pillar over the hammock)
            bool lying = Motion == NpcMotion.Lie;
            if (col != null && lying != colLying)
            {
                colLying = lying;
                col.direction = lying ? 2 : 1;
                col.center = lying ? new Vector3(0f, LieHipHeight, 0f) : new Vector3(0f, Def.height * 0.5f, 0f);
                col.height = lying ? Def.height * 0.9f : Def.height + 0.1f;
            }

            float measured = dt > 0f ? NpcUtil.FlatDist(visPos, lastVisPos) / dt : 0f;
            lastVisPos = visPos;
            speedSmooth = Mathf.Lerp(speedSmooth, Mathf.Min(measured, 6f), 1f - Mathf.Exp(-10f * dt));

            UpdateMarker(dt, cameraPos, camD);
            if (camD > 75f * 75f) return;   // far: no limb animation (cheap)
            AnimateBody(dt, lookAt);
        }

        void UpdateMarker(float dt, Vector3 cam, float camD)
        {
            bool on = MarkerValue > 0 && camD < 40f * 40f && Motion != NpcMotion.Lie;
            if (markerRoot.gameObject.activeSelf != on) markerRoot.gameObject.SetActive(on);
            if (!on) return;
            if (markerBang.gameObject.activeSelf != (MarkerValue == 1)) markerBang.gameObject.SetActive(MarkerValue == 1);
            if (markerQuestion.gameObject.activeSelf != (MarkerValue == 2)) markerQuestion.gameObject.SetActive(MarkerValue == 2);
            float t = Time.time;
            markerRoot.localPosition = new Vector3(0f, Def.height + 0.72f + Mathf.Sin(t * 2.4f + rng01 * 6f) * 0.07f, 0f);
            Vector3 d = cam - markerRoot.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.01f) markerRoot.rotation = Quaternion.LookRotation(-d.normalized, Vector3.up);
            float s = 1f + Mathf.Sin(t * 4.8f) * 0.05f;
            markerRoot.localScale = new Vector3(s, s, s);
        }

        // ---------------------------------------------------------------- procedural animation
        void AnimateBody(float dt, Vector3? lookAt)
        {
            var b = Body;
            if (b == null || b.pivot == null) return;
            float t = Time.time + rng01 * 10f;
            bool horst = Def.id == NpcDatabase.Horst;
            float sp = speedSmooth;
            bool moving = sp > 0.15f && Motion != NpcMotion.Sit && Motion != NpcMotion.Lie && Motion != NpcMotion.Swim && Motion != NpcMotion.Paddle && Motion != NpcMotion.Dazed;
            bool running = moving && sp > 3f;
            float stride = running ? 0.85f : 0.55f;
            float stepRate = moving ? Mathf.Clamp(sp / stride, 0.8f, 3.2f) : 0f;
            phase += stepRate * dt * Mathf.PI;
            float sw = Mathf.Sin(phase), cw = Mathf.Cos(phase);
            float moveW = Mathf.Clamp01(sp / 1.2f);

            // ---- defaults
            float legAmp = (running ? 42f : 26f) * moveW;
            float armAmp = (running ? 48f : 22f) * moveW;
            Quaternion hipL = Quaternion.Euler(-sw * legAmp, 0f, 0f), hipR = Quaternion.Euler(sw * legAmp, 0f, 0f);
            Quaternion kneeL = Quaternion.Euler(Mathf.Max(0f, cw) * (running ? 60f : 30f) * moveW, 0f, 0f);
            Quaternion kneeR = Quaternion.Euler(Mathf.Max(0f, -cw) * (running ? 60f : 30f) * moveW, 0f, 0f);
            float armOut = 7f + (horst ? 14f : 0f);
            Quaternion shL = Quaternion.Euler(sw * armAmp, 0f, -armOut), shR = Quaternion.Euler(-sw * armAmp, 0f, armOut);
            Quaternion elL = Quaternion.Euler(running ? -75f : -10f - 8f * moveW, 0f, 0f), elR = elL;
            Vector3 pivotPos = b.pivotBasePos;
            Quaternion pivotRot = b.pivotBase;
            float bounce = moving ? Mathf.Abs(sw) * (running ? 0.07f : 0.04f) : 0f;
            pivotPos.y += bounce;
            float roll = moving ? sw * (horst ? 8f : 4f) : 0f;
            float lean = running ? 10f : moving ? 2f : 0f;
            breath += dt * (running ? 4f : 1.6f);
            float breathS = 1f + Mathf.Sin(breath) * 0.015f;

            switch (Motion)
            {
                case NpcMotion.Sit:
                    hipL = hipR = Quaternion.Euler(-82f, 0f, 0f);
                    kneeL = kneeR = Quaternion.Euler(80f, 0f, 0f);
                    pivotPos.y = 0.46f;
                    shL = Quaternion.Euler(-18f, 0f, -10f); shR = Quaternion.Euler(-18f, 0f, 10f);
                    elL = elR = Quaternion.Euler(-35f, 0f, 0f);
                    break;
                case NpcMotion.Lie:
                {
                    // on the back, head toward −Z; in a hammock (a registered sleep spot under the root) the torso and the
                    // legs follow the cloth's curve (head end / feet end up) – round 3: Horst's hammock, the camp hammocks
                    EnsureLieSpot();
                    float hr = lieHead, fr = lieFeet;
                    pivotRot = Quaternion.Euler(-88f + hr, 0f, 0f);
                    pivotPos = new Vector3(0f, LieHipHeight, 0f);
                    hipL = Quaternion.Euler(-2f - fr - hr, 0f, -6f); hipR = Quaternion.Euler(-2f - fr - hr, 0f, 6f);
                    kneeL = kneeR = Quaternion.Euler(8f + fr * 0.6f, 0f, 0f);
                    shL = Quaternion.Euler(0f, 0f, -18f + Mathf.Sin(t * 0.5f) * 3f); shR = Quaternion.Euler(0f, 0f, 18f);
                    elL = elR = Quaternion.Euler(-12f, 0f, 0f);
                    if (lieHammock) roll += Mathf.Sin(t * 0.9f) * 2.5f;   // gentle sway
                    break;
                }
                case NpcMotion.Dazed:
                    // knocked down: sits in the sand, leaning back on the hands, the head wobbles
                    hipL = Quaternion.Euler(-80f, 0f, -12f); hipR = Quaternion.Euler(-75f, 0f, 12f);
                    kneeL = Quaternion.Euler(55f, 0f, 0f); kneeR = Quaternion.Euler(35f, 0f, 0f);
                    pivotPos.y = 0.17f;
                    lean -= 14f;
                    roll += Mathf.Sin(t * 3.1f) * 6f;
                    shL = Quaternion.Euler(28f, 0f, -24f); shR = Quaternion.Euler(28f, 0f, 24f);
                    elL = elR = Quaternion.Euler(-8f, 0f, 0f);
                    break;
                case NpcMotion.ArmsCrossed:
                    shL = Quaternion.Euler(-28f, 28f, -6f); shR = Quaternion.Euler(-28f, -28f, 6f);
                    elL = Quaternion.Euler(-108f, 0f, 0f); elR = Quaternion.Euler(-100f, 0f, 0f);
                    lean -= 3f;
                    break;
                case NpcMotion.Swim:
                    pivotPos.y = -b.hipY * 0.6f + Mathf.Sin(t * 2f) * 0.05f;
                    shL = Quaternion.Euler(-60f + Mathf.Sin(t * 5f) * 40f, 0f, -40f); shR = Quaternion.Euler(-60f - Mathf.Sin(t * 5f) * 40f, 0f, 40f);
                    hipL = Quaternion.Euler(Mathf.Sin(t * 6f) * 25f, 0f, 0f); hipR = Quaternion.Euler(-Mathf.Sin(t * 6f) * 25f, 0f, 0f);
                    break;
                case NpcMotion.Weave:
                case NpcMotion.Mend:
                    shL = Quaternion.Euler(-45f + Mathf.Sin(t * 3f) * 10f, 0f, -12f); shR = Quaternion.Euler(-45f - Mathf.Sin(t * 3f) * 10f, 0f, 12f);
                    elL = Quaternion.Euler(-40f + Mathf.Sin(t * 6f) * 12f, 0f, 0f); elR = Quaternion.Euler(-40f - Mathf.Sin(t * 6f) * 12f, 0f, 0f);
                    lean += 8f;
                    break;
                case NpcMotion.Cook:
                    shR = Quaternion.Euler(-50f, Mathf.Sin(t * 4f) * 20f, 18f + Mathf.Cos(t * 4f) * 10f);
                    elR = Quaternion.Euler(-50f, 0f, 0f);
                    shL = Quaternion.Euler(-10f, 0f, -14f);
                    break;
                case NpcMotion.Carve:
                    shR = Quaternion.Euler(-70f + Mathf.Abs(Mathf.Sin(t * 5f)) * 40f, 0f, 12f);
                    elR = Quaternion.Euler(-60f, 0f, 0f);
                    shL = Quaternion.Euler(-45f, 0f, -10f);
                    elL = Quaternion.Euler(-40f, 0f, 0f);
                    lean += 10f;
                    break;
                case NpcMotion.Marimba:
                    shL = Quaternion.Euler(-55f + Mathf.Abs(Mathf.Sin(t * 7f)) * 18f, 0f, -18f); shR = Quaternion.Euler(-55f + Mathf.Abs(Mathf.Cos(t * 7f)) * 18f, 0f, 18f);
                    elL = elR = Quaternion.Euler(-35f, 0f, 0f);
                    roll += Mathf.Sin(t * 2.2f) * 4f;
                    break;
                case NpcMotion.Strum:
                    shR = Quaternion.Euler(-35f + Mathf.Sin(t * 9f) * 12f, 0f, 20f);
                    elR = Quaternion.Euler(-70f, 0f, 0f);
                    shL = Quaternion.Euler(-40f, 0f, -25f);
                    elL = Quaternion.Euler(-80f, 0f, 0f);
                    roll += Mathf.Sin(t * 2f) * 3f;
                    break;
                case NpcMotion.Doze:
                    lean += 6f + Mathf.Sin(t * 0.7f) * 3f;
                    shL = Quaternion.Euler(0f, 0f, -4f); shR = Quaternion.Euler(0f, 0f, 4f);
                    break;
                case NpcMotion.Cards:
                    hipL = hipR = Quaternion.Euler(-82f, 0f, 0f);
                    kneeL = kneeR = Quaternion.Euler(80f, 0f, 0f);
                    pivotPos.y = 0.46f;
                    shL = Quaternion.Euler(-55f, 0f, -16f); shR = Quaternion.Euler(-55f + Mathf.Max(0f, Mathf.Sin(t * 1.3f)) * 25f, 0f, 16f);
                    elL = elR = Quaternion.Euler(-45f, 0f, 0f);
                    break;
                case NpcMotion.Paddle:
                    hipL = hipR = Quaternion.Euler(-80f, 0f, 0f);
                    kneeL = kneeR = Quaternion.Euler(75f, 0f, 0f);
                    pivotPos.y = 0.42f;
                    float pp = Mathf.Sin(t * 3f);
                    shL = Quaternion.Euler(-60f + pp * 30f, 0f, -25f); shR = Quaternion.Euler(-60f - pp * 30f, 0f, 25f);
                    elL = elR = Quaternion.Euler(-30f, 0f, 0f);
                    break;
                case NpcMotion.Cheer:
                    shL = Quaternion.Euler(-160f + Mathf.Sin(t * 6f) * 12f, 0f, -20f); shR = Quaternion.Euler(-160f - Mathf.Sin(t * 6f) * 12f, 0f, 20f);
                    elL = elR = Quaternion.Euler(-12f, 0f, 0f);
                    pivotPos.y += Mathf.Abs(Mathf.Sin(t * 6f)) * 0.04f;
                    break;
                default:
                    if (!moving)
                    {
                        // idle: weight shift every few seconds, small arm sway
                        idleShift += dt;
                        float shift = Mathf.Sin(idleShift * 0.35f + rng01 * 5f);
                        roll += shift * 2.2f;
                        shL = Quaternion.Euler(Mathf.Sin(t * 0.9f) * 3f, 0f, -armOut - shift * 2f);
                        shR = Quaternion.Euler(Mathf.Sin(t * 0.9f + 1f) * 3f, 0f, armOut - shift * 2f);
                    }
                    break;
            }

            // ---- one-shot gestures (layered over the arms)
            if (GestureSeq != lastGestureSeq)
            {
                lastGestureSeq = GestureSeq;
                if (Gesture != NpcGesture.None)
                {
                    playingGesture = Gesture;
                    gestureT = 0f;
                    gestureLen = GestureLength(Gesture);
                }
            }
            if (gestureT >= 0f)
            {
                gestureT += dt;
                float w = Mathf.Clamp01(Mathf.Min(gestureT / 0.2f, (gestureLen - gestureT) / 0.25f));
                if (gestureT >= gestureLen) { gestureT = -1f; playingGesture = NpcGesture.None; }
                else ApplyGesture(playingGesture, gestureT, w, ref shL, ref shR, ref elL, ref elR, ref pivotPos, ref lean, ref roll);
            }

            // ---- talking: small hand gestures while speaking
            if (talkTimer > 0f)
            {
                talkTimer -= dt;
                if (gestureT < 0f && Motion == NpcMotion.Idle && !moving)
                {
                    float g = Mathf.Sin(t * 2.6f) * 0.5f + 0.5f;
                    shR = Quaternion.Slerp(shR, Quaternion.Euler(-35f - g * 20f, 10f, 22f), 0.7f);
                    elR = Quaternion.Slerp(elR, Quaternion.Euler(-55f, 0f, 0f), 0.7f);
                }
            }

            // ---- apply
            b.pivot.localPosition = pivotPos;
            b.pivot.localRotation = pivotRot * Quaternion.Euler(lean, 0f, roll);
            b.pivot.localScale = new Vector3(1f, breathS, 1f);
            if (b.hipL != null) { b.hipL.localRotation = hipL; b.hipR.localRotation = hipR; }
            if (b.kneeL != null) { b.kneeL.localRotation = kneeL; b.kneeR.localRotation = kneeR; }
            if (b.shoulderL != null) { b.shoulderL.localRotation = shL; b.shoulderR.localRotation = shR; }
            if (b.elbowL != null) { b.elbowL.localRotation = elL; b.elbowR.localRotation = elR; }

            // ---- head: look at the target (clamped), counter the body roll
            if (b.head != null)
            {
                Quaternion want = Quaternion.Euler(-lean * 0.5f, 0f, -roll * 0.6f);
                if (lookAt.HasValue && Motion != NpcMotion.Lie && Motion != NpcMotion.Doze)
                {
                    Vector3 local = transform.InverseTransformPoint(lookAt.Value) - new Vector3(0f, Def.height * 0.9f, 0f);
                    float yawL = Mathf.Clamp(Mathf.Atan2(local.x, Mathf.Max(0.01f, local.z)) * Mathf.Rad2Deg, -60f, 60f);
                    float pitchL = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -25f, 25f);
                    if (local.z < -0.5f) yawL = 0f;
                    want = Quaternion.Euler(pitchL, yawL, -roll * 0.6f);
                }
                if (Motion == NpcMotion.Doze) want = Quaternion.Euler(18f + Mathf.Sin(t * 0.7f) * 6f, 0f, 6f);
                headRot = Quaternion.Slerp(headRot, want, 1f - Mathf.Exp(-6f * dt));
                b.head.localRotation = Quaternion.Euler(-Def.height * 0f, 0f, 0f) * headRot;
            }

            // ---- blink
            blinkTimer -= dt;
            if (blinkTimer <= 0f)
            {
                blinkT = 0.14f;
                blinkTimer = 3f + Mathf.Repeat(t * 1.37f, 3f);
            }
            float lid = 1f;
            if (blinkT > 0f) { blinkT -= dt; lid = 0.12f; }
            if (Motion == NpcMotion.Doze || Motion == NpcMotion.Lie) lid = 0.1f;   // round 3: lying = asleep (hammocks)
            if (b.lidL != null) { b.lidL.localScale = new Vector3(1f, lid, 1f); b.lidR.localScale = new Vector3(1f, lid, 1f); }

            // ---- mouth + moustache bob
            talkLevel = Mathf.MoveTowards(talkLevel, 0f, dt * 7f);
            if (b.mouthOpen != null) b.mouthOpen.localScale = new Vector3(1f, Mathf.Max(0.01f, talkLevel), 1f);
            if (b.moustache != null) b.moustache.localRotation = Quaternion.Euler(-talkLevel * 10f + (moving ? Mathf.Sin(phase * 2f) * 3f : 0f), 0f, 0f);
            if (b.hat != null && moving) b.hat.localPosition = new Vector3(b.hat.localPosition.x, b.hat.localPosition.y, b.hat.localPosition.z);
        }

        static float GestureLength(NpcGesture g)
        {
            switch (g)
            {
                case NpcGesture.Wave: return 1.6f;
                case NpcGesture.TaDa: return 1.2f;
                case NpcGesture.Twirl: return 2.0f;
                case NpcGesture.Whistle: return 1.4f;
                case NpcGesture.Selfie: return 2.2f;
                case NpcGesture.Laugh: return 1.6f;
                case NpcGesture.Fall: return 1.4f;
                case NpcGesture.Flail: return 2.4f;
                case NpcGesture.Greet: return 0.9f;
                case NpcGesture.Shoo: return 1.6f;
                case NpcGesture.HandOnHeart: return 1.8f;
                case NpcGesture.Swing: return 0.8f;
                case NpcGesture.Flinch: return 0.6f;
                case NpcGesture.Bow: return 1.6f;
                case NpcGesture.Spyglass: return 2.6f;
                case NpcGesture.Pose: return 2.0f;
                default: return 1.2f;
            }
        }

        static void ApplyGesture(NpcGesture g, float t, float w, ref Quaternion shL, ref Quaternion shR, ref Quaternion elL, ref Quaternion elR,
                                 ref Vector3 pivotPos, ref float lean, ref float roll)
        {
            Quaternion gL = shL, gR = shR, eL = elL, eR = elR;
            switch (g)
            {
                case NpcGesture.Wave:
                case NpcGesture.Greet:
                    // round 3 (GDD §19: „Kein komischer Arm-Gruß“): no NPC waves an arm to greet any more – both old
                    // greeting gestures are a friendly nod now
                    lean += Mathf.Sin(Mathf.Clamp01(t / 0.9f) * Mathf.PI * 2f) * 6f;
                    break;
                case NpcGesture.Shoo:
                {
                    float s = Mathf.Sin(t * 14f);
                    gL = Quaternion.Euler(-62f + s * 18f, 0f, -18f); gR = Quaternion.Euler(-62f - s * 18f, 0f, 18f);
                    eL = eR = Quaternion.Euler(-35f + s * 15f, 0f, 0f);
                    lean -= 4f;
                    break;
                }
                case NpcGesture.HandOnHeart:
                    gR = Quaternion.Euler(-58f, -38f, 4f);
                    eR = Quaternion.Euler(-118f, 0f, 0f);
                    lean += 14f * Mathf.Sin(Mathf.Clamp01(t / 1.8f) * Mathf.PI);
                    break;
                case NpcGesture.Swing:
                    // wind-up over the shoulder (0–0.45 s), then the bonk down
                    if (t < 0.45f) { gR = Quaternion.Euler(-165f, 0f, 22f); eR = Quaternion.Euler(-45f, 0f, 0f); lean -= 6f; }
                    else { gR = Quaternion.Euler(-35f, 0f, 8f); eR = Quaternion.Euler(-10f, 0f, 0f); lean += 12f; }
                    break;
                case NpcGesture.Flinch:
                    lean -= 16f * Mathf.Sin(Mathf.Clamp01(t / 0.6f) * Mathf.PI);
                    gL = Quaternion.Euler(-110f, 0f, -30f); gR = Quaternion.Euler(-110f, 0f, 30f);
                    eL = eR = Quaternion.Euler(-95f, 0f, 0f);
                    break;
                case NpcGesture.Bow:
                    lean += 34f * Mathf.Sin(Mathf.Clamp01(t / 1.6f) * Mathf.PI);
                    gL = Quaternion.Euler(-10f, 0f, -6f); gR = Quaternion.Euler(-10f, 0f, 6f);
                    break;
                case NpcGesture.Spyglass:
                    gR = Quaternion.Euler(-98f, -32f, 10f);
                    eR = Quaternion.Euler(-132f, 0f, 0f);
                    gL = Quaternion.Euler(-70f, 20f, -10f);
                    eL = Quaternion.Euler(-60f, 0f, 0f);
                    break;
                case NpcGesture.Pose:
                    gL = Quaternion.Euler(-12f, 0f, -48f); eL = Quaternion.Euler(-95f, 0f, 0f);
                    gR = Quaternion.Euler(-150f, 0f, 28f); eR = Quaternion.Euler(-10f, 0f, 0f);
                    lean -= 5f;
                    break;
                case NpcGesture.Point:
                    gR = Quaternion.Euler(-85f, 0f, 12f);
                    eR = Quaternion.Euler(-5f, 0f, 0f);
                    break;
                case NpcGesture.Shrug:
                    gL = Quaternion.Euler(-30f, 0f, -45f); gR = Quaternion.Euler(-30f, 0f, 45f);
                    eL = eR = Quaternion.Euler(-70f, 0f, 0f);
                    pivotPos.y += 0.02f;
                    break;
                case NpcGesture.ThumbsUp:
                    gR = Quaternion.Euler(-70f, 0f, 20f);
                    eR = Quaternion.Euler(-80f, 0f, 0f);
                    break;
                case NpcGesture.Facepalm:
                    gR = Quaternion.Euler(-120f, -20f, 10f);
                    eR = Quaternion.Euler(-120f, 0f, 0f);
                    lean += 10f;
                    break;
                case NpcGesture.TaDa:
                    gL = Quaternion.Euler(-160f, 0f, -35f); gR = Quaternion.Euler(-160f, 0f, 35f);
                    eL = eR = Quaternion.Euler(-5f, 0f, 0f);
                    pivotPos.y += 0.05f * Mathf.Sin(Mathf.Clamp01(t / 0.4f) * Mathf.PI);
                    break;
                case NpcGesture.Twirl:
                    gR = Quaternion.Euler(-115f, -10f, 0f);
                    eR = Quaternion.Euler(-120f + Mathf.Sin(t * 10f) * 10f, 0f, 0f);
                    if (t > 1.3f) lean -= 8f;
                    break;
                case NpcGesture.Whistle:
                    gR = Quaternion.Euler(-120f, -15f, 0f);
                    eR = Quaternion.Euler(-130f, 0f, 0f);
                    gL = Quaternion.Euler(-60f + Mathf.Sin(t * 9f) * 15f, 0f, -20f);
                    break;
                case NpcGesture.Selfie:
                    gR = Quaternion.Euler(-110f, 25f, 25f);
                    eR = Quaternion.Euler(-10f, 0f, 0f);
                    gL = Quaternion.Euler(-30f, 0f, -40f);
                    break;
                case NpcGesture.Laugh:
                    lean += Mathf.Sin(t * 14f) * 4f - 6f;
                    gL = Quaternion.Euler(-40f, 0f, -12f); gR = Quaternion.Euler(-40f, 0f, 12f);
                    eL = eR = Quaternion.Euler(-60f, 0f, 0f);
                    break;
                case NpcGesture.Nod:
                    lean += Mathf.Sin(t * 10f) * 5f;
                    break;
                case NpcGesture.Think:
                    gR = Quaternion.Euler(-95f, -25f, 0f);
                    eR = Quaternion.Euler(-125f, 0f, 0f);
                    break;
                case NpcGesture.Clap:
                    float c = Mathf.Abs(Mathf.Sin(t * 9f));
                    gL = Quaternion.Euler(-65f, 0f, -10f - c * 18f); gR = Quaternion.Euler(-65f, 0f, 10f + c * 18f);
                    eL = eR = Quaternion.Euler(-50f, 0f, 0f);
                    break;
                case NpcGesture.Fall:
                    lean -= Mathf.Clamp01(t / 0.6f) * 50f;
                    gL = Quaternion.Euler(-150f + Mathf.Sin(t * 16f) * 30f, 0f, -40f); gR = Quaternion.Euler(-150f - Mathf.Sin(t * 16f) * 30f, 0f, 40f);
                    break;
                case NpcGesture.Flail:
                    gL = Quaternion.Euler(-140f + Mathf.Sin(t * 14f) * 40f, 0f, -30f); gR = Quaternion.Euler(-140f - Mathf.Sin(t * 14f) * 40f, 0f, 30f);
                    eL = eR = Quaternion.Euler(-20f, 0f, 0f);
                    break;
                case NpcGesture.Give:
                    gR = Quaternion.Euler(-75f, 0f, 8f); gL = Quaternion.Euler(-70f, 0f, -8f);
                    eL = eR = Quaternion.Euler(-15f, 0f, 0f);
                    lean += 6f;
                    break;
            }
            shL = Quaternion.Slerp(shL, gL, w);
            shR = Quaternion.Slerp(shR, gR, w);
            elL = Quaternion.Slerp(elL, eL, w);
            elR = Quaternion.Slerp(elR, eR, w);
        }

        // ---------------------------------------------------------------- lying (round 3: hammocks)
        /// Height of the pelvis above the root while lying on the back (the root sits on the mattress / cloth / sand).
        public const float LieHipHeight = 0.14f;
        Vector3 lieAt = new Vector3(float.NaN, 0f, 0f);
        float lieHead, lieFeet;
        bool lieHammock;

        /// Finds the registered sleep spot under the root (hammock / bed – same on every machine) for the cloth curve.
        void EnsureLieSpot()
        {
            if (!float.IsNaN(lieAt.x) && (lieAt - pos).sqrMagnitude < 0.04f) return;
            lieAt = pos;
            lieHead = 0f; lieFeet = 0f; lieHammock = false;
            var spots = PropInfo.SleepSpots;
            for (int i = 0; i < spots.Count; i++)
            {
                var s = spots[i];
                Vector3 hip = s.hip != Vector3.zero ? s.hip : s.lie;
                if ((hip - pos).sqrMagnitude > 0.8f * 0.8f) continue;
                lieHammock = s.kind == SleepSpotInfo.Hammock;
                lieHead = Mathf.Clamp(s.headRise, 0f, 30f);
                lieFeet = Mathf.Clamp(s.feetRise, 0f, 30f);
                return;
            }
        }

        // ---------------------------------------------------------------- IHittable (round 3: the stick fights)
        /// Hermits can be bonked (they fight back, Q6/Q7); everybody else just complains (no wear, no hype).
        public bool CanBeHit => Present && Motion != NpcMotion.Hidden && Motion != NpcMotion.Lie && isActiveAndEnabled;

        public void OnHit(in HitInfo hit) => NpcDirector.LocalHitNpc(this, hit);

        // ---------------------------------------------------------------- IInteractable (local player)
        public string GetPrompt() => NpcDirector.PromptFor(this);

        public bool CanInteract() => Present && Motion != NpcMotion.Hidden && NpcDirector.CanInteract(this);

        public void Interact() => NpcDirector.LocalInteract(this);
    }
}
