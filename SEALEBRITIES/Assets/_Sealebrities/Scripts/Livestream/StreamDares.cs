using UnityEngine;

namespace Sealebrities
{
    /// One C.5 dare. Risk 0 low / 1 medium / 2 high; reward = k × ExpectedUploadMoney(F); `seconds` to finish once
    /// accepted; `hold` = how long the condition must hold (0 = an instant action).
    public sealed class DareDef
    {
        public string id;
        public byte risk;
        public float k;
        public float seconds;
        public float hold;
        /// DareBits flag that must be available on the target's machine.
        public int bit;
        /// Solo text (imperative) and the co-op text with {p} = the named player.
        public string de, en, coDe, coEn;
        /// Short name for the end card ("Mutprobe: Hai angestupst").
        public string doneDe, doneEn;

        public string Done => Loc.Tr(doneDe, doneEn);
    }

    /// The 11 dares of Design_Runde2 C.5 ("really risky, big money", Q83).
    public static class StreamDares
    {
        public const byte Low = 0, Medium = 1, High = 2;

        public static readonly DareDef[] All =
        {
            new DareDef { id = "dare_starfish", risk = Low, k = 0.3f, seconds = 40f, bit = DareBits.Starfish,
                de = "Stups einen Seestern an!", en = "Boop a starfish!",
                coDe = "{p} soll einen Seestern anstupsen!", coEn = "{p} has to boop a starfish!",
                doneDe = "Seestern angestupst", doneEn = "Starfish booped" },
            new DareDef { id = "dare_three", risk = Low, k = 0.3f, seconds = 30f, bit = DareBits.Three,
                de = "3 Arten in 30 Sekunden filmen!", en = "Film 3 species in 30 s!",
                coDe = "{p} soll 3 Arten in 30 Sekunden filmen!", coEn = "{p} has to film 3 species in 30 s!",
                doneDe = "3 Arten in 30 s", doneEn = "3 species in 30 s" },
            new DareDef { id = "dare_dark", risk = Low, k = 0.35f, seconds = 45f, hold = 20f, bit = DareBits.Dark,
                de = "Lampe aus – 20 Sekunden im Dunkeln!", en = "Lamp off – 20 s in the dark!",
                coDe = "{p}: Lampe aus, 20 Sekunden im Dunkeln!", coEn = "{p}: lamp off, 20 s in the dark!",
                doneDe = "20 s im Dunkeln", doneEn = "20 s in the dark" },
            new DareDef { id = "dare_school", risk = Medium, k = 0.6f, seconds = 45f, bit = DareBits.School,
                de = "Schwimm mitten durch den Fischschwarm!", en = "Swim right through the school!",
                coDe = "{p} soll mitten durch den Schwarm schwimmen!", coEn = "{p} has to swim right through the school!",
                doneDe = "Mitten durch den Schwarm", doneEn = "Right through the school" },
            new DareDef { id = "dare_moray", risk = Medium, k = 0.7f, seconds = 60f, hold = 3f, bit = DareBits.Moray,
                de = "Muräne aus 1,5 m filmen!", en = "Film a moray from 1.5 m!",
                coDe = "{p} soll eine Muräne aus 1,5 m filmen!", coEn = "{p} has to film a moray from 1.5 m!",
                doneDe = "Muräne aus 1,5 m", doneEn = "Moray from 1.5 m" },
            new DareDef { id = "dare_deep5", risk = Medium, k = 0.8f, seconds = 60f, hold = 10f, bit = DareBits.Deep5,
                de = "5 m tiefer als erlaubt – 10 Sekunden!", en = "5 m deeper than allowed – 10 s!",
                coDe = "{p}: 5 m tiefer als erlaubt, 10 Sekunden!", coEn = "{p}: 5 m deeper than allowed, 10 s!",
                doneDe = "5 m zu tief", doneEn = "5 m too deep" },
            new DareDef { id = "dare_lowair", risk = Medium, k = 0.8f, seconds = 120f, bit = DareBits.LowAir,
                de = "Tauch erst unter 15 % Luft auf!", en = "Surface only below 15 % air!",
                coDe = "{p} taucht erst unter 15 % Luft auf!", coEn = "{p} surfaces only below 15 % air!",
                doneDe = "Mit 15 % Luft aufgetaucht", doneEn = "Surfaced at 15 % air" },
            new DareDef { id = "dare_shark", risk = High, k = 1.5f, seconds = 90f, bit = DareBits.Shark,
                de = "Stups den Hai an!", en = "Poke the shark!",
                coDe = "{p} soll den Hai anstupsen!", coEn = "{p} has to poke the shark!",
                doneDe = "Hai angestupst", doneEn = "Shark poked" },
            new DareDef { id = "dare_deep15", risk = High, k = 1.6f, seconds = 90f, hold = 15f, bit = DareBits.Deep15,
                de = "15 m tiefer als erlaubt – 15 Sekunden!", en = "15 m deeper than allowed – 15 s!",
                coDe = "{p}: 15 m tiefer als erlaubt, 15 Sekunden!", coEn = "{p}: 15 m deeper than allowed, 15 s!",
                doneDe = "15 m zu tief", doneEn = "15 m too deep" },
            new DareDef { id = "dare_wreck_dark", risk = High, k = 1.4f, seconds = 60f, hold = 30f, bit = DareBits.WreckDark,
                de = "Lampe aus im Wrack – 30 Sekunden!", en = "Lamp off inside the wreck – 30 s!",
                coDe = "{p}: Lampe aus im Wrack, 30 Sekunden!", coEn = "{p}: lamp off inside the wreck, 30 s!",
                doneDe = "Dunkel im Wrack", doneEn = "Dark in the wreck" },
            new DareDef { id = "dare_angler", risk = High, k = 1.8f, seconds = 90f, hold = 15f, bit = DareBits.Angler,
                de = "Folge dem Licht des Anglerfischs!", en = "Follow the anglerfish's light!",
                coDe = "{p} soll dem Licht des Anglerfischs folgen!", coEn = "{p} has to follow the anglerfish's light!",
                doneDe = "Dem Anglerfisch gefolgt", doneEn = "Followed the anglerfish" },
        };

        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return i;
            return -1;
        }

