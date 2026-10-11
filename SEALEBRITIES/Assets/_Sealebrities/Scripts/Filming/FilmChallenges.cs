using System;
using System.Globalization;
using UnityEngine;

namespace Sealebrities
{
    /// K.3 daily photo challenges: today's 2 challenges (Challenges.Today, shared, seeded by the host) are checked
    /// against every photo of the local player (FilmPhotoReport) and – for "FrameCount" – against the live detection
    /// while filming. A met challenge is completed through Challenges.Complete (clients forward ChallengeKey, the host
    /// pays clamp(0.3 × upload money, 40, 400) $ and +10 % views today).
    /// Props / npc may plug precise checks into LanternCounter and InsideWreck; until then simple fallbacks apply
    /// (lantern evening near the village; within the wreck hull around PropInfo.WreckCenter).
    public static class FilmChallenges
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// Optional (props/npc, L.8): number of floating lanterns visible to this lens. Null → fallback rule.
        public static Func<Camera, int> LanternCounter;
        /// Optional (props, H.1): is this point inside the wreck interior? Null → fallback around PropInfo.WreckCenter.
        public static Func<Vector3, bool> InsideWreck;

        /// Fallback "inside the wreck": horizontal distance and height band around PropInfo.WreckCenter.
        public const float WreckFallbackRadius = 9f, WreckFallbackHeight = 4.5f;
        /// Fallback "lanterns in frame": on Lantern Evening within this distance of the village, not looking down.
        public const float LanternFallbackDistance = 160f;

        /// "Dolphin at the bow": the boat's speed counts for this long after it was reached – the driver needs a moment to
        /// let go of the wheel and take the camera (solo), the dolphins keep riding the bow wave meanwhile.
        public const float BoatSpeedMemorySeconds = 8f;

        static float boatPeak, boatPeakTime = -100f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { LanternCounter = null; InsideWreck = null; boatPeak = 0f; boatPeakTime = -100f; }

        /// Called every frame by the hand camera: remembers the boat's recent top speed (BoatTelemetry).
        public static void SampleBoat()
        {
            float now = Time.time;
            float v = BoatTelemetry.Valid ? BoatTelemetry.Speed : 0f;
            if (float.IsNaN(v)) v = 0f;
            if (v >= boatPeak || now - boatPeakTime > BoatSpeedMemorySeconds) { boatPeak = v; boatPeakTime = now; }
        }

        /// The boat's top speed within the last BoatSpeedMemorySeconds (m/s).
        public static float RecentBoatSpeed => Time.time - boatPeakTime <= BoatSpeedMemorySeconds ? boatPeak : 0f;

        /// Checks a finished photo against today's open challenges.
        public static void OnPhoto(FilmPhotoReport r, Camera lens)
        {
            if (r == null || GameManager.I == null || !GameManager.I.InGame) return;
            var today = Challenges.Today;
            for (int i = 0; i < today.Count; i++)
            {
                string id = today[i];
                if (Challenges.IsDone(id)) continue;
                var def = ChallengeDatabase.Get(id);
                if (def == null) continue;
                bool met;
                try { met = CheckPhoto(def, r, lens); }
                catch (Exception e)
                {
                    Debug.LogWarning("[Filming] challenge " + id + " check failed: " + e.Message);
                    met = false;
                }
                if (met) Challenges.Complete(id);
            }
        }

        /// Live check while the camera films / aims (FrameCount: "School: 20+ fish in frame").
        public static void OnTick(FilmDetector det, bool filming)
        {
            if (!filming || det == null || det.Count == 0 || GameManager.I == null || !GameManager.I.InGame) return;
            var today = Challenges.Today;
            for (int i = 0; i < today.Count; i++)
            {
                string id = today[i];
                var def = ChallengeDatabase.Get(id);
                if (def == null || def.kind != "FrameCount" || Challenges.IsDone(id)) continue;
                if (det.Count >= I(def, 0, 20)) Challenges.Complete(id);
            }
        }

