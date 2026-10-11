using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Proximity voice chat. Microphone → 16 kHz mono → 40 ms frames → µ-law → unreliable message (host relays).
    /// Sends only while Settings.MicLive allows it: open mic (the default, F.5) above the noise gate
    /// (Settings.voiceGateDb, −45 dBFS, 300 ms hang time), or the push-to-talk key held (GameAction.PushToTalk); never
    /// when the voice mode is Off or the mic is muted. Receivers play each speaker through a 3D source on the avatar's
    /// head with the F.5 range rules (NetCoopRules.Link): above water 2–30 m; under water (speaker OR listener)
    /// 2–10 m muffled; with the underwater radio (every diver involved wears it, every party above water is on the
    /// boat) up to 200 m with a radio band-pass and a light crackle. ~120 ms jitter buffer. No microphone → receiving
    /// still works. Testing: start with "-voiceloopback" to hear your own microphone (works offline, too).
    public sealed class NetVoice : MonoBehaviour
    {
        const float OpenMicHold = 0.3f;       // F.5: 300 ms hang time after the voice drops below the gate
        const float SpeakingHold = 0.3f;
        // frame flags
        const byte FlagUnder = 1, FlagRadio = 2, FlagBoat = 4;

        /// Open-mic gate as RMS (Settings.voiceGateDb, default −45 dBFS ≈ 0.0056).
        static float GateRms => Mathf.Pow(10f, Mathf.Clamp(Settings.Data.voiceGateDb, -80f, -10f) / 20f);

        /// Link the voice of a player currently uses toward this machine (diagnostics, tests: "10 m under water").
        public static VoiceLink LinkOf(int playerId) => Instance != null && Instance.links.TryGetValue(playerId, out var l) ? l : VoiceLink.Air;
        /// Hearing range in meters of that player's voice right now.
        public static float RangeOf(int playerId) => NetCoopRules.MaxDistance(LinkOf(playerId));

        public static NetVoice Instance { get; private set; }

        /// A microphone exists (and could be opened).
        public static bool MicAvailable { get; private set; }
        /// Name of the capture device in use (null = none / system default).
        public static string MicDevice { get; private set; }
        /// The local microphone is being transmitted right now.
        public static bool LocalTransmitting { get; private set; }
        /// Local mic level 0..1 (for a settings meter), also while not transmitting.
        public static float LocalLevel { get; private set; }

        /// Test hook (automated co-op test, Debug/AutoPlaytestMP): while > 0 a synthetic sine tone of this frequency is
        /// sent through the normal voice pipeline (resample → µ-law → network → jitter buffer → 3D source) instead of the
        /// microphone. Ignores push-to-talk / open-mic gating and keeps the microphone closed. Online only.
        public static float TestToneHz;
        /// Peak amplitude of the test tone (0..1).
        public static float TestToneLevel = 0.08f;
        /// Voice frames sent / received since start (diagnostics and tests).
        public static int FramesSent { get; private set; }
        public static int FramesReceived { get; private set; }

        static bool loopback;
        static readonly Dictionary<int, float> lastVoiceTime = new Dictionary<int, float>();
        static readonly Dictionary<int, float> loudness = new Dictionary<int, float>();

        /// True while that player's voice is coming in (or, for Net.LocalId, while we transmit).
        public static bool IsSpeaking(int playerId)
        {
            if (playerId == Net.LocalId && (LocalTransmitting || (loopback && Instance != null && Instance.loopbackTalking))) return true;
            return lastVoiceTime.TryGetValue(playerId, out float t) && Time.unscaledTime - t < SpeakingHold;
        }

        /// 0..1 mouth-flap loudness of a speaker (0 when they are silent).
        public static float Loudness(int playerId)
        {
            if (playerId == Net.LocalId && Instance != null && (LocalTransmitting || Instance.loopbackTalking)) return Instance.lastLocalLevel;
            if (!loudness.TryGetValue(playerId, out float l)) return 0f;
            return lastVoiceTime.TryGetValue(playerId, out float t) && Time.unscaledTime - t < SpeakingHold ? l : 0f;
        }

        // ------------------------------------------------------------------ capture state
        AudioClip micClip;
        string micDevice;
        int micRate, micSamples, readPos, chunkIn;
        float[] micBuf;
        readonly float[] frame = new float[NetVoiceCodec.FrameSamples];
        readonly byte[] encoded = new byte[NetVoiceCodec.FrameSamples];
        readonly float[] decoded = new float[NetVoiceCodec.FrameSamples];
        readonly SyncWriter writer = new SyncWriter(NetVoiceCodec.FrameSamples + 8);
        ushort seq;
        float openUntil;
        bool loopbackTalking;
        float micRetryTime;
        bool micWarned;
        float lastLocalLevel;
        float lastSendTime = -10f, lastLoopTime = -10f;
        float toneClock, tonePhase;

        // ------------------------------------------------------------------ playback
        readonly Dictionary<int, NetVoicePlayer> players = new Dictionary<int, NetVoicePlayer>();
        readonly List<int> stale = new List<int>(4);
        readonly Dictionary<int, byte> speakerFlags = new Dictionary<int, byte>();
        readonly Dictionary<int, VoiceLink> links = new Dictionary<int, VoiceLink>();
        NetVoicePlayer loopbackPlayer;
        bool handlersOn;
        // the local player's radio (crew item, equipped per player) – re-read once a second
        bool localRadio;
        float radioTimer;

        void Awake()
        {
            Instance = this;
            try
            {
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                    if (string.Equals(args[i], "-voiceloopback", StringComparison.OrdinalIgnoreCase)) loopback = true;
            }
            catch (Exception) { }
            RefreshDevices();
        }

        void OnEnable()
        {
            NetBus.Register(NetSyncMsg.VoiceFrame, OnFrame);
            handlersOn = true;
            Net.PlayerLeft += OnPlayerLeft;
            Net.SessionEnded += OnSessionEnded;
        }

        void OnDisable()
        {
            if (handlersOn)
            {
                NetBus.Unregister(NetSyncMsg.VoiceFrame);
                handlersOn = false;
            }
            Net.PlayerLeft -= OnPlayerLeft;
            Net.SessionEnded -= OnSessionEnded;
            StopMic();
        }

        void OnDestroy()
        {
            StopMic();
            if (Instance == this) Instance = null;
        }

        void OnPlayerLeft(NetPlayerInfo p)
        {
            if (p == null) return;
            if (players.TryGetValue(p.id, out var vp))
            {
                players.Remove(p.id);
                if (vp != null) Destroy(vp.gameObject);
            }
            lastVoiceTime.Remove(p.id);
            loudness.Remove(p.id);
            speakerFlags.Remove(p.id);
            links.Remove(p.id);
        }

        void OnSessionEnded(string reason)
        {
            foreach (var kv in players) if (kv.Value != null) Destroy(kv.Value.gameObject);
            players.Clear();
            lastVoiceTime.Clear();
            loudness.Clear();
            speakerFlags.Clear();
            links.Clear();
        }

        /// Re-checks the capture devices (e.g. after plugging in a headset).
        public static void RefreshDevices()
        {
            string[] devs = null;
            try { devs = Microphone.devices; }
            catch (Exception) { devs = null; }
            MicAvailable = devs != null && devs.Length > 0;
            MicDevice = MicAvailable ? devs[0] : null;
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            bool online = Net.IsOnline;
            var d = Settings.Data;
            bool tone = online && TestToneHz > 0f;
            bool wantMic = !tone && (online || loopback) && MicAvailable && d.voiceMode != VoiceMode.Off && !d.micMuted;
            if (wantMic && micClip == null && Time.unscaledTime >= micRetryTime) StartMic();
            else if (!wantMic && micClip != null) StopMic();

            if (tone) PumpTestTone();
            else
            {
                toneClock = 0f;
                if (micClip != null) PumpMic(online);
                else LocalLevel = 0f;
            }
            // frames come every 40 ms (not every rendered frame): hold the flags across the gaps
            float now = Time.unscaledTime;
            LocalTransmitting = (micClip != null || tone) && now - lastSendTime < 0.15f;
            loopbackTalking = micClip != null && now - lastLoopTime < 0.15f;

            radioTimer -= Time.unscaledDeltaTime;
            if (radioTimer <= 0f)
            {
                radioTimer = 1f;
                var gm = GameManager.I;
                localRadio = gm != null && gm.InGame && gm.Gear.hasRadio;
            }

            UpdatePlayers();
        }

        static bool LocalOnBoat()
        {
            var b = Refs.Boat;
            return NetWorldSync.Alive(b) && (b.PlayerAboard || b.PlayerDriving);
        }

        // ------------------------------------------------------------------ microphone
        void StartMic()
        {
            micRetryTime = Time.unscaledTime + 5f;
            try
            {
                micDevice = MicDevice;
                int min = 0, max = 0;
                Microphone.GetDeviceCaps(micDevice, out min, out max);
                int rate = NetVoiceCodec.SampleRate;
                if (min > 0 || max > 0)
                {
                    if (max > 0 && rate > max) rate = max;
                    if (min > 0 && rate < min) rate = min;
                }
                micClip = Microphone.Start(micDevice, true, 1, rate);
                if (micClip == null) throw new InvalidOperationException("Microphone.Start returned null");
                micRate = micClip.frequency > 0 ? micClip.frequency : rate;
                micSamples = micClip.samples;
                chunkIn = Mathf.Max(1, Mathf.RoundToInt(micRate * 0.04f));
                if (micBuf == null || micBuf.Length != chunkIn) micBuf = new float[chunkIn];
                readPos = 0;
                openUntil = 0f;
                micWarned = false;
            }
            catch (Exception e)
            {
                if (!micWarned)
                {
                    micWarned = true;
                    Debug.LogWarning("Sealebrities/Voice: microphone unavailable – voice chat is receive-only (" + e.Message + ")");
                }
                micClip = null;
                RefreshDevices();
            }
        }

        void StopMic()
        {
            if (micClip == null) return;
            try { Microphone.End(micDevice); }
            catch (Exception) { }
            micClip = null;
            LocalTransmitting = false;
        }

        void PumpMic(bool online)
        {
            int pos;
            try
            {
                if (!Microphone.IsRecording(micDevice)) { StopMic(); return; }
                pos = Microphone.GetPosition(micDevice);
            }
            catch (Exception) { StopMic(); return; }
            if (pos < 0 || micSamples <= 0) return;

            int avail = pos - readPos;
            if (avail < 0) avail += micSamples;
            // fell far behind (hitch): skip ahead instead of sending stale audio
            if (avail > micSamples / 2)
            {
                readPos = (pos - chunkIn * 2 + micSamples) % micSamples;
                avail = chunkIn * 2;
            }
            int guard = 0;
            while (avail >= chunkIn && guard++ < 8)
            {
                if (!micClip.GetData(micBuf, readPos)) break;
                readPos = (readPos + chunkIn) % micSamples;
                avail -= chunkIn;
                ProcessChunk(online);
            }
        }

        void ProcessChunk(bool online)
        {
            // resample to 640 samples @ 16 kHz (linear) and measure the level
            int n = NetVoiceCodec.FrameSamples;
            float sum = 0f;
            if (chunkIn == n)
            {
                for (int i = 0; i < n; i++) { float s = micBuf[i]; frame[i] = s; sum += s * s; }
            }
            else
            {
                float k = (chunkIn - 1) / (float)(n - 1);
                for (int i = 0; i < n; i++)
                {
                    float x = i * k;
                    int i0 = (int)x;
                    int i1 = Mathf.Min(i0 + 1, chunkIn - 1);
                    float s = micBuf[i0] + (micBuf[i1] - micBuf[i0]) * (x - i0);
                    frame[i] = s;
                    sum += s * s;
                }
            }
            float rms = Mathf.Sqrt(sum / n);
            LocalLevel = Mathf.Clamp01(rms * 6f);

            // gate: push-to-talk / open mic (+ noise gate, F.5) / off / muted
            var d = Settings.Data;
            bool live = Settings.MicLive;
            if (live && d.voiceMode == VoiceMode.OpenMic)
            {
                if (rms >= GateRms) openUntil = Time.unscaledTime + OpenMicHold;
                live = Time.unscaledTime < openUntil;
            }
            if (!live) return;
            SendFrame(online, rms, loopback);
        }

        /// Test hook: one 40 ms sine frame per 40 ms of real time through the normal send path (see TestToneHz).
        void PumpTestTone()
        {
            toneClock += Time.unscaledDeltaTime;
            if (toneClock > 0.25f) toneClock = 0.25f;   // hitch: do not burst
            int n = NetVoiceCodec.FrameSamples;
            float w = 2f * Mathf.PI * TestToneHz / NetVoiceCodec.SampleRate;
            float amp = Mathf.Clamp01(TestToneLevel);
            int guard = 0;
            while (toneClock >= 0.04f && guard++ < 8)
            {
                toneClock -= 0.04f;
                float sum = 0f;
                for (int i = 0; i < n; i++)
                {
                    float s = Mathf.Sin(tonePhase) * amp;
                    tonePhase += w;
                    frame[i] = s;
                    sum += s * s;
                }
                tonePhase = Mathf.Repeat(tonePhase, 2f * Mathf.PI);
                float rms = Mathf.Sqrt(sum / n);
                LocalLevel = Mathf.Clamp01(rms * 6f);
                SendFrame(true, rms, false);
            }
        }

        /// Encodes `frame` and sends it (online) and/or plays it back locally (loopback test).
        void SendFrame(bool online, float rms, bool toLoopback)
        {
            int n = NetVoiceCodec.FrameSamples;
            NetVoiceCodec.EncodeFrame(frame, encoded, n);
            seq++;
            var p = Refs.Player;
            bool under = NetWorldSync.Alive(p) && p.IsUnderwater;
            if (online)   // also in the lobby / while loading: receivers without an avatar hear it non-spatial
            {
                lastSendTime = Time.unscaledTime;
                lastLocalLevel = Mathf.Clamp01(rms * 8f);
                byte fl = 0;
                if (under) fl |= FlagUnder;
                if (localRadio) fl |= FlagRadio;
                if (LocalOnBoat()) fl |= FlagBoat;
                writer.Reset();
                writer.Byte((byte)Mathf.Clamp(Net.LocalId, 0, 255));
                writer.UShort(seq);
                writer.Byte(fl);
                writer.Bytes(encoded, 0, n);
                NetBus.Send(NetSyncMsg.VoiceFrame, writer.ToArray(), NetBus.Everyone, false);
                FramesSent++;
            }
            if (toLoopback)
            {
                lastLoopTime = Time.unscaledTime;
                lastLocalLevel = Mathf.Clamp01(rms * 8f);
                if (loopbackPlayer == null) loopbackPlayer = NetVoicePlayer.Create(transform, Net.LocalId, false);
                NetVoiceCodec.DecodeFrame(encoded, decoded, n);
                loopbackPlayer.Enqueue(decoded, n, seq);
            }
        }

        // ------------------------------------------------------------------ receive
        void OnFrame(int fromId, byte[] payload)
        {
            if (payload == null || payload.Length < 4 + NetVoiceCodec.FrameSamples) return;
            if (Net.IsHost && fromId != Net.LocalId) NetBus.SendExcept(NetSyncMsg.VoiceFrame, payload, fromId, false);
            var r = new SyncReader(payload);
            int origin = r.Byte();
            if (Net.IsHost) origin = fromId;
            if (origin == Net.LocalId) return;
            int s = r.UShort();
            byte speakerFl = r.Byte();
            if (!r.Bytes(encoded, NetVoiceCodec.FrameSamples)) return;
            FramesReceived++;
            if (Settings.Data.voiceMode == VoiceMode.Off) return;   // "Off" also mutes everybody else

            NetVoiceCodec.DecodeFrame(encoded, decoded, NetVoiceCodec.FrameSamples);
            float sum = 0f;
            for (int i = 0; i < decoded.Length; i++) sum += decoded[i] * decoded[i];
            float rms = Mathf.Sqrt(sum / decoded.Length);
            float now = Time.unscaledTime;
            if (rms > 0.004f) lastVoiceTime[origin] = now;
            loudness[origin] = Mathf.Clamp01(rms * 8f);

            if (!players.TryGetValue(origin, out var vp) || vp == null)
            {
                vp = NetVoicePlayer.Create(transform, origin, true);
                players[origin] = vp;
            }
            vp.Enqueue(decoded, NetVoiceCodec.FrameSamples, s);
            speakerFlags[origin] = speakerFl;
        }

        void UpdatePlayers()
        {
            var p = Refs.Player;
            bool alive = NetWorldSync.Alive(p);
            bool listenerUnder = alive && p.IsUnderwater;
            bool listenerBoat = LocalOnBoat();
            Vector3 listenerPos = Vector3.zero;
            if (alive && p.View != null) listenerPos = p.View.transform.position;
            else if (Camera.main != null) listenerPos = Camera.main.transform.position;

            stale.Clear();
            foreach (var kv in players)
            {
                var vp = kv.Value;
                if (vp == null) { stale.Add(kv.Key); continue; }
                int id = kv.Key;
                bool spatial = NetAvatarSync.TryGet(id, out var av) && av.Visible;
                Vector3 pos = spatial ? av.HeadPosition : listenerPos;
                speakerFlags.TryGetValue(id, out byte sf);
                bool su = (sf & FlagUnder) != 0 || (spatial && av.Underwater);
                bool sr = (sf & FlagRadio) != 0;
                bool sb = (sf & FlagBoat) != 0 || (spatial && av.OnBoat);
                var link = NetCoopRules.Link(su, sr, sb, listenerUnder, localRadio, listenerBoat);
                links[id] = link;
                float dist = spatial ? Vector3.Distance(pos, listenerPos) : 0f;
                vp.UpdateOutput(pos, spatial, link, dist);
            }
            for (int i = 0; i < stale.Count; i++) { players.Remove(stale[i]); links.Remove(stale[i]); }

            if (loopbackPlayer != null) loopbackPlayer.UpdateOutput(listenerPos, false, listenerUnder ? VoiceLink.Water : VoiceLink.Air, 0f);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            MicAvailable = false;
            MicDevice = null;
            LocalTransmitting = false;
            LocalLevel = 0f;
            loopback = false;
            TestToneHz = 0f;
            TestToneLevel = 0.08f;
            FramesSent = 0;
            FramesReceived = 0;
            lastVoiceTime.Clear();
            loudness.Clear();
        }
    }
}
