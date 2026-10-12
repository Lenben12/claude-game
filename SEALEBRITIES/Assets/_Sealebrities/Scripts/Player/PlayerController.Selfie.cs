using UnityEngine;

namespace Sealebrities
{
    /// K.4 selfie view (GameAction.Selfie, default H – rebindable): works on land and under water. Needs the camera on
    /// the hotbar (it is taken into its hand automatically; without it: „Kamera auf die Hotbar legen“). The main camera
    /// moves to arm's length (1.1 m, wheel 0.8–1.4 m) in front of the face and looks back; the mouse orbits it
    /// (yaw ±70°, pitch −30…+40°), WASD still moves at ×0.6. The first-person hands hide and the player's OWN bean diver
    /// (DiverModels.Build(Settings.Look), local only) is shown holding the camera toward the lens. Expressions: automatic
    /// (grin; scared with a predator within 5 m; wow with a new species / huge animal in frame; laugh after mischief) or
    /// by hand with the hotbar keys 1–6 (1 grin, 2 wow, 3 scared, 4 kissy, 5 wink, 6 laugh; same key again = auto).
    /// H again, Esc / any menu, a seat, a ladder or fainting leave the view. Recording and photos: FilmHandCamera
    /// (Use = record, Photo / RMB = photo) – clips get the selfie bonus. SelfieState / UIState.SelfieOpen are published
    /// for filming, UI and net (the avatar faces the camera: NetLocalAvatar.SelfieFacing).
    public partial class PlayerController
    {
        const float SelfieFov = 62f;
        const float SelfieMoveFactor = 0.6f;

        bool selfie;
        float selfieYaw, selfiePitch = 8f, selfieArm = 1.1f, selfieArmShown = 1.1f;
        int selfieManual;            // 0 = automatic
        float selfieAutoTimer, selfieLaughUntil;
        int selfieAuto = (int)SelfieExpression.Grin;
        GameObject selfieModel;
        DiverPuppet selfiePuppet;
        int selfieLookHash;
        bool selfieLookDirty = true;
        static string noCameraLine;

        /// The selfie view is active (K.4).
        public bool InSelfie => selfie;

        void OnSelfieMischief(string kind)
        {
            if (selfie) selfieLaughUntil = Time.time + 3f;
        }

        void OnSelfieLookSaved() { selfieLookDirty = true; }

        /// H pressed (and every frame while active: leave the view when it is no longer possible).
        void UpdateSelfieToggle()
        {
            bool pressed = GameInput.SelfiePressed;
            if (selfie)
            {
                bool stop = pressed || UIState.BlocksGameplay || seated || IsBusy || IsFainting || menuMode || !Hands.IsHeld(HoldItem.Camera) || lyingCam;
                if (stop) ExitSelfie();
                return;
            }
            if (!pressed || UIState.BlocksGameplay || seated || IsBusy || IsFainting || menuMode || lyingCam || FishingReel.Active) return;
            TryEnterSelfie();
        }

        /// Enters the selfie view (also for tests / AutoShot). False when no camera is on the hotbar.
        public bool TryEnterSelfie()
        {
            if (selfie) return true;
            if (!initialized || menuMode || seated || IsBusy) return false;
            if (!Hands.IsHeld(HoldItem.Camera))
            {
                if (!Hands.Owns(HoldItem.Camera) || !Hands.Find(HoldItem.Camera, out int slot))
                {
                    if (noCameraLine == null || Loc.Current != selfieLineLang) { selfieLineLang = Loc.Current; noCameraLine = Loc.Tr("Kamera auf die Hotbar legen", "Put the camera on your hotbar"); }
                    GameEvents.RaiseNotify(NotifyKind.Warning, noCameraLine);
                    return false;
                }
                Hands.Select(slot);
                if (!Hands.IsHeld(HoldItem.Camera)) return false;
            }
            if (crouching && CanStandUp()) SetCrouch(false);
            selfie = true;
            selfieYaw = 0f;
            selfiePitch = 8f;
            selfieArmShown = selfieArm;
            selfieManual = 0;
            selfieAutoTimer = 0f;
            SelfieState.Active = true;
            SelfieState.Expression = (int)SelfieExpression.Grin;
            UIState.SelfieOpen = true;
            NetLocalAvatar.SelfieCamera = null;   // the main camera itself moves to the selfie position
            EnsureSelfieModel();
            if (selfieModel != null) selfieModel.SetActive(true);
            FXSfx.Play(FXSound.Equip, 0.5f);
            return true;
        }

        static Language selfieLineLang;

        public void ExitSelfie()
        {
            if (!selfie) return;
            selfie = false;
            SelfieState.Active = false;
            SelfieState.Expression = 0;
            UIState.SelfieOpen = false;
            if (selfieModel != null) selfieModel.SetActive(false);
            if (cam != null) cam.fieldOfView = GameplayFov;
            if (camT != null && head != null)
            {
                camT.localPosition = Vector3.zero;
                camT.localRotation = Quaternion.identity;
            }
            camRotValid = false;
        }

        /// Mouse = orbit, wheel = arm length, 1–6 = expressions (called instead of the normal look while active).
        void UpdateSelfieLook(bool input, float dt)
        {
            if (!input) return;
            Vector2 d = GameInput.Look;
            if (lookSkipFrames > 0) { lookSkipFrames--; d = Vector2.zero; }
            d = Vector2.ClampMagnitude(d, 60f);
            lookDelta = d;
            selfieYaw = Mathf.Clamp(selfieYaw - d.x, -70f, 70f);
            selfiePitch = Mathf.Clamp(selfiePitch - d.y, -30f, 40f);
            float wheel = GameInput.ZoomStep;
            if (wheel != 0f) selfieArm = Mathf.Clamp(selfieArm - wheel * 0.1f, 0.8f, 1.4f);
            for (int i = 0; i < 3; i++)
            {
                if (GameInput.SlotPressed(Hand.Right, i)) PickExpression(1 + i);
                if (GameInput.SlotPressed(Hand.Left, i)) PickExpression(4 + i);
            }
            // automatic expression (4 Hz)
            selfieAutoTimer -= dt;
            if (selfieAutoTimer <= 0f)
            {
                selfieAutoTimer = 0.25f;
                selfieAuto = AutoExpression();
            }
            SelfieState.Expression = selfieManual != 0 ? selfieManual : selfieAuto;
        }