        public static DareDef At(int i) => i >= 0 && i < All.Length ? All[i] : null;

        /// Localized dare text for the player who has to do it (co-op: names the player).
        public static string Text(DareDef d, int targetId)
        {
            if (d == null) return "";
            if (Net.IsOnline && Net.PlayerCount > 1)
            {
                var p = Net.Find(targetId);
                string name = p != null && !string.IsNullOrEmpty(p.name) ? p.name : Loc.Tr("Taucher", "Diver");
                if (p != null && p.IsLocal) return Loc.Tr(d.de, d.en) + KeySuffix(d);
                return Loc.Tr(d.coDe, d.coEn).Replace("{p}", name) + KeySuffix(d);
            }
            return Loc.Tr(d.de, d.en) + KeySuffix(d);
        }

        /// The poke dares name their key („Stups den Hai an! [E]“) – nobody guessed E before.
        public static bool IsPoke(DareDef d) => d != null && (d.id == "dare_starfish" || d.id == "dare_shark");

        static string keySuffix;
        static int keySuffixVersion = -1;
        static Language keySuffixLang;

        static string KeySuffix(DareDef d)
        {
            if (!IsPoke(d)) return "";
            if (keySuffix == null || keySuffixVersion != GameInput.BindingsVersion || keySuffixLang != Loc.Current)
            {
                keySuffixVersion = GameInput.BindingsVersion;
                keySuffixLang = Loc.Current;
                keySuffix = " [" + GameInput.BindingLabel(GameAction.Interact) + "]";
            }
            return keySuffix;
        }

        public static Color RiskColor(byte risk) =>
            risk == Low ? UIPalette.Leaf : risk == Medium ? UIPalette.Sun : UIPalette.Coral;