        /// One challenge against one photo (kinds: Data/ChallengeDatabase.cs).
        public static bool CheckPhoto(ChallengeDef d, FilmPhotoReport r, Camera lens)
        {
            var sp = r.subject;
            switch (d.kind)
            {
                case "PhotoPitch":
                    return sp != null && FilmTags.Matches(sp, Arg(d, 0)) && r.cameraPitch <= F(d, 1, -45f);
                case "PhotoSpecies":
                    return r.speciesCount >= I(d, 0, 3);
                case "PhotoFill":
                    return sp != null && r.subjectCm <= F(d, 0, 15f) && r.subjectFill >= F(d, 1, 0.5f);
                case "FrameCount":
                    return r.animalCount >= I(d, 0, 20);
                case "PhotoNightLamp":
                    return sp != null && r.lampOn && IsNight(r.timeOfDay);
                case "PhotoSurface":
                    return sp != null && FilmTags.Matches(sp, Arg(d, 0)) && r.subjectDepth <= F(d, 1, 2f);
                case "PhotoTagNear":
                    return sp != null && FilmTags.Matches(sp, Arg(d, 0)) && r.subjectDistance <= F(d, 1, 4f);
                case "SelfieCount":
                    return r.selfie && r.animalCount >= I(d, 0, 5);
                case "Record":
                    return r.isRecord;
                case "Tier":
                    return sp != null && (int)r.subjectTier >= I(d, 0, 3);
                case "PhotoGlowDeep":
                    return sp != null && sp.glows && r.subjectDepth >= F(d, 0, 40f);
                case "PhotoEvent":
                    return !string.IsNullOrEmpty(r.eventId) && r.animalCount > 0;
                case "PhotoLand":
                    return sp != null && sp.land && r.subjectDistance <= F(d, 0, 2f);
                case "PhotoLanterns":
                    return LanternsInFrame(r, lens) >= I(d, 0, 5);
                case "PhotoInWreck":
                    return sp != null && InWreck(r.subjectPos) && InWreck(r.cameraPos);
                case "PhotoBow":
                    return r.HasTag("dolphin") && Mathf.Max(RecentBoatSpeed, BoatTelemetry.Valid ? BoatTelemetry.Speed : 0f) >= F(d, 0, 6f);
                default:
                    return false;
            }
        }

        /// 21:00–04:00.
        public static bool IsNight(float timeOfDay)
        {
            float h = Mathf.Repeat(timeOfDay, 1f) * 24f;
            return h >= 21f || h < 4f;
        }

        static int LanternsInFrame(FilmPhotoReport r, Camera lens)
        {
            if (LanternCounter != null && lens != null)
            {
                try { return LanternCounter(lens); }
                catch (Exception e) { Debug.LogWarning("[Filming] lantern counter failed: " + e.Message); }
            }
            // fallback: Lantern Evening (19–22 h, every 7th day), near the village, the lens above water and not looking down
            if (!Village.IsLanternEvening || r.cameraDepth > 0f || r.cameraPitch < -15f) return 0;
            Vector3 d = r.cameraPos - WorldLayout.Village;
            d.y = 0f;
            return d.magnitude <= LanternFallbackDistance ? 99 : 0;
        }

        static bool InWreck(Vector3 p)
        {
            if (InsideWreck != null)
            {
                try { return InsideWreck(p); }
                catch (Exception e) { Debug.LogWarning("[Filming] wreck check failed: " + e.Message); }
            }
            // the exact interior volume of the MS Clickbait once props built it (H.1)
            if (PropInfo.WreckInterior.size.sqrMagnitude > 0.01f) return PropInfo.InWreckInterior(p);
            Vector3 c = PropInfo.WreckCenter;
            if (c == Vector3.zero) return false;
            Vector3 d = p - c;
            return Mathf.Abs(d.y) <= WreckFallbackHeight && new Vector2(d.x, d.z).magnitude <= WreckFallbackRadius;
        }

        static string Arg(ChallengeDef d, int i) => d.args != null && i < d.args.Length ? d.args[i] : null;

        static float F(ChallengeDef d, int i, float def)
        {
            string s = Arg(d, i);
            return s != null && float.TryParse(s, NumberStyles.Float, Inv, out float v) ? v : def;
        }

        static int I(ChallengeDef d, int i, int def)
        {
            string s = Arg(d, i);
            return s != null && int.TryParse(s, NumberStyles.Integer, Inv, out int v) ? v : def;
        }
    }
}