        void PickExpression(int e)
        {
            selfieManual = selfieManual == e ? 0 : e;
            FXSfx.Play(FXSound.Click, 0.3f);
        }

        int AutoExpression()
        {
            if (Time.time < selfieLaughUntil) return (int)SelfieExpression.Laugh;
            Vector3 p = transform.position;
            var list = CreatureRegistry.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null || (c is Object o && o == null) || !c.IsAlive || c.Species == null) continue;
                var b = c.Species.behavior;
                if ((b == BehaviorType.Predator || b == BehaviorType.Ambush) && (c.Position - p).sqrMagnitude < 25f) return (int)SelfieExpression.Scared;
            }
            int n = Mathf.Min(ViewfinderInfo.BracketCount, ViewfinderInfo.MaxBrackets);
            for (int i = 0; i < n; i++)
                if (ViewfinderInfo.BracketNew[i] || ViewfinderInfo.BracketTier[i] >= (byte)SizeTier.Huge) return (int)SelfieExpression.Wow;
            return (int)SelfieExpression.Grin;
        }

        /// Places the main camera at arm's length in front of the face, looking back (after the normal camera pose).
        void ApplySelfieCamera(float dt)
        {
            if (!selfie || camT == null || head == null) return;
            Vector3 eye = head.position;
            // round 3: aim at the face of the human-proportioned diver (eyes at 1.715 m × the height option)
            if (selfieModel != null) eye = transform.position + Vector3.up * (1.69f * (1f + Mathf.Clamp(Settings.Data.height, -2, 2) * 0.04f));
            Quaternion dirQ = Quaternion.Euler(0f, yaw + selfieYaw, 0f) * Quaternion.Euler(-selfiePitch, 0f, 0f);
            Vector3 dir = dirQ * Vector3.forward;
            // keep the lens out of walls, planks and hulls
            float arm = selfieArm;
            int mask = Physics.DefaultRaycastLayers & ~(1 << PlayerViewModelCamera.ViewModelLayer);
            if (Physics.SphereCast(eye, 0.12f, dir, out RaycastHit hit, arm, mask, QueryTriggerInteraction.Ignore))
                arm = Mathf.Max(0.35f, hit.distance - 0.05f);
            selfieArmShown = Mathf.Lerp(selfieArmShown, arm, 1f - Mathf.Exp(-12f * dt));
            Vector3 pos = eye + dir * selfieArmShown + Vector3.up * 0.04f;
            Vector3 look = eye + Vector3.up * 0.02f - pos;
            camT.SetPositionAndRotation(pos, Quaternion.LookRotation(look.sqrMagnitude > 1e-6f ? look : -dir, Vector3.up));
            if (cam != null && Mathf.Abs(cam.fieldOfView - SelfieFov) > 0.01f) cam.fieldOfView = SelfieFov;

            // the own bean diver at the feet, facing the body direction; the head turns toward the lens
            if (selfieModel != null)
            {
                selfieModel.transform.SetPositionAndRotation(transform.position, Quaternion.Euler(0f, yaw, 0f));
                if (selfiePuppet != null)
                {
                    selfiePuppet.HeadYaw = Mathf.Clamp(selfieYaw * 0.7f, -55f, 55f);
                    HoldItem r = Hands.Active(Hand.Right), l = Hands.Active(Hand.Left);
                    selfiePuppet.SetMotion(MotionState.Selfie, isInWater ? measuredVel.magnitude : new Vector2(measuredVel.x, measuredVel.z).magnitude,
                        motionPhase, r, l, FlashlightOn);
                    selfiePuppet.SetExpression(SelfieState.Expression);
                }
            }
        }

        void EnsureSelfieModel()
        {
            DiverLook look = Settings.Look;
            look.sponsor = DiverSponsor.CurrentIndex;   // the channel's sponsor patch on the shoulder (K.7)
            int hash = LookHash(look);
            if (selfieModel != null && !selfieLookDirty && hash == selfieLookHash) return;
            if (selfieModel != null) Destroy(selfieModel);
            selfieModel = null;
            selfiePuppet = null;
            selfieLookDirty = false;
            selfieLookHash = hash;
            var build = DiverModels.Build;
            if (build == null) return;
            GameObject go = null;
            try { go = build(look); }
            catch (System.Exception e) { Debug.LogWarning("[Player] selfie diver could not be built: " + e.Message); }
            if (go == null) return;
            go.name = "SelfieDiver";
            go.transform.SetParent(worldRoot != null ? worldRoot : transform.parent, true);
            // never a collider, never on the view-model layer
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++) Destroy(cols[i]);
            DiverLod.Force(go, true);   // the selfie is always a close-up
            selfieModel = go;
            selfiePuppet = go.GetComponent<DiverPuppet>();
            go.SetActive(false);
        }

        /// Everything visible incl. the round-3 body / hair / beard / nose / accessory fields.
        static int LookHash(DiverLook l) => NetLookCodec.Hash(l);

        void DestroySelfieModel()
        {
            if (selfieModel != null) Destroy(selfieModel);
            selfieModel = null;
            selfiePuppet = null;
        }
    }
}