        public static string RiskName(byte risk) =>
            risk == Low ? Loc.Tr("leicht", "easy") : risk == Medium ? Loc.Tr("mutig", "brave") : Loc.Tr("irre", "insane");
    }

    /// Checks the running dare on the machine of the player who has to do it (C.5 co-op rule) and reports the result.
    /// Allocation free; Tick runs every frame while a dare targets the local player.
    public sealed class DareChecker
    {
        public enum Result { None, Done, Failed }

        int serial = -1;
        DareDef def;
        float hold, grace, elapsed;
        bool wasUnder, finished;
        readonly SpeciesDef[] seen = new SpeciesDef[8];
        /// dare_three: seconds each species was framed – it counts after DwellSeconds (no instant win on accept)
        readonly float[] seenT = new float[8];
        int seenCount;
        const float DwellSeconds = 1f;
        /// dare_school: the diver must first be outside the school once; dare_dark: the lamp must have been on
        bool schoolLeft, lampSeenOn;

        // shark aftermath: "the shark turns aggressive (bump chance 70 % in the next 10 s)"
        float bumpAt = -1f;

        /// 0..1 progress of a timed/counted condition (overlay bar), -1 = none.
        public float Progress01 = -1f;

        public int Serial => serial;
        public bool Active => def != null && !finished;

        public void Begin(int dareSerial, DareDef d)
        {
            serial = dareSerial;
            def = d;
            hold = 0f; grace = 0f; elapsed = 0f;
            finished = false;
            seenCount = 0;
            for (int i = 0; i < seen.Length; i++) { seen[i] = null; seenT[i] = 0f; }
            schoolLeft = false;
            lampSeenOn = false;
            var p = Refs.Player;
            wasUnder = p != null && !(p is Object o && o == null) && p.IsUnderwater;
            Progress01 = d != null && (d.hold > 0f || d.id == "dare_three") ? 0f : -1f;
        }

        public void Stop()
        {
            def = null;
            finished = true;
            Progress01 = -1f;
        }

        /// Shark aftermath runs on even after the dare finished.
        public void TickAftermath()
        {
            if (bumpAt < 0f || Time.time < bumpAt) return;
            bumpAt = -1f;
            var p = Refs.Player;
            if (p == null || (p is Object o && o == null) || !p.IsUnderwater || p.IsFainting) return;
            GameEvents.RaisePlayerBumped(Random.Range(8f, 14f));
        }

        public Result Tick(float dt, StreamEye eye)
        {
            if (def == null || finished || eye == null || !eye.Valid) return Result.None;
            elapsed += dt;
            switch (def.id)
            {
                case "dare_starfish":
                    if (GameInput.InteractPressed && !UIState.BlocksGameplay)
                    {
                        StreamEye.Nearest(eye.CamPos, BodyPlan.Starfish, null, null, out float d);
                        if (d <= 1.2f) return Finish(Result.Done);
                    }
                    break;
                case "dare_shark":
                    if (GameInput.InteractPressed && !UIState.BlocksGameplay)
                    {
                        StreamEye.Nearest(eye.CamPos, BodyPlan.Shark, null, null, out float d);
                        if (d <= 1.6f)
                        {
                            if (Random.value < StreamRules.SharkBumpChance) bumpAt = Time.time + Random.Range(1.2f, StreamRules.SharkBumpWindow - 2f);
                            return Finish(Result.Done);
                        }
                    }
                    break;
                case "dare_three":
                {
                    if (eye.CameraInHand)
                        for (int i = 0; i < eye.InViewCount; i++) Dwell(eye.InView[i], dt);
                    int counted = 0;
                    for (int i = 0; i < seenCount; i++) if (seenT[i] >= DwellSeconds) counted++;
                    Progress01 = Mathf.Clamp01(counted / 3f);
                    if (counted >= 3) return Finish(Result.Done);
                    break;
                }
                case "dare_school":
                    if (eye.SchoolCount < 5 || eye.SchoolDist > 1.6f) { schoolLeft = true; break; }
                    if (schoolLeft) return Finish(Result.Done);
                    break;
                case "dare_dark":
                    // the dare starts with the lamp on (StreamEye offers it only then): switching it OFF starts the hold
                    if (eye.FlashlightOn) lampSeenOn = true;
                    return Hold(dt, lampSeenOn && !eye.FlashlightOn && eye.Depth > 10f);
                case "dare_moray":
                    return Hold(dt, eye.CameraInHand && eye.MorayInView && eye.DistMoray <= 1.5f);
                case "dare_deep5":
                    return Hold(dt, eye.Underwater && eye.Depth >= eye.MaxSafeDepth + 5f);
                case "dare_deep15":
                    return Hold(dt, eye.Underwater && eye.Depth >= eye.MaxSafeDepth + 15f);
                case "dare_wreck_dark":
                    return Hold(dt, eye.InWreck && !eye.FlashlightOn);
                case "dare_angler":
                    return Hold(dt, eye.Underwater && eye.DistAngler <= 3f);
                case "dare_lowair":
                    if (eye.Underwater) { wasUnder = true; break; }
                    if (wasUnder && !eye.Fainting)
                        return Finish(eye.Air01 < 0.15f ? Result.Done : Result.Failed);
                    break;
            }
            return Result.None;
        }

        /// Adds framing time to a species (dare_three).
        void Dwell(SpeciesDef s, float dt)
        {
            if (s == null) return;
            for (int i = 0; i < seenCount; i++) if (seen[i] == s) { seenT[i] += dt; return; }
            if (seenCount < seen.Length) { seen[seenCount] = s; seenT[seenCount] = dt; seenCount++; }
        }

        /// Continuous condition with a short grace period (the eye scans at 5 Hz; brief flickers do not reset it).
        Result Hold(float dt, bool ok)
        {
            if (ok) { hold += dt; grace = 0f; }
            else
            {
                grace += dt;
                if (grace > 0.75f) hold = 0f;
            }
            Progress01 = def.hold > 0f ? Mathf.Clamp01(hold / def.hold) : 0f;
            return hold >= def.hold ? Finish(Result.Done) : Result.None;
        }

        Result Finish(Result r)
        {
            finished = true;
            if (r == Result.Done) Progress01 = 1f;
            return r;
        }
    }
}
