using UnityEngine;

namespace Sealebrities
{
    /// Extra rig roles of the round-2 body plans (crabs, starfish, birds, bats, lizards, quadrupeds, primates, the
    /// goblin shark's jaw, the gulper's mouth pouch). Sits next to CreatureRig (core contract, unchanged) on every
    /// model built by CreatureFactory; clones (pool, co-op proxies) copy it with remapped references.
    /// Read by FaunaGaitAnimator.
    public sealed class CreatureRigExtra : MonoBehaviour
    {
        public CreatureGait gait;
        /// Leg hip bones; knees[i] = knee bone of leg i (child of the hip) or null.
        public Transform[] legs = new Transform[0];
        public Transform[] knees = new Transform[0];
        /// Wing shoulder bones; wingTips[i] = wrist bone or null.
        public Transform[] wings = new Transform[0];
        public Transform[] wingTips = new Transform[0];
        /// Crab claw arms and their movable fingers.
        public Transform[] claws = new Transform[0];
        public Transform[] fingers = new Transform[0];
        public Transform[] eyeStalks = new Transform[0];
        /// Land-animal tail chain (wag / sway).
        public Transform[] tail = new Transform[0];
        public Transform[] ears = new Transform[0];
        /// Goblin-shark jaw / gulper mouth pouch.
        public Transform jaw;
        /// Head (land animals, birds, marine mammals) – for the hermit crab: its shell.
        public Transform head;
        /// Body bone that carries legs / claws (crabs, birds, quadrupeds).
        public Transform body;
    }
}
