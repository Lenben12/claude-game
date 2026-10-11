using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// The npc+story module's brain (A, B, K.5, K.8, L.7, L.8, L.12 lines). One instance under the world root.
    ///   • builds every NPC actor and story vehicle once, hides them in the menu;
    ///   • HOST / offline: runs the schedules, the prologue, Horst's tutorial beats and gags, visitors (Dr. Fin, the
    ///     Bottom brothers, Wanda), the village Altriff, Toni's chain, Gus' missions, side quests and research, and every
    ///     conversation (NpcNet replicates actors, vehicles and lines to co-op clients);
    ///   • EVERY machine: animation, babble voices, subtitles (fallback overlay), radio calls (local – triggered by
    ///     shared state), quest objects, local photo/clip/recording checks (reported to the host), the quest-line feed.
    /// Partial files: NpcDirector.Story.cs (prologue, Horst, Gus), .Visitors.cs (Wanda, Dr. Fin, rivals),
    /// .Village.cs (Altriff, Toni, lanterns, canoes), .Quests.cs (side quests, research, objects, tracker).
    [DefaultExecutionOrder(80)]
    public sealed partial class NpcDirector : MonoBehaviour
    {
        public static NpcDirector I { get; private set; }

        NpcActor[] actors = new NpcActor[0];
        NpcVehicle[] vehicles = new NpcVehicle[0];
        public const int VYacht = 0, VCat = 1, VSub = 2, VChopper = 3, VCanoe1 = 4, VCanoe2 = 5;
        NpcGull gull;
        Transform worldRoot, npcRoot;
        bool built;
        float thinkTimer, netTimer;
        Camera cam;

        // ---------------------------------------------------------------- lifecycle
        public static NpcDirector Create(Transform parent)
        {
            var go = new GameObject("SB_NpcDirector");
            go.transform.SetParent(parent, false);
            var d = go.AddComponent<NpcDirector>();
            d.worldRoot = parent;
            return d;
        }

        void Awake()
        {
            I = this;
            NpcBabble.Create(transform);
            NpcOverlay.Create(transform);
            GameEvents.GameStarted += OnGameStarted;
            GameEvents.ReturnedToMenu += OnReturnedToMenu;
            GameEvents.DayStarted += OnDayStarted;
            GameEvents.MilestoneReached += OnMilestone;
            GameEvents.StoryFlagSet += OnStoryFlag;
            GameEvents.FishCaught += OnFishCaught;
            GameEvents.UploadCreated += OnUploadCreated;
            GameEvents.UploadResolved += OnUploadResolved;
            GameEvents.ClipRecorded += OnClipRecorded;
            GameEvents.Sighted += OnSighted;
            GameEvents.DeliveryReceived += OnDeliveryReceived;
            GameEvents.DeliveryOrdered += OnDeliveryOrdered;
            GameEvents.DeliveryMishapHappened += OnMishap;
            GameEvents.FoodEaten += OnFoodEaten;
            GameEvents.MissionChanged += OnMissionChanged;
            GameEvents.SideQuestChanged += OnSideQuestChanged;
            GameEvents.LanguageChanged += OnLanguage;
            GameEvents.CarryChanged += OnCarryChanged;
            Gallery.Added += OnPhoto;
            Net.StateSynced += OnStateSynced;
            Net.PlayerLeft += OnPlayerLeft;
            Dialogue.Resolve = DialogueDatabase.Resolve;
        }

        void Start()
        {
            // installed late (e.g. -host started a game during the boot): catch up with the running game
            if (InGame && !built) OnGameStarted();
        }

        void OnDestroy()
        {
            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.ReturnedToMenu -= OnReturnedToMenu;
            GameEvents.DayStarted -= OnDayStarted;
            GameEvents.MilestoneReached -= OnMilestone;
            GameEvents.StoryFlagSet -= OnStoryFlag;
            GameEvents.FishCaught -= OnFishCaught;
            GameEvents.UploadCreated -= OnUploadCreated;
            GameEvents.UploadResolved -= OnUploadResolved;
            GameEvents.ClipRecorded -= OnClipRecorded;
            GameEvents.Sighted -= OnSighted;
            GameEvents.DeliveryReceived -= OnDeliveryReceived;
            GameEvents.DeliveryOrdered -= OnDeliveryOrdered;
            GameEvents.DeliveryMishapHappened -= OnMishap;
            GameEvents.FoodEaten -= OnFoodEaten;
            GameEvents.MissionChanged -= OnMissionChanged;
            GameEvents.SideQuestChanged -= OnSideQuestChanged;
            GameEvents.LanguageChanged -= OnLanguage;
            GameEvents.CarryChanged -= OnCarryChanged;
            Gallery.Added -= OnPhoto;
            Net.StateSynced -= OnStateSynced;
            Net.PlayerLeft -= OnPlayerLeft;
            if (I == this) I = null;
        }

        void EnsureBuilt()
        {
            if (built) return;
            built = true;
            npcRoot = new GameObject("NPCs").transform;
            npcRoot.SetParent(worldRoot != null ? worldRoot : transform, false);
            var defs = NpcDatabase.All;
            actors = new NpcActor[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                if (!NpcDatabase.HasActor(defs[i])) continue;
                try
                {
                    var go = NpcBuilder.Build(defs[i].id);
                    go.transform.SetParent(npcRoot, false);
                    var a = go.AddComponent<NpcActor>();
                    a.Init(defs[i], i, go.GetComponent<NpcBody>());
                    a.SetPresent(false);
                    go.SetActive(false);
                    actors[i] = a;
                }
                catch (Exception e) { Debug.LogWarning("[Npc] building " + defs[i].id + " failed: " + e.Message); }
            }
            gull = new NpcGull(npcRoot);
            var vl = new List<NpcVehicle>();
            vl.Add(Vehicle("clout_nine", VYacht, NpcVehicleBuilder.Yacht()));
            vl.Add(Vehicle("rv_plankton", VCat, NpcVehicleBuilder.Catamaran()));
            var sub = Vehicle("bottom_line", VSub, NpcVehicleBuilder.MiniSub());
            sub.Floating = false;
            vl.Add(sub);
            var chop = Vehicle("wanda_chopper", VChopper, NpcVehicleBuilder.Chopper(out var rotor, out var tail));
            chop.Floating = false;
            chop.Rotor = rotor;
            chop.TailRotor = tail;
            vl.Add(chop);
            vl.Add(Vehicle("canoe1", VCanoe1, NpcVehicleBuilder.Canoe(2)));
            vl.Add(Vehicle("canoe2", VCanoe2, NpcVehicleBuilder.Canoe(2)));
            vehicles = vl.ToArray();
            BuildDressing();
            Debug.Log("[Npc] built " + CountActors() + " NPCs, " + vehicles.Length + " vehicles, " + DialogueDatabase.Count + " lines");
        }

        NpcVehicle Vehicle(string id, int index, GameObject go)
        {
            go.transform.SetParent(npcRoot, false);
            go.SetActive(false);
            return new NpcVehicle(id, index, go.transform) { Present = false };
        }

        int CountActors()
        {
            int n = 0;
            for (int i = 0; i < actors.Length; i++) if (actors[i] != null) n++;
            return n;
        }

        public NpcActor ActorAt(int index) => index >= 0 && index < actors.Length ? actors[index] : null;
        public NpcVehicle VehicleAt(int index) => index >= 0 && index < vehicles.Length ? vehicles[index] : null;

        public NpcActor Actor(string id)
        {
            int i = NpcDatabase.IndexOf(id);
            return i >= 0 && i < actors.Length ? actors[i] : null;
        }

        static bool InGame => GameManager.I != null && GameManager.I.InGame;
        static GameState S => GameManager.I != null ? GameManager.I.State : null;

        // ---------------------------------------------------------------- session
        void OnGameStarted()
        {
            EnsureBuilt();
            QuestSpots.ClearCache();
            ResetSession();
            for (int i = 0; i < actors.Length; i++)
                if (actors[i] != null) { actors[i].SetPresent(false); actors[i].ResetNet(); actors[i].Motion = NpcMotion.Idle; actors[i].MarkerValue = 0; actors[i].Flags = 0; }
            for (int i = 0; i < vehicles.Length; i++) vehicles[i].Present = false;
            Dialogue.Resolve = DialogueDatabase.Resolve;
            if (Net.HasAuthority) HostSessionStart();
            ClientSessionStart();
            thinkTimer = 0.2f;
        }

        void OnReturnedToMenu()
        {
            ResetSession();
            for (int i = 0; i < actors.Length; i++)
                if (actors[i] != null) { actors[i].SetPresent(false); actors[i].gameObject.SetActive(false); }
            for (int i = 0; i < vehicles.Length; i++) { vehicles[i].Present = false; vehicles[i].Root.gameObject.SetActive(false); }
            if (gull != null) gull.Root.gameObject.SetActive(false);
            QuestTracker.SetEntry(QuestSource.Story, null, null, null, null);
            QuestTracker.SetEntry(QuestSource.Side, null, null, null, null);
            ClearObjects();
        }

        void ResetSession()
        {
            convs.Clear();
            radioQueue.Clear();
            radioCurrent = null;
            clientConvAnchor = -1;
            clientConvUntil = 0f;
            StoryUI.ConversationHint = false;
            StoryUI.OfferAccept = null;
            StoryUI.OfferDecline = null;
            for (int i = 0; i < offers.Length; i++) offers[i] = default;
            playedLocal.Clear();
            ResetStorySession();
            ResetVisitorSession();
            ResetVillageSession();
            ResetQuestSession();
        }

        /// HOST: a guest left (disconnect / back to the menu) while carrying a quest pickup → it respawns at its spot.
        void OnPlayerLeft(NetPlayerInfo p)
        {
            if (!Net.HasAuthority || p == null || string.IsNullOrEmpty(p.key)) return;
            var rec = PlayerRecords.Find(p.key);
            if (rec == null || string.IsNullOrEmpty(rec.carried)) return;
            string c = rec.carried;
            rec.carried = null;
            HostObjectLost(c);   // ignores ids that are no quest items
            objectsDirty = true;
            trackerDirty = true;
        }

        void OnLanguage() { trackerDirty = true; }

        // ---------------------------------------------------------------- update
        void Update()
        {
            if (!built || !InGame) { StoryUI.ConversationHint = false; return; }
            float dt = Time.deltaTime;
            if (cam == null || !cam.isActiveAndEnabled) cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : NpcUtil.LocalPlayerPos;

            if (Net.HasAuthority)
            {
                thinkTimer -= dt;
                if (thinkTimer <= 0f)
                {
                    thinkTimer = 0.1f;
                    try { HostThink(); }
                    catch (Exception e) { Debug.LogException(e); thinkTimer = 1f; }
                }
                for (int i = 0; i < actors.Length; i++) if (actors[i] != null) actors[i].HostTick(dt);
                for (int i = 0; i < vehicles.Length; i++) vehicles[i].HostTick(dt);
                TickConversations(dt);
                if (Net.IsHost)
                {
                    netTimer -= Time.unscaledDeltaTime;
                    if (netTimer <= 0f) { netTimer = 0.25f; NpcNet.SendState(actors, vehicles); }
                }
            }

            // every machine: visuals
            for (int i = 0; i < actors.Length; i++)
            {
                var a = actors[i];
                if (a == null) continue;
                a.Animate(dt, camPos, LookTargetFor(a));
            }
            for (int i = 0; i < vehicles.Length; i++) vehicles[i].Animate(dt, camPos);
            if (gull != null) gull.Tick(dt, Actor(NpcDatabase.Horst), camPos);

            try
            {
                LocalInput(dt);
                TickRadio(dt);
                LocalTick(dt);
                TickObjects(dt);
                TickTracker(dt);
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// The nearest diver's head within 8 m (NPCs look at you), else the current conversation partner.
        Vector3? LookTargetFor(NpcActor a)
        {
            var t = Divers.Nearest(a.Position, out float d);
            if (t != null && d < 8f) return t.position + Vector3.up * 1.5f;
            return null;
        }

        // ================================================================ conversations (HOST)
        sealed class Conv
        {
            public NpcActor anchor;
            public readonly List<string> ids = new List<string>(8);
            public readonly List<string> speakers = new List<string>(8);
            public string[] args;
            public int index = -1;
            public float timer;
            public bool paused, skippable = true;
            public float radius = 25f;
            public Action onDone;
            public string offerQuest;
            public bool offerChoice;
            public string key;
        }

        readonly List<Conv> convs = new List<Conv>(6);

        /// HOST: plays a conversation (a Seq prefix or a single line id) anchored at an NPC. Returns false when the NPC
        /// already talks (the request is dropped). speakerOverride replaces the line's default speaker.
        bool Talk(NpcActor anchor, string prefixOrId, Action done = null, string speakerOverride = null, string[] args = null, float radius = 25f)
        {
            if (!Net.HasAuthority) return false;
            if (anchor != null && IsTalking(anchor)) return false;
            var c = new Conv { anchor = anchor, args = args, radius = radius, onDone = done, key = prefixOrId };
            Append(c, prefixOrId, speakerOverride);
            if (c.ids.Count == 0) { done?.Invoke(); return true; }
            convs.Add(c);
            if (anchor != null) anchor.Flags |= NpcActor.FlagConversation;
            return true;
        }

        static void Append(Conv c, string prefixOrId, string speakerOverride)
        {
            var seq = DialogueDatabase.Seq(prefixOrId);
            if (seq.Count > 0)
            {
                for (int i = 0; i < seq.Count; i++) { c.ids.Add(seq[i]); c.speakers.Add(speakerOverride ?? DialogueDatabase.SpeakerOf(seq[i])); }
            }
            else if (DialogueDatabase.Has(prefixOrId))
            {
                c.ids.Add(prefixOrId);
                c.speakers.Add(speakerOverride ?? DialogueDatabase.SpeakerOf(prefixOrId));
            }
        }

        /// HOST: adds lines to the running conversation of an anchor (or starts one).
        void TalkMore(NpcActor anchor, string prefixOrId, string speakerOverride = null)
        {
            var c = ConvOf(anchor);
            if (c == null) { Talk(anchor, prefixOrId, null, speakerOverride); return; }
            Append(c, prefixOrId, speakerOverride);
        }

        Conv ConvOf(NpcActor anchor)
        {
            for (int i = 0; i < convs.Count; i++) if (convs[i].anchor == anchor) return convs[i];
            return null;
        }

        bool IsTalking(NpcActor a) => ConvOf(a) != null;

        bool AnyoneNear(Vector3 p, float r)
        {
            var all = Divers.All;
            float r2 = r * r;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var t = all[i];
                if (t == null) continue;
                if ((t.position - p).sqrMagnitude <= r2) return true;
            }
            return false;
        }

        void TickConversations(float dt)
        {
            for (int i = convs.Count - 1; i >= 0; i--)
            {
                var c = convs[i];
                Vector3 at = c.anchor != null ? c.anchor.Position : NpcUtil.LocalPlayerPos;
                bool near = c.anchor == null || AnyoneNear(at, c.radius > 25f ? c.radius : 12f);
                if (!near)
                {
                    // walking away > 12 m pauses the beat; returning replays the current line
                    if (!c.paused) c.paused = true;
                    continue;
                }
                if (c.paused)
                {
                    c.paused = false;
                    if (c.index >= 0) { c.index--; c.timer = 0f; }
                }
                c.timer -= dt;
                if (c.timer > 0f) continue;
                c.index++;
                if (c.index >= c.ids.Count) { FinishConv(i, false); continue; }
                c.timer = PlayLine(c, c.index);
            }
        }

        float PlayLine(Conv c, int i)
        {
            string id = c.ids[i], speaker = c.speakers[i] ?? DialogueDatabase.SpeakerOf(id);
            string text = DialogueDatabase.Resolve(id, c.args);
            float seconds = DialogueDatabase.SecondsFor(text);
            int anchor = c.anchor != null ? c.anchor.Index : -1;
            if (c.anchor == null || NpcUtil.FlatDistSq(NpcUtil.LocalPlayerPos, c.anchor.Position) <= c.radius * c.radius)
                Dialogue.PlayLocal(speaker, id, seconds, c.args);
            NpcNet.SendLine(speaker, id, seconds, anchor, c.args);
            var sp = Actor(speaker);
            if (sp != null) { sp.BeginTalking(seconds); if (c.anchor != null && sp != c.anchor) sp.FacePoint = c.anchor.Position; }
            hostConvAnchor = anchor;
            hostConvUntil = Time.time + seconds + 0.6f;
            if (GameManager.AutomatedRun) Debug.Log("[Npc] line " + id);
            return seconds + 0.25f;
        }

        void FinishConv(int i, bool skipped)
        {
            var c = convs[i];
            convs.RemoveAt(i);
            if (c.anchor != null)
            {
                c.anchor.Flags &= unchecked((byte)~NpcActor.FlagConversation);
                if (c.offerQuest != null) OpenOffer(c.anchor, c.offerQuest, c.offerChoice);
            }
            try { c.onDone?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// HOST: next line / skip the whole conversation of an anchor (any listener may request it).
        void ConvNext(int anchorIndex, bool skipAll)
        {
            for (int i = 0; i < convs.Count; i++)
            {
                var c = convs[i];
                int idx = c.anchor != null ? c.anchor.Index : -1;
                if (idx != anchorIndex) continue;
                if (skipAll && c.skippable) { FinishConv(i, true); return; }
                c.timer = Mathf.Min(c.timer, 0.05f);
                return;
            }
        }

        // ================================================================ offers (quest accept / decline / choice)
        struct Offer { public string quest; public float until; public bool choice; }
        readonly Offer[] offers = new Offer[32];

        void OpenOffer(NpcActor a, string quest, bool choice)
        {
            if (a == null || a.Index >= offers.Length) return;
            offers[a.Index] = new Offer { quest = quest, until = Time.time + 18f, choice = choice };
            a.Flags |= choice ? (byte)(NpcActor.FlagOffer | NpcActor.FlagChoice) : NpcActor.FlagOffer;
        }

        void CloseOffer(NpcActor a)
        {
            if (a == null || a.Index >= offers.Length) return;
            offers[a.Index] = default;
            a.Flags &= unchecked((byte)~(NpcActor.FlagOffer | NpcActor.FlagChoice));
        }

        void TickOffers()
        {
            for (int i = 0; i < actors.Length && i < offers.Length; i++)
            {
                if (offers[i].quest == null) continue;
                var a = actors[i];
                if (a == null || Time.time > offers[i].until || !a.Present || !AnyoneNear(a.Position, 10f)) CloseOffer(a);
            }
        }

        // ================================================================ requests (HOST) – local or from clients
        /// Runs a request from a player (fromId) on the host: talk / next / skip / accept / decline / letter / evt / obj …
        public static void HostRequest(int fromId, string op, string a, string b, string c, string d)
        {
            var dir = I;
            if (dir == null || !Net.HasAuthority || !InGame) return;
            try { dir.HandleRequest(fromId, op, a, b, c, d); }
            catch (Exception e) { Debug.LogWarning("[Npc] request " + op + " failed: " + e.Message); }
        }

        void HandleRequest(int fromId, string op, string a, string b, string c, string d)
        {
            switch (op)
            {
                case "talk": OnTalkRequest(fromId, Actor(a)); break;
                case "next": ConvNext(NpcNet.ParseI(a, -1), false); break;
                case "skip": ConvNext(NpcNet.ParseI(a, -1), true); if (NpcNet.ParseI(a, -1) == -2) SkipPrologue(); break;
                case "accept": OnAccept(fromId, Actor(a), false); break;
                case "decline": OnAccept(fromId, Actor(a), true); break;
                case "letter": OnLetterClosed(); break;
                case "evt": HostEvent(fromId, a, b, c, d); break;
                case "obj": HostObject(fromId, a, b); break;
                case "carry": SetRemoteCarry(fromId, a); break;
            }
        }

        /// The interaction prompt of an NPC for the LOCAL player (cached strings – no per-frame allocation).
        public static string PromptFor(NpcActor a)
        {
            var dir = I;
            if (dir == null || a == null) return null;
            if ((a.Flags & NpcActor.FlagChoice) != 0) return Prompts.Get("give", "Zurückgeben", "Give back");
            if ((a.Flags & NpcActor.FlagOffer) != 0) return Prompts.Get("accept", "Annehmen", "Accept");
            if ((a.Flags & NpcActor.FlagConversation) != 0) return Prompts.Get("next", "Weiter", "Next");
            if (Carry.IsCarrying && a.MarkerValue == 2) return Prompts.Get("handin", "Übergeben", "Hand over");
            return Prompts.Name(a.Def);
        }

        public static bool CanInteract(NpcActor a)
        {
            if (a == null || I == null) return false;
            if (a.Motion == NpcMotion.Swim) return false;
            return true;
        }

        /// The local player pressed E on an NPC.
        public static void LocalInteract(NpcActor a)
        {
            if (a == null || I == null) return;
            if ((a.Flags & NpcActor.FlagOffer) != 0) { NpcNet.Request("accept", a.Id); return; }
            if ((a.Flags & NpcActor.FlagConversation) != 0) { NpcNet.Request("next", NpcNet.I(a.Index)); return; }
            NpcNet.Request("talk", a.Id);
        }

        // ================================================================ client side
        int clientConvAnchor = -1;
        float clientConvUntil;
        int hostConvAnchor = -1;
        float hostConvUntil;

        /// A line from the host: shown when the local player is within 25 m of the anchor NPC (or always for anchor −1).
        public static void ClientLine(string speaker, string id, float seconds, int anchor, string[] args)
        {
            var dir = I;
            if (dir == null) return;
            var a = dir.ActorAt(anchor);
            if (anchor >= 0 && a != null && NpcUtil.FlatDistSq(NpcUtil.LocalPlayerPos, a.Position) > 25f * 25f) return;
            Dialogue.PlayLocal(speaker, id, seconds, args);
            var sp = dir.Actor(speaker);
            if (sp != null) sp.BeginTalking(seconds);
            dir.clientConvAnchor = anchor;
            dir.clientConvUntil = Time.time + seconds + 0.8f;
        }

        public static void ClientEvent(byte ev, string a, string b)
        {
            var dir = I;
            if (dir == null) return;
            switch (ev)
            {
                case NpcNet.EvLetter: dir.ShowLetterLocal(); break;
                case NpcNet.EvDropCarry: if (Carry.Current == a) Carry.Drop(); break;
                case NpcNet.EvPickCarry: if (!Carry.IsCarrying) Carry.Pick(a); break;
                case NpcNet.EvGullSteal: if (dir.gull != null) dir.gull.Steal(); break;
                case NpcNet.EvNotify: dir.LocalNotify(a, b); break;
            }
        }

        // ================================================================ local input (E next / hold E skip / Q decline / letter)
        float holdE;
        bool skipSent;

        void LocalInput(float dt)
        {
            if (UIState.BlocksGameplay || UIState.TextInput) { StoryUI.ConversationHint = false; holdE = 0f; return; }
            // letter card (fallback): E / LMB closes
            if (StoryUI.LetterOpen)
            {
                if (GameInput.InteractPressed || GameInput.UsePressed) StoryUI.CloseLetter();
                StoryUI.ConversationHint = false;
                return;
            }
            Vector3 me = NpcUtil.LocalPlayerPos;
            int anchor = -1;
            bool listening = false;
            if (Net.HasAuthority)
            {
                for (int i = 0; i < convs.Count; i++)
                {
                    var c = convs[i];
                    if (c.anchor == null) continue;
                    if (NpcUtil.FlatDistSq(me, c.anchor.Position) <= 12f * 12f) { listening = true; anchor = c.anchor.Index; break; }
                }
            }
            else if (clientConvAnchor >= 0 && Time.time < clientConvUntil)
            {
                var a = ActorAt(clientConvAnchor);
                if (a != null && NpcUtil.FlatDistSq(me, a.Position) <= 12f * 12f) { listening = true; anchor = clientConvAnchor; }
            }
            bool radioOn = radioCurrent != null;
            StoryUI.ConversationHint = listening;

            bool freeKey = InteractionPrompt.Text == null || IsOwnPrompt(InteractionPrompt.Text);
            bool consumed = false;   // this E press already skipped a line – it must not also accept an offer
            if (listening || radioOn)
            {
                // E (not aimed at something else) or LMB with empty hands = next line (A.2)
                bool lmb = GameInput.UsePressed && Hands.Active(Hand.Right) == HoldItem.None && Hands.Active(Hand.Left) == HoldItem.None;
                if ((GameInput.InteractPressed && InteractionPrompt.Text == null) || lmb)
                {
                    if (listening) NpcNet.Request("next", NpcNet.I(anchor));
                    else RadioNext();
                    consumed = true;
                }
                if (GameInput.Held(GameAction.Interact) && freeKey)
                {
                    holdE += dt;
                    StoryUI.SkipProgress = Mathf.Clamp01(holdE / 1f);
                    if (holdE >= 1f && !skipSent)
                    {
                        skipSent = true;
                        if (listening) NpcNet.Request("skip", NpcNet.I(prologueActive ? -2 : anchor));
                        else RadioSkip();
                    }
                }
                else { holdE = 0f; skipSent = false; StoryUI.SkipProgress = 0f; }
            }
            else { holdE = 0f; skipSent = false; StoryUI.SkipProgress = 0f; }

            // open offer at a nearby NPC: chip "[E] Annehmen · [Q] Später"
            NpcActor offerNpc = null;
            for (int i = 0; i < actors.Length; i++)
            {
                var a = actors[i];
                if (a == null || (a.Flags & NpcActor.FlagOffer) == 0) continue;
                if (NpcUtil.FlatDistSq(me, a.Position) <= 6.5f * 6.5f) { offerNpc = a; break; }
            }
            if (offerNpc != null)
            {
                bool choice = (offerNpc.Flags & NpcActor.FlagChoice) != 0;
                StoryUI.OfferAccept = choice ? Prompts.Get("give", "Zurückgeben", "Give back") : Prompts.Get("accept", "Annehmen", "Accept");
                StoryUI.OfferDecline = choice ? Prompts.Key("keep", GameAction.Anchor, "Behalten", "Keep") : Prompts.Key("later", GameAction.Anchor, "Später", "Later");
                if (!consumed && GameInput.InteractPressed && InteractionPrompt.Text == null) NpcNet.Request("accept", offerNpc.Id);
                else if (GameInput.AnchorPressed && !Refs_PlayerOnBoat()) NpcNet.Request("decline", offerNpc.Id);
            }
            else { StoryUI.OfferAccept = null; StoryUI.OfferDecline = null; }
        }

        /// Q also toggles the anchor for anyone aboard (BoatController) – aboard, Q never declines an offer (it expires
        /// after 18 s or is declined from the dock).
        static bool Refs_PlayerOnBoat() => Refs.Boat != null && !(Refs.Boat is UnityEngine.Object o && o == null) && (Refs.Boat.PlayerDriving || Refs.Boat.PlayerAboard);

        static bool IsOwnPrompt(string text)
        {
            var dir = I;
            if (dir == null) return false;
            for (int i = 0; i < dir.actors.Length; i++)
                if (dir.actors[i] != null && ReferenceEquals(PromptFor(dir.actors[i]), text)) return true;
            return false;
        }

        // ================================================================ radio (LOCAL per machine)
        sealed class RadioCall
        {
            public string key, caller;
            public readonly List<string> ids = new List<string>(6);
            public readonly List<string> speakers = new List<string>(6);
            public string[] args;
            public int index = -1;
            public float timer;
            public bool underwaterOk;
        }

        readonly List<RadioCall> radioQueue = new List<RadioCall>(6);
        RadioCall radioCurrent;
        readonly HashSet<string> playedLocal = new HashSet<string>();
        float radioGap;

        /// Queues a radio call on THIS machine (Gus, Wanda's mishap calls, Horst's beach alarm, the brothers' SOS).
        /// key = de-duplication key (null = always). Calls wait while the player is under water (unless they own the
        /// underwater radio, E.5) and while an NPC conversation runs nearby.
        void Radio(string caller, string prefixOrId, string key = null, string[] args = null, string speakerOverride = null)
        {
            if (key != null)
            {
                if (playedLocal.Contains(key)) return;
                playedLocal.Add(key);
                for (int i = 0; i < radioQueue.Count; i++) if (radioQueue[i].key == key) return;
            }
            var call = new RadioCall { key = key, caller = caller, args = args };
            var seq = DialogueDatabase.Seq(prefixOrId);
            if (seq.Count > 0) for (int i = 0; i < seq.Count; i++) { call.ids.Add(seq[i]); call.speakers.Add(speakerOverride ?? DialogueDatabase.SpeakerOf(seq[i])); }
            else if (DialogueDatabase.Has(prefixOrId)) { call.ids.Add(prefixOrId); call.speakers.Add(speakerOverride ?? DialogueDatabase.SpeakerOf(prefixOrId)); }
            if (call.ids.Count == 0) return;
            radioQueue.Add(call);
        }

        void TickRadio(float dt)
        {
            if (radioCurrent == null)
            {
                if (radioQueue.Count == 0) return;
                radioGap -= dt;
                if (radioGap > 0f) return;
                bool under = NpcUtil.LocalUnderwater && !(GameManager.I != null && GameManager.I.Gear.hasRadio);
                bool busy = Time.time < clientConvUntil || Time.time < hostConvUntil && hostConvAnchor >= 0 && NearAnchor(hostConvAnchor);
                if (under || busy || UIState.BlocksGameplay) return;
                radioCurrent = radioQueue[0];
                radioQueue.RemoveAt(0);
                radioCurrent.index = -1;
                radioCurrent.timer = 0.4f;
                GameEvents.RaiseRadioCall(radioCurrent.caller, true);
                return;
            }
            radioCurrent.timer -= dt;
            if (radioCurrent.timer > 0f) return;
            radioCurrent.index++;
            if (radioCurrent.index >= radioCurrent.ids.Count) { EndRadio(); return; }
            string id = radioCurrent.ids[radioCurrent.index];
            string text = DialogueDatabase.Resolve(id, radioCurrent.args);
            float s = DialogueDatabase.SecondsFor(text);
            Dialogue.PlayLocal(radioCurrent.speakers[radioCurrent.index], id, s, radioCurrent.args);
            if (GameManager.AutomatedRun) Debug.Log("[Npc] radio " + id);
            radioCurrent.timer = s + 0.2f;
        }

        bool NearAnchor(int anchor)
        {
            var a = ActorAt(anchor);
            return a != null && NpcUtil.FlatDistSq(NpcUtil.LocalPlayerPos, a.Position) < 25f * 25f;
        }

        void EndRadio()
        {
            if (radioCurrent == null) return;
            GameEvents.RaiseRadioCall(radioCurrent.caller, false);
            radioCurrent = null;
            radioGap = 1.2f;
        }

        void RadioNext() { if (radioCurrent != null) radioCurrent.timer = Mathf.Min(radioCurrent.timer, 0.05f); }
        void RadioSkip() { if (radioCurrent != null) { radioCurrent.index = radioCurrent.ids.Count; radioCurrent.timer = 0f; } }

        // ================================================================ notifications (local)
        /// Shows a notification on this machine. key = "radio:<prefix>" (radio call), else a Notify line id pair "de|en".
        void LocalNotify(string key, string arg)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (key.StartsWith("radio:", StringComparison.Ordinal))
            {
                string pre = key.Substring(6);
                Radio(arg ?? DialogueDatabase.SpeakerOf(pre + ".1") ?? DialogueDatabase.SpeakerOf(pre) ?? NpcDatabase.Gus, pre, "net:" + pre + ":" + NpcUtil.Day);
                return;
            }
            if (key.StartsWith("banner:", StringComparison.Ordinal))
            {
                if (key == "banner:duel") GameEvents.RaiseBanner(Loc.Tr("Duell gewonnen!\n+5 % Aufrufe", "Duel won!\n+5 % views"), new Color(0.95f, 0.79f, 0.3f));
                return;
            }
        }

        /// HOST: a radio call on every machine (host local + broadcast).
        void RadioAll(string caller, string prefix)
        {
            Radio(caller, prefix, "net:" + prefix + ":" + NpcUtil.Day);
            NpcNet.SendEvent(NpcNet.EvNotify, "radio:" + prefix, caller);
        }

        // ================================================================ prompts (cached per language + binding)
        static class Prompts
        {
            static readonly Dictionary<string, string> cache = new Dictionary<string, string>();
            static Language lang;
            static string ekey;

            static void Check()
            {
                string k = GameInput.BindingLabel(GameAction.Interact);
                if (lang != Loc.Current || !ReferenceEquals(k, ekey)) { cache.Clear(); lang = Loc.Current; ekey = k; }
            }

            public static string Get(string id, string de, string en)
            {
                Check();
                if (!cache.TryGetValue(id, out var s)) { s = (ekey ?? "E") + " – " + Loc.Tr(de, en); cache[id] = s; }
                return s;
            }

            public static string Key(string id, GameAction a, string de, string en)
            {
                Check();
                string k = "k_" + id;
                if (!cache.TryGetValue(k, out var s)) { s = GameInput.BindingLabel(a) + " – " + Loc.Tr(de, en); cache[k] = s; }
                return s;
            }

            public static string Name(NpcDef d)
            {
                Check();
                string k = "n_" + d.id;
                if (!cache.TryGetValue(k, out var s)) { s = (ekey ?? "E") + " – " + d.Name; cache[k] = s; }
                return s;
            }
        }

        // ================================================================ helpers
        static void Notify(NotifyKind kind, string de, string en, string chip = null) => GameEvents.RaiseNotify(kind, Loc.Tr(de, en), chip);

        static void Pay(long money)
        {
            if (money <= 0 || GameManager.I == null) return;
            GameManager.I.AddMoney(money);
        }

        static float Hour => NpcUtil.Hour;

        static bool Between(float h, float from, float to) => from <= to ? h >= from && h < to : h >= from || h < to;

        /// Nearest diver transform within r (null when none).
        static Transform NearestDiver(Vector3 p, float r)
        {
            var t = Divers.Nearest(p, out float d);
            return t != null && d <= r ? t : null;
        }

        /// The diver whose position belongs to player id (local player for the host's own id).
        static Vector3 PlayerPos(int playerId)
        {
            if (playerId == Net.LocalId || !Net.IsOnline) return NpcUtil.LocalPlayerPos;
            var all = Divers.All;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null) continue;
                var na = t.GetComponentInParent<NetAvatar>();
                if (na != null && na.PlayerId == playerId) return t.position;
            }
            return NpcUtil.LocalPlayerPos;
        }
    }
}
